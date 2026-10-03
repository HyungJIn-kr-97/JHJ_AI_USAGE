using costats.Core.Pulse;

namespace costats.Infrastructure.Expense;

/// <summary>
/// Analyzes token consumption and produces digest summaries.
/// </summary>
public sealed class ExpenseAnalyzer
{
    // 계약: "Last 30 days" 합계가 보는 기간
    private const int TotalsWindowDays = 30;

    // 왜: 화면의 90일·180일·1년 범위를 채우려고 일별 내역은 합계보다 길게 읽는다
    private const int DefaultWindowDays = 365;

    /// <summary>
    /// Produces a consumption digest for Claude Code.
    /// </summary>
    public async Task<ConsumptionDigest> AnalyzeClaudeAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(DefaultWindowDays - 1));

        var slices = await LogDigestor.DigestClaudeLogsAsync(windowStart, today, cancellationToken).ConfigureAwait(false);
        return BuildDigest(slices, today, DefaultWindowDays);
    }

    /// <summary>
    /// Produces a consumption digest for Claude Code from a specific log directory.
    /// </summary>
    public async Task<ConsumptionDigest> AnalyzeClaudeAsync(string logDirectory, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(DefaultWindowDays - 1));

        var slices = await LogDigestor.DigestClaudeLogsAsync(logDirectory, windowStart, today, cancellationToken).ConfigureAwait(false);
        return BuildDigest(slices, today, DefaultWindowDays);
    }

    /// <summary>
    /// Produces a consumption digest for Codex.
    /// </summary>
    public async Task<ConsumptionDigest> AnalyzeCodexAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(DefaultWindowDays - 1));

        var slices = await LogDigestor.DigestCodexLogsAsync(windowStart, today, cancellationToken).ConfigureAwait(false);
        return BuildDigest(slices, today, DefaultWindowDays);
    }

    private static ConsumptionDigest BuildDigest(
        IReadOnlyList<ConsumptionSlice> slices,
        DateOnly today,
        int windowDays)
    {
        if (slices.Count == 0)
            return ConsumptionDigest.None;

        // Today's consumption
        var todaySlices = slices.Where(s => s.Period == today).ToList();
        var todayTokens = todaySlices.Aggregate(TokenLedger.Empty, (acc, s) => acc.Combine(s.Tokens));
        var todayCost = todaySlices.Sum(s => s.ComputedCostUsd);

        // Rolling window consumption
        var totalsStart = today.AddDays(-(TotalsWindowDays - 1));
        var totalsSlices = slices.Where(s => s.Period >= totalsStart).ToList();
        var windowTokens = totalsSlices.Aggregate(TokenLedger.Empty, (acc, s) => acc.Combine(s.Tokens));
        var windowCost = totalsSlices.Sum(s => s.ComputedCostUsd);

        return new ConsumptionDigest
        {
            TodayTokens = todayTokens,
            TodayCostUsd = todayCost,
            RollingWindowTokens = windowTokens,
            RollingWindowCostUsd = windowCost,
            RollingWindowDays = TotalsWindowDays,
            DailyBreakdown = slices,
            ComputedAt = DateTimeOffset.UtcNow
        };
    }
}
