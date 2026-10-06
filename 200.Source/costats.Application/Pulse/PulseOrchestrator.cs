using costats.Application.Abstractions;
using costats.Core.Pulse;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace costats.Application.Pulse;

public sealed class PulseOrchestrator : BackgroundService, IPulseOrchestrator
{
    // 왜: 계정을 더하면 목록을 통째로 바꿔 끼운다 — 도는 중인 갱신이 반쯤 바뀐 목록을 보지 않는다
    private volatile IReadOnlyList<ISignalSource> _sources;
    private readonly ISourceSelector _selector;
    private readonly IClock _clock;
    private readonly PulseBroadcaster _broadcaster;
    private readonly IPulseSnapshotWriter _snapshotWriter;
    private readonly ILogger<PulseOrchestrator> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _intervalLock = new();

    private TimeSpan _refreshInterval;
    private CancellationTokenSource? _timerCts;
    private PulseState? _lastState;
    private bool _hasSuccessfulLoad;

    public PulseOrchestrator(
        IEnumerable<ISignalSource> sources,
        ISourceSelector selector,
        IClock clock,
        PulseBroadcaster broadcaster,
        IPulseSnapshotWriter snapshotWriter,
        IOptions<PulseOptions> options,
        ILogger<PulseOrchestrator> logger)
    {
        _sources = sources.ToList();
        _selector = selector;
        _clock = clock;
        _broadcaster = broadcaster;
        _snapshotWriter = snapshotWriter;
        _refreshInterval = options.Value.RefreshInterval;
        _logger = logger;
    }

    public IObservable<PulseState> PulseStream => _broadcaster;

    public void ReplaceSources(IEnumerable<ISignalSource> add, Func<ISignalSource, bool>? remove = null)
    {
        lock (_intervalLock)
        {
            var kept = _sources.Where(source => remove is null || !remove(source)).ToList();
            var known = kept.Select(source => source.Profile.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            kept.AddRange(add.Where(source => known.Add(source.Profile.ProviderId)));
            _sources = kept;
        }
    }

    public void UpdateRefreshInterval(TimeSpan interval)
    {
        lock (_intervalLock)
        {
            _refreshInterval = interval;
            try { _timerCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        _logger.LogInformation("Refresh interval updated to {Interval}", interval);
    }

    public async Task RefreshOnceAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ShouldShowShimmer(trigger))
            {
                PublishRefreshing(trigger);
            }

            var byProvider = _sources
                .GroupBy(source => source.Profile.ProviderId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<ISignalSource>)group.ToList());

            var errors = new List<string>();

            // Keep provider reads sequential to avoid overlapping heavy file scans.
            var providerReads = new Dictionary<string, ProviderReading>(StringComparer.OrdinalIgnoreCase);
            foreach (var (providerId, providerSources) in byProvider)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reading = await _selector.SelectAsync(providerId, providerSources, cancellationToken).ConfigureAwait(false);
                providerReads[providerId] = reading;
            }

            var state = new PulseState(providerReads, _clock.UtcNow, errors, false, trigger);
            _lastState = state;
            _hasSuccessfulLoad = true;
            _broadcaster.Publish(state);
            await _snapshotWriter.WriteAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pulse refresh failed");
            var keepRefreshing = trigger == RefreshTrigger.Initial && !_hasSuccessfulLoad;
            var baseState = _lastState ?? new PulseState(
                new Dictionary<string, ProviderReading>(StringComparer.OrdinalIgnoreCase),
                _clock.UtcNow,
                Array.Empty<string>(),
                keepRefreshing,
                trigger);

            var state = baseState with
            {
                LastRefresh = _clock.UtcNow,
                Errors = new List<string> { ex.Message },
                IsRefreshing = keepRefreshing,
                Trigger = trigger
            };

            if (!keepRefreshing)
            {
                _lastState ??= state;
            }

            _broadcaster.Publish(state);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task RefreshProviderAsync(string providerId, CancellationToken cancellationToken)
    {
        // Silent refresh - don't wait if another refresh is in progress
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("Skipping silent refresh for {ProviderId} - refresh already in progress", providerId);
            return;
        }

