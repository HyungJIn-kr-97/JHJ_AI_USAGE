using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using costats.App.ViewModels;
using costats.Core.Pulse;

namespace costats.App.Services;

/// <summary>
/// 팝업이 보여 주는 값을 앱 바깥에서도 읽을 수 있게 파일로 남긴다 — %LOCALAPPDATA%\JHJ_AI-Usage-Monitor\data\.
/// 계약: 저장 구조·칸의 의미·갱신 주기의 정본은 300.Docs\표시-데이터-저장소.md 다.
/// 계약: 값은 화면이 쓴 것을 그대로 옮긴다 — 이 클래스는 집계를 새로 하지 않고 기간 합계만 더한다.
/// 왜: UsageHistoryStore 는 날짜×모델 사용량만 갖는다 — 신원·플랜·한도창·활용도는 어디에도 남지 않아 다른 도구가 읽을 수 없었다.
/// </summary>
public static class DisplayDataStore
{
    public const int SchemaVersion = 1;

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JHJ_AI-Usage-Monitor", "data");

    private static readonly string SnapshotPath = Path.Combine(RootDir, "snapshot.json");

    private static readonly string TimelineDir = Path.Combine(RootDir, "timeline");

    // 계약: 월 1파일, 13개월이 지난 파일은 지운다 — 갱신마다 한 줄이라 지우지 않으면 끝없이 자란다
    private const int KeepMonths = 13;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // 계약: 칸 이름은 camelCase 다 — 300.Docs\표시-데이터-저장소.md 와 같은 철자여야 읽는 쪽이 깨지지 않는다
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions Line = new(Json) { WriteIndented = false };

    // 왜: 같은 값이 연달아 들어오면 이력 줄을 늘리지 않는다 — 갱신 시각만 다른 줄은 값을 더하지 않는다
    private static readonly Dictionary<string, string> LastPoint = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    /// <summary>한 번의 갱신으로 화면에 올라간 값을 통째로 남긴다. 실패해도 화면 갱신을 막지 않는다.</summary>
    public static void Write(
        IReadOnlyList<(string ProviderId, ProviderReading Reading, ProviderPulseViewModel Vm)> cards,
        DateTimeOffset refreshedAt,
        DateTimeOffset? nextRefreshAt)
    {
        if (cards.Count == 0)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                var accounts = cards.Select(card => Build(card.ProviderId, card.Reading, card.Vm)).ToList();
                var snapshot = new DisplaySnapshot(
                    SchemaVersion,
                    Version,
                    DateTimeOffset.Now,
                    refreshedAt,
                    nextRefreshAt,
                    accounts,
                    DeviceInfo.Current);

                Directory.CreateDirectory(RootDir);
                WriteAtomic(SnapshotPath, JsonSerializer.Serialize(snapshot, Json));

                foreach (var account in accounts)
                {
                    AppendTimeline(account);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 다음 갱신에 다시 쓴다 — 저장 실패가 화면을 멈추게 하지 않는다
        }
    }

    private static DisplayAccount Build(string providerId, ProviderReading reading, ProviderPulseViewModel vm)
    {
        var metaId = providerId.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "claude:default" : providerId;
        var identity = reading.Identity;
        var account = new AccountFacts(
            vm.DisplayName,
            Blank(vm.AccountType),
            Blank(vm.AccountEmail) ?? identity?.Email,
            AccountMeta.UuidOf(metaId),
            identity?.Org,
            Blank(vm.PlanText) ?? identity?.Plan,
            identity?.PlanTier,
            vm.MonthlyFeeUsd,
            vm.FeeIsEstimate,
            !vm.NeedsLink,
            vm.AccountStateText);

        return new DisplayAccount(
            Key(providerId),
            providerId,
            vm.ProviderKind,
            account,
            BuildPoint(reading, vm),
            BuildUsage(vm));
    }

