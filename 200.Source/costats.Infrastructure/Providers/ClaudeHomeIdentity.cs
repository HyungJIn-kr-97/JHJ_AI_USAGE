using System.Diagnostics;
using System.Text.Json;

namespace costats.Infrastructure.Providers;

/// <summary>
/// 기본 자리(~/.claude)의 로그인 신원 — 이메일 · 조직 · 계정 UUID.
/// 왜: ~/.claude.json 의 oauthAccount 는 데스크톱 앱의 모든 Code 세션과 CLI 가 함께 쓴다 — 다른 계정으로 연 세션이 마지막에 쓰면
///     토큰(.credentials.json)은 그대로인데 기본 자리 이름만 그 계정으로 바뀐다(2026-10-07 실측).
/// 계약: 토큰 파일의 등급(rateLimitTier · subscriptionType)과 홈 파일의 등급(organizationRateLimitTier · organizationType)이 다르면
///     빌린 값으로 본다 — 그때는 등급이 맞았을 때 적어 둔 신원(identity\default.json) → ~/.claude/.claude.json → 홈 값 순으로 쓴다.
/// 함정: 토큰 파일의 수정 시각은 근거로 쓰지 않는다 — 토큰 갱신 때도 다시 쓰여 재로그인과 구별되지 않는다(2026-10-08 실측).
/// 함정: 두 계정의 등급이 같으면(예: 둘 다 Max 20x) 빌린 값을 가려내지 못한다.
/// </summary>
public static class ClaudeHomeIdentity
{
    public sealed record Identity(string? Email, string? Organization, string? Uuid);

    private sealed record Pin(string? Email, string? Organization, string? Uuid, string? Tier);

    private sealed record Account(Identity Identity, string? OrgTier, string? UserTier, string? OrgType);

    private sealed record TokenPlan(string? Tier, string? Subscription)
    {
        public string? Key => Tier ?? Subscription;
    }

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor");

    public static string HomeFile => Path.Combine(Home, ".claude.json");

    private static string CredentialsFile => Path.Combine(Home, ".claude", ".credentials.json");

    // 왜: CLI 가 ~/.claude 안에 남긴 .claude.json 은 데스크톱 세션이 덮어쓰지 않는다
    private static string InnerFile => Path.Combine(Home, ".claude", ".claude.json");

    private static string PinPath => Path.Combine(DataDir, "identity", "default.json");

    private static readonly object Gate = new();

    /// <summary>기본 자리의 신원. 파일이 없거나 로그인 정보가 없으면 null.</summary>
    public static Identity? Read()
    {
        var live = ReadAccount(HomeFile);
        var token = ReadTokenPlan();
        lock (Gate)
        {
            if (live?.Identity.Uuid is not { Length: > 0 } liveUuid)
            {
                return live?.Identity ?? PinFor(token)?.ToIdentity() ?? InnerFor(token, exceptUuid: null)?.Identity;
            }

            switch (Matches(live, token))
            {
                case true:
                    WritePin(new Pin(live.Identity.Email, live.Identity.Organization, liveUuid, token!.Key));
                    return live.Identity;
                case null:
                    // 왜: 어느 한쪽에 등급 칸이 없으면 판정할 수 없다 — 핀을 건드리지 않고 홈 값을 그대로 쓴다
                    return live.Identity;
            }

            if (PinFor(token) is { } pin && !SameUuid(pin.Uuid, liveUuid))
            {
                Trace.WriteLine($"[costats-identity] home .claude.json tier differs from token tier ({Short(liveUuid)}…); using pinned identity");
                return pin.ToIdentity();
            }

            if (InnerFor(token, exceptUuid: liveUuid) is { } inner)
            {
                Trace.WriteLine($"[costats-identity] home .claude.json tier differs from token tier ({Short(liveUuid)}…); using ~/.claude/.claude.json");
                WritePin(new Pin(inner.Identity.Email, inner.Identity.Organization, inner.Identity.Uuid, token!.Key));
                return inner.Identity;
            }

            return live.Identity;
        }
    }

    /// <summary>한 .claude.json 의 oauthAccount 만 읽는다. 토큰은 읽지 않는다.</summary>
    public static Identity? ReadFile(string path) => ReadAccount(path)?.Identity;

    private static Account? ReadAccount(string path)
    {
        // 함정: ~/.claude.json 은 대소문자만 다른 키가 섞여 있어 사전 변환이 실패한다 — JsonDocument 로 필요한 칸만 읽는다
        using var doc = Open(path);
        if (doc is null || !doc.RootElement.TryGetProperty("oauthAccount", out var account) || account.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new Account(
            new Identity(GetString(account, "emailAddress"), GetString(account, "organizationName"), GetString(account, "accountUuid")),
            GetString(account, "organizationRateLimitTier"),
            GetString(account, "userRateLimitTier"),
            GetString(account, "organizationType"));
    }

    /// <summary>토큰 파일에서 등급 칸만 읽는다 — 토큰 값은 읽지 않는다.</summary>
    private static TokenPlan? ReadTokenPlan()
    {
        using var doc = Open(CredentialsFile);
        if (doc is null || !doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var plan = new TokenPlan(GetString(oauth, "rateLimitTier"), GetString(oauth, "subscriptionType"));
        return plan.Key is null ? null : plan;
    }

    /// <summary>로그인 파일과 토큰의 등급이 같은가 — 비교할 칸이 없으면 null.</summary>
    private static bool? Matches(Account account, TokenPlan? token)
    {
        if (token is null)
        {
            return null;
        }

        if (token.Tier is not null && (account.OrgTier ?? account.UserTier) is not null)
        {
            return Same(token.Tier, account.OrgTier) || Same(token.Tier, account.UserTier);
        }

        if (token.Subscription is not null && account.OrgType is not null)
        {
            // 계약: organizationType 은 「claude_team」, subscriptionType 은 「team」 꼴이다
            var type = account.OrgType.StartsWith("claude_", StringComparison.OrdinalIgnoreCase) ? account.OrgType[7..] : account.OrgType;
            return Same(token.Subscription, type);
        }

        return null;
    }

    private static Pin? PinFor(TokenPlan? token)
    {
        var pin = ReadPin();
        return pin?.Uuid is { Length: > 0 } && token?.Key is not null && Same(pin.Tier, token.Key) ? pin : null;
    }

    private static Account? InnerFor(TokenPlan? token, string? exceptUuid)
    {
        var inner = ReadAccount(InnerFile);
        if (inner?.Identity.Uuid is not { Length: > 0 } uuid || SameUuid(uuid, exceptUuid))
        {
            return null;
        }

        return Matches(inner, token) == false ? null : inner;
    }

    private static JsonDocument? Open(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Pin? ReadPin()
    {
        try
        {
            return File.Exists(PinPath) ? JsonSerializer.Deserialize<Pin>(File.ReadAllText(PinPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WritePin(Pin pin)
    {
        try
        {
            // 왜: Read() 는 갱신 때마다 불린다 — 같은 내용이면 디스크에 다시 쓰지 않는다
            if (ReadPin() == pin)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PinPath)!);
            File.WriteAllText(PinPath, JsonSerializer.Serialize(pin));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[costats-identity] pin write failed: {ex.Message}");
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static bool Same(string? a, string? b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool SameUuid(string? a, string? b) => Same(a, b);

    private static string Short(string uuid) => uuid[..Math.Min(8, uuid.Length)];

    private static Identity ToIdentity(this Pin pin) => new(pin.Email, pin.Organization, pin.Uuid);
}
