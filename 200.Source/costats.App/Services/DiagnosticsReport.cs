using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using costats.Application.Settings;
using costats.Core.Pulse;
using costats.Infrastructure.Providers;

namespace costats.App.Services;

/// <summary>
/// 진단 보고서(Markdown) — 설정 · Claude 자리(.claude.json · 자격 · 로그) · 라우팅 표 · 세션 메타 · 이력 파일 · 마지막 갱신 · 이벤트 로그.
/// 계약: 메일은 첫 글자+도메인, UUID 는 앞 8자만 남긴다. 토큰·대화 내용·경로의 사용자 이름은 넣지 않는다.
/// 함정: 사용자가 GitHub 이슈에 그대로 붙인다 — 새 항목을 더할 때는 Mask 를 거쳤는지 먼저 본다.
/// </summary>
public static class DiagnosticsReport
{
    private static readonly Regex EmailRx = new(@"[\w.+-]+@([\w-]+\.)+\w+", RegexOptions.Compiled);
    private static readonly Regex UuidRx = new(@"\b([0-9a-f]{8})-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Build(AppSettings settings, PulseState? state, string version)
    {
        var sb = new StringBuilder();
        // 계약: 첫 줄은 이슈 제목으로 잘라 붙이는 줄이다(bug_report.yml 의 "[버그] " 접두와 같다) — 그 아래가 본문
        sb.AppendLine($"[버그] JHJ_AI-Usage-Monitor v{version} — (한 줄 요약을 적어 주십시오)");
        sb.AppendLine();
        sb.AppendLine("# JHJ_AI-Usage-Monitor 진단 정보");
        sb.AppendLine();
        sb.AppendLine("## 증상 — 여기에 적어 주십시오");
        sb.AppendLine();
        sb.AppendLine("- 무엇을 하다가: ");
        sb.AppendLine("- 기대한 것: ");
        sb.AppendLine("- 실제로 보인 것: ");
        sb.AppendLine("- 재현: 항상 / 가끔 / 한 번");
        sb.AppendLine();
        sb.AppendLine("## 환경");
        sb.AppendLine();
        sb.AppendLine($"- 생성: {DateTime.Now:yyyy-MM-dd HH:mm:ss} · 앱 v{version}");
        sb.AppendLine($"- OS: {Environment.OSVersion.VersionString} · {RuntimeInformation.OSArchitecture} · .NET {Environment.Version} · {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"- 장비: {DeviceInfo.Current.Name} · 명칭 {DeviceInfo.Current.Label ?? "-"} · ID {DeviceInfo.Current.Id}");
        sb.AppendLine($"- 실행 위치: {Mask(Environment.ProcessPath ?? "?")}");
        sb.AppendLine($"- 동의: {DiagnosticsConsent.GivenText ?? "없음"}");
        sb.AppendLine();

        Section(sb, "설정", () =>
        {
            sb.AppendLine("```json");
            // 왜: 기본 인코더는 '+' 와 한글을 + · 회 로 적어 읽을 수 없다 — 사람이 읽는 보고서라 그대로 둔다
            sb.AppendLine(Mask(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
            sb.AppendLine("```");
        });

        Section(sb, "Claude 자리", () =>
        {
            DescribeSlot(sb, "기본", ClaudeProgramRouter.DefaultConfigDir);
            if (Directory.Exists(AccountProfileStore.RootDir))
            {
                foreach (var dir in Directory.GetDirectories(AccountProfileStore.RootDir))
                {
                    DescribeSlot(sb, Path.GetFileName(dir), dir);
                }
            }
        });

        Section(sb, "프로그램 라우팅 표", () => sb.AppendLine(Mask(ClaudeProgramRouter.Describe())));

        Section(sb, "세션 메타", () =>
        {
            var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");
            if (Directory.Exists(desktop))
            {
                foreach (var account in Directory.GetDirectories(desktop))
                {
                    var files = Directory.GetFiles(account, "*.json", SearchOption.AllDirectories);
                    sb.AppendLine($"- 데스크톱 {Mask(Path.GetFileName(account))}: 세션 메타 {files.Length}개");
                }
            }
            else
            {
                sb.AppendLine("- 데스크톱 세션 메타 폴더 없음");
            }

            // 왜: 에이전트 모드 폴더는 수십 개라 줄마다 적으면 보고서를 덮는다 — 합계 한 줄로 줄인다
            var agentDirs = ClaudeProgramRouter.AgentProjectDirs().ToList();
            var agentFiles = agentDirs.Where(Directory.Exists)
                .SelectMany(dir => new DirectoryInfo(dir).GetFiles("*.jsonl", SearchOption.AllDirectories))
                .ToList();
            sb.AppendLine(agentFiles.Count == 0
                ? $"- 에이전트 모드 폴더 {agentDirs.Count}개 · jsonl 0개"
                : $"- 에이전트 모드 폴더 {agentDirs.Count}개 · jsonl {agentFiles.Count}개 · {agentFiles.Sum(f => f.Length) / 1_048_576.0:0.#} MB · {agentFiles.Min(f => f.LastWriteTime):yyyy-MM-dd} ~ {agentFiles.Max(f => f.LastWriteTime):yyyy-MM-dd}");
        });

        Section(sb, "이력 파일", () =>
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JHJ_AI-Usage-Monitor", "history");
            if (!Directory.Exists(root))
            {
                sb.AppendLine("- 없음");
                return;
            }

            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(f => f))
            {
                try
                {
                    var entries = JsonSerializer.Deserialize<List<UsageHistoryEntry>>(File.ReadAllText(file)) ?? [];
                    var name = Path.GetRelativePath(root, file);
                    sb.AppendLine(entries.Count == 0
                        ? $"- {Mask(name)}: 0건"
                        : $"- {Mask(name)}: {entries.Count}건 · {entries.Min(e => e.Day):yyyy-MM-dd} ~ {entries.Max(e => e.Day):yyyy-MM-dd} · {UsageFormatter.FormatCurrency(entries.Sum(e => e.Cost))} · {UsageFormatter.FormatTokenCount(entries.Sum(e => e.Tokens))} 토큰");
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    sb.AppendLine($"- {Mask(Path.GetFileName(file))}: 읽기 실패 ({ex.GetType().Name})");
                }
            }
        });

        Section(sb, "마지막 갱신", () =>
        {
            if (state is null)
            {
                sb.AppendLine("- 아직 갱신 전");
                return;
            }

            sb.AppendLine($"- 시각 {state.LastRefresh.LocalDateTime:yyyy-MM-dd HH:mm:ss} · 트리거 {state.Trigger} · 갱신 중 {state.IsRefreshing}");
            foreach (var error in state.Errors)
            {
                sb.AppendLine($"- 오류: {Mask(error)}");
            }

            foreach (var (id, reading) in state.Providers.OrderBy(p => p.Key))
            {
                var usage = reading.Usage;
                sb.AppendLine($"- {id}: {reading.Confidence}/{reading.Source} · 플랜 {reading.Identity?.Plan ?? "-"} ({reading.Identity?.PlanTier ?? "-"}) · 상태 「{Mask(reading.StatusSummary ?? "-")}」");
                if (usage is not null)
                {
                    sb.AppendLine($"  세션 {usage.SessionUsed}/{usage.SessionLimit?.ToString() ?? "-"} · 주간 {usage.WeekUsed}/{usage.WeekLimit?.ToString() ?? "-"} · 오늘 {UsageFormatter.FormatCurrency(usage.Consumption?.TodayCostUsd ?? 0)} · 30일 {UsageFormatter.FormatCurrency(usage.Consumption?.RollingWindowCostUsd ?? 0)} · 일별 {usage.Consumption?.DailyBreakdown.Count ?? 0}건");
                }
            }
        });

        Section(sb, "이벤트 로그 (최근 200줄)", () =>
        {
            var lines = DiagnosticsLog.Tail(200);
            if (lines.Count == 0)
            {
                sb.AppendLine("- 없음");
                return;
            }

            sb.AppendLine("```text");
            foreach (var line in lines)
            {
                sb.AppendLine(Mask(line));
            }

            sb.AppendLine("```");
        });

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, Action body)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        try
        {
            body();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"- 수집 실패: {ex.GetType().Name} {Mask(ex.Message)}");
        }

        sb.AppendLine();
    }

