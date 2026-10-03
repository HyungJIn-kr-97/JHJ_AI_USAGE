namespace costats.Core.Pulse;

public sealed record UsagePulse(
    string ProviderId,
    DateTimeOffset CapturedAt,
    long? SessionUsed,
    long? SessionLimit,
    long? WeekUsed,
    long? WeekLimit,
    MonetaryBucket? SpendingBucket,
    ConsumptionDigest? Consumption,
    QuotaWindow? SessionWindow,
    QuotaWindow? WeekWindow)
{
    /// <summary>
    /// 계약: 전체 주간 한도와 따로 걸리는 모델별 주간 한도(예: Fable). 없으면 빈 목록이다.
    /// </summary>
    public IReadOnlyList<ModelQuota> ModelWeeks { get; init; } = [];
}

/// <summary>
/// 모델별 주간 한도 한 건. UsedPercent 는 0~100.
/// </summary>
public sealed record ModelQuota(string Label, double UsedPercent, DateTimeOffset? ResetsAt);
