namespace costats.App.ViewModels;

/// <summary>
/// 일별 비용 차트의 막대 하나. BarHeight 는 픽셀이다.
/// </summary>
public sealed record DailyUsageBar(double BarHeight, string Tooltip, bool IsToday);
