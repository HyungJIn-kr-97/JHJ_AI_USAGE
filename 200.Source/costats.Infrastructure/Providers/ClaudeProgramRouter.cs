using System.Text.Json;

namespace costats.Infrastructure.Providers;

/// <summary>
/// Claude 기본 폴더(~/.claude)의 대화 기록을 계정(providerId)에 나눈다.
/// 왜: 로그 줄에는 계정이 없다 — 데스크톱 앱 세션은 앱이 남긴 세션 메타(claude-code-sessions\&lt;계정UUID&gt;\…)로 세션마다 주인을 정하고,
///     메타가 없는 줄(VS Code · CLI · 메타 없는 데스크톱 세션)만 「프로그램 → 계정」 연동표를 따른다.
/// 계약: 기록 단위(source)는 "claude-desktop@&lt;계정UUID&gt;" · "local-agent@&lt;계정UUID&gt;" 또는 entrypoint 그대로다. 등록되지 않은 계정 UUID 의 세션은 어느 계정에도 세지 않는다.
/// 계약: 주인 판정 순서·연동 이력·자동/고정의 정본은 300.Docs/프로그램-연동-규칙.md 다.
/// </summary>
public static class ClaudeProgramRouter
{
    public const string UnknownProgram = "unknown";
    private const string DesktopProgram = "claude-desktop";

    private static readonly Dictionary<string, string> Empty = new(StringComparer.OrdinalIgnoreCase);

    private static Snapshot _current = new(false, "claude:default", Empty, Empty, Empty, Empty);

    public static bool IsActive => _current.Active;

    public static string DefaultProviderId => _current.DefaultId;

