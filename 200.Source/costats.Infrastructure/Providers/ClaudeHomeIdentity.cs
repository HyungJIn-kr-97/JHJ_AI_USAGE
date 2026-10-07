using System.Diagnostics;
using System.Text.Json;

namespace costats.Infrastructure.Providers;

/// <summary>
/// 기본 자리(~/.claude)의 로그인 신원 — 이메일 · 조직 · 계정 UUID.
/// 왜: ~/.claude.json 의 oauthAccount 는 데스크톱 앱의 모든 Code 세션과 CLI 가 함께 쓴다 — 다른 계정으로 연 세션이 마지막에 쓰면
///     토큰(.credentials.json)은 그대로인데 기본 자리 이름만 그 계정으로 바뀐다(2026-10-07 실측).
/// 계약: 홈 파일의 UUID 가 추가 자리(accounts\&lt;이름&gt;\.claude.json) 중 하나와 같으면 빌린 값으로 본다 — 토큰 파일이 그 뒤에 바뀌지 않았으면
///     마지막으로 확인한 신원(identity\default.json)을 쓰고, 토큰이 바뀌었으면 진짜 재로그인으로 보고 홈 파일을 믿는다.
/// </summary>
public static class ClaudeHomeIdentity
{
    public sealed record Identity(string? Email, string? Organization, string? Uuid);

    private sealed record Pin(string? Email, string? Organization, string? Uuid, DateTime CredentialsWriteUtc);

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor");

    public static string HomeFile => Path.Combine(Home, ".claude.json");

    private static string CredentialsFile => Path.Combine(Home, ".claude", ".credentials.json");

    // 함정: AccountProfileStore.RootDir(App 프로젝트)와 같은 경로여야 한다 — 여기서는 참조할 수 없어 값을 맞춰 둔다
    private static string AccountsRoot => Path.Combine(DataDir, "accounts");

    private static string PinPath => Path.Combine(DataDir, "identity", "default.json");

    private static readonly object Gate = new();

    /// <summary>기본 자리의 신원. 파일이 없거나 로그인 정보가 없으면 null.</summary>
    public static Identity? Read()
    {
        var live = ReadFile(HomeFile);
        lock (Gate)
        {
            var credentialsWrite = CredentialsWriteUtc();
            if (live?.Uuid is null)
            {
                return live ?? ReadPin()?.ToIdentity();
            }

            var extra = ExtraSlotUuids().ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (extra.Contains(live.Uuid))
            {
                var pin = ReadPin();
                if (pin is not null && pin.CredentialsWriteUtc >= credentialsWrite && !string.Equals(pin.Uuid, live.Uuid, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine($"[costats-identity] home .claude.json carries another slot's account ({live.Uuid[..Math.Min(8, live.Uuid.Length)]}…); keeping pinned identity");
                    return pin.ToIdentity();
                }

                // 왜: 핀이 아직 없을 때의 2차 근거 — CLI 가 ~/.claude 안에 남긴 .claude.json 은 세션이 덮어쓰지 않는다
                var inner = ReadFile(Path.Combine(Home, ".claude", ".claude.json"));
                if (pin is null && inner?.Uuid is { Length: > 0 } && !extra.Contains(inner.Uuid))
                {
                    WritePin(new Pin(inner.Email, inner.Organization, inner.Uuid, credentialsWrite));
                    return inner;
                }
            }

            WritePin(new Pin(live.Email, live.Organization, live.Uuid, credentialsWrite));
            return live;
        }
    }

    /// <summary>한 .claude.json 의 oauthAccount 만 읽는다. 토큰은 읽지 않는다.</summary>
    public static Identity? ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // 함정: ~/.claude.json 은 대소문자만 다른 키가 섞여 있어 사전 변환이 실패한다 — JsonDocument 로 필요한 칸만 읽는다
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("oauthAccount", out var account) || account.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new Identity(GetString(account, "emailAddress"), GetString(account, "organizationName"), GetString(account, "accountUuid"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ExtraSlotUuids()
    {
        if (!Directory.Exists(AccountsRoot))
        {
            yield break;
        }

        foreach (var dir in Directory.EnumerateDirectories(AccountsRoot))
        {
            if (ReadFile(Path.Combine(dir, ".claude.json"))?.Uuid is { Length: > 0 } uuid)
            {
                yield return uuid;
            }
        }
    }

    private static DateTime CredentialsWriteUtc()
    {
        try
        {
            return File.Exists(CredentialsFile) ? File.GetLastWriteTimeUtc(CredentialsFile) : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
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
            Directory.CreateDirectory(Path.GetDirectoryName(PinPath)!);
            File.WriteAllText(PinPath, JsonSerializer.Serialize(pin));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[costats-identity] pin write failed: {ex.Message}");
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Identity ToIdentity(this Pin pin) => new(pin.Email, pin.Organization, pin.Uuid);
}
