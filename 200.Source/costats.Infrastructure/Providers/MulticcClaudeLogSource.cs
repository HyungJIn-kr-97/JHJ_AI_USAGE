using costats.Application.Pulse;
using costats.Core.Pulse;
using costats.Infrastructure.Expense;
using costats.Infrastructure.Usage;
using static costats.Core.Pulse.UsageFormatter;

namespace costats.Infrastructure.Providers;

/// <summary>
/// An <see cref="ISignalSource"/> for a single multicc profile.
/// Identical logic to <see cref="ClaudeLogSource"/> but reads from the profile's configDir.
/// </summary>
public sealed class MulticcClaudeLogSource : ISignalSource, IDisposable
{
    private static readonly TimeSpan SessionDuration = TimeSpan.FromHours(5);
    private static readonly TimeSpan WeekDuration = TimeSpan.FromDays(7);

    private readonly MulticcProfile _profile;
    private readonly UsageLogScanner _scanner = new();
    private readonly ClaudeOAuthUsageFetcher _oauthFetcher;
    private readonly ExpenseAnalyzer _expenseAnalyzer = new();
    private readonly string _logDirectory;

    public MulticcClaudeLogSource(MulticcProfile profile)
    {
        _profile = profile;
        _oauthFetcher = new ClaudeOAuthUsageFetcher(profile.ConfigDir);
        _logDirectory = Path.Combine(profile.ConfigDir, "projects");
    }

    public ProviderProfile Profile => new(
        $"claude:{_profile.Name}",
        _profile.Name,
        "#FF7A00");

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // OAuth is a network call - run in parallel with file I/O
        var oauthTask = _oauthFetcher.FetchAsync(cancellationToken);

        // Log scan and expense analysis both read the same files - run sequentially
        var roots = LogRoots();
        var logResult = await _scanner.ScanClaudeAsync(roots, cancellationToken).ConfigureAwait(false);
        var consumption = await SafeAnalyzeExpenseAsync(roots, cancellationToken).ConfigureAwait(false);

        var oauthResult = await oauthTask.ConfigureAwait(false);

        // 왜: 한도 조회가 실패해도(토큰 만료 401) 로그로 센 비용은 버리지 않는다 — 버리면 그 계정 카드·합계가 갱신이 멈춘 것처럼 보였다
        if (oauthResult is null && logResult.SessionTokens == 0 && logResult.WeekTokens == 0 &&
            (consumption is null || consumption.RollingWindowTokens.TotalConsumed == 0))
        {
            return new ProviderReading(
                Usage: null,
                Identity: null,
                // 왜: 연동된 프로그램이 없는 추가 자리는 로그가 0 이라 늘 여기로 온다 — 한도 조회가 왜 실패했는지 가리지 않는다
                StatusSummary: !_oauthFetcher.HasToken
                    ? "No Claude token on this PC — sign in from Settings › Accounts"
                    : _oauthFetcher.LastError is { } noDataError
                        ? $"Usage lookup failed ({noDataError})"
                        : $"No data for {_profile.Name}",
                CapturedAt: now,
                Confidence: ReadingConfidence.Low,
                Source: ReadingSource.LocalLog);
        }

        // Prefer OAuth data for percentages
        var sessionUsedPercent = oauthResult?.FiveHourUsedPercent;
        var weeklyUsedPercent = oauthResult?.SevenDayUsedPercent;

        var sessionResetsAt = oauthResult?.FiveHourResetsAt ?? CalculateSessionReset(logResult.SessionStart, now);
        var weeklyResetsAt = oauthResult?.SevenDayResetsAt ?? CalculateWeeklyReset(now);

        var sessionWindow = new QuotaWindow(SessionDuration, sessionResetsAt);
        var weekWindow = new QuotaWindow(WeekDuration, weeklyResetsAt);

        long? sessionUsed;
        long? sessionLimit;
        long? weekUsed;
        long? weekLimit;

        if (sessionUsedPercent is not null)
        {
            sessionUsed = (long)Math.Round(sessionUsedPercent.Value);
            sessionLimit = 100;
        }
        else
        {
            sessionUsed = logResult.SessionTokens > 0 ? logResult.SessionTokens : null;
            sessionLimit = null;
        }

        if (weeklyUsedPercent is not null)
        {
            weekUsed = (long)Math.Round(weeklyUsedPercent.Value);
            weekLimit = 100;
        }
        else
        {
            weekUsed = logResult.WeekTokens > 0 ? logResult.WeekTokens : null;
            weekLimit = null;
        }