    public static string DefaultConfigDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    /// <param name="accounts">계정 UUID → providerId — 각 자리의 .claude.json 이 아는 로그인 계정</param>
    /// <param name="slotAccounts">providerId → 계정 UUID — 위의 반대 방향. 손으로 고정한 연동을 UUID 로 바꾸는 데 쓴다</param>
    /// <param name="pins">손으로 고정한 연동(AppSettings.ProgramAccountPins) — 없는 프로그램은 지금 로그인한 계정을 따른다</param>
    public static void Configure(string defaultProviderId, IReadOnlyDictionary<string, string>? map, IEnumerable<string> registeredIds,
        IReadOnlyDictionary<string, string>? accounts = null, IReadOnlyDictionary<string, string>? slotAccounts = null,
        IReadOnlyDictionary<string, string>? pins = null)
    {
        var known = new HashSet<string>(registeredIds, StringComparer.OrdinalIgnoreCase);
        var routes = (map ?? Empty)
            .Where(pair => known.Contains(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var byAccount = (accounts ?? Empty)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var bySlot = (slotAccounts ?? Empty)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var pinned = (pins ?? Empty)
            .Where(pair => known.Contains(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        // 왜: 소스는 갱신마다 병렬로 읽는다 — 표를 통째로 바꿔 끼워 읽는 쪽이 반쯤 바뀐 표를 보지 않게 한다
        Volatile.Write(ref _current, new Snapshot(true, defaultProviderId, routes, byAccount, bySlot, pinned));
        SyncLinks();
    }

    // 계약: 자리 목록으로 채운다 — 각 자리의 .claude.json 에서 로그인 계정 UUID 를 읽어 세션 메타와 잇는다
    public static void Configure(string defaultProviderId, IReadOnlyDictionary<string, string>? map,
        IEnumerable<costats.Core.Pulse.MulticcProfile> profiles, IReadOnlyDictionary<string, string>? pins = null)
    {
        var slots = profiles.Select(p => (Id: "claude:" + p.Name, Account: AccountUuidOf(p.ConfigDir))).ToList();
        var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bySlot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in slots.Where(s => s.Account is not null))
        {
            accounts.TryAdd(slot.Account!, slot.Id);
            bySlot[slot.Id] = slot.Account!;
        }

        Configure(defaultProviderId, map, slots.Select(s => s.Id), accounts, bySlot, pins);
    }

    /// <summary>설정 › 「프로그램 연동」 표에 서는 프로그램 — 세션 메타로 주인을 못 찾는 줄만 이 연동을 따른다.</summary>
    public static readonly string[] LinkedPrograms = [DesktopProgram, VsCodeProgram, CliProgram];

    private static readonly object SyncGate = new();
    private static DateTime _syncedAt = DateTime.MinValue;

    // 왜: 한 번 갱신하면 자리 수만큼 소스가 돌아 같은 일을 되풀이한다 — 갱신 주기(최소 1분)보다 짧은 간격의 재호출만 걷어낸다
    private static readonly TimeSpan SyncGap = TimeSpan.FromSeconds(30);

    /// <summary>갱신마다 불린다 — 지금 로그인한 계정을 다시 읽어 바뀌었으면 연동 이력에 한 줄 남긴다.</summary>
    public static void SyncLinksIfDue()
    {
        lock (SyncGate)
        {
            if (DateTime.UtcNow - _syncedAt < SyncGap)
            {
                return;
            }

            _syncedAt = DateTime.UtcNow;
        }

        SyncLinks();
    }

    /// <summary>
    /// 지금 이 PC 의 로그인 계정을 프로그램마다 읽어 연동 이력(ClaudeProgramLinks)에 남긴다.
    /// 계약: 손으로 고정한 줄은 그 자리의 계정을 쓰고, 나머지는 지금 로그인한 계정을 따른다.
    /// </summary>
    public static void SyncLinks()
    {
        var snapshot = Volatile.Read(ref _current);
        if (!snapshot.Active)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        // 왜: ~/.claude.json 읽기를 프로그램마다 되풀이할 필요가 없다 — 한 번 읽어 셋에 나눠 쓴다
        var home = AccountUuidOf(DefaultConfigDir);
        foreach (var program in LinkedPrograms)
        {
            var account = snapshot.Pins.TryGetValue(program, out var pinned) && snapshot.SlotAccounts.TryGetValue(pinned, out var pinnedAccount)
                ? pinnedAccount
                : program == DesktopProgram ? ClaudeDesktopSessions.NewestAccount() ?? home : home;
            ClaudeProgramLinks.Record(program, account, now);
        }
    }

    /// <summary>그 계정 UUID 를 쓰는 자리(providerId). 이 PC 에 등록되지 않은 계정이면 null.</summary>
    public static string? SlotOfAccount(string account) =>
        Volatile.Read(ref _current).Accounts.TryGetValue(account, out var id) ? id : null;

    /// <summary>지금의 연동 — 프로그램 → 자리(providerId). 이력이 없으면 옛 표, 그것도 없으면 기본 계정이다.</summary>
    public static IReadOnlyDictionary<string, string> CurrentLinks()
    {
        var snapshot = Volatile.Read(ref _current);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in LinkedPrograms)
        {
            if (ClaudeProgramLinks.CurrentAccount(program) is { } account && snapshot.Accounts.TryGetValue(account, out var id))
            {
                map[program] = id;
            }
            else
            {
                map[program] = snapshot.Routes.TryGetValue(program, out var legacy) ? legacy : snapshot.DefaultId;
            }
        }

        return map;
    }

    // 계약: at 은 그 줄의 시각 — VS Code·CLI 새 세션에 그때의 로그인 계정을 붙이는 데 쓴다(ClaudeCliSessions)
    public static string SourceOf(string? entrypoint, string? sessionId, DateTimeOffset? at = null)
    {
        var program = string.IsNullOrWhiteSpace(entrypoint) ? UnknownProgram : entrypoint.Trim();
        // 함정: 이 PC 에 자리가 없는 계정으로 로그인했던 세션은 붙여도 셀 곳이 없다 — 그때는 연동표로 떨어뜨린다
        if (program is VsCodeProgram or CliProgram && ClaudeCliSessions.AccountOf(sessionId, at) is { } cliAccount &&
            Volatile.Read(ref _current).Accounts.ContainsKey(cliAccount))
        {
            return $"{program}@{cliAccount}";
        }

        // 왜: 에이전트 모드 줄은 연동표의 프로그램이 아니다 — 계정을 모르면 「@unknown」으로 두어 어느 자리에도 세지 않는다
        if (program == AgentProgram)
        {
            return $"{AgentProgram}@{ClaudeAgentSessions.AccountOf(sessionId) ?? "unknown"}";
        }

        if (program == DesktopProgram && ClaudeDesktopSessions.AccountOf(sessionId) is { } account)
        {
            return $"{DesktopProgram}@{account}";
        }

        // 계약: 세션으로 주인을 못 찾은 줄은 그 시각의 연동 이력을 따른다 — 이력이 시작되기 전 줄만 옛 「프로그램 연동」 표로 간다
        return ClaudeProgramLinks.AccountAt(program, at) is { } linked && Volatile.Read(ref _current).Accounts.ContainsKey(linked)
            ? $"{program}@{linked}"
            : program;
    }

    private const string AgentProgram = "local-agent";
    private const string VsCodeProgram = "claude-vscode";
    private const string CliProgram = "cli";

    /// <summary>데스크톱 앱 에이전트 모드 세션의 projects 폴더 전부 — 자리마다 이 폴더들을 주인 판정과 함께 읽는다.</summary>
    public static IReadOnlyList<string> AgentProjectDirs() => ClaudeAgentSessions.ProjectDirs();

    // 계약: null 이면 이 PC 에 등록되지 않은 계정의 기록이다 — 아무 자리에도 세지 않는다
    public static string? OwnerOf(string source)
    {
        var snapshot = Volatile.Read(ref _current);
        var at = source.IndexOf('@');
        if (at > 0)
        {
            return snapshot.Accounts.TryGetValue(source[(at + 1)..], out var owner) ? owner : null;
        }

        return snapshot.Routes.TryGetValue(source, out var id) ? id : snapshot.DefaultId;
    }

    public static bool IsDefaultDir(string configDir) =>
        string.Equals(Normalize(configDir), Normalize(DefaultConfigDir), StringComparison.OrdinalIgnoreCase);

    // 계약: 그 자리의 로그인 계정 UUID — 기본 자리는 ~/.claude.json, 추가 자리는 &lt;자리&gt;\.claude.json
    public static string? AccountUuidOf(string configDir) =>
        IsDefaultDir(configDir)
            ? ClaudeHomeIdentity.Read()?.Uuid
            : ClaudeHomeIdentity.ReadFile(Path.Combine(configDir, ".claude.json"))?.Uuid;

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // 계약: 진단 보고서용 — 표를 한 줄씩 적는다. UUID 가림은 호출자(DiagnosticsReport.Mask)가 한다
    public static string Describe()
    {
        var snapshot = Volatile.Read(ref _current);
        var lines = new List<string>
        {
            $"- 활성 {snapshot.Active} · 기본 계정 {snapshot.DefaultId}",
            $"- 연동표(프로그램 → 계정): {(snapshot.Routes.Count == 0 ? "비어 있음(전부 기본 계정)" : string.Join(" · ", snapshot.Routes.Select(p => $"{p.Key} → {p.Value}")))}",
            $"- 자리별 로그인(계정 UUID → 자리): {(snapshot.Accounts.Count == 0 ? "없음" : string.Join(" · ", snapshot.Accounts.Select(p => $"{p.Key} → {p.Value}")))}",
            $"- 손으로 고정한 연동: {(snapshot.Pins.Count == 0 ? "없음(전부 자동)" : string.Join(" · ", snapshot.Pins.Select(p => $"{p.Key} → {p.Value}")))}",
            $"- 연동 이력 {ClaudeProgramLinks.All().Count}줄: {string.Join(" · ", ClaudeProgramLinks.All().TakeLast(6).Select(e => $"{e.From:yyyy-MM-dd HH:mm} {e.Program} → {e.Account}"))}",
            $"- 에이전트 모드 폴더 {AgentProjectDirs().Count}개"
        };
        return string.Join(Environment.NewLine, lines);
    }

    /// <param name="Routes">옛 「프로그램 연동」 표 — 연동 이력이 시작되기 전 줄에만 쓴다</param>
    /// <param name="Accounts">계정 UUID → 자리(providerId)</param>
    /// <param name="SlotAccounts">자리(providerId) → 계정 UUID</param>
    /// <param name="Pins">손으로 고정한 연동 — 프로그램 → 자리(providerId)</param>
    private sealed record Snapshot(bool Active, string DefaultId, IReadOnlyDictionary<string, string> Routes,
        IReadOnlyDictionary<string, string> Accounts, IReadOnlyDictionary<string, string> SlotAccounts,
        IReadOnlyDictionary<string, string> Pins);
}

/// <summary>
/// 데스크톱 앱이 Code 탭 세션마다 남기는 메타 — %APPDATA%\Claude\claude-code-sessions\&lt;계정UUID&gt;\&lt;조직UUID&gt;\{local,deleted}_*.json 의 cliSessionId.
/// </summary>
internal static class ClaudeDesktopSessions
{
    // 함정: 스토어(MSIX) 설치본은 %APPDATA% 쓰기가 Packages\Claude_*\LocalCache\Roaming 으로 돌려진다 — 패키지 밖에서는 %APPDATA%\Claude 에 안 보인다
    internal static IEnumerable<string> Roots(string folder = "claude-code-sessions")
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", folder);
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var package in Directory.EnumerateDirectories(packages, "Claude_*"))
            {
                yield return Path.Combine(package, "LocalCache", "Roaming", "Claude", folder);
            }
        }
    }

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private static (DateTime BuiltAt, IReadOnlyDictionary<string, string> Map, string? Newest) _cache =
        (DateTime.MinValue, new Dictionary<string, string>(), null);

    public static string? AccountOf(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId) && Current().Map.TryGetValue(sessionId, out var account) ? account : null;

    /// <summary>데스크톱 앱이 가장 최근에 세션 메타를 쓴 계정 UUID — 「지금 데스크톱 앱이 쓰는 계정」으로 본다.</summary>
    public static string? NewestAccount() => Current().Newest;

    private static (DateTime BuiltAt, IReadOnlyDictionary<string, string> Map, string? Newest) Current()
    {
        var cache = _cache;
        if (DateTime.UtcNow - cache.BuiltAt > Ttl)
        {
            var (map, newest) = Build();
            cache = (DateTime.UtcNow, map, newest);
            _cache = cache;
        }

        return cache;
    }

    private static (IReadOnlyDictionary<string, string> Map, string? Newest) Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? newest = null;
        var newestAt = DateTime.MinValue;
        foreach (var accountDir in Roots().Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories))
        {
            var account = Path.GetFileName(accountDir);
            foreach (var file in Directory.EnumerateFiles(accountDir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith("local_", StringComparison.Ordinal) && !name.StartsWith("deleted_", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    // 왜: 메타를 마지막으로 쓴 계정이 지금 데스크톱 앱에 로그인한 계정이다 — 홈 .claude.json 은 CLI 와 섞여 믿을 수 없다
                    var writtenAt = File.GetLastWriteTimeUtc(file);
                    if (writtenAt > newestAt)
                    {
                        (newest, newestAt) = (account, writtenAt);
                    }

                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("cliSessionId", out var id) && id.GetString() is { Length: > 0 } cliSessionId)
                    {
                        map[cliSessionId] = account;
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    // 쓰는 중이거나 깨진 메타는 건너뛴다 — 그 세션은 프로그램 연동을 따른다
                }
            }
        }

        return (map, newest);
    }
}

