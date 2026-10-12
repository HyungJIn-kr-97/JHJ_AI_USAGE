using costats.Application.Settings;
using costats.Core.Pulse;

namespace costats.Application.Alerts;

/// <summary>
/// 사용률 임계 알림의 판정기. 갱신마다 PulseState 의 모든 한도 창(세션 · 주간 · 모델별 주간)을 보고
/// 설정의 단계(80 · 85 · …)를 새로 넘은 창만 UsageAlert 로 돌려준다. 문구는 만들지 않는다 — App 층이 Loc 으로 입힌다.
/// 계약: 같은 창의 같은 단계는 한 번만 알린다. 창이 초기화되면(ResetsAt 이 바뀌거나 사용률이 첫 단계 아래로 내려가면) 다시 센다.
/// 계약: 권장(AdviceKind)은 지금까지의 평균 소진 속도로 초기화 시점의 사용률을 예측해 정한다 — UsagePace 와 같은 계산이다.
/// </summary>
public sealed class UsageAlertEvaluator
{
    private static readonly TimeSpan ClaudeSession = TimeSpan.FromHours(5);
    private static readonly TimeSpan OneWeek = TimeSpan.FromDays(7);
    // 왜: 주간 창은 「초기화 임박」을 시간 단위로 본다 — 세션의 분 단위 값을 그대로 쓰면 7일 창에서 뜻이 없다
    private static readonly TimeSpan WeeklyRelax = TimeSpan.FromHours(12);
    private static readonly TimeSpan ResetJitter = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, (int Step, DateTimeOffset? ResetsAt)> _fired = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>지금 상태의 모든 한도 창 — 설정 탭의 「현재 상태」 표와 알림 판정이 같은 목록을 본다.</summary>
    public IReadOnlyList<UsageWindowStatus> Inspect(PulseState state, AppSettings settings, DateTimeOffset now)
    {
        var rows = new List<UsageWindowStatus>();
        foreach (var (providerId, reading) in state.Providers)
        {
            if (reading.Usage is not { } usage)
            {
                continue;
            }

            var kind = providerId.Split(':')[0].ToLowerInvariant();
            if (settings.UsageAlertSession && Percent(usage.SessionUsed, usage.SessionLimit) is { } session)
            {
                var window = usage.SessionWindow;
                rows.Add(Build(providerId, kind, UsageWindowKind.Session, null, session,
                    window?.ResetsAt, window?.Duration ?? (kind == "claude" ? ClaudeSession : null), settings, now));
            }

            if (settings.UsageAlertWeekly && Percent(usage.WeekUsed, usage.WeekLimit) is { } week)
            {
                var window = usage.WeekWindow;
                rows.Add(Build(providerId, kind, UsageWindowKind.Weekly, null, week,
                    window?.ResetsAt, window?.Duration ?? (kind == "claude" ? OneWeek : null), settings, now));
            }

            if (settings.UsageAlertModelWeek)
            {
                foreach (var quota in usage.ModelWeeks)
                {
                    rows.Add(Build(providerId, kind, UsageWindowKind.ModelWeek, quota.Label, quota.UsedPercent,
                        quota.ResetsAt, OneWeek, settings, now));
                }
            }
        }

        return rows;
    }

    /// <summary>이번 갱신에서 새로 단계를 넘은 창만 돌려준다. 호출할 때마다 상태가 갱신된다.</summary>
    public IReadOnlyList<UsageAlert> Evaluate(PulseState state, AppSettings settings, DateTimeOffset now)
    {
        var alerts = new List<UsageAlert>();
        if (!settings.UsageAlertEnabled)
        {
            return alerts;
        }

        var steps = settings.UsageAlertSteps.Where(s => s is > 0 and <= 100).Distinct().OrderBy(s => s).ToList();
        if (steps.Count == 0)
        {
            return alerts;
        }

        foreach (var row in Inspect(state, settings, now))
        {
            var key = row.Key;
            var step = steps.Where(s => row.Percent + 1e-9 >= s).DefaultIfEmpty(0).Max();
            _fired.TryGetValue(key, out var last);

            // 왜: 초기화 시각이 바뀌었거나 첫 단계 아래로 내려갔으면 새 창이다 — 지난 알림 기록을 버린다
            // 함정: API 의 resets_at 은 갱신마다 밀리초가 흔들린다(실측 2026-10-12 · 같은 80% 가 1분마다 다시 울렸다) — 1분 넘게 움직였을 때만 새 창이다
            var resetChanged = last.ResetsAt is { } lastReset && row.ResetsAt is { } cur && (cur - lastReset).Duration() > ResetJitter;
            if (step == 0 || resetChanged)
            {
                _fired.Remove(key);
                last = default;
            }

            if (step > 0 && step > last.Step)
            {
                _fired[key] = (step, row.ResetsAt);
                alerts.Add(new UsageAlert(row, step));
            }
        }

        return alerts;
    }

    public void Clear() => _fired.Clear();

