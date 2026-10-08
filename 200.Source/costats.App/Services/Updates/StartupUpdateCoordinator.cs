using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;

namespace costats.App.Services.Updates;

/// <summary>GitHub 릴리스 하나 — 설정 화면의 버전 비교·선택 설치에 쓴다.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, DateTimeOffset? PublishedAt, string HtmlUrl, bool Prerelease, bool HasPackage)
{
    // 계약: 날짜가 붙은 버전은 날짜가 이미 이름에 있다 — 옛 세 자리 릴리스만 게시일을 덧붙인다
    public string Label => Version.Revision < 0 && PublishedAt is { } at
        ? $"v{Version.ToString(3)} · {at.ToLocalTime():yyyy-MM-dd}"
        : "v" + StartupUpdateCoordinator.Display(Version);
}

public sealed class StartupUpdateCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Regex SemVerRegex = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<date>\d{8}))?(?:\+.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // 계약: 릴리스 버전은 「배포 버전.빌드 날짜」(1.0.0.20261006) — 날짜는 Version.Revision 에 담겨 비교에 쓰인다. 옛 세 자리 릴리스는 Revision -1 이라 같은 번호의 날짜판보다 낮다
    public static string Display(Version version) => version.Revision >= 0 ? version.ToString(4) : version.ToString(3);

    private static readonly Regex ShaLineRegex = new(
        @"^(?<hash>[A-Fa-f0-9]{64})\s+\*?(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly UpdateOptions _options;
    private readonly HttpClient _httpClient;
    private readonly string _appBaseDirectory;
    private readonly string _executablePath;
    private readonly string _updatesRoot;
    private readonly string _statePath;
    private readonly string _pendingPath;
    private readonly string _runtimeRid;
    private readonly Version _currentVersion;
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    // 계약: 지금 실행 파일 이름. 설치 파일명과 같다
    internal const string ExecutableName = "AI-Usage-Monitor_JHJ.exe";

    // 왜: 1.1.0 이전 버전이 만든 꾸러미·스테이징에는 이 이름만 있다 — 찾을 때는 새 이름 다음에 이것도 본다
    // TODO: 옛 이름 사본을 꾸러미에서 빼는 릴리스에서 함께 지운다
    internal const string LegacyExecutableName = "AiUsageMonitor.exe";

    public StartupUpdateCoordinator(UpdateOptions options)
    {
        _options = options;
        _appBaseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _executablePath = Environment.ProcessPath ?? Path.Combine(_appBaseDirectory, ExecutableName);
        _runtimeRid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        _currentVersion = ResolveCurrentVersion();

        _updatesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiUsageMonitor",
            "updates");
        _statePath = Path.Combine(_updatesRoot, "state.json");
        _pendingPath = Path.Combine(_updatesRoot, "pending.json");

        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AiUsageMonitor", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Task<bool> TryApplyPendingUpdateAsync(CancellationToken cancellationToken)
        => TryApplyPendingUpdateAsync(cancellationToken, manualTrigger: false);

    public async Task<bool> TryApplyPendingUpdateAsync(CancellationToken cancellationToken, bool manualTrigger)
    {
        if (!_options.Enabled || !CanSelfUpdate())
        {
            return false;
        }

        // Only gate on ApplyStagedUpdateOnStartup for automatic (non-manual) triggers
        if (!manualTrigger && !_options.ApplyStagedUpdateOnStartup)
        {
            return false;
        }

        try
        {
            var pending = await ReadJsonAsync<PendingUpdate>(_pendingPath, cancellationToken).ConfigureAwait(false);
            if (pending is null)
            {
                return false;
            }

            const int maxApplyAttempts = 3;
            if (pending.FailedAttempts >= maxApplyAttempts)
            {
                Trace.WriteLine($"[costats-update] pending update {pending.Version} failed {pending.FailedAttempts} times, giving up");
                SafeDeleteFile(_pendingPath);
                SafeDeleteDirectory(pending.StagingDirectory);
                return false;
            }

            if (!TryParseSemVer(pending.Version, out var pendingVersion) || !IsInstallable(pending, pendingVersion))
            {
                SafeDeleteFile(_pendingPath);
                SafeDeleteDirectory(pending.StagingDirectory);
                return false;
            }

            if (!TryResolvePendingExecutable(pending, out var stagedExe, out var executableRelativePath))
            {
                SafeDeleteFile(_pendingPath);
                return false;
            }

            Directory.CreateDirectory(_updatesRoot);
            var scriptPath = Path.Combine(_updatesRoot, "apply-update.ps1");

            // Prefer the script shipped with the staged update (from the new version's ZIP).
            // This prevents a chicken-and-egg problem where the running version's embedded
            // script has a bug that can only be fixed by the version being installed.
            var stagedScript = Path.Combine(pending.StagingDirectory, "apply-update.ps1");
            if (File.Exists(stagedScript))
            {
                File.Copy(stagedScript, scriptPath, overwrite: true);
            }
            else
            {
                await File.WriteAllTextAsync(scriptPath, UpdaterScriptContents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                    .ConfigureAwait(false);
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                // 함정: 작업 폴더를 물려주면 설치 폴더가 스크립트의 현재 폴더가 되어 폴더 교체(Move-Item)가 매번 실패한다
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("-TargetPid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-InstallDir");
            psi.ArgumentList.Add(_appBaseDirectory);
            psi.ArgumentList.Add("-StagingDir");
            psi.ArgumentList.Add(pending.StagingDirectory);
            psi.ArgumentList.Add("-ExecutableRelativePath");
            psi.ArgumentList.Add(executableRelativePath);
            psi.ArgumentList.Add("-PendingFilePath");
            psi.ArgumentList.Add(_pendingPath);

            var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            Trace.WriteLine($"[costats-update] launching updater for version {pending.Version} from {stagedExe}");
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[costats-update] apply staged update failed: {ex}");
            return false;
        }
    }

    public async Task<UpdateCheckResult> CheckAndStageUpdateAsync(CancellationToken cancellationToken, bool forceCheck = false)
    {
        if (!_options.Enabled || !CanSelfUpdate())
        {
            return UpdateCheckResult.Disabled;
        }

        if (!await _checkLock.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false))
        {
            return UpdateCheckResult.AlreadyRunning;
        }

        try
        {
            Directory.CreateDirectory(_updatesRoot);
            var pending = await ReadJsonAsync<PendingUpdate>(_pendingPath, cancellationToken).ConfigureAwait(false);
            if (pending is not null && IsPendingValidAndNewer(pending))
            {
                return UpdateCheckResult.UpdateAlreadyStaged;
            }

            var state = await ReadJsonAsync<UpdateState>(_statePath, cancellationToken).ConfigureAwait(false) ?? new UpdateState();
            var now = DateTimeOffset.UtcNow;
            var interval = TimeSpan.FromHours(_options.CheckIntervalHours);
            if (!forceCheck && state.LastCheckedUtc.HasValue && now - state.LastCheckedUtc.Value < interval)
            {
                return UpdateCheckResult.Skipped;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildLatestReleaseUri(_options.Repository));
            // 함정: 받아 두고 적용에 실패한 릴리스가 있으면 304 가 「최신」으로 읽혀 다시 받지 않는다 — 본 버전이 지금보다 높으면 ETag 를 보내지 않는다
            var seenNewer = TryParseSemVer(state.LastSeenVersion, out var lastSeen) && lastSeen > _currentVersion;
            if (!seenNewer && !string.IsNullOrWhiteSpace(state.ETag) &&
                EntityTagHeaderValue.TryParse(state.ETag, out var eTagHeader))
            {
                request.Headers.IfNoneMatch.Add(eTagHeader);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            state.LastCheckedUtc = now;
            state.ETag = response.Headers.ETag?.Tag ?? state.ETag;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                await WriteJsonAsync(_statePath, state, cancellationToken).ConfigureAwait(false);
                return UpdateCheckResult.UpToDate;
            }

            if (!response.IsSuccessStatusCode)
            {
                await WriteJsonAsync(_statePath, state, cancellationToken).ConfigureAwait(false);
                return UpdateCheckResult.CheckFailed;
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await ParseReleaseAsync(contentStream, cancellationToken).ConfigureAwait(false);
            if (release is null)
            {
                await WriteJsonAsync(_statePath, state, cancellationToken).ConfigureAwait(false);
                return UpdateCheckResult.CheckFailed;
            }

            if (release.Prerelease && !_options.AllowPrerelease)
            {
                await WriteJsonAsync(_statePath, state, cancellationToken).ConfigureAwait(false);
                return UpdateCheckResult.UpToDate;
            }

            if (!TryGetBestAsset(release, out var zipAsset, out var releaseVersion))
            {
                await WriteJsonAsync(_statePath, state, cancellationToken).ConfigureAwait(false);
                return UpdateCheckResult.UpToDate;
            }

            if (releaseVersion <= _currentVersion)
            {
                state.LastSeenVersion = Display(releaseVersion);
                await WriteJsonAsync(_statePath, state, cancellationToken).ConfigureAwait(false);
                return UpdateCheckResult.UpToDate;
            }

            await StageAsync(release, zipAsset, releaseVersion, allowDowngrade: false, cancellationToken).ConfigureAwait(false);
            state.LastSeenVersion = Display(releaseVersion);
            await WriteJsonAsync(_statePath, state, cancellationToken).ConfigureAwait(false);

            return UpdateCheckResult.UpdateStaged;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[costats-update] check/stage failed: {ex}");
            return UpdateCheckResult.CheckFailed;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    // 계약: 받은 zip 을 검증해 staging 에 풀고 pending.json 을 남긴다 — 실제 교체는 TryApplyPendingUpdateAsync 가 한다
    private async Task StageAsync(ReleaseDocument release, ReleaseAsset zipAsset, Version releaseVersion, bool allowDowngrade, CancellationToken cancellationToken, IProgress<UpdateProgress>? progress = null)
    {
        var downloadsDir = Path.Combine(_updatesRoot, "downloads");
        Directory.CreateDirectory(downloadsDir);
        var zipPath = Path.Combine(downloadsDir, zipAsset.Name);
        await DownloadToFileAsync(zipAsset.DownloadUrl, zipPath, cancellationToken, progress).ConfigureAwait(false);

        progress?.Report(new UpdateProgress(UpdateStage.Verifying, 0, 0));
        var expectedHash = await TryResolveChecksumAsync(release, zipAsset, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(expectedHash))
        {
            var actualHash = await ComputeSha256Async(zipPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Downloaded update checksum does not match release checksum.");
            }
        }

        var stageDir = Path.Combine(
            _updatesRoot,
            "staging",
            $"{Display(releaseVersion)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        if (Directory.Exists(stageDir))
        {
            Directory.Delete(stageDir, recursive: true);
        }

        Directory.CreateDirectory(stageDir);
        progress?.Report(new UpdateProgress(UpdateStage.Extracting, 0, 0));
        ZipFile.ExtractToDirectory(zipPath, stageDir, overwriteFiles: true);

        if (!TryFindStagedExecutable(stageDir, out var stagedExecutablePath))
        {
            throw new FileNotFoundException($"Staged update did not contain {ExecutableName}.");
        }

        var executableRelativePath = Path.GetRelativePath(stageDir, stagedExecutablePath);
        var pendingUpdate = new PendingUpdate
        {
            Version = Display(releaseVersion),
            CreatedUtc = DateTimeOffset.UtcNow,
            StagingDirectory = stageDir,
            ExecutableRelativePath = executableRelativePath,
            AllowDowngrade = allowDowngrade
        };

        await WriteJsonAsync(_pendingPath, pendingUpdate, cancellationToken).ConfigureAwait(false);
        SafeDeleteFile(zipPath);
        CleanupOldStagingDirectories(stageDir);
    }

    private static string BuildLatestReleaseUri(string repository)
    {
        return $"https://api.github.com/repos/{repository}/releases/latest";
    }

    public Version CurrentVersion => _currentVersion;

    public string ReleasesPageUrl => $"https://github.com/{_options.Repository}/releases";

    // 계약: 설치 폴더에서 돌 때만 true — 개발 빌드(bin\)·다운로드 폴더 실행은 비교만 하고 설치는 못 한다
    public bool CanInstall => _options.Enabled && CanSelfUpdate();

    /// <summary>받아 둔(staging) 업데이트의 버전 — 없거나 지금 버전보다 높지 않으면 null.</summary>
    public async Task<Version?> GetStagedVersionAsync(CancellationToken cancellationToken)
    {
        var pending = await ReadJsonAsync<PendingUpdate>(_pendingPath, cancellationToken).ConfigureAwait(false);
        return pending is not null && IsPendingValidAndNewer(pending) && TryParseSemVer(pending.Version, out var version) ? version : null;
    }

    private readonly Dictionary<Version, ReleaseDocument> _releaseCache = [];

    /// <summary>
    /// GitHub 릴리스 목록(최신 순). 계약: 이 PC 의 RID 용 zip 이 있는 릴리스만 HasPackage=true 다.
    /// 함정: 업데이트가 꺼져 있어도(개발 빌드) 비교는 해야 하므로 CanSelfUpdate 를 보지 않는다.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        var uri = $"https://api.github.com/repos/{_options.Repository}/releases?per_page=30";
        using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var list = new List<ReleaseInfo>();
        lock (_releaseCache)
        {
            _releaseCache.Clear();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var release = ParseReleaseElement(element);
                if (release is null || element.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                {
                    continue;
                }

                var tag = element.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
                if (!TryParseSemVer(tag, out var version))
                {
                    continue;
                }

                var published = element.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String &&
                                DateTimeOffset.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                    ? at
                    : (DateTimeOffset?)null;
                var url = element.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;

                _releaseCache[version] = release;
                list.Add(new ReleaseInfo(version, tag, published, url, release.Prerelease, TryGetBestAsset(release, out _, out _)));
            }
        }

        // 계약: 같은 배포 버전(세 자리)에서는 날짜가 가장 늦은 릴리스 하나만 보인다 — 옛 날짜판은 800.Deploy\prune-releases.ps1 이 지운다
        return list
            .GroupBy(r => (r.Version.Major, r.Version.Minor, r.Version.Build))
            .Select(g => g.MaxBy(r => r.Version)!)
            .OrderByDescending(r => r.Version)
            .ToList();
    }

    /// <summary>
    /// 고른 버전을 받아 설치 준비까지 한다 — 낮은 버전(되돌리기)도 된다. 적용은 TryApplyPendingUpdateAsync(manualTrigger: true).
    /// 계약: GetReleasesAsync 로 목록을 먼저 읽어 둔 버전만 받는다.
    /// </summary>
    public async Task<UpdateCheckResult> StageReleaseAsync(Version version, CancellationToken cancellationToken, IProgress<UpdateProgress>? progress = null)
    {
        if (!CanInstall)
        {
            return UpdateCheckResult.Disabled;
        }

        ReleaseDocument? release;
        lock (_releaseCache)
        {
            _releaseCache.TryGetValue(version, out release);
        }

        if (release is null || !TryGetBestAsset(release, out var zipAsset, out var assetVersion))
        {
            return UpdateCheckResult.CheckFailed;
        }

        if (!await _checkLock.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false))
        {
            return UpdateCheckResult.AlreadyRunning;
        }

        try
        {
            Directory.CreateDirectory(_updatesRoot);
            await StageAsync(release, zipAsset, assetVersion, allowDowngrade: assetVersion < _currentVersion, cancellationToken, progress).ConfigureAwait(false);
            return UpdateCheckResult.UpdateStaged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.WriteLine($"[costats-update] stage {version} failed: {ex}");
            return UpdateCheckResult.CheckFailed;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private bool CanSelfUpdate()
    {
        if (!File.Exists(_executablePath))
        {
            return false;
        }

        if (_appBaseDirectory.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
        {
            // MSIX/AppInstaller installs are updated by App Installer.
            return false;
        }

        if (_appBaseDirectory.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) &&
            _appBaseDirectory.Contains(@"\200.Source\", StringComparison.OrdinalIgnoreCase))
        {
            // Development runs should not self-update.
            return false;
        }

        if (!SelfInstaller.IsInstalledLocation)
        {
            // 함정: 적용 스크립트가 실행 폴더를 통째로 갈아 끼운다 — 다운로드 폴더 등에서 돌면 그 폴더의 다른 파일이 사라진다
            return false;
        }

        if (!HasWriteAccess(_appBaseDirectory))
        {
            return false;
        }

        return true;
    }

    private static bool HasWriteAccess(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var testPath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testPath, "ok");
            File.Delete(testPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsPendingValidAndNewer(PendingUpdate pending)
    {
        if (!TryResolvePendingExecutable(pending, out _, out _))
        {
            SafeDeleteFile(_pendingPath);
            return false;
        }

        return TryParseSemVer(pending.Version, out var pendingVersion) && IsInstallable(pending, pendingVersion);
    }

    private static bool TryResolvePendingExecutable(PendingUpdate pending, out string stagedExePath, out string executableRelativePath)
    {
        stagedExePath = string.Empty;
        executableRelativePath = ExecutableName;

        if (string.IsNullOrWhiteSpace(pending.StagingDirectory) || !Directory.Exists(pending.StagingDirectory))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(pending.ExecutableRelativePath))
        {
            var candidate = Path.Combine(pending.StagingDirectory, pending.ExecutableRelativePath);
            if (File.Exists(candidate))
            {
                stagedExePath = candidate;
                executableRelativePath = pending.ExecutableRelativePath;
                return true;
            }
        }

        if (!TryFindStagedExecutable(pending.StagingDirectory, out var discoveredExecutable))
        {
            return false;
        }

        stagedExePath = discoveredExecutable;
        executableRelativePath = Path.GetRelativePath(pending.StagingDirectory, discoveredExecutable);
        return true;
    }

    // 계약: 새 이름을 먼저 찾고 없으면 옛 이름 — 꾸러미에 둘 다 들어 있는 과도기를 지난다
    private static bool TryFindStagedExecutable(string stageDirectory, out string executablePath)
    {
        foreach (var name in new[] { ExecutableName, LegacyExecutableName })
        {
            executablePath = Path.Combine(stageDirectory, name);
            if (File.Exists(executablePath))
            {
                return true;
            }
        }

        var discovered = new[] { ExecutableName, LegacyExecutableName }
            .Select(name => Directory.EnumerateFiles(stageDirectory, name, SearchOption.AllDirectories).FirstOrDefault())
            .FirstOrDefault(found => !string.IsNullOrWhiteSpace(found));

        if (string.IsNullOrWhiteSpace(discovered))
        {
            executablePath = string.Empty;
            return false;
        }

        executablePath = discovered;
        return true;
    }

    private static void CleanupOldStagingDirectories(string keepPath)
    {
        var parent = Path.GetDirectoryName(keepPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(parent))
        {
            if (string.Equals(dir, keepPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static Version ResolveCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .Select(attribute => attribute.InformationalVersion)
            .FirstOrDefault();

        if (TryParseSemVer(informational, out var informationalVersion))
        {
            return informationalVersion;
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null && assemblyVersion.Major >= 0 && assemblyVersion.Minor >= 0 && assemblyVersion.Build >= 0)
        {
            return new Version(assemblyVersion.Major, assemblyVersion.Minor, assemblyVersion.Build);
        }

        return new Version(0, 0, 0);
    }

    private bool TryGetBestAsset(ReleaseDocument release, out ReleaseAsset selectedAsset, out Version selectedVersion)
    {
        selectedAsset = default!;
        selectedVersion = new Version(0, 0, 0);

        var candidates = new List<(ReleaseAsset Asset, Version Version)>();
        foreach (var asset in release.Assets)
        {
            if (!TryExtractVersionFromAssetName(asset.Name, out var assetRid, out var parsedVersion))
            {
                continue;
            }

            candidates.Add((asset with { RuntimeIdentifier = assetRid }, parsedVersion));
        }

        var best = candidates
            .Where(candidate => string.Equals(candidate.Asset.RuntimeIdentifier, _runtimeRid, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.Version)
            .FirstOrDefault();

        if (best.Asset is null || string.IsNullOrWhiteSpace(best.Asset.Name))
        {
            return false;
        }

        selectedAsset = best.Asset;
        selectedVersion = best.Version;
        return true;
    }

    private static bool TryExtractVersionFromAssetName(string assetName, out string runtimeIdentifier, out Version version)
    {
        runtimeIdentifier = string.Empty;
        version = new Version(0, 0, 0);

        if (!assetName.StartsWith("AiUsageMonitor-win-", StringComparison.OrdinalIgnoreCase) ||
            !assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var markerIndex = assetName.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= 0)
        {
            return false;
        }

        runtimeIdentifier = assetName["AiUsageMonitor-".Length..markerIndex];
        var versionText = assetName[(markerIndex + 2)..^4];
        return TryParseSemVer(versionText, out version);
    }

    private static bool TryParseSemVer(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = SemVerRegex.Match(value.TrimStart('v', 'V').Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = match.Groups["date"].Success && int.TryParse(match.Groups["date"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var date)
            ? new Version(major, minor, patch, date)
            : new Version(major, minor, patch);
        return true;
    }

    private async Task<string?> TryResolveChecksumAsync(ReleaseDocument release, ReleaseAsset packageAsset, CancellationToken cancellationToken)
    {
        var directChecksumAsset = release.Assets
            .FirstOrDefault(asset => string.Equals(asset.Name, $"{packageAsset.Name}.sha256", StringComparison.OrdinalIgnoreCase));
        if (directChecksumAsset is not null && !string.IsNullOrWhiteSpace(directChecksumAsset.Name))
        {
            var checksumText = await DownloadAsStringAsync(directChecksumAsset.DownloadUrl, cancellationToken).ConfigureAwait(false);
            return ExtractChecksum(checksumText, packageAsset.Name);
        }

        var checksumsAsset = release.Assets
            .FirstOrDefault(asset => string.Equals(asset.Name, "checksums.txt", StringComparison.OrdinalIgnoreCase));
        if (checksumsAsset is not null && !string.IsNullOrWhiteSpace(checksumsAsset.Name))
        {
            var checksumText = await DownloadAsStringAsync(checksumsAsset.DownloadUrl, cancellationToken).ConfigureAwait(false);
            return ExtractChecksum(checksumText, packageAsset.Name);
        }

        return null;
    }

    private static string? ExtractChecksum(string checksumText, string packageName)
    {
        if (string.IsNullOrWhiteSpace(checksumText))
        {
            return null;
        }

        foreach (var line in checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Regex.IsMatch(line, "^[A-Fa-f0-9]{64}$"))
            {
                return line.Trim();
            }

            var match = ShaLineRegex.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var candidateName = match.Groups["name"].Value.Trim();
            if (string.Equals(candidateName, packageName, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups["hash"].Value.Trim();
            }
        }

        return null;
    }

    private async Task<string> DownloadAsStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadToFileAsync(string url, string destinationPath, CancellationToken cancellationToken, IProgress<UpdateProgress>? progress = null)
    {
        var tempPath = $"{destinationPath}.part";
        SafeDeleteFile(tempPath);

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // HttpClient.Timeout only covers headers with ResponseHeadersRead.
        // Add an explicit timeout for the body download to prevent indefinite hangs.
        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        downloadCts.CancelAfter(TimeSpan.FromMinutes(3));

        // 왜: 65MB 꾸러미는 수십 초 걸린다 — 256KB 마다 받은 양을 알려 화면이 멈춘 것처럼 보이지 않게 한다
        var total = response.Content.Headers.ContentLength ?? -1;
        progress?.Report(new UpdateProgress(UpdateStage.Downloading, 0, total));
        await using (var source = await response.Content.ReadAsStreamAsync(downloadCts.Token).ConfigureAwait(false))
        await using (var destination = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            long done = 0, lastReported = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, downloadCts.Token).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), downloadCts.Token).ConfigureAwait(false);
                done += read;
                if (done - lastReported >= 262_144)
                {
                    lastReported = done;
                    progress?.Report(new UpdateProgress(UpdateStage.Downloading, done, total));
                }
            }

            progress?.Report(new UpdateProgress(UpdateStage.Downloading, done, total < 0 ? done : total));
        }

        SafeDeleteFile(destinationPath);
        File.Move(tempPath, destinationPath);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<ReleaseDocument?> ParseReleaseAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseReleaseElement(document.RootElement);
    }

    private static ReleaseDocument? ParseReleaseElement(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var assets = new List<ReleaseAsset>();
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            var name = assetElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var downloadUrl = assetElement.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            assets.Add(new ReleaseAsset(name, downloadUrl));
        }

        var prerelease = root.TryGetProperty("prerelease", out var prereleaseElement) && prereleaseElement.GetBoolean();
        return new ReleaseDocument(prerelease, assets);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return default;
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void SafeDeleteDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private sealed record ReleaseDocument(bool Prerelease, IReadOnlyList<ReleaseAsset> Assets);

    private sealed record ReleaseAsset(string Name, string DownloadUrl, string RuntimeIdentifier = "");

    private sealed class UpdateState
    {
        public DateTimeOffset? LastCheckedUtc { get; set; }
        public string? ETag { get; set; }
        public string? LastSeenVersion { get; set; }
    }

    private sealed class PendingUpdate
    {
        public string Version { get; set; } = "0.0.0";
        public DateTimeOffset CreatedUtc { get; set; }
        public string StagingDirectory { get; set; } = string.Empty;
        public string ExecutableRelativePath { get; set; } = ExecutableName;
        public int FailedAttempts { get; set; }

        // 계약: 사용자가 낮은 버전을 골라 설치할 때만 true — 자동 업데이트는 내려가지 않는다
        public bool AllowDowngrade { get; set; }
    }

    private bool IsInstallable(PendingUpdate pending, Version pendingVersion) =>
        pendingVersion > _currentVersion || (pending.AllowDowngrade && pendingVersion != _currentVersion);

    private const string UpdaterScriptContents = """
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$StagingDir,
    [Parameter(Mandatory = $true)][string]$ExecutableRelativePath,
    [Parameter(Mandatory = $true)][string]$PendingFilePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$logDir = Join-Path $env:LOCALAPPDATA "AiUsageMonitor\updates"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir "apply-update.log"

# Trap: this process inherits the app's working directory, which is the install folder. Windows refuses to move
# a folder that is some process's current directory, so the swap below failed every time. Step out first.
# Set-Location alone is not enough: it leaves the Win32 current directory of this process on the install folder.
Set-Location -LiteralPath $env:TEMP
[Environment]::CurrentDirectory = $env:TEMP

# Track state for guaranteed relaunch
$updateSucceeded = $false
$backupDir = "$InstallDir.__backup"
$oldExePath = Join-Path $InstallDir $ExecutableRelativePath
$newExePath = $null

function Write-Log {
    param([string]$Message)
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    Add-Content -Path $logPath -Value "[$stamp] $Message"
}

function Invoke-WithRetry {
    param(
        [scriptblock]$Action,
        [int]$Attempts = 20,
        [int]$DelayMs = 1500
    )

    for ($i = 1; $i -le $Attempts; $i++) {
        try {
            & $Action
            return
        } catch {
            if ($i -ge $Attempts) {
                throw
            }
            Start-Sleep -Milliseconds $DelayMs
        }
    }
}

function Relaunch-App {
    # Try new exe first, fall back to old exe, fall back to any exe we can find
    $candidates = @()
    if ($newExePath -and (Test-Path $newExePath)) { $candidates += $newExePath }
    $currentExe = Join-Path $InstallDir $ExecutableRelativePath
    if ((Test-Path $currentExe) -and ($candidates -notcontains $currentExe)) { $candidates += $currentExe }
    $backupExe = Join-Path $backupDir $ExecutableRelativePath
    if ((Test-Path $backupExe) -and ($candidates -notcontains $backupExe)) { $candidates += $backupExe }
    $stagedExe = Join-Path $StagingDir $ExecutableRelativePath
    if ((Test-Path $stagedExe) -and ($candidates -notcontains $stagedExe)) { $candidates += $stagedExe }

    foreach ($exe in $candidates) {
        try {
            Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) | Out-Null
            Write-Log "Launched app: $exe"
            return
        } catch {
            Write-Log "Failed to launch $exe : $($_.Exception.Message)"
        }
    }
    Write-Log "CRITICAL: Could not launch any executable. Candidates: $($candidates -join ', ')"
}

function Increment-FailedAttempts {
    try {
        if (Test-Path $PendingFilePath) {
            $json = Get-Content -Raw -Path $PendingFilePath | ConvertFrom-Json
            if (-not (Get-Member -InputObject $json -Name "failedAttempts" -MemberType NoteProperty)) {
                $json | Add-Member -NotePropertyName "failedAttempts" -NotePropertyValue 0
            }
            $json.failedAttempts = $json.failedAttempts + 1
            $json | ConvertTo-Json -Depth 10 | Set-Content -Path $PendingFilePath -Encoding UTF8
            Write-Log "Incremented failedAttempts to $($json.failedAttempts)."
        }
    } catch {
        Write-Log "Failed to increment failedAttempts: $($_.Exception.Message)"
    }
}

Write-Log "Starting staged update."
Write-Log "InstallDir=$InstallDir"
Write-Log "StagingDir=$StagingDir"

# --- Circuit breaker: abort if too many failed attempts ---
$maxAttempts = 3
try {
    if (Test-Path $PendingFilePath) {
        $pendingJson = Get-Content -Raw -Path $PendingFilePath | ConvertFrom-Json
        $currentAttempts = 0
        if (Get-Member -InputObject $pendingJson -Name "failedAttempts" -MemberType NoteProperty) {
            $currentAttempts = $pendingJson.failedAttempts
        }
        if ($currentAttempts -ge $maxAttempts) {
            Write-Log "Update has failed $currentAttempts times (max $maxAttempts). Giving up and removing pending update."
            Remove-Item -Force $PendingFilePath -ErrorAction SilentlyContinue
            Relaunch-App
            return
        }
    }
} catch {
    Write-Log "Failed to read failedAttempts: $($_.Exception.Message)"
}

try {
    # --- Wait for target process to exit ---
    Write-Log "Waiting for process $TargetPid to exit..."
    for ($i = 0; $i -lt 120; $i++) {
        if (-not (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue)) {
            Write-Log "Process exited after $([math]::Round($i * 0.5, 1))s."
            break
        }
        Start-Sleep -Milliseconds 500
    }

    if (Get-Process -Id $TargetPid -ErrorAction SilentlyContinue) {
        Write-Log "Target process still running after 60s. Stopping forcefully."
        Stop-Process -Id $TargetPid -Force -ErrorAction SilentlyContinue
    }

    # Wait for Windows to fully release file handles after process death.
    # Antivirus, Windows Search indexer, and .NET single-file extraction cache
    # can hold handles for several seconds after the process is gone.
    Write-Log "Waiting for file handles to release..."
    Start-Sleep -Seconds 5

    # --- Validate staging ---
    if (-not (Test-Path $StagingDir)) {
        Write-Log "Staging directory not found: $StagingDir"
        Relaunch-App
        return
    }

    $stagedExeCheck = Join-Path $StagingDir $ExecutableRelativePath
    if (-not (Test-Path $stagedExeCheck)) {
        Write-Log "Staged executable not found: $stagedExeCheck"
        Relaunch-App
        return
    }

    # --- Clean old backup ---
    if (Test-Path $backupDir) {
        try {
            Invoke-WithRetry { Remove-Item -Recurse -Force $backupDir }
            Write-Log "Cleaned old backup directory."
        } catch {
            Write-Log "Could not clean old backup: $($_.Exception.Message)"
            # Non-fatal: try the swap anyway, old backup might not block it
        }
    }

    # --- Swap: move current install to backup ---
    try {
        Invoke-WithRetry { Move-Item -Path $InstallDir -Destination $backupDir }
        Write-Log "Moved install to backup."
    } catch {
        Write-Log "Cannot move install to backup: $($_.Exception.Message)"
        Write-Log "Update deferred to next startup. Relaunching current app."
        Increment-FailedAttempts
        Relaunch-App
        return
    }

    # --- Swap: move staging to install ---
    try {
        Invoke-WithRetry { Move-Item -Path $StagingDir -Destination $InstallDir }
        Write-Log "Moved staging to install."
    } catch {
        Write-Log "Cannot move staging to install: $($_.Exception.Message)"
        # Rollback: restore backup to install dir
        try {
            Move-Item -Path $backupDir -Destination $InstallDir -Force
            Write-Log "Rollback completed."
        } catch {
            Write-Log "CRITICAL: Rollback also failed: $($_.Exception.Message)"
        }
        Relaunch-App
        return
    }

    # --- Verify new executable ---
    $newExePath = Join-Path $InstallDir $ExecutableRelativePath
    if (-not (Test-Path $newExePath)) {
        Write-Log "New executable not found after swap: $newExePath. Rolling back."
        try {
            if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue }
            Move-Item -Path $backupDir -Destination $InstallDir -Force
            Write-Log "Rollback completed."
        } catch {
            Write-Log "Rollback failed: $($_.Exception.Message)"
        }
        Relaunch-App
        return
    }

    $updateSucceeded = $true
    Write-Log "Swap completed successfully."

    # --- Cleanup ---
    if (Test-Path $PendingFilePath) {
        Remove-Item -Force $PendingFilePath -ErrorAction SilentlyContinue
    }

    if (Test-Path $backupDir) {
        try {
            Remove-Item -Recurse -Force $backupDir
        } catch {
            Write-Log "Backup cleanup failed (non-fatal): $($_.Exception.Message)"
        }
    }

    # --- Launch updated app ---
    Relaunch-App
    Write-Log "Update finished successfully."

} catch {
    Write-Log "Unexpected error: $($_.Exception.Message)"
    # Guarantee relaunch no matter what
    Relaunch-App
}
""";
}
