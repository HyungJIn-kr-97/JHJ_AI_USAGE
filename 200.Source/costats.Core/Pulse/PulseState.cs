namespace costats.Core.Pulse;

public sealed record PulseState(
    IReadOnlyDictionary<string, ProviderReading> Providers,
    DateTimeOffset LastRefresh,
    IReadOnlyList<string> Errors,
    bool IsRefreshing,
    RefreshTrigger Trigger,
    // 계약: 그 갱신이 걸린 시간을 계정별로 적은 글 — 이벤트 기록의 refresh 줄 끝에 그대로 붙는다
    string? Timing = null);
