namespace costats.Core.Pulse;

public sealed record ProviderReading(
    UsagePulse? Usage,
    IdentityCard? Identity,
    string? StatusSummary,
    DateTimeOffset CapturedAt,
    ReadingConfidence Confidence,
    ReadingSource Source)
{
    // 계약: 토큰은 있는데 죽어서(만료 · 401 · 403) 한도를 못 받았나 — 화면은 「다시 연동」 단추를 보이고 깜빡인다. 토큰이 없는 자리(App 연동)는 false 다
    public bool NeedsRelink { get; init; }
}
