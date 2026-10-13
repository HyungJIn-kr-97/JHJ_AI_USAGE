using System.Text.Json;

namespace costats.Infrastructure.Providers;

/// <summary>
/// Claude 데스크톱 앱이 스스로 남기는 한도 사용률 — %APPDATA%\Claude\plan-usage-history.json 의
/// samples[{ t: 밀리초, org: 조직 UUID, u: { fh: 5시간 %, sd: 7일 % } }].
/// 계약: 토큰 조회(CLI 출처)가 실패했을 때만 쓰는 두 번째 출처다 — 토큰·쿠키는 읽지 않고 이 파일과 계정 UUID 만 본다(JHJ 규약 §4 「계정 정보는 세 출처」).
/// 함정: 표본은 조직별이고 계정 칸이 없다 — 한 조직에 계정이 여럿이면 누구 값인지 못 가려, 지금 앱에 로그인한 계정의 자리에만 쓴다.
/// 함정: 표본은 앱이 값을 받았을 때만 쌓여 간격이 5분~며칠이다 — 초기화 시각도 없어 창 길이보다 훨씬 짧은 동안만 믿는다.
/// </summary>
public static class ClaudeDesktopUsage
{
    public sealed record Sample(double? FiveHour, double? SevenDay, DateTimeOffset At);

    private const string FileName = "plan-usage-history.json";

    // 왜: 5시간 창은 그 안에 한 번은 초기화된다 — 창 길이만큼 묵은 값은 이미 다른 창의 값이다
    private static readonly TimeSpan FiveHourTrust = TimeSpan.FromHours(5);

    // 왜: 7일 값은 천천히 오르지만 며칠 묵으면 주 초기화를 넘길 수 있다 — 하루까지만 보인다
    private static readonly TimeSpan SevenDayTrust = TimeSpan.FromDays(1);

    /// <summary>지금 데스크톱 앱에 로그인한 계정 UUID — 「App」 연동이 이 계정을 자리에 잇는다(ClaudeDesktopSessions 는 internal).</summary>
    public static string? CurrentAccount() => ClaudeDesktopSessions.CurrentAccount();

    /// <summary>그 자리(설정 폴더)의 계정이 지금 데스크톱 앱에 로그인한 계정일 때, 그 조직의 가장 최근 표본.</summary>
    public static Sample? LatestFor(string configDir, DateTimeOffset now)
    {
        var identity = ClaudeProgramRouter.IsDefaultDir(configDir)
            ? ClaudeHomeIdentity.Read()
            : ClaudeHomeIdentity.ReadFile(Path.Combine(configDir, ".claude.json"));
        if (identity?.OrganizationUuid is not { Length: > 0 } org || identity.Uuid is not { Length: > 0 } account ||
            !string.Equals(account, ClaudeDesktopSessions.CurrentAccount(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Trusted(Latest(org), now);
    }

    /// <param name="IsCurrent">지금 앱에 로그인된 계정인가 — 아니면 로그인한 적만 있다</param>
    /// <param name="Latest">그 조직의 가장 늦은 표본 — 지금 로그인된 계정일 때만 싣는다(묵은 정도는 가리지 않는다)</param>
    public sealed record AppLogin(bool IsCurrent, Sample? Latest);

    /// <summary>데스크톱 앱이 그 계정을 아는가 — 세션 폴더(claude-code-sessions\&lt;계정UUID&gt;)가 있거나 지금 로그인된 계정이면 안다.</summary>
    public static AppLogin? LoginOf(string? account, string? org)
    {
        // 왜: 계정 값을 폴더 이름으로 잇는다 — UUID 꼴이 아니면 경로로 쓰지 않는다
        if (!Guid.TryParse(account, out _))
        {
            return null;
        }

        var current = string.Equals(account, ClaudeDesktopSessions.CurrentAccount(), StringComparison.OrdinalIgnoreCase);
        if (!current && !ClaudeDesktopSessions.Roots().Any(root => Directory.Exists(Path.Combine(root, account!))))
        {
            return null;
        }

        return new AppLogin(current, current && org is { Length: > 0 } ? Latest(org) : null);
    }

    /// <summary>모든 설치 자리(일반 · 스토어)의 기록에서 그 조직의 가장 늦은 표본 — 묵은 정도는 가리지 않는다.</summary>
    public static Sample? Latest(string org)
    {
        Sample? latest = null;
        foreach (var path in ClaudeDesktopSessions.Roots(FileName))
        {
            foreach (var sample in Read(path, org))
            {
                if (latest is null || sample.At > latest.At)
                {
                    latest = sample;
                }
            }
        }

        return latest;
    }

    private static Sample? Trusted(Sample? sample, DateTimeOffset now)
    {
        if (sample is null)
        {
            return null;
        }

        var age = now - sample.At;
        var trusted = sample with
        {
            FiveHour = age <= FiveHourTrust ? sample.FiveHour : null,
            SevenDay = age <= SevenDayTrust ? sample.SevenDay : null
        };
        return trusted.FiveHour is null && trusted.SevenDay is null ? null : trusted;
    }

    private static IEnumerable<Sample> Read(string path, string org)
    {
        var found = new List<Sample>();
        try
        {
            if (!File.Exists(path))
            {
                return found;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
            {
                return found;
            }

            foreach (var item in samples.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("org", out var itemOrg) || itemOrg.ValueKind != JsonValueKind.String ||
                    !string.Equals(itemOrg.GetString(), org, StringComparison.OrdinalIgnoreCase) ||
                    !item.TryGetProperty("t", out var at) || !at.TryGetInt64(out var millis) ||
                    !item.TryGetProperty("u", out var usage) || usage.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                found.Add(new Sample(Percent(usage, "fh"), Percent(usage, "sd"), DateTimeOffset.FromUnixTimeMilliseconds(millis)));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // 계약: 앱이 쓰는 중이거나 형식이 바뀌었으면 이 출처는 없는 것으로 친다
        }

        return found;
    }

    private static double? Percent(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var percent)
            ? Math.Clamp(percent, 0, 100)
            : null;

    /// <summary>상태 줄 — 「Claude Desktop app values, 25m old」. 문구 번역은 LocStrings 의 같은 꼴 정규식이 맡는다.</summary>
    public static string Describe(Sample sample, DateTimeOffset now)
    {
        var age = now - sample.At;
        var text = age.TotalHours >= 1 ? $"{(int)age.TotalHours}h" : $"{Math.Max(1, (int)age.TotalMinutes)}m";
        return $"Claude Desktop app values, {text} old";
    }
}