    private static double? Percent(long? used, long? limit) =>
        used is { } u && limit is { } l && l > 0 ? Math.Clamp(u * 100.0 / l, 0, 100) : null;

    private static UsageWindowStatus Build(
        string providerId, string kind, UsageWindowKind windowKind, string? label, double percent,
        DateTimeOffset? resetsAt, TimeSpan? duration, AppSettings settings, DateTimeOffset now)
    {
        TimeSpan? timeLeft = resetsAt is { } r && r > now ? r - now : null;
        double? ratePerHour = null;
        double? projected = null;
        TimeSpan? eta = null;

        if (timeLeft is { } left && duration is { } dur && dur > left)
        {
            var elapsed = dur - left;
            if (elapsed > TimeSpan.Zero)
            {
                ratePerHour = percent / elapsed.TotalHours;
                projected = Math.Min(999, percent + ratePerHour.Value * left.TotalHours);
                if (ratePerHour > 0 && percent < 100)
                {
                    var hours = (100 - percent) / ratePerHour.Value;
                    eta = hours < left.TotalHours ? TimeSpan.FromHours(hours) : null;
                }
            }
        }

        var advice = settings.UsageAlertRecommend
            ? Advise(windowKind, percent, timeLeft, projected, eta, TimeSpan.FromMinutes(Math.Max(0, settings.UsageAlertRelaxMinutes)))
            : AdviceKind.None;

        return new UsageWindowStatus(providerId, kind, windowKind, label, percent, resetsAt, timeLeft, ratePerHour, projected, eta, advice);
    }

    // 계약: 모델별 주간(Fable)은 「그 모델을 쓰지 말라」, 세션·주간은 「어느 등급의 모델을 쓰라」로 갈린다
    private static AdviceKind Advise(UsageWindowKind kind, double percent, TimeSpan? timeLeft, double? projected, TimeSpan? eta, TimeSpan sessionRelax)
    {
        if (kind == UsageWindowKind.ModelWeek)
        {
            if (percent >= 100) return AdviceKind.ModelExhausted;
            if (percent >= 95) return AdviceKind.ModelNearlyExhausted;
            return eta is not null ? AdviceKind.ModelWillExhaust : AdviceKind.ModelFine;
        }

        if (percent >= 100)
        {
            return AdviceKind.ExhaustedWaitForReset;
        }

        if (timeLeft is { } left && left <= (kind == UsageWindowKind.Session ? sessionRelax : WeeklyRelax))
        {
            return AdviceKind.ResetSoonAnyModel;
        }

        if (eta is { } e && timeLeft is { } total)
        {
            // 왜: 남은 시간의 절반도 못 가면 가장 가벼운 모델, 그 밖은 한 단계 아래 모델 — 두 단계면 충분히 읽힌다
            return e < total / 2 ? AdviceKind.WillExhaustUseLightest : AdviceKind.WillExhaustUseLighter;
        }

        if (projected is not null)
        {
            return AdviceKind.LastsKeepModel;
        }

        return AdviceKind.None;
    }
}

public enum UsageWindowKind
{
    Session,
    Weekly,
    ModelWeek
}

public enum AdviceKind
{
    None,
    /// 초기화가 임박해 고토큰 모델을 써도 된다
    ResetSoonAnyModel,
    /// 한도 소진 — 초기화를 기다려야 한다
    ExhaustedWaitForReset,
    /// 지금 속도면 초기화 전에 소진 — 한 단계 가벼운 모델 권장
    WillExhaustUseLighter,
    /// 남은 시간의 절반도 못 간다 — 가장 가벼운 모델 권장
    WillExhaustUseLightest,
    /// 지금 속도면 초기화까지 버틴다 — 현재 모델 유지
    LastsKeepModel,
    /// 모델별 주간 한도 소진 — 그 모델을 쓰지 않는다
    ModelExhausted,
    /// 모델별 주간 95% 이상 — 어려운 작업에만 남긴다
    ModelNearlyExhausted,
    /// 모델별 주간이 지금 속도면 초기화 전에 소진
    ModelWillExhaust,
    /// 모델별 주간이 초기화까지 버틴다
    ModelFine
}

/// <summary>한도 창 하나의 지금 상태. Percent 는 0~100, RatePerHour 는 %/시간(창 시작부터의 평균), ProjectedAtReset 은 초기화 시점 예상 사용률.</summary>
public sealed record UsageWindowStatus(
    string ProviderId,
    string ProviderKind,
    UsageWindowKind Kind,
    string? ModelLabel,
    double Percent,
    DateTimeOffset? ResetsAt,
    TimeSpan? TimeLeft,
    double? RatePerHour,
    double? ProjectedAtReset,
    TimeSpan? EtaUntilExhausted,
    AdviceKind Advice)
{
    public string Key => $"{ProviderId}|{Kind}|{ModelLabel}";
}

public sealed record UsageAlert(UsageWindowStatus Window, int Step);
