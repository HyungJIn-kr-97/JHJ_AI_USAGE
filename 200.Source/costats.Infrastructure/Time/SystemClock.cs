using costats.Application.Abstractions;

namespace costats.Infrastructure.Time;

// 계약: Core 에도 같은 것이 있지만 IClock 인터페이스가 이 저장소 것이라(UtcNow) 여기 둔다 — 층을 맞추는 날 함께 옮긴다
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
