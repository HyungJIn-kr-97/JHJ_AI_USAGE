using costats.Core.Pulse;
using costats.Infrastructure.Providers;

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

    // 계약: 폴더마다 주인을 달아 읽는다 — ClaudeLogRoot 참조
    public async Task<ConsumptionDigest> AnalyzeClaudeAsync(IReadOnlyList<ClaudeLogRoot> roots, CancellationToken cancellationToken = default)
    {
        // 왜: 줄의 주인을 가르기 전에 지금 로그인한 계정을 다시 읽는다 — 갱신 주기마다 연동이 따라간다(ClaudeProgramLinks)
        costats.Infrastructure.Providers.ClaudeProgramRouter.SyncLinksIfDue();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(DefaultWindowDays - 1));

        var slices = await LogDigestor.DigestClaudeLogsAsync(roots, windowStart, today, cancellationToken).ConfigureAwait(false);
        return BuildDigest(slices, today, DefaultWindowDays);
    }

    /// <summary>
    /// Produces a consumption digest for Codex.
    /// </summary>
    public async Task<ConsumptionDigest> AnalyzeCodexAsync(CancellationToken cancellationToken = default, string? codexHome = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var windowStart = today.AddDays(-(DefaultWindowDays - 1));

        var slices = await LogDigestor.DigestCodexLogsAsync(windowStart, today, cancellationToken, codexHome).ConfigureAwait(false);
        return BuildDigest(slices, today, DefaultWindowDays);
    }

    /// <summary>
    /// 계약: 이미 일별·모델별로 모은 내역을 받아 같은 규칙(오늘·최근 30일)으로 요약한다 — 자기 로그 형식을 직접 읽는 제공자용.
    /// </summary>
    public ConsumptionDigest FromSlices(IReadOnlyList<ConsumptionSlice> slices) =>
        BuildDigest(slices, DateOnly.FromDateTime(DateTime.Now), DefaultWindowDays);

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
