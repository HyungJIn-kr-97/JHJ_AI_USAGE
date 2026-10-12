using System.Diagnostics;
using costats.Core.Pulse;
using Jhj.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace costats.Application.Pulse;

public sealed class SourceSelector : ISourceSelector
{
    // 계약: 출처 하나가 이 시간을 넘기면 diagnostics\events.log 에 slow-source 한 줄을 남긴다
    private static readonly TimeSpan SlowSource = TimeSpan.FromSeconds(10);

    private readonly ILogger<SourceSelector> _logger;

    public SourceSelector(ILogger<SourceSelector> logger)
    {
        _logger = logger;
    }

    public async Task<ProviderReading> SelectAsync(
        string providerId,
        IReadOnlyList<ISignalSource> sources,
        CancellationToken cancellationToken)
    {
        // Fetch all sources in parallel for performance
        var readTasks = sources.Select(async source =>
        {
            var watch = Stopwatch.StartNew();
            string? failure = null;
            try
            {
                return await source.ReadAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name;
                _logger.LogWarning(ex, "Source {Source} failed for {ProviderId}", source.GetType().Name, providerId);
                return null;
            }
            finally
            {
                // 왜: 갱신 전체 시간만으로는 어느 읽기가 붙잡았는지 모른다 — 느린 출처를 이름째 남긴다
                if (watch.Elapsed >= SlowSource)
                {
                    DiagnosticsLog.Record("slow-source",
                        $"{providerId} · {source.GetType().Name} · {watch.Elapsed.TotalSeconds:0.0}초" +
                        (failure is null ? string.Empty : $" · 실패 {failure}"));
                }
            }
        });

        var readings = await Task.WhenAll(readTasks);

        // Select the best reading from all successful results
        ProviderReading? best = null;
        foreach (var reading in readings)
        {
            if (reading is not null && (best is null || IsBetter(reading, best)))
            {
                best = reading;
            }
        }

        return best ?? new ProviderReading(
            Usage: null,
            Identity: null,
            StatusSummary: "No data",
            CapturedAt: DateTimeOffset.UtcNow,
            Confidence: ReadingConfidence.Unknown,
            Source: ReadingSource.Unknown);
    }

    private static bool IsBetter(ProviderReading candidate, ProviderReading current)
    {
        if (candidate.Confidence != current.Confidence)
        {
            return candidate.Confidence > current.Confidence;
        }

        return candidate.CapturedAt > current.CapturedAt;
    }
}