        try
        {
            var providerSources = _sources
                .Where(s => s.Profile.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (providerSources.Count == 0)
            {
                _logger.LogWarning("No sources found for provider {ProviderId}", providerId);
                return;
            }

            var reading = await _selector.SelectAsync(providerId, providerSources, cancellationToken).ConfigureAwait(false);

            // Merge with existing state
            var existingProviders = _lastState?.Providers
                ?? new Dictionary<string, ProviderReading>(StringComparer.OrdinalIgnoreCase);

            var updatedProviders = new Dictionary<string, ProviderReading>(existingProviders, StringComparer.OrdinalIgnoreCase)
            {
                [providerId] = reading
            };

            var state = new PulseState(updatedProviders, _clock.UtcNow, Array.Empty<string>(), false, RefreshTrigger.Silent);
            _lastState = state;
            _broadcaster.Publish(state);

            _logger.LogDebug("Silent refresh completed for {ProviderId}", providerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Silent refresh failed for {ProviderId}", providerId);
            // Silent refresh failures are non-blocking - don't propagate
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshOnceAsync(RefreshTrigger.Initial, stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan currentInterval;
                CancellationToken timerToken;
                lock (_intervalLock)
                {
                    currentInterval = _refreshInterval;
                    _timerCts?.Dispose();
                    _timerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timerToken = _timerCts.Token;
                }

                try
                {
                    // 왜: 「시작한 때로부터 5분마다」가 아니라 시계 눈금(:00 · :05 …)에 맞춘다 — 언제 갱신될지 사용자가 미리 안다
                    var next = NextBoundary(_clock.UtcNow.ToLocalTime(), currentInterval);
                    NextRefreshAt = next;

                    // 함정: Task.Delay 는 절전 중 흐른 시간을 세지 않는다 — 짧게 끊어 자면서 벽시계와 다시 맞춘다
                    TimeSpan remaining;
                    while ((remaining = next - _clock.UtcNow) > TimeSpan.Zero)
                    {
                        var nap = remaining < TimeSpan.FromSeconds(30) ? remaining : TimeSpan.FromSeconds(30);
                        await Task.Delay(nap, timerToken).ConfigureAwait(false);
                    }

                    await RefreshOnceAsync(RefreshTrigger.Scheduled, timerToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Timer was cancelled due to interval change, restart with new interval
                    _logger.LogDebug("Restarting timer with new interval");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "PulseOrchestrator crashed");
            throw;
        }
    }

    public DateTimeOffset? NextRefreshAt { get; private set; }

    // 계약: now 보다 뒤인 첫 눈금 — 로컬 자정부터 interval 간격으로 센다(분 단위 주기는 정시에 맞아떨어진다)
    private static DateTimeOffset NextBoundary(DateTimeOffset localNow, TimeSpan interval)
    {
        var step = Math.Max(TimeSpan.FromSeconds(1).Ticks, interval.Ticks);
        return new DateTimeOffset((localNow.Ticks / step + 1) * step, localNow.Offset);
    }

    private bool ShouldShowShimmer(RefreshTrigger trigger)
    {
        return trigger == RefreshTrigger.Manual || (trigger == RefreshTrigger.Initial && !_hasSuccessfulLoad);
    }

    private void PublishRefreshing(RefreshTrigger trigger)
    {
        // Show last known good state with loading indicator
        var baseState = _lastState ?? new PulseState(
            new Dictionary<string, ProviderReading>(StringComparer.OrdinalIgnoreCase),
            _clock.UtcNow,
            Array.Empty<string>(),
            true,
            trigger);

        var refreshing = baseState with
        {
            IsRefreshing = true,
            Trigger = trigger,
            LastRefresh = _clock.UtcNow
        };

        _broadcaster.Publish(refreshing);
    }

    public override void Dispose()
    {
        lock (_intervalLock)
        {
            _timerCts?.Dispose();
            _timerCts = null;
        }

        _refreshGate.Dispose();
        base.Dispose();
    }
}