    // 계약: 자리 하나 — .claude.json(계정 UUID) · .credentials.json(플랜·등급·만료) · projects 로그 요약. 토큰 값은 읽지 않는다
    private static void DescribeSlot(StringBuilder sb, string label, string configDir)
    {
        sb.AppendLine($"- **{label}** `{Mask(configDir)}` 존재 {Directory.Exists(configDir)}");
        var uuid = ClaudeProgramRouter.AccountUuidOf(configDir);
        sb.AppendLine($"  - 로그인 계정: {(uuid is null ? "없음" : Mask(uuid))}");

        var credentials = Path.Combine(configDir, ".credentials.json");
        if (ClaudeProgramRouter.IsDefaultDir(configDir) && !File.Exists(credentials))
        {
            credentials = Path.Combine(UserProfile, ".claude", ".credentials.json");
        }

        if (File.Exists(credentials))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(credentials));
                if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
                {
                    var type = oauth.TryGetProperty("subscriptionType", out var st) ? st.GetString() : null;
                    var tier = oauth.TryGetProperty("rateLimitTier", out var rlt) ? rlt.GetString() : null;
                    var expires = oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                        : "-";
                    var hasToken = oauth.TryGetProperty("accessToken", out var at) && at.ValueKind == JsonValueKind.String && at.GetString()!.Length > 0;
                    sb.AppendLine($"  - 자격: 토큰 {(hasToken ? "있음" : "없음")} · 플랜 {type ?? "-"} · 등급 {tier ?? "-"} · 만료 {expires}");
                }
                else
                {
                    sb.AppendLine("  - 자격: claudeAiOauth 칸 없음");
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                sb.AppendLine($"  - 자격: 읽기 실패 ({ex.GetType().Name})");
            }
        }
        else
        {
            sb.AppendLine("  - 자격: 파일 없음");
        }

        sb.AppendLine($"  - 로그: {DescribeLogs(Path.Combine(configDir, "projects"))}");
    }


    private static string DescribeLogs(string projectsDir)
    {
        if (!Directory.Exists(projectsDir))
        {
            return "폴더 없음";
        }

        var files = new DirectoryInfo(projectsDir).GetFiles("*.jsonl", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return "jsonl 0개";
        }

        return $"jsonl {files.Length}개 · {files.Sum(f => f.Length) / 1_048_576.0:0.#} MB · {files.Min(f => f.LastWriteTime):yyyy-MM-dd} ~ {files.Max(f => f.LastWriteTime):yyyy-MM-dd}";
    }

    private static string Mask(string text)
    {
        var masked = EmailRx.Replace(text, m => m.Value[0] + "***@" + m.Value[(m.Value.IndexOf('@') + 1)..]);
        masked = UuidRx.Replace(masked, m => m.Groups[1].Value + "…");
        return UserProfile.Length > 0 ? masked.Replace(UserProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : masked;
    }
}
