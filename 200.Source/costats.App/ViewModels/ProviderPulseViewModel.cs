using CommunityToolkit.Mvvm.ComponentModel;
using costats.App.Localization;
using costats.Core.Pulse;
using costats.Infrastructure.Providers;

namespace costats.App.ViewModels;

public sealed partial class ProviderPulseViewModel : ObservableObject
{
    [ObservableProperty]
    private string providerId = string.Empty;

    /// <summary>
    /// 계약: "claude:work" 같은 계정별 ID 에서 종류("claude")만 돌려준다 — 화면의 색·링크 분기는 이 값으로 한다.
    /// </summary>
    // 함정: 사용량이 없으면 ProviderId 에 표시 이름("Codex")이 들어온다 — 소문자로 접어야 링크·색 트리거가 맞는다
    public string ProviderKind => ProviderId.Split(':')[0].ToLowerInvariant();

    partial void OnProviderIdChanged(string value) => OnPropertyChanged(nameof(ProviderKind));

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string statusSummary = "No data";

    [ObservableProperty]
    private string planText = string.Empty;

    // Session metrics
    [ObservableProperty]
    private double sessionProgress;

    [ObservableProperty]
    private string sessionUsageLabel = "--";

    [ObservableProperty]
    private string sessionResetText = string.Empty;

    [ObservableProperty]
    private string sessionPaceText = string.Empty;

    [ObservableProperty]
    private double sessionPaceProgress;

    [ObservableProperty]
    private bool sessionPaceOnTop;

    // Weekly metrics
    [ObservableProperty]
    private double weekProgress;

    [ObservableProperty]
    private string weekUsageLabel = "--";

    [ObservableProperty]
    private string weekResetText = string.Empty;

    [ObservableProperty]
    private string weekPaceText = string.Empty;

    [ObservableProperty]
    private double weekPaceProgress;

    [ObservableProperty]
    private bool weekPaceOnTop;

    // Extra usage / Credits
    [ObservableProperty]
    private string extraUsageLabel = "--";

    [ObservableProperty]
    private double extraUsageProgress;

    [ObservableProperty]
    private bool hasExtraUsage;

    // Cost tracking
    [ObservableProperty]
    private string todayCostText = "--";

    [ObservableProperty]
    private string monthCostText = "--";

    // 계약: 최근 30일 합계를 30 으로 나눈 하루치 — 기간 칩과 무관하게 「최근 30일」 줄과 같은 창이다
    [ObservableProperty]
    private string avgCostText = "--";

    [ObservableProperty]
    private string avgTokensText = "--";

    [ObservableProperty]
    private bool hasCostData;

    // Utilization status for traffic-light indicators (multicc stacked view)
    [ObservableProperty]
    private string sessionStatusColor = "#10B981"; // Green default

    [ObservableProperty]
    private string weekStatusColor = "#10B981";

    [ObservableProperty]
    private string overallStatusText = "OK";

    [ObservableProperty]
    private string overallStatusColor = "#10B981";

    // Readable percentage text for multi-panel hero numbers (WCAG AA contrast on lavender)
    [ObservableProperty]
    private string sessionPercentText = "0%";

    [ObservableProperty]
    private string weekPercentText = "0%";

    [ObservableProperty]
    private string sessionPercentColor = "#047857";

    [ObservableProperty]
    private string weekPercentColor = "#047857";

    // Compact cost line for multicc stacked cards (e.g. "$4.20 today · $82.50 / 30d")
    [ObservableProperty]
    private string compactCostText = string.Empty;

    [ObservableProperty]
    private bool hasCompactCost;

    // 계약: 오래된 날 → 오늘 순서로 DailyChartDays 개, 기록이 없는 날은 높이 0 으로 채운다
    [ObservableProperty]
    private IReadOnlyList<DailyUsageBar> dailyBars = [];

    [ObservableProperty]
    private bool hasDailyBars;

    [ObservableProperty]
    private string dailyPeakText = string.Empty;

    [ObservableProperty]
    private string dailyStartLabel = string.Empty;

    // 계약: 최근 DailyChartDays 일의 모델별 합계를 비용 내림차순으로, 최대 MaxModelRows 줄
    [ObservableProperty]
    private IReadOnlyList<ModelUsageRow> modelUsages = [];

    [ObservableProperty]
    private bool hasModelUsages;

    // 계약: 전체 주간 한도 아래에 따로 그리는 모델별 주간 한도(예: Fable). 서비스가 주지 않으면 비어 있다
    [ObservableProperty]
    private IReadOnlyList<ModelWeekRow> modelWeeks = [];

    [ObservableProperty]
    private string modelsHeaderText = "Models";

    // 계약: 선택한 기간의 토큰을 유형별로 나눈 줄 — 유형을 모르는 옛 이력은 마지막 "Unknown" 줄로 모인다
    [ObservableProperty]
    private IReadOnlyList<TokenTypeRow> tokenTypes = [];

    [ObservableProperty]
    private bool hasTokenTypes;

    [ObservableProperty]
    private string tokenTypesHeaderText = "Token types";

