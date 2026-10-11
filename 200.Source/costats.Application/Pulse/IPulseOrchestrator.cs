using costats.Core.Pulse;

namespace costats.Application.Pulse;

public interface IPulseOrchestrator
{
    IObservable<PulseState> PulseStream { get; }

    Task RefreshOnceAsync(RefreshTrigger trigger, CancellationToken cancellationToken);

    /// <summary>
    /// Silently refresh a specific provider (no loading indicator).
    /// 계약: waitForTurn 이 false 면 다른 갱신이 도는 중일 때 건너뛰고, true 면 그 갱신이 끝나기를 기다렸다가 읽는다.
    /// </summary>
    Task RefreshProviderAsync(string providerId, CancellationToken cancellationToken, bool waitForTurn = false);

    void UpdateRefreshInterval(TimeSpan interval);

    // 계약: 실행 중에 계정 소스를 더한다 — remove 에 걸린 기존 소스는 빼고, 다음 갱신부터 새 구성으로 읽는다
    void ReplaceSources(IEnumerable<ISignalSource> add, Func<ISignalSource, bool>? remove = null);

    /// <summary>
    /// 계약: 다음 예약 갱신 시각(로컬). 주기를 시계 눈금에 맞춘다 — 5분이면 :00 · :05 · :10 … 아직 잡히지 않았으면 null.
    /// </summary>
    DateTimeOffset? NextRefreshAt { get; }
}
