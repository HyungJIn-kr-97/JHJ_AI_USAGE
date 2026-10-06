namespace costats.Core.Pulse;

/// <summary>
/// Tracks token consumption across different categories.
/// </summary>
public sealed record TokenLedger
{
    // 왜: 한 계정의 30일 합이 int 를 넘는다(캐시 읽기만 수십억) — int 면 음수로 돌아가 화면에 -20억이 찍혔다
    public required long StandardInput { get; init; }
    public required long CachedInput { get; init; }
    public required long GeneratedOutput { get; init; }
    public long CacheWriteInput { get; init; } // Claude-specific

    public long TotalConsumed => StandardInput + CachedInput + GeneratedOutput + CacheWriteInput;
    public long NetInput => StandardInput + CacheWriteInput; // Excludes cache reads

    public static TokenLedger Empty => new()
    {
        StandardInput = 0,
        CachedInput = 0,
        GeneratedOutput = 0,
        CacheWriteInput = 0
    };

    public TokenLedger Combine(TokenLedger other) => new()
    {
        StandardInput = StandardInput + other.StandardInput,
        CachedInput = CachedInput + other.CachedInput,
        GeneratedOutput = GeneratedOutput + other.GeneratedOutput,
        CacheWriteInput = CacheWriteInput + other.CacheWriteInput
    };
}

/// <summary>
/// Consumption record for a specific time period with cost attached.
/// </summary>
public sealed record ConsumptionSlice
{
    public required DateOnly Period { get; init; }
    public required string ModelIdentifier { get; init; }
    public required TokenLedger Tokens { get; init; }
    public required decimal ComputedCostUsd { get; init; }

    // 계약: Claude 기본 폴더(~/.claude)에서 프로그램별로 나눠 읽은 줄이면 그 프로그램(entrypoint), 계정 자기 폴더의 줄이면 null
    public string? Program { get; init; }
}

/// <summary>
/// Aggregated consumption summary over a time window.
/// </summary>
public sealed record ConsumptionDigest
{
    public required TokenLedger TodayTokens { get; init; }
    public required decimal TodayCostUsd { get; init; }
    public required TokenLedger RollingWindowTokens { get; init; }
    public required decimal RollingWindowCostUsd { get; init; }
    public required int RollingWindowDays { get; init; }
    public required IReadOnlyList<ConsumptionSlice> DailyBreakdown { get; init; }
    public required DateTimeOffset ComputedAt { get; init; }

    public static ConsumptionDigest None => new()
    {
        TodayTokens = TokenLedger.Empty,
        TodayCostUsd = 0,
        RollingWindowTokens = TokenLedger.Empty,
        RollingWindowCostUsd = 0,
        RollingWindowDays = 30,
        DailyBreakdown = [],
        ComputedAt = DateTimeOffset.UtcNow
    };
}