        // Build overage spending bucket when available
        MonetaryBucket? spendingBucket = null;
        if (oauthResult is { OverageEnabled: true, OverageSpentUsd: not null, OverageCeilingUsd: not null })
        {
            spendingBucket = MonetaryBucket.ForOverageSpend(
                (decimal)oauthResult.OverageSpentUsd.Value,
                (decimal)oauthResult.OverageCeilingUsd.Value);
        }

        var usage = new UsagePulse(
            ProviderId: Profile.ProviderId,
            CapturedAt: oauthResult?.FetchedAt ?? logResult.LatestTimestamp ?? now,
            SessionUsed: sessionUsed,
            SessionLimit: sessionLimit,
            WeekUsed: weekUsed,
            WeekLimit: weekLimit,
            SpendingBucket: spendingBucket,
            Consumption: consumption,
            SessionWindow: sessionWindow,
            WeekWindow: weekWindow)
        {
            ModelWeeks = oauthResult?.ModelWeeks ?? []
        };

        var planText = FormatPlanText(oauthResult?.SubscriptionType);
        var statusSummary = oauthResult is not null
            ? _oauthFetcher.StaleSummary ?? $"Updated {FormatRelativeTime(oauthResult.FetchedAt, now)}"
            : !_oauthFetcher.HasToken
                ? "No Claude token on this PC — sign in from Settings › Accounts"
                : _oauthFetcher.LastError is { } error
                    ? $"Usage lookup failed ({error})"
                    : $"Updated {FormatRelativeTime(logResult.LatestTimestamp ?? now, now)}";

        var confidence = oauthResult is not null ? ReadingConfidence.High : ReadingConfidence.Medium;
        var source = oauthResult is not null ? ReadingSource.Api : ReadingSource.LocalLog;

        return new ProviderReading(
            Usage: usage,
            Identity: new IdentityCard(Profile.ProviderId, _profile.Name, null, null, planText, "OAuth", oauthResult?.RateLimitTier),
            StatusSummary: statusSummary,
            CapturedAt: usage.CapturedAt,
            Confidence: confidence,
            Source: source);
    }

    private static string FormatPlanText(string? subscriptionType)
    {
        if (string.IsNullOrEmpty(subscriptionType))
        {
            return "Max";
        }

        return char.ToUpper(subscriptionType[0]) + subscriptionType[1..].ToLower();
    }

    private static DateTimeOffset? CalculateSessionReset(DateTimeOffset? sessionStart, DateTimeOffset now)
    {
        if (sessionStart is null)
        {
            return now + SessionDuration;
        }

        var elapsed = now - sessionStart.Value;
        if (elapsed >= SessionDuration)
        {
            return now + SessionDuration;
        }

        return sessionStart.Value + SessionDuration;
    }

    private static DateTimeOffset CalculateWeeklyReset(DateTimeOffset now)
    {
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0 && now.TimeOfDay > TimeSpan.Zero)
        {
            daysUntilMonday = 7;
        }

        var nextMonday = now.Date.AddDays(daysUntilMonday);
        return new DateTimeOffset(nextMonday, TimeSpan.Zero);
    }

    // 계약: 기본 자리는 기본 폴더 중 자기 프로그램 몫만, 추가 자리는 자기 폴더 전체 + 기본 폴더 중 자기에게 연동된 프로그램 몫
    // 계약: 에이전트 모드 폴더는 모든 자리가 함께 읽고, 세션 경로의 계정UUID 가 이 자리인 줄만 센다
    private IReadOnlyList<ClaudeLogRoot> LogRoots()
    {
        var shared = new ClaudeLogRoot(Path.Combine(ClaudeProgramRouter.DefaultConfigDir, "projects"), Profile.ProviderId);
        var agent = ClaudeProgramRouter.AgentProjectDirs().Select(dir => new ClaudeLogRoot(dir, Profile.ProviderId));
        return ClaudeProgramRouter.IsDefaultDir(_profile.ConfigDir)
            ? [shared, .. agent]
            : [new ClaudeLogRoot(_logDirectory, null), shared, .. agent];
    }

    private async Task<ConsumptionDigest?> SafeAnalyzeExpenseAsync(IReadOnlyList<ClaudeLogRoot> roots, CancellationToken cancellationToken)
    {
        try
        {
            return await _expenseAnalyzer.AnalyzeClaudeAsync(roots, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Cost analysis failure should not break usage display
            return null;
        }
    }

    public void Dispose()
    {
        _oauthFetcher.Dispose();
    }
}