/// <summary>
/// VS Code·CLI 세션의 주인 — 둘 다 ~/.claude 를 쓰고 로그 줄에 계정이 없어, 앱이 처음 본 순간의 ~/.claude.json 로그인 계정을 세션에 붙여 둔다.
/// 계약: 기능을 켠 시각(StartedAt) 이후의 줄만 붙인다 — 그 전 세션은 「프로그램 연동」 표를 따른다. 저장은 %LOCALAPPDATA%\JHJ_AI-Usage-Monitor\claude-cli-sessions.json.
/// 함정: 붙이는 때는 갱신 주기(5분)만큼 늦다 — 그사이 다른 계정으로 다시 로그인하면 새 계정으로 붙는다.
/// </summary>
internal static class ClaudeCliSessions
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JHJ_AI-Usage-Monitor", "claude-cli-sessions.json");

    private static readonly object Gate = new();
    private static Store? _store;
    private static (DateTime ReadAt, string? Uuid) _login = (DateTime.MinValue, null);

    public static string? AccountOf(string? sessionId, DateTimeOffset? at)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        lock (Gate)
        {
            var store = _store ??= Load();
            if (store.Sessions.TryGetValue(sessionId, out var known))
            {
                return known;
            }

            if (at is null || at < store.StartedAt || CurrentLogin() is not { } uuid)
            {
                return null;
            }

            store.Sessions[sessionId] = uuid;
            Save(store);
            return uuid;
        }
    }

    private static string? CurrentLogin()
    {
        if (DateTime.UtcNow - _login.ReadAt > TimeSpan.FromMinutes(1))
        {
            _login = (DateTime.UtcNow, ClaudeProgramRouter.AccountUuidOf(ClaudeProgramRouter.DefaultConfigDir));
        }

        return _login.Uuid;
    }

    private static Store Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) is { } loaded)
            {
                loaded.Sessions = new Dictionary<string, string>(loaded.Sessions, StringComparer.OrdinalIgnoreCase);
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 깨진 파일은 새로 시작한다 — 시작 시각이 지금이 되어 지난 세션은 연동표를 따른다
        }

        var created = new Store { StartedAt = DateTimeOffset.UtcNow };
        Save(created);
        return created;
    }

    private static void Save(Store store)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(store));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 실패는 집계를 막지 않는다 — 다음에 그 세션을 다시 붙인다
        }
    }

    private sealed class Store
    {
        public DateTimeOffset StartedAt { get; set; }

        public Dictionary<string, string> Sessions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 데스크톱 앱 에이전트 모드 세션 — local-agent-mode-sessions\&lt;계정UUID&gt;\&lt;조직UUID&gt;\local_*\.claude\projects\&lt;프로젝트&gt;\&lt;세션ID&gt;.jsonl.
/// 계약: 세션의 주인은 경로의 계정UUID 다 — 로그 파일 이름이 곧 줄의 sessionId 다.
/// </summary>
internal static class ClaudeAgentSessions
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);
    private static (DateTime BuiltAt, IReadOnlyList<string> Dirs, IReadOnlyDictionary<string, string> Map) _cache =
        (DateTime.MinValue, [], new Dictionary<string, string>());

    public static IReadOnlyList<string> ProjectDirs() => Current().Dirs;

    public static string? AccountOf(string? sessionId) =>
        !string.IsNullOrEmpty(sessionId) && Current().Map.TryGetValue(sessionId, out var account) ? account : null;

    private static (DateTime BuiltAt, IReadOnlyList<string> Dirs, IReadOnlyDictionary<string, string> Map) Current()
    {
        var cache = _cache;
        if (DateTime.UtcNow - cache.BuiltAt > Ttl)
        {
            cache = Build();
            _cache = cache;
        }

        return cache;
    }

    private static (DateTime, IReadOnlyList<string>, IReadOnlyDictionary<string, string>) Build()
    {
        var dirs = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var accountDir in ClaudeDesktopSessions.Roots("local-agent-mode-sessions").Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories))
            {
                var account = Path.GetFileName(accountDir);
                foreach (var session in Directory.EnumerateDirectories(accountDir).SelectMany(org => Directory.EnumerateDirectories(org, "local_*")))
                {
                    var projects = Path.Combine(session, ".claude", "projects");
                    if (!Directory.Exists(projects))
                    {
                        continue;
                    }

                    dirs.Add(projects);
                    foreach (var file in Directory.EnumerateDirectories(projects).SelectMany(p => Directory.EnumerateFiles(p, "*.jsonl")))
                    {
                        map[Path.GetFileNameWithoutExtension(file)] = account;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 앱이 세션 폴더를 만들거나 지우는 중이면 다음 갱신에 다시 센다
        }

        return (DateTime.UtcNow, dirs, map);
    }
}

/// <summary>
/// 계약: Owner 가 null 이면 그 폴더의 줄을 모두 센다. 값이 있으면 그 계정이 주인인 줄만 세고 slice 에 Program(기록 단위)을 단다.
/// </summary>
public readonly record struct ClaudeLogRoot(string ProjectsDir, string? Owner);