    private static PointFacts BuildPoint(ProviderReading reading, ProviderPulseViewModel vm)
    {
        var usage = reading.Usage;
        var totals = vm.Totals;
        return new PointFacts(
            reading.CapturedAt,
            reading.Source.ToString(),
            reading.Confidence.ToString(),
            Blank(vm.StatusSummary),
            Window(usage?.SessionUsed, usage?.SessionLimit, vm.SessionProgress, usage?.SessionWindow),
            Window(usage?.WeekUsed, usage?.WeekLimit, vm.WeekProgress, usage?.WeekWindow),
            (usage?.ModelWeeks ?? [])
                .Select(quota => new ModelWeekFacts(quota.Label, quota.UsedPercent, quota.ResetsAt))
                .ToList(),
            usage?.SpendingBucket is { } bucket
                ? new SpendingFacts(bucket.Kind.ToString(), bucket.Consumed, bucket.Ceiling, bucket.Available,
                    bucket.CurrencySymbol, bucket.CycleEndsAt)
                : null,
            new CostFacts(
                totals.TodayCost,
                totals.TodayTokens,
                ProviderPulseViewModel.RangeDays,
                vm.RangeCostUsd ?? totals.WindowCost,
                vm.RangeTokens ?? totals.WindowTokens,
                (vm.RangeCostUsd ?? totals.WindowCost) / ProviderPulseViewModel.RangeDays,
                (vm.RangeTokens ?? totals.WindowTokens) / ProviderPulseViewModel.RangeDays),
            vm.HasSubscription
                ? new ValueFacts(vm.ValueRatio, Blank(vm.ValueGradeText), vm.MonthlyFeeUsd)
                : null);
    }

    private static WindowFacts? Window(long? used, long? limit, double progress, QuotaWindow? window)
    {
        if (used is null && limit is null && window?.ResetsAt is null)
        {
            return null;
        }

        return new WindowFacts(used, limit, Math.Round(progress * 100, 2), window?.ResetsAt, window?.Duration.TotalHours);
    }

    private static UsageFacts BuildUsage(ProviderPulseViewModel vm)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-(ProviderPulseViewModel.RangeDays - 1));
        var entries = vm.HistoryEntries.Where(e => e.Day >= from && e.Day <= today).ToList();

        var programs = vm.ProgramHistory
            .Select(pair =>
            {
                var rows = pair.Value.Where(e => e.Day >= from && e.Day <= today).ToList();
                return new ProgramFacts(pair.Key, rows.Sum(e => e.Cost), rows.Sum(e => e.Tokens));
            })
            .Where(row => row.CostUsd > 0 || row.Tokens > 0)
            .OrderByDescending(row => row.CostUsd)
            .ToList();

        return new UsageFacts(
            ProviderPulseViewModel.RangeDays,
            from.ToString("yyyy-MM-dd"),
            today.ToString("yyyy-MM-dd"),
            entries
                .OrderBy(e => e.Day)
                .ThenBy(e => e.Model, StringComparer.Ordinal)
                .Select(e => new DayModelFacts(e.Day.ToString("yyyy-MM-dd"), e.Model, e.Cost, e.Tokens,
                    e.Input, e.Output, e.CacheRead, e.CacheWrite))
                .ToList(),
            programs);
    }

    private static void AppendTimeline(DisplayAccount account)
    {
        // 함정: 갱신 시각만 다른 줄은 값을 더하지 않는다 — 비교에서 CapturedAt 을 빼고 지문을 만든다
        var fingerprint = JsonSerializer.Serialize(account.Point with { CapturedAt = default }, Line);
        if (LastPoint.TryGetValue(account.Key, out var previous) && previous == fingerprint)
        {
            return;
        }

        LastPoint[account.Key] = fingerprint;

        var dir = Path.Combine(TimelineDir, account.Key);
        Directory.CreateDirectory(dir);
        var line = JsonSerializer.Serialize(
            new TimelinePoint(SchemaVersion, account.Key, account.ProviderId, account.Point,
                DeviceInfo.Current.Id is { Length: > 0 } deviceId ? deviceId : null), Line);
        File.AppendAllText(Path.Combine(dir, $"{account.Point.CapturedAt.ToLocalTime():yyyy-MM}.jsonl"), line + Environment.NewLine);
        Prune(dir);
    }

    private static void Prune(string dir)
    {
        var oldest = DateTime.Now.AddMonths(-KeepMonths).ToString("yyyy-MM");
        foreach (var path in Directory.GetFiles(dir, "*.jsonl"))
        {
            if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(path), oldest) < 0)
            {
                File.Delete(path);
            }
        }
    }

    // 왜: 깨진 파일을 남기지 않는다 — 읽는 쪽이 갱신 도중의 반쪽 JSON 을 보지 않게 임시 파일에 쓰고 바꿔 끼운다
    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    private static string Key(string providerId) =>
        string.Concat(providerId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));

    // 함정: AssemblyVersion 은 날짜 자리를 담지 못한다(각 자리 상한 65534) — 빌드 날짜는 InformationalVersion 에만 있다
    // 함정: InformationalVersion 끝에는 '+<git 해시>' 가 붙는다 — 버전만 남긴다
    internal static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