    // 계약: 차트 아래 눈금 — 칸마다 그 구간이 시작하는 날짜(또는 달)를 왼쪽 정렬로 적는다
    [ObservableProperty]
    private IReadOnlyList<string> axisLabels = [];

    private const int MaxModelRows = 4;

    [ObservableProperty]
    private string chartTitleText = "Daily cost";

    public static IReadOnlyList<int> RangeChoices { get; } = [7, 14, 30, 90, 180, 365];

    /// <summary>
    /// 계약: 차트와 모델 비중이 보는 기간(일). 30일 넘는 범위는 UsageHistoryStore 에 쌓인 만큼만 채워진다.
    /// 함정: static 이다 — VM 이 갱신마다 새로 만들어져 인스턴스에 두면 선택이 날아간다. 바꾼 뒤에는 PulseViewModel 이 다시 그려야 한다.
    /// </summary>
    public static int RangeDays { get; set; } = 30;

    private static int DailyChartDays => RangeDays;
    private const double DailyChartHeight = 56;

    // Token tracking
    [ObservableProperty]
    private string todayTokensText = "--";

    [ObservableProperty]
    private string monthTokensText = "--";

    // Legacy properties for compatibility
    [ObservableProperty]
    private string sessionText = "--";

    [ObservableProperty]
    private string weekText = "--";

    [ObservableProperty]
    private string creditsText = "--";

    // 계약: false 면 팝업이 그 한도 막대를 숨긴다 — Gemini 가 한도를 못 받았을 때만 false 가 된다
    [ObservableProperty]
    private bool hasSessionQuota = true;

    [ObservableProperty]
    private bool hasWeekQuota = true;

    [ObservableProperty]
    private bool needsLink;

    // 계약: 이 카드의 자리로 연동이 진행 중이거나 결과를 보이는 중 — 카드 바로 밑에 연동 진행 칸이 펼쳐진다(팝업 창이 넣는다)
    [ObservableProperty]
    private bool isLinking;

    // 계약: 설정 「계정」에서 정한 유형(회사·개인 등)과 연동된 메일 — 카드 이름 옆·아래에 보인다
    [ObservableProperty]
    private string accountType = string.Empty;

    [ObservableProperty]
    private string accountEmail = string.Empty;

    public bool HasAccountType => AccountType.Length > 0;

    partial void OnAccountTypeChanged(string value) => OnPropertyChanged(nameof(HasAccountType));

    [ObservableProperty]
    private string accountStateText = string.Empty;

    [ObservableProperty]
    private System.Windows.Media.Brush accountStateBrush = System.Windows.Media.Brushes.Transparent;

    private static readonly System.Windows.Media.Brush LinkedBrush = FrozenBrush("#10B981");
    private static readonly System.Windows.Media.Brush NeedsLinkBrush = FrozenBrush("#EF4444");

