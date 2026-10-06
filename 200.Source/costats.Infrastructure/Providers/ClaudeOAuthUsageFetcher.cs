using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using costats.Core.Pulse;

namespace costats.Infrastructure.Providers;

/// <summary>
/// Fetches Claude usage data via the Anthropic OAuth API.
/// </summary>
public sealed class ClaudeOAuthUsageFetcher : IDisposable
{
    private const string BaseUrl = "https://api.anthropic.com";
    private const string UsagePath = "/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";

    private static readonly TimeSpan BackoffBase = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackoffCap = TimeSpan.FromHours(6);

    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan RefreshCooldownSuccess = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshCooldownFailure = TimeSpan.FromSeconds(20);

    private readonly HttpClient _httpClient;
    private readonly string? _configDir;

    private int _consecutiveFailures;
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;
    private string? _lastCredentialFingerprint;
    private ClaudeOAuthUsageResult? _memoryCache;
    private DateTimeOffset _memoryCacheWrittenAt = DateTimeOffset.MinValue;
    private DateTimeOffset _refreshBlockedUntil = DateTimeOffset.MinValue;

    public ClaudeOAuthUsageFetcher()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("anthropic-beta", BetaHeader);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "claude-code/2.1.70");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public ClaudeOAuthUsageFetcher(string configDir) : this()
    {
        _configDir = configDir;
    }

    //  Public API
    // 계약: 마지막 FetchAsync 때 토큰 파일(.credentials.json)이 있었나 — 없으면 화면은 한도 대신 「로그인 필요」를 보인다
    public bool HasToken { get; private set; } = true;

    // 계약: 마지막 조회가 실패한 까닭(「HTTP 403」 등, 토큰 값은 담지 않는다) — 화면 상태 줄에 그대로 보인다
    public string? LastError { get; private set; }

    public async Task<ClaudeOAuthUsageResult?> FetchAsync(CancellationToken cancellationToken)
    {
        // 1. Load credentials & detect changes
        var credentials = await LoadCredentialsAsync(_configDir);
        var account = ReadAccountEmail(_configDir);
        var fingerprint = ComputeFingerprint(credentials);

        // 왜: 토큰이 없을 때 캐시를 내면 다른 계정이 받아 둔 한도가 지금 로그인 이름 아래에 보인다
        HasToken = credentials?.AccessToken is not null;
        if (!HasToken)
        {
            _memoryCache = null;
            return null;
        }

        if (fingerprint != _lastCredentialFingerprint)
        {
            _lastCredentialFingerprint = fingerprint;
            _consecutiveFailures = 0;
            _blockedUntil = DateTimeOffset.MinValue;
        }

        // 2. If token is expired, try delegated refresh via Claude CLI
        if (credentials is not null && IsTokenExpired(credentials))
        {
            await TryDelegatedRefreshAsync(cancellationToken).ConfigureAwait(false);
            credentials = await LoadCredentialsAsync(_configDir);

            var newFingerprint = ComputeFingerprint(credentials);
            if (newFingerprint != _lastCredentialFingerprint)
            {
                _lastCredentialFingerprint = newFingerprint;
                _consecutiveFailures = 0;
                _blockedUntil = DateTimeOffset.MinValue;
            }
        }

        // 3. Check failure gate
        if (DateTimeOffset.UtcNow < _blockedUntil)
        {
            return GetCachedResult(account);
        }

        // 4. Attempt fresh fetch
        var fresh = await TryFetchAsync(credentials, cancellationToken).ConfigureAwait(false);
        if (fresh is not null)
        {
            fresh = fresh with { AccountEmail = account };
            _consecutiveFailures = 0;
            _blockedUntil = DateTimeOffset.MinValue;
            SetMemoryCache(fresh);
            _ = WriteDiskCacheAsync(fresh);
            return fresh;
        }

        // 5. Record failure & compute backoff
        _consecutiveFailures++;
        _blockedUntil = DateTimeOffset.UtcNow + ComputeBackoff(_consecutiveFailures);

        return GetCachedResult(account);
    }

    //  HTTP
    private async Task<ClaudeOAuthUsageResult?> TryFetchAsync(
        ClaudeCredentials? credentials, CancellationToken cancellationToken)
    {
        try
        {
            if (credentials?.AccessToken is null || IsTokenExpired(credentials))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Get, UsagePath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var parsed = ParseResponse(content, credentials.SubscriptionType, credentials.RateLimitTier);
                LastError = parsed is null ? "unreadable response" : null;
                return parsed;
            }

            LastError = $"HTTP {(int)response.StatusCode}";
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name;
            return null;
        }
    }

    /// <summary>
    /// Runs <c>claude /status</c> which internally triggers Claude Code's
    /// OAuth token refresh.  After the command returns the credentials file
    /// is expected to contain a fresh token.
    /// </summary>
    private async Task TryDelegatedRefreshAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _refreshBlockedUntil)
        {
            return;
        }

        try
        {
            var claudePath = FindClaudeCli();
            if (claudePath is null)
            {
                _refreshBlockedUntil = DateTimeOffset.UtcNow + RefreshCooldownFailure;
                return;
            }

            // 함정: npm 설치본은 claude.cmd 다 — 배치 파일은 cmd.exe 를 거쳐야 뜨고, 입력을 닫아 주지 않으면 대화형으로 매달린다
            var isBatch = claudePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || claudePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = isBatch ? "cmd.exe" : claudePath,
                Arguments = isBatch ? $"/c \"\"{claudePath}\" /status\"" : "/status",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Propagate the profile config dir so Claude Code uses the right credentials.
            if (_configDir is not null)
            {
                process.StartInfo.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = _configDir;
            }

            process.Start();
            process.StandardInput.Close();

            // 왜: node 로 뜨는 CLI 는 부팅 직후 5초를 넘긴다(실측 평소 2초) — 넉넉히 기다려야 갱신 전에 죽이지 않는다
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(20));

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out — kill the process but don't propagate
                try { process.Kill(); } catch { /* best effort */ }
            }

            _refreshBlockedUntil = DateTimeOffset.UtcNow + RefreshCooldownSuccess;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _refreshBlockedUntil = DateTimeOffset.UtcNow + RefreshCooldownFailure;
        }
    }

    private static string? FindClaudeCli()
    {
        // Check well-known locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".local", "bin", "claude.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Fall back to PATH
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = "claude",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            process.Start();
            var lines = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            process.WaitForExit(2000);
            // 함정: where 의 첫 줄은 확장자 없는 sh 스크립트(npm\claude)일 수 있다 — Windows 가 실행할 수 있는 .exe · .cmd 만 고른다
            var runnable = lines.FirstOrDefault(l => l.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(l))
                ?? lines.FirstOrDefault(l => l.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) && File.Exists(l));
            if (runnable is not null)
            {
                return runnable;
            }
        }
        catch
        {
            // Ignore — CLI not available
        }

        return null;
    }

    //  Backoff
    private static TimeSpan ComputeBackoff(int failures)
    {
        var multiplier = Math.Pow(2, Math.Max(failures - 1, 0));
        var seconds = BackoffBase.TotalSeconds * multiplier;
        return seconds >= BackoffCap.TotalSeconds
            ? BackoffCap
            : TimeSpan.FromSeconds(seconds);
    }

    //  Two-tier cache (memory + disk)
    private void SetMemoryCache(ClaudeOAuthUsageResult result)
    {
        _memoryCache = result;
        _memoryCacheWrittenAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns the best available cached result: memory first, then disk.
    /// The result is validated against age and quota window freshness.
    /// </summary>
    // 계약: 캐시는 받아 둔 계정과 지금 로그인 계정(이메일)이 같을 때만 쓴다
    private ClaudeOAuthUsageResult? GetCachedResult(string? account)
    {
        // Try memory cache (30 min TTL)
        if (_memoryCache is not null
            && DateTimeOffset.UtcNow - _memoryCacheWrittenAt <= MemoryCacheTtl
            && IsCacheValid(_memoryCache, account))
        {
            return _memoryCache;
        }

        // Try disk cache
        var fromDisk = ReadDiskCache();
        if (fromDisk is not null && IsCacheValid(fromDisk, account))
        {
            SetMemoryCache(fromDisk);
            return fromDisk;
        }

        return null;
    }

    private static bool IsCacheValid(ClaudeOAuthUsageResult cached, string? account)
    {
        if (account is null || !string.Equals(cached.AccountEmail, account, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        if (now - cached.FetchedAt > MaxCacheAge)
        {
            return false;
        }

        var sessionExpired = cached.FiveHourResetsAt.HasValue && now > cached.FiveHourResetsAt.Value;
        var weekExpired = cached.SevenDayResetsAt.HasValue && now > cached.SevenDayResetsAt.Value;
        return !(sessionExpired && weekExpired);
    }

    // Disk cache I/O
    private string GetDiskCachePath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profileSuffix = _configDir is not null
            ? "_" + Path.GetFileName(_configDir)
            : "";
        return Path.Combine(basePath, "AiUsageMonitor", "cache", $"claude-oauth{profileSuffix}.json");
    }

    private async Task WriteDiskCacheAsync(ClaudeOAuthUsageResult result)
    {
        try
        {
            var path = GetDiskCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(result, DiskCacheJsonOptions);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }
        catch
        {
            // Non-critical — memory cache still works
        }
    }

    private ClaudeOAuthUsageResult? ReadDiskCache()
    {
        try
        {
            var path = GetDiskCachePath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ClaudeOAuthUsageResult>(json, DiskCacheJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions DiskCacheJsonOptions = new(JsonSerializerDefaults.Web);

    //  Credential helpers
    // 계약: 기본 로그인은 ~/.claude.json, 추가 계정은 <CLAUDE_CONFIG_DIR>/.claude.json 의 oauthAccount.emailAddress
    private static string? ReadAccountEmail(string? configDir)
    {
        var path = configDir is not null
            ? Path.Combine(configDir, ".claude.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("oauthAccount", out var oa) && oa.TryGetProperty("emailAddress", out var e)
                ? e.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ComputeFingerprint(ClaudeCredentials? credentials)
    {
        if (credentials?.AccessToken is null)
        {
            return null;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(credentials.AccessToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 8);
    }

    private static bool IsTokenExpired(ClaudeCredentials credentials)
    {
        return credentials.ExpiresAt.HasValue
            && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > credentials.ExpiresAt.Value;
    }

    private static async Task<ClaudeCredentials?> LoadCredentialsAsync(string? configDir)
    {
        string credentialsPath;
        if (configDir is not null)
        {
            credentialsPath = Path.Combine(configDir, ".credentials.json");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            credentialsPath = Path.Combine(home, ".claude", ".credentials.json");
        }

        if (!File.Exists(credentialsPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(credentialsPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                return null;
            }

            return new ClaudeCredentials(
                oauth.TryGetProperty("accessToken", out var at) ? at.GetString() : null,
                oauth.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null,
                oauth.TryGetProperty("expiresAt", out var exp) ? exp.GetInt64() : null,
                oauth.TryGetProperty("subscriptionType", out var st) ? st.GetString() : null,
                oauth.TryGetProperty("rateLimitTier", out var rlt) ? rlt.GetString() : null);
        }
        catch
        {
            return null;
        }
    }

    //  Response parsing
    private static ClaudeOAuthUsageResult? ParseResponse(string json, string? subscriptionType, string? rateLimitTier)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            double? fiveHourPercent = null;
            DateTimeOffset? fiveHourResetsAt = null;
            double? sevenDayPercent = null;
            DateTimeOffset? sevenDayResetsAt = null;

            if (root.TryGetProperty("five_hour", out var fiveHour))
            {
                if (fiveHour.TryGetProperty("utilization", out var util))
                {
                    fiveHourPercent = util.ValueKind == JsonValueKind.Number ? util.GetDouble() : null;
                }
                if (fiveHour.TryGetProperty("resets_at", out var resets) && resets.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(resets.GetString(), out var resetsTime))
                    {
                        fiveHourResetsAt = resetsTime;
                    }
                }
            }

            if (root.TryGetProperty("seven_day", out var sevenDay))
            {
                if (sevenDay.TryGetProperty("utilization", out var util))
                {
                    sevenDayPercent = util.ValueKind == JsonValueKind.Number ? util.GetDouble() : null;
                }
                if (sevenDay.TryGetProperty("resets_at", out var resets) && resets.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(resets.GetString(), out var resetsTime))
                    {
                        sevenDayResetsAt = resetsTime;
                    }
                }
            }

            double? extraUsed = null;
            double? extraLimit = null;
            bool overageEnabled = false;
            if (root.TryGetProperty("extra_usage", out var extra))
            {
                if (extra.TryGetProperty("is_enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                {
                    overageEnabled = true;
                }
                if (extra.TryGetProperty("used_credits", out var used) && used.ValueKind == JsonValueKind.Number)
                {
                    extraUsed = used.GetDouble();
                }
                if (extra.TryGetProperty("monthly_limit", out var limit) && limit.ValueKind == JsonValueKind.Number)
                {
                    extraLimit = limit.GetDouble();
                }

                if (extraUsed.HasValue && extraLimit.HasValue)
                {
                    (extraUsed, extraLimit) = NormalizeMonetaryValues(extraUsed.Value, extraLimit.Value, subscriptionType);
                }
            }

            return new ClaudeOAuthUsageResult(
                fiveHourPercent,
                fiveHourResetsAt,
                sevenDayPercent,
                sevenDayResetsAt,
                overageEnabled,
                extraUsed,
                extraLimit,
                subscriptionType,
                rateLimitTier,
                DateTimeOffset.UtcNow)
            {
                ModelWeeks = ParseModelWeeks(root)
            };
        }
        catch
        {
            return null;
        }
    }

    // 왜: 모델별 주간 한도는 "seven_day_<모델>" 로 온다 — 새 모델(Fable 등)이 생겨도 코드를 고치지 않도록 이름을 박지 않고 훑는다
    // 함정: seven_day_oauth_apps 는 모델이 아니라 앱 연동 한도라 뺀다
    private static IReadOnlyList<costats.Core.Pulse.ModelQuota> ParseModelWeeks(JsonElement root)
    {
        const string prefix = "seven_day_";
        var result = new List<costats.Core.Pulse.ModelQuota>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        // 계약: 실측(2026-10)한 응답은 limits[] 에 kind="weekly_scoped" 로 모델별 한도를 싣고, 이름은 scope.model.display_name 이다
        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var limit in limits.EnumerateArray())
            {
                if (limit.ValueKind != JsonValueKind.Object ||
                    !limit.TryGetProperty("kind", out var kind) || kind.GetString() != "weekly_scoped" ||
                    !limit.TryGetProperty("percent", out var percent) || percent.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                string? label = null;
                if (limit.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object)
                {
                    if (scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object &&
                        model.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String)
                    {
                        label = display.GetString();
                    }
                    else if (scope.TryGetProperty("surface", out var surface) && surface.ValueKind == JsonValueKind.String)
                    {
                        label = surface.GetString();
                    }
                }

                DateTimeOffset? scopedResetsAt = null;
                if (limit.TryGetProperty("resets_at", out var scopedResets) &&
                    scopedResets.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(scopedResets.GetString(), out var scopedParsed))
                {
                    scopedResetsAt = scopedParsed;
                }

                result.Add(new costats.Core.Pulse.ModelQuota(label ?? "Scoped", percent.GetDouble(), scopedResetsAt));
            }

            if (result.Count > 0)
            {
                return result;
            }
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.StartsWith(prefix, StringComparison.Ordinal) ||
                property.Name == "seven_day_oauth_apps" ||
                property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty("utilization", out var util) ||
                util.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            DateTimeOffset? resetsAt = null;
            if (property.Value.TryGetProperty("resets_at", out var resets) &&
                resets.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(resets.GetString(), out var parsed))
            {
                resetsAt = parsed;
            }

            var words = property.Name[prefix.Length..].Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);
            result.Add(new costats.Core.Pulse.ModelQuota(string.Join(' ', words), util.GetDouble(), resetsAt));
        }

        return result;
    }

    private static (double used, double limit) NormalizeMonetaryValues(double rawUsed, double rawLimit, string? tier)
    {
        var usedDollars = rawUsed / 100.0;
        var limitDollars = rawLimit / 100.0;

        const double PlausibilityThreshold = 500.0;
        var isEnterpriseTier = tier?.Contains("enterprise", StringComparison.OrdinalIgnoreCase) == true;

        if (!isEnterpriseTier && limitDollars > PlausibilityThreshold)
        {
            usedDollars /= 100.0;
            limitDollars /= 100.0;
        }

        return (usedDollars, limitDollars);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record ClaudeCredentials(
        string? AccessToken,
        string? RefreshToken,
        long? ExpiresAt,
        string? SubscriptionType,
        string? RateLimitTier);
}

public sealed record ClaudeOAuthUsageResult(
    double? FiveHourUsedPercent,
    DateTimeOffset? FiveHourResetsAt,
    double? SevenDayUsedPercent,
    DateTimeOffset? SevenDayResetsAt,
    bool OverageEnabled,
    double? OverageSpentUsd,
    double? OverageCeilingUsd,
    string? SubscriptionType,
    string? RateLimitTier,
    DateTimeOffset FetchedAt)
{
    public IReadOnlyList<costats.Core.Pulse.ModelQuota> ModelWeeks { get; init; } = [];

    public string? AccountEmail { get; init; }
}