public sealed record DisplaySnapshot(
    int SchemaVersion, string App, DateTimeOffset GeneratedAt, DateTimeOffset RefreshedAt,
    DateTimeOffset? NextRefreshAt, IReadOnlyList<DisplayAccount> Accounts, DeviceFacts? Device = null);

public sealed record DisplayAccount(
    string Key, string ProviderId, string ProviderKind, AccountFacts Account, PointFacts Point, UsageFacts Usage);

/// 계약: 계정 단위 — 갱신해도 거의 바뀌지 않는 값이다.
public sealed record AccountFacts(
    string DisplayName, string? AccountType, string? Email, string? Uuid, string? Organization,
    string? Plan, string? PlanTier, decimal? MonthlyFeeUsd, bool FeeIsEstimate, bool Linked, string StateText);

/// 계약: 시점 단위 — 갱신마다 바뀌는 값이고 timeline\ 에 한 줄씩 쌓인다.
public sealed record PointFacts(
    DateTimeOffset CapturedAt, string Source, string Confidence, string? StatusText,
    WindowFacts? Session, WindowFacts? Week, IReadOnlyList<ModelWeekFacts> ModelWeeks,
    SpendingFacts? Spending, CostFacts? Cost, ValueFacts? Value);

/// 계약: Percent 는 0~100, DurationHours 는 한도창의 길이(시간). ResetsAt 은 그 창이 풀리는 절대 시각이다.
public sealed record WindowFacts(long? Used, long? Limit, double? Percent, DateTimeOffset? ResetsAt, double? DurationHours);

public sealed record ModelWeekFacts(string Label, double Percent, DateTimeOffset? ResetsAt);

public sealed record SpendingFacts(
    string Kind, decimal Consumed, decimal Ceiling, decimal Available, string Currency, DateTimeOffset? CycleEndsAt);

/// 계약: 금액은 USD, WindowDays 는 팝업의 기간 칩이 고른 날수다.
public sealed record CostFacts(
    decimal TodayUsd, long TodayTokens, int WindowDays, decimal WindowUsd, long WindowTokens,
    decimal AvgPerDayUsd, long AvgPerDayTokens);

/// 계약: Ratio 는 구독료 대비 배수(2.5 = 2.5배), Grade 는 화면에 뜬 등급 문구다.
public sealed record ValueFacts(double? Ratio, string? Grade, decimal? MonthlyFeeUsd);

public sealed record UsageFacts(
    int RangeDays, string From, string To, IReadOnlyList<DayModelFacts> ByDayModel, IReadOnlyList<ProgramFacts> ByProgram);

public sealed record DayModelFacts(
    string Day, string Model, decimal CostUsd, long Tokens, long Input, long Output, long CacheRead, long CacheWrite);

public sealed record ProgramFacts(string Program, decimal CostUsd, long Tokens);

// 계약: 줄마다 장비 전체를 싣지 않고 ID 만 둔다 — 이름·OS 는 snapshot.json 의 device 가 갖는다
public sealed record TimelinePoint(int SchemaVersion, string Key, string ProviderId, PointFacts Point, string? DeviceId = null);
