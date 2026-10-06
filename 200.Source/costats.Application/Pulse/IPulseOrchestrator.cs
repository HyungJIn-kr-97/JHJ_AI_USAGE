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

    // 계약: 실행 중에 계정 소스를 더한다 — remove 에 걸린 기존 소스는 빼고, 다음 갱신부터 새 구성으로 읽는다
    void ReplaceSources(IEnumerable<ISignalSource> add, Func<ISignalSource, bool>? remove = null);

    /// <summary>
    /// 계약: 다음 예약 갱신 시각(로컬). 주기를 시계 눈금에 맞춘다 — 5분이면 :00 · :05 · :10 … 아직 잡히지 않았으면 null.
    /// </summary>
    DateTimeOffset? NextRefreshAt { get; }
}
