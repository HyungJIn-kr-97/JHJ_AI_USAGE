namespace costats.App.ViewModels;

/// <summary>
/// 모델별 주간 한도 한 줄. Progress 는 0~1.
/// </summary>
public sealed record ModelWeekRow(string Title, double Progress, string UsageLabel, string ResetText);