    private static System.Windows.Media.Brush FrozenBrush(string hex)
    {
        var brush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    // 함정: 데이터가 없는 판(연동 전 자리)은 Usage 가 null 이다 — providerId 를 안 넘기면 화면 이름이 id 가 되어 Claude 자리로 안 잡힌다
    public static ProviderPulseViewModel FromReading(ProviderReading reading, string displayNameFallback, string? providerId = null)
    {
        var vm = new ProviderPulseViewModel
        {
            ProviderId = reading.Usage?.ProviderId ?? providerId ?? displayNameFallback,
            DisplayName = displayNameFallback,
            StatusSummary = FormatStatusSummary(reading),
            PlanText = reading.Identity?.Plan ?? "Max"
        };

        PopulateSessionMetrics(vm, reading);
        PopulateWeekMetrics(vm, reading);
        PopulateExtraUsage(vm, reading);
        PopulateCostData(vm, reading);

        // 왜: 한도를 못 받은 판(Gemini 좌석 없음 · Claude 토큰 없음)에서 「0% 사용」 막대는 한도가 남은 것처럼 읽힌다
        vm.HasSessionQuota = reading.Usage?.SessionLimit is not null;
        vm.HasWeekQuota = reading.Usage?.WeekLimit is not null;
        if (!vm.HasSessionQuota)
        {
            vm.SessionProgress = 0;
            vm.SessionUsageLabel = "--";
            vm.SessionPaceText = string.Empty;
        }

        if (!vm.HasWeekQuota)
        {
            vm.WeekProgress = 0;
            vm.WeekUsageLabel = "--";
            vm.WeekPaceText = string.Empty;
        }

        if (!vm.HasSessionQuota)
        {
            vm.SessionPercentText = "--";
        }

        if (!vm.HasWeekQuota)
        {
            vm.WeekPercentText = "--";
        }

        // 계약: 한도를 하나도 못 받은 Claude·Codex 자리는 「연동 필요」 — 머리글·전체 카드에 「연동」 버튼이 뜨고 전체 요약에서 따로 센다
        if (vm.ProviderKind is "claude" or "codex")
        {
            vm.NeedsLink = !vm.HasSessionQuota && !vm.HasWeekQuota;
            vm.AccountStateText = Loc.T(vm.NeedsLink ? "Needs linking" : "Linked");
            vm.AccountStateBrush = vm.NeedsLink ? NeedsLinkBrush : LinkedBrush;
        }

        // Set overall status based on the higher of session or week utilization
        var sessionPercent = vm.SessionProgress * 100.0;
        var weekPercent = vm.WeekProgress * 100.0;
        var worstPercent = Math.Max(sessionPercent, weekPercent);
        vm.OverallStatusColor = GetUtilizationColor(worstPercent);
        vm.OverallStatusText = GetStatusText(worstPercent);

        // Legacy fields
        vm.SessionText = FormatUsageRatio(reading.Usage?.SessionUsed, reading.Usage?.SessionLimit);
        vm.WeekText = FormatUsageRatio(reading.Usage?.WeekUsed, reading.Usage?.WeekLimit);
        vm.CreditsText = reading.Usage?.SpendingBucket?.Available.ToString("0.##") ?? "--";

        vm.ModelWeeks = (reading.Usage?.ModelWeeks ?? [])
            .Select(quota => new ModelWeekRow(
                Loc.T("Weekly · {0}", quota.Label),
                Math.Clamp(quota.UsedPercent / 100.0, 0, 1),
                Loc.Tr($"{(int)Math.Round(quota.UsedPercent)}% used"),
                quota.ResetsAt is { } resetsAt ? Loc.Tr($"Resets {UsageFormatter.ResetCountdown(resetsAt)}") : string.Empty))
            .ToList();
        // 왜: 탭을 바꿀 때 창 높이가 출렁이지 않게 한다 — 모델별 한도가 없는 도구도 같은 자리를 빈 값으로 차지한다
        if (vm.ModelWeeks.Count == 0)
        {
            vm.ModelWeeks = [new ModelWeekRow(Loc.T("Weekly · per model"), 0, "--", string.Empty)];
        }

        // 왜: 아래 문장들은 코어·인프라 층이 영어로 만들어 준다 — 화면에 나가기 직전에 한 번에 옮긴다
        // 왜: 「방금 갱신」은 읽은 순간의 말이라 열어 둔 사이 낡는다 — 갱신 시각을 함께 적어 언제 값인지 알게 한다
        var updatedAt = vm.StatusSummary.StartsWith("Updated ", StringComparison.Ordinal)
            ? $" · {reading.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
            : string.Empty;
        vm.StatusSummary = Loc.Tr(vm.StatusSummary) + updatedAt;
        vm.SessionUsageLabel = Loc.Tr(vm.SessionUsageLabel);
        vm.WeekUsageLabel = Loc.Tr(vm.WeekUsageLabel);
        vm.SessionResetText = Loc.Tr(vm.SessionResetText);
        vm.WeekResetText = Loc.Tr(vm.WeekResetText);
        vm.SessionPaceText = Loc.Tr(vm.SessionPaceText);
        vm.WeekPaceText = Loc.Tr(vm.WeekPaceText);
        vm.ExtraUsageLabel = Loc.Tr(vm.ExtraUsageLabel);
        vm.CompactCostText = Loc.Tr(vm.CompactCostText);

        return vm;
    }

    private static void PopulateSessionMetrics(ProviderPulseViewModel vm, ProviderReading reading)
    {
        var usage = reading.Usage;
        if (usage is null)
        {
            return;
        }

        var usedPercent = CalculateUsedPercent(usage.SessionUsed, usage.SessionLimit);
        vm.SessionProgress = usedPercent / 100.0;
        vm.SessionUsageLabel = FormatUsageLabel(usedPercent, usage.SessionUsed);

        // Reset text
        if (usage.SessionWindow?.ResetsAt is { } sessionResets)
        {
            vm.SessionResetText = $"Resets {UsageFormatter.ResetCountdown(sessionResets)}";

            // Pace calculation
            var pace = UsagePace.Calculate(
                usedPercent,
                sessionResets,
                usage.SessionWindow.Duration);

            if (pace is not null)
            {
                vm.SessionPaceText = UsageFormatter.FormatPace(pace) ?? string.Empty;
                vm.SessionPaceProgress = pace.ExpectedUsedPercent / 100.0;
                vm.SessionPaceOnTop = pace.DeltaPercent < 0; // Behind = pace marker above actual
            }
        }

        vm.SessionStatusColor = GetUtilizationColor(usedPercent);
        vm.SessionPercentText = $"{(int)Math.Round(usedPercent)}%";
        vm.SessionPercentColor = GetPercentTextColor(usedPercent);
    }

    private static void PopulateWeekMetrics(ProviderPulseViewModel vm, ProviderReading reading)
    {
        var usage = reading.Usage;
        if (usage is null)
        {
            return;
        }

        var usedPercent = CalculateUsedPercent(usage.WeekUsed, usage.WeekLimit);
        vm.WeekProgress = usedPercent / 100.0;
        vm.WeekUsageLabel = FormatUsageLabel(usedPercent, usage.WeekUsed);

        // Reset text
        if (usage.WeekWindow?.ResetsAt is { } weekResets)
        {
            vm.WeekResetText = $"Resets {UsageFormatter.ResetCountdown(weekResets)}";

            // Pace calculation
            var pace = UsagePace.Calculate(
                usedPercent,
                weekResets,
                usage.WeekWindow.Duration);

            if (pace is not null)
            {
                vm.WeekPaceText = UsageFormatter.FormatPace(pace) ?? string.Empty;
                vm.WeekPaceProgress = pace.ExpectedUsedPercent / 100.0;
                vm.WeekPaceOnTop = pace.DeltaPercent < 0;
            }
        }

        vm.WeekStatusColor = GetUtilizationColor(usedPercent);
        vm.WeekPercentText = $"{(int)Math.Round(usedPercent)}%";
        vm.WeekPercentColor = GetPercentTextColor(usedPercent);
    }

    private static void PopulateExtraUsage(ProviderPulseViewModel vm, ProviderReading reading)
    {
        var bucket = reading.Usage?.SpendingBucket;
        if (bucket is null)
        {
            vm.HasExtraUsage = false;
            vm.ExtraUsageLabel = "--";
            return;
        }

        vm.HasExtraUsage = true;

        switch (bucket.Kind)
        {
            case BucketKind.OverageSpend:
                // Claude-style: show spent / ceiling
                vm.ExtraUsageLabel = $"Overage: {bucket.CurrencySymbol}{bucket.Consumed:F2} / {bucket.CurrencySymbol}{bucket.Ceiling:F2}";
                vm.ExtraUsageProgress = bucket.FillRatio;
                break;

            case BucketKind.PrepaidBalance:
                // Codex-style: show remaining balance
                vm.ExtraUsageLabel = $"Balance: {bucket.CurrencySymbol}{bucket.Available:F2} remaining";
                vm.ExtraUsageProgress = 0; // No progress bar for prepaid
                break;
        }
    }

    private static void PopulateCostData(ProviderPulseViewModel vm, ProviderReading reading)
    {
        var consumption = reading.Usage?.Consumption;
        if (consumption is null || (consumption.TodayTokens.TotalConsumed == 0 && consumption.RollingWindowTokens.TotalConsumed == 0))
        {
            vm.HasCostData = false;
            // 왜: 비용이 없어도 차트·모델·토큰 유형 구역은 빈 값으로 그린다 — 탭마다 창 높이가 달라지지 않게
            PopulateDailyBars(vm, consumption);
            return;
        }

        vm.HasCostData = true;

        vm._totals = (consumption.TodayCostUsd, consumption.TodayTokens.TotalConsumed,
            consumption.RollingWindowCostUsd, consumption.RollingWindowTokens.TotalConsumed);
        ApplyCostTexts(vm);
        PopulateDailyBars(vm, consumption);
    }

    // 계약: 오늘·최근 30일 합계 — 「전체」 통계(Combine)가 계정마다 더한다
    private (decimal TodayCost, long TodayTokens, decimal WindowCost, long WindowTokens) _totals;

    // 계약: 이 계정이 보는 (날짜, 모델) 이력 전부 — 기간 칩과 무관하게 걸러지기 전 목록이다
    private List<costats.App.Services.UsageHistoryEntry> _history = [];

    private static void ApplyCostTexts(ProviderPulseViewModel vm)
    {
        var (todayCost, todayTokens, windowCost, windowTokens) = vm._totals;
        vm.TodayCostText = UsageFormatter.FormatCurrency(todayCost);
        vm.TodayTokensText = UsageFormatter.FormatTokenCount(todayTokens);
        vm.MonthCostText = UsageFormatter.FormatCurrency(windowCost);
        vm.MonthTokensText = UsageFormatter.FormatTokenCount(windowTokens);
        vm.AvgCostText = UsageFormatter.FormatCurrency(windowCost / 30m);
        vm.AvgTokensText = UsageFormatter.FormatTokenCount(windowTokens / 30);

        // Compact single-line cost for stacked multicc cards
        var todayFormatted = UsageFormatter.FormatCurrency(todayCost);
        var monthFormatted = UsageFormatter.FormatCurrency(windowCost);
        vm.CompactCostText = $"{todayFormatted} today  ·  {monthFormatted} / 30d";
        vm.HasCompactCost = true;
    }

    /// <summary>
    /// 계약: 여러 계정의 통계를 합친 판 — 「전체」 보기의 통계 구역용. 한도 막대는 계정마다 달라 합치지 않는다.
    /// </summary>
    public static ProviderPulseViewModel Combine(IReadOnlyList<ProviderPulseViewModel> parts, string displayName, string providerId)
    {
        var vm = new ProviderPulseViewModel { ProviderId = providerId, DisplayName = displayName };
        vm._totals = (parts.Sum(p => p._totals.TodayCost), parts.Sum(p => p._totals.TodayTokens),
            parts.Sum(p => p._totals.WindowCost), parts.Sum(p => p._totals.WindowTokens));
        vm.HasCostData = parts.Any(p => p.HasCostData);
        if (vm.HasCostData)
        {
            ApplyCostTexts(vm);
            vm.CompactCostText = Loc.Tr(vm.CompactCostText);
        }

        RenderHistory(vm, SumByDayModel(parts.SelectMany(p => p._history)));
        return vm;
    }

    private static List<costats.App.Services.UsageHistoryEntry> SumByDayModel(IEnumerable<costats.App.Services.UsageHistoryEntry> entries) =>
        entries
            .GroupBy(e => (e.Day, e.Model))
            .Select(g => g.Count() == 1 ? g.First() : new costats.App.Services.UsageHistoryEntry(
                g.Key.Day, g.Key.Model, g.Sum(e => e.Cost), g.Sum(e => e.Tokens),
                g.Sum(e => e.Input), g.Sum(e => e.Output), g.Sum(e => e.CacheRead), g.Sum(e => e.CacheWrite)))
            .ToList();

    private static void PopulateDailyBars(ProviderPulseViewModel vm, ConsumptionDigest? consumption)
    {
        vm._history = MergedHistory(vm.ProviderId, consumption?.DailyBreakdown ?? []);
        RenderHistory(vm, vm._history);
    }

    private static void RenderHistory(ProviderPulseViewModel vm, IReadOnlyList<costats.App.Services.UsageHistoryEntry> history)
    {
        vm.ChartTitleText = Loc.T(RangeDays <= 30 ? "Daily cost" : RangeDays <= 180 ? "Weekly cost" : "Monthly cost");
        vm.ModelsHeaderText = RangeDays == 365 ? Loc.T("Models · 1 year") : Loc.T("Models · {0} days", RangeDays);
        vm.TokenTypesHeaderText = RangeDays == 365 ? Loc.T("Token types · 1 year") : Loc.T("Token types · {0} days", RangeDays);

        // 왜: DailyBreakdown 은 일×모델 단위다 — 모델 이름을 접어 합친 뒤, 로그에서 이미 지워진 날은 쌓아 둔 이력으로 메운다
        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = today.AddDays(-(RangeDays - 1));
        var entries = history
            .Where(entry => entry.Day >= start && entry.Day <= today)
            .ToList();

        var buckets = BuildBuckets(start, today);
        var totals = buckets
            .Select(bucket => entries.Where(e => e.Day >= bucket.From && e.Day <= bucket.To).ToList())
            .ToList();
        var peak = totals.Select(list => list.Sum(e => e.Cost)).DefaultIfEmpty(0m).Max();
        var peakFormat = RangeDays <= 30 ? "total {0} · best day {1}" : RangeDays <= 180 ? "total {0} · best week {1}" : "total {0} · best month {1}";

        if (peak <= 0)
        {
            // 계약: 데이터가 없어도 막대 자리·눈금·헤더는 그대로 두고 값만 비운다 — 창 높이를 지키기 위해서다
            vm.DailyBars = buckets.Select(b => new DailyUsageBar(0, $"{b.Label}\n--", false)).ToList();
            vm.AxisLabels = BuildAxisLabels(buckets);
            vm.DailyPeakText = Loc.T(peakFormat, "--", "--");
            vm.HasDailyBars = true;
            PopulateModelUsages(vm, entries);
            PopulateTokenTypes(vm, entries);
            return;
        }

        var bars = new List<DailyUsageBar>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
        {
            var cost = totals[i].Sum(e => e.Cost);
            var tokens = totals[i].Sum(e => e.Tokens);
            var height = cost <= 0 ? 0 : Math.Max(2, (double)(cost / peak) * DailyChartHeight);
            var tooltip = $"{buckets[i].Label}\n{UsageFormatter.FormatCurrency(cost)} · {UsageFormatter.FormatTokenCount(tokens)}";
            tooltip += string.Concat(totals[i]
                .GroupBy(e => e.Model)
                .Select(g => (Name: g.Key, Cost: g.Sum(e => e.Cost)))
                .OrderByDescending(m => m.Cost)
                .Take(3)
                .Select(m => $"\n{m.Name}  {UsageFormatter.FormatCurrency(m.Cost)}"));

            bars.Add(new DailyUsageBar(height, tooltip, i == buckets.Count - 1));
        }

        PopulateModelUsages(vm, entries);
        PopulateTokenTypes(vm, entries);

        vm.DailyBars = bars;
        // 왜: 막대 하나가 하루인지 한 주인지 한 달인지 숫자만으로는 알 수 없다 — 최고값에 단위를 붙인다
        vm.DailyPeakText = Loc.T(peakFormat, UsageFormatter.FormatCurrency(entries.Sum(e => e.Cost)), UsageFormatter.FormatCurrency(peak));
        vm.DailyStartLabel = start.ToString(RangeDays <= 30 ? "MM-dd" : "yyyy-MM-dd");
        vm.AxisLabels = BuildAxisLabels(buckets);
        vm.HasDailyBars = true;
    }

    // 계약: 계정 자기 폴더의 이력 + 지금 이 계정에 연동된 프로그램의 이력을 (날짜, 모델)로 합친다 — ClaudeProgramRouter
    private static List<costats.App.Services.UsageHistoryEntry> MergedHistory(string providerId, IReadOnlyList<ConsumptionSlice> slices)
    {
        var own = costats.App.Services.UsageHistoryStore.Merge(providerId, ToHistory(slices.Where(s => s.Program is null)));
        if (!ClaudeProgramRouter.IsActive)
        {
            return own.ToList();
        }

        foreach (var group in slices.Where(s => s.Program is not null).GroupBy(s => s.Program!, StringComparer.OrdinalIgnoreCase))
        {
            costats.App.Services.UsageHistoryStore.MergeProgram(group.Key, ToHistory(group));
        }

        var programs = costats.App.Services.UsageHistoryStore.Programs()
            .ToDictionary(p => p, p => costats.App.Services.UsageHistoryStore.MergeProgram(p, []), StringComparer.OrdinalIgnoreCase);
        var linked = programs
            .Where(p => string.Equals(ClaudeProgramRouter.OwnerOf(p.Key), providerId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Value);

        // 왜: 프로그램별로 쌓기 전의 기본 계정 이력(history\claude.json)은 프로그램이 섞여 있어 나눌 수 없다 — 프로그램 이력이 시작되기 전 날짜에만 쓴다
        if (providerId.Equals(ClaudeProgramRouter.DefaultProviderId, StringComparison.OrdinalIgnoreCase))
        {
            var firstProgramDay = programs.Values.SelectMany(list => list).Select(e => e.Day).DefaultIfEmpty(DateOnly.MaxValue).Min();
            own = own.Where(e => e.Day < firstProgramDay).ToList();
        }

        return SumByDayModel(own.Concat(linked));
    }

    private static IEnumerable<costats.App.Services.UsageHistoryEntry> ToHistory(IEnumerable<ConsumptionSlice> slices) =>
        slices
            .GroupBy(slice => (slice.Period, Model: ShortModelName(slice.ModelIdentifier)))
            .Select(group => new costats.App.Services.UsageHistoryEntry(
                group.Key.Period,
                group.Key.Model,
                group.Sum(s => s.ComputedCostUsd),
                group.Sum(s => (long)s.Tokens.TotalConsumed),
                group.Sum(s => (long)s.Tokens.StandardInput),
                group.Sum(s => (long)s.Tokens.GeneratedOutput),
                group.Sum(s => (long)s.Tokens.CachedInput),
                group.Sum(s => (long)s.Tokens.CacheWriteInput)))
            .ToList();

    // 계약: 막대 수가 적으면 막대마다, 많으면 같은 폭의 구간 몇 개로 나눠 구간 첫 막대의 시작일을 적는다
    private static List<string> BuildAxisLabels(List<(DateOnly From, DateOnly To, string Label)> buckets)
    {
        var format = RangeDays <= 180 ? "MM-dd" : "yy-MM";
        var segments = buckets.Count <= 7 ? buckets.Count : RangeDays <= 180 ? 5 : 6;
        return Enumerable.Range(0, segments)
            .Select(i => buckets[i * buckets.Count / segments].From.ToString(format))
            .ToList();
    }

    private static readonly System.Windows.Media.Brush[] TokenTypeColors = CreateModelColors("#5B8DEF", "#3FB68B", "#E6B450", "#C98A5B", "#8F847C");

    private static void PopulateTokenTypes(ProviderPulseViewModel vm, IReadOnlyList<costats.App.Services.UsageHistoryEntry> entries)
    {
        var total = entries.Sum(e => e.Tokens);
        var typed = entries.Sum(e => e.TypedTokens);

        // 계약: 네 기본 유형은 0 이어도 "--" 로 늘 그린다 — 줄 수가 같아야 탭·기간을 바꿔도 창 높이가 같다
        var parts = new List<(string Name, string Hint, long Tokens, int Color)>
        {
            ("Input", "Fresh input sent to the model (not cached)", entries.Sum(e => e.Input), 0),
            ("Output", "Text the model generated", entries.Sum(e => e.Output), 1),
            ("Cache read", "Input re-read from the prompt cache (cheapest)", entries.Sum(e => e.CacheRead), 2),
            ("Cache write", "Input stored into the prompt cache", entries.Sum(e => e.CacheWrite), 3),
            // 왜: 유형 칸이 생기기 전에 쌓인 이력은 나눌 수 없다 — 숨기면 합계가 안 맞으므로 따로 보인다
            ("Unknown", "Older history saved before token types were recorded", Math.Max(0, total - typed), 4)
        };

        vm.TokenTypes = parts
            .Where(part => part.Tokens > 0 || part.Color < 4)
            .Select(part =>
            {
                var share = total <= 0 ? 0 : (double)part.Tokens / total;
                return new TokenTypeRow(
                    Loc.T(part.Name),
                    part.Tokens > 0 ? UsageFormatter.FormatTokenCount(part.Tokens) : "--",
                    part.Tokens <= 0 ? string.Empty : share < 0.01 ? "<1%" : $"{share:P0}",
                    share,
                    TokenTypeColors[part.Color],
                    $"{Loc.T(part.Name)} · {Loc.T("{0} tokens", UsageFormatter.FormatTokenCount(part.Tokens))}\n{Loc.T(part.Hint)}");
            })
            .ToList();
        vm.HasTokenTypes = true;
    }

    /// <summary>
    /// 계약: 30일까지는 하루, 180일까지는 오늘에서 거꾸로 7일씩, 그보다 길면 달력 월 단위로 묶는다 — 오래된 것부터 돌려준다.
    /// </summary>
    private static List<(DateOnly From, DateOnly To, string Label)> BuildBuckets(DateOnly start, DateOnly today)
    {
        var buckets = new List<(DateOnly From, DateOnly To, string Label)>();
        if (RangeDays <= 30)
        {
            for (var day = start; day <= today; day = day.AddDays(1))
            {
                buckets.Add((day, day, day.ToString("MM-dd (ddd)")));
            }
        }
        else if (RangeDays <= 180)
        {
            for (var to = today; to >= start; to = to.AddDays(-7))
            {
                var from = to.AddDays(-6) < start ? start : to.AddDays(-6);
                buckets.Insert(0, (from, to, $"{from:MM-dd} ~ {to:MM-dd}"));
            }
        }
        else
        {
            for (var from = new DateOnly(start.Year, start.Month, 1); from <= today; from = from.AddMonths(1))
            {
                var monthEnd = from.AddMonths(1).AddDays(-1);
                buckets.Add((from < start ? start : from, monthEnd > today ? today : monthEnd, from.ToString("yyyy-MM")));
            }
        }

        return buckets;
    }

    private static void PopulateModelUsages(ProviderPulseViewModel vm, IReadOnlyList<costats.App.Services.UsageHistoryEntry> entries)
    {
        var models = entries
            .GroupBy(entry => entry.Model)
            .Select(group => (
                Name: group.Key,
                Cost: group.Sum(e => e.Cost),
                Tokens: group.Sum(e => e.Tokens),
                Types: TypeLine(group)))
            .OrderByDescending(m => m.Cost)
            .ThenByDescending(m => m.Tokens)
            .ToList();

        var total = models.Sum(m => m.Cost);
        if (models.Count == 0 || total <= 0)
        {
            // 계약: 모델이 없어도 구역은 남긴다 — 도넛 자리(76px)가 높이를 잡아 주므로 줄은 비워 둔다
            vm.ModelUsages = [];
            vm.HasModelUsages = true;
            return;
        }

        // 왜: 줄 수를 고정해야 창 높이가 흔들리지 않는다 — 넘치는 모델은 "Others" 한 줄로 접는다
        var shown = models.Count > MaxModelRows ? models.Take(MaxModelRows - 1).ToList() : models;
        var rest = models.Skip(shown.Count).ToList();
        if (rest.Count > 0)
        {
            shown.Add((Loc.T("Others ({0})", rest.Count), rest.Sum(m => m.Cost), rest.Sum(m => m.Tokens), string.Empty));
        }

        var rows = new List<ModelUsageRow>(shown.Count);
        var startAngle = 0.0;
        for (var i = 0; i < shown.Count; i++)
        {
            var m = shown[i];
            var share = (double)(m.Cost / total);
            var sweep = share * 360.0;
            rows.Add(new ModelUsageRow(
                m.Name,
                UsageFormatter.FormatCurrency(m.Cost),
                $"{share:P0}",
                $"{m.Name}\n{UsageFormatter.FormatCurrency(m.Cost)} · {Loc.T("{0} tokens", UsageFormatter.FormatTokenCount(m.Tokens))}{m.Types}",
                ModelColors[Math.Min(i, ModelColors.Length - 1)],
                BuildDonutSlice(startAngle, sweep)));
            startAngle += sweep;
        }

        vm.ModelUsages = rows;
        vm.HasModelUsages = true;
    }

    // 계약: 모델 툴팁에 붙는 유형별 토큰 줄 — 유형을 모르는 기록뿐이면 빈 문자열
    private static string TypeLine(IEnumerable<costats.App.Services.UsageHistoryEntry> entries)
    {
        var list = entries.ToList();
        if (list.Sum(e => e.TypedTokens) <= 0)
        {
            return string.Empty;
        }

        return "\n" + string.Join(" · ", new (string Name, long Tokens)[]
            {
                ("Input", list.Sum(e => e.Input)),
                ("Output", list.Sum(e => e.Output)),
                ("Cache read", list.Sum(e => e.CacheRead)),
                ("Cache write", list.Sum(e => e.CacheWrite))
            }
            .Where(part => part.Tokens > 0)
            .Select(part => $"{Loc.T(part.Name)} {UsageFormatter.FormatTokenCount(part.Tokens)}"));
    }

    // 왜: 라이트·다크 양쪽 배경에서 다 읽히는 색만 골랐다 — 마지막 회색은 "Others" 몫이다
    private static readonly System.Windows.Media.Brush[] ModelColors = CreateModelColors("#E5484D", "#E6B450", "#C98A5B", "#8F847C");

    private static System.Windows.Media.Brush[] CreateModelColors(params string[] hex)
    {
        return hex.Select(value =>
        {
            var brush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return (System.Windows.Media.Brush)brush;
        }).ToArray();
    }

    /// <summary>
    /// 계약: 각도는 12시 방향에서 시계 방향(도)이고, 좌표계는 ModelUsageRow.DonutSize 정사각형이다.
    /// </summary>
    private static System.Windows.Media.Geometry BuildDonutSlice(double startAngle, double sweepAngle)
    {
        const double outer = ModelUsageRow.DonutSize / 2;
        const double inner = outer * 0.58;

        // 함정: 시작점과 끝점이 같으면 호가 그려지지 않는다 — 100% 한 조각은 360 에 못 미치게 자른다
        var sweep = Math.Clamp(sweepAngle, 0, 359.99);
        var end = startAngle + sweep;

        System.Windows.Point At(double radius, double angle)
        {
            var radians = (angle - 90) * Math.PI / 180.0;
            return new System.Windows.Point(outer + radius * Math.Cos(radians), outer + radius * Math.Sin(radians));
        }

        var isLarge = sweep > 180;
        var figure = new System.Windows.Media.PathFigure { StartPoint = At(outer, startAngle), IsClosed = true };
        figure.Segments.Add(new System.Windows.Media.ArcSegment(
            At(outer, end), new System.Windows.Size(outer, outer), 0, isLarge, System.Windows.Media.SweepDirection.Clockwise, true));
        figure.Segments.Add(new System.Windows.Media.LineSegment(At(inner, end), true));
        figure.Segments.Add(new System.Windows.Media.ArcSegment(
            At(inner, startAngle), new System.Windows.Size(inner, inner), 0, isLarge, System.Windows.Media.SweepDirection.Counterclockwise, true));

        var geometry = new System.Windows.Media.PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static string ShortModelName(string? modelIdentifier)
    {
        if (string.IsNullOrWhiteSpace(modelIdentifier))
        {
            return "unknown";
        }

        // 왜: "claude-opus-4-5-20251101" 처럼 끝에 붙는 날짜 꼬리는 같은 모델을 여러 줄로 쪼갠다
        return System.Text.RegularExpressions.Regex.Replace(modelIdentifier.Trim(), @"-\d{8}$", string.Empty);
    }

    private static double CalculateUsedPercent(long? used, long? limit)
    {
        if (used is null)
        {
            return 0;
        }

        // If limit is 100, the "used" value IS the percentage directly
        // This happens when we get percentage data from CLI probe
        if (limit == 100)
        {
            return Math.Clamp(used.Value, 0, 100);
        }

        if (limit is null || limit <= 0)
        {
            return 0;
        }

        return Math.Clamp((double)used.Value / limit.Value * 100, 0, 100);
    }

    private static string FormatUsageLabel(double usedPercent, long? used)
    {
        if (used is null || used == 0)
        {
            return "0% used";
        }

        return $"{(int)Math.Round(usedPercent)}% used";
    }

    private static string FormatUsageRatio(long? used, long? limit)
    {
        if (used is null && limit is null)
        {
            return "--";
        }

        if (limit is null)
        {
            return used?.ToString() ?? "--";
        }

        return $"{used ?? 0}/{limit.Value}";
    }

    private static string FormatStatusSummary(ProviderReading reading)
    {
        if (reading.StatusSummary is not null)
        {
            return reading.StatusSummary;
        }

        return reading.Source switch
        {
            ReadingSource.LocalLog => $"Updated {UsageFormatter.FormatRelativeTime(reading.CapturedAt)}",
            ReadingSource.Api => "API",
            ReadingSource.Cli => "CLI",
            _ => "No data"
        };
    }

    private static string GetUtilizationColor(double percent)
    {
        return percent switch
        {
            >= 95 => "#EF4444",  // Red - at/over limit
            >= 80 => "#F97316",  // Orange - near limit
            >= 50 => "#F59E0B",  // Amber - moderate
            _     => "#10B981",  // Green - healthy
        };
    }

    private static string GetStatusText(double percent)
    {
        return percent switch
        {
            >= 95 => "At limit",
            >= 80 => "Near limit",
            >= 50 => "Moderate",
            _     => "OK",
        };
    }

    /// <summary>
    /// Returns WCAG AA-compliant text colors for percentage hero numbers on lavender background.
    /// Darker variants of the bar colors ensure 4.5:1+ contrast ratio.
    /// </summary>
    private static string GetPercentTextColor(double percent)
    {
        return percent switch
        {
            >= 95 => "#DC2626",  // Red-600 (~6.5:1 on lavender)
            >= 80 => "#C2410C",  // Orange-700 (~6.0:1)
            >= 50 => "#B45309",  // Amber-700 (~5.4:1)
            _     => "#047857",  // Emerald-700 (~4.6:1)
        };
    }
}
