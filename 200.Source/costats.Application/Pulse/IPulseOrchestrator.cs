using costats.Core.Pulse;

namespace costats.Application.Pulse;

public interface IPulseOrchestrator
{
    IObservable<PulseState> PulseStream { get; }

    Task RefreshOnceAsync(RefreshTrigger trigger, CancellationToken cancellationToken);

    /// <summary>
    /// Silently refresh a specific provider (no loading indicator).
    /// </summary>
    Task RefreshProviderAsync(string providerId, CancellationToken cancellationToken);

    void UpdateRefreshInterval(TimeSpan interval);

    /// <summary>
    /// 계약: 다음 예약 갱신 시각(로컬). 주기를 시계 눈금에 맞춘다 — 5분이면 :00 · :05 · :10 … 아직 잡히지 않았으면 null.
    /// </summary>
    DateTimeOffset? NextRefreshAt { get; }
}
