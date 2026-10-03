using System.Windows.Media;

namespace costats.App.ViewModels;

/// <summary>
/// 모델 비중 목록의 한 줄이자 도넛 차트의 한 조각. ShareText 는 전체 비용 대비 비율이다.
/// 계약: Slice 는 DonutSize × DonutSize 좌표계의 도형이고 Color 와 함께 Freeze 된 상태로 온다.
/// </summary>
public sealed record ModelUsageRow(string Name, string CostText, string ShareText, string Tooltip, Brush Color, Geometry Slice)
{
    public const double DonutSize = 76;
}
