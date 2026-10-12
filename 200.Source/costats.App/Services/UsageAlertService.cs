using System.Collections.ObjectModel;
using System.Windows;
using costats.App.Views;
using costats.Application.Alerts;
using costats.Application.Pulse;
using costats.Application.Settings;
using costats.Core.Pulse;
using Serilog;

namespace costats.App.Services;

/// <summary>
/// 사용률 임계 알림 — 갱신 스트림을 구독해 단계를 넘은 창을 토스트·Windows 알림으로 낸다.
/// 판정은 Application 층 UsageAlertEvaluator, 문구는 여기서 Loc 으로 입힌다. 설정 「사용률 알림」 탭의 「현재 상태」 표도 여기서 만든다.
/// 계약: Notify(Windows 알림) · OpenWidget · Widget 은 TrayHost 가 만들어진 뒤 App 이 꽂는다 — 생성자에서 TrayHost 를 받으면 순환이 된다.
/// </summary>
public sealed class UsageAlertService : IObserver<PulseState>, IDisposable
{
    private readonly AppSettings _settings;
    private readonly TaskbarPositionService _anchor;
    private readonly UsageAlertEvaluator _evaluator = new();
    private readonly Dictionary<string, ProviderReading> _readings = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDisposable _subscription;
    private UsageAlertToastWindow? _toast;
    private PulseState? _merged;

    public Action<string, string>? Notify { get; set; }
    public Action? OpenWidget { get; set; }
    public Func<Window?> Widget { get; set; } = () => null;

    /// <summary>설정 탭 「현재 상태」 — 갱신마다 통째로 다시 채운다.</summary>
    public ObservableCollection<UsageAlertStatusRow> Status { get; } = [];

    public UsageAlertService(IPulseOrchestrator orchestrator, AppSettings settings, TaskbarPositionService anchor)
    {
        _settings = settings;
        _anchor = anchor;
        _subscription = orchestrator.PulseStream.Subscribe(this);
        Loc.LanguageChanged += RebuildStatus;
    }

    public void OnNext(PulseState value)
    {
        // 왜: 갱신 중 상태와 부분 상태(한 계정만)는 판정 재료가 아니다 — 마지막 완전한 값에 덧씌워 본다
        if (value.IsRefreshing || value.Providers.Count == 0)
        {
            return;
        }

        foreach (var (id, reading) in value.Providers)
        {
            _readings[id] = reading;
        }

        _merged = value with { Providers = new Dictionary<string, ProviderReading>(_readings, StringComparer.OrdinalIgnoreCase) };
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var alerts = _evaluator.Evaluate(_merged, _settings, now);
                RebuildStatus();
                foreach (var alert in alerts)
                {
                    Show(alert.Window, alert.Step);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Usage alert evaluation failed");
            }
        });
    }

    public void OnError(Exception error) { }

    public void OnCompleted() { }

    /// <summary>설정이 바뀐 뒤 — 켜졌으면 지금 상태를 처음부터 다시 판정해 바로 알린다.</summary>
    public void Reevaluate(bool restart)
    {
        if (restart)
        {
            _evaluator.Clear();
        }

        if (_merged is { } state)
        {
            OnNext(state);
        }
    }

    /// <summary>설정 탭 「테스트 알림」 — 가짜 세션 85% 로 토스트·알림 모양을 보여 준다.</summary>
    public void Test()
    {
        var now = DateTimeOffset.UtcNow;
        var sample = new UsageWindowStatus("claude:default", "claude", UsageWindowKind.Session, null, 85,
            now.AddHours(2).AddMinutes(10), TimeSpan.FromHours(2) + TimeSpan.FromMinutes(10), 30, 150, TimeSpan.FromMinutes(30),
            _settings.UsageAlertRecommend ? AdviceKind.WillExhaustUseLightest : AdviceKind.None);
        Show(sample, 85, force: true);
    }

    /// <summary>설정 「토스트 위치」가 바뀌었다 — 떠 있는 토스트를 바로 옮긴다.</summary>
    public void RepositionToast() => _toast?.Reposition();

    private void Show(UsageWindowStatus w, int step, bool force = false)
    {
        var title = $"{AccountLabel(w.ProviderId)} · {WindowLabel(w)} {(int)Math.Round(w.Percent)}%";
        var body = BodyOf(w);
        var advice = AdviceOf(w);
        var style = _settings.UsageAlertStyle;
        var toast = style is "toast" or "both" || force;
        var balloon = style is "balloon" or "both";

        if (toast)
        {
            _toast ??= new UsageAlertToastWindow(_anchor, Widget, () => _settings.UsageAlertPosition);
            _toast.Push(new UsageToastItem
            {
                Key = w.Key,
                Kind = w.Kind,
                // 왜: 우측에 큰 % 가 따로 있다 — 제목에 또 넣으면 「세션 82% 82%」 로 읽힌다
                Title = $"{AccountLabel(w.ProviderId)} · {WindowLabel(w)}",
                Body = body,
                Advice = advice,
                Percent = (int)Math.Round(w.Percent),
                BarColor = ColorOf(w.Percent),
                Open = OpenWidget,
            }, _settings.UsageAlertToastSeconds);
        }

        if (balloon)
        {
            // 왜: Windows 알림은 폭이 고정이라 문장마다 줄을 바꾼다(080.CS앱-화면-공통 §4)
            var text = advice.Length > 0 ? body + Environment.NewLine + advice : body;
            Notify?.Invoke(title, text);
        }

        DiagnosticsLog.Record("alert", $"{w.ProviderId} {w.Kind}{(w.ModelLabel is null ? "" : "/" + w.ModelLabel)} {w.Percent:0}% step {step} · {advice}");
    }

    private void RebuildStatus()
    {
        if (_merged is not { } state)
        {
            return;
        }

        var rows = _evaluator.Inspect(state, _settings, DateTimeOffset.UtcNow)
            .OrderByDescending(w => w.Percent)
            .Select(w => new UsageAlertStatusRow(
                AccountLabel(w.ProviderId),
                WindowLabel(w),
                $"{(int)Math.Round(w.Percent)}%",
                ColorOf(w.Percent),
                BodyOf(w),
                AdviceOf(w)))
            .ToList();

        Status.Clear();
        foreach (var row in rows)
        {
            Status.Add(row);
        }
    }

    private string AccountLabel(string providerId)
    {
        var metaId = providerId.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "claude:default" : providerId;
        if (metaId.Contains(':'))
        {
            return AccountMeta.DisplayNameOf(_settings, metaId);
        }

        var kind = AccountMeta.KindOf(metaId);
        return kind.Length > 1 ? char.ToUpperInvariant(kind[0]) + kind[1..] : kind;
    }

    private static string WindowLabel(UsageWindowStatus w) => w.Kind switch
    {
        UsageWindowKind.ModelWeek => Loc.T("Weekly · {0}", w.ModelLabel),
        UsageWindowKind.Session => Loc.T(w.ProviderKind == "gemini" ? "Pro models · daily" : "Session"),
        _ => Loc.T(w.ProviderKind == "gemini" ? "Flash models · daily" : "Weekly"),
    };

    // 계약: 초기화 시각 한 줄 + 속도 한 줄 — 「{rate}%/h · 2h 10m 후 소진」 꼴
    private static string BodyOf(UsageWindowStatus w)
    {
        var parts = new List<string>();
        if (w.ResetsAt is { } resetsAt)
        {
            parts.Add(Loc.Tr($"Resets {UsageFormatter.ResetCountdown(resetsAt)}"));
        }

        if (w.RatePerHour is { } rate)
        {
            var pace = $"{rate:0.#}%/h";
            if (w.EtaUntilExhausted is { } eta)
            {
                pace += " · " + Loc.T("runs out in {0}", Loc.Duration(Span(eta)));
            }
            else if (w.ProjectedAtReset is { } projected)
            {
                pace += " · " + Loc.T("{0}% at reset", (int)Math.Round(projected));
            }

            parts.Add(pace);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string AdviceOf(UsageWindowStatus w)
    {
        var claude = w.ProviderKind == "claude";
        var heavy = claude ? "Fable" : Loc.T("the heaviest model");
        var lighter = claude ? "Opus" : Loc.T("a lighter model");
        var lightest = claude ? "Sonnet · Haiku" : Loc.T("the lightest model");
        var model = w.ModelLabel ?? heavy;
        var alt = claude && model.Equals("Fable", StringComparison.OrdinalIgnoreCase) ? "Opus" : lighter;
        var left = w.TimeLeft is { } t ? Loc.Duration(Span(t)) : string.Empty;

        return w.Advice switch
        {
            AdviceKind.ResetSoonAnyModel => Loc.T("Resets in {0} — any model is fine, {1} included", left, heavy),
            AdviceKind.ExhaustedWaitForReset => Loc.T("Limit reached — wait for the reset"),
            AdviceKind.WillExhaustUseLighter => Loc.T("Runs out before the reset at this pace — prefer {0} over {1}", lighter, heavy),
            AdviceKind.WillExhaustUseLightest => Loc.T("Runs out well before the reset — switch to {0}", lightest),
            AdviceKind.LastsKeepModel => Loc.T("Lasts until the reset at this pace — keep the current model"),
            AdviceKind.ModelExhausted => Loc.T("{0} is used up this week — use {1} until the reset", model, alt),
            AdviceKind.ModelNearlyExhausted => Loc.T("{0} is almost used up — keep it for hard tasks, use {1} otherwise", model, alt),
            AdviceKind.ModelWillExhaust => Loc.T("{0} runs out before the weekly reset at this pace — mix in {1}", model, alt),
            AdviceKind.ModelFine => Loc.T("{0} lasts until the weekly reset at this pace", model),
            _ => string.Empty,
        };
    }

    // 계약: UsageFormatter.ResetCountdown 과 같은 모양("2h 10m")이라 Loc.Duration 이 그대로 옮긴다
    private static string Span(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        var days = minutes / (24 * 60);
        var hours = minutes / 60 % 24;
        var mins = minutes % 60;
        if (days > 0) return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        if (hours > 0) return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
        return $"{mins}m";
    }

    // 왜: ProviderPulseViewModel.GetPercentTextColor 와 같은 네 색 — 팝업의 숫자 색과 어긋나지 않게
    private static string ColorOf(double percent) => percent switch
    {
        >= 95 => "#DC2626",
        >= 80 => "#C2410C",
        >= 50 => "#B45309",
        _ => "#047857",
    };

    public void Dispose()
    {
        Loc.LanguageChanged -= RebuildStatus;
        _subscription.Dispose();
        _toast?.Close();
    }
}

/// <summary>설정 탭 「현재 상태」 한 줄.</summary>
public sealed record UsageAlertStatusRow(string Account, string Window, string PercentText, string Color, string Pace, string Advice);
