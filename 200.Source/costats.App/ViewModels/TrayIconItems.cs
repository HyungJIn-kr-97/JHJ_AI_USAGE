using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace costats.App.ViewModels;

/// <summary>설정 「아이콘」 탭의 칸 하나 — 모양 견본이거나(Preview 있음) 「+ 그리기」·「+ 불러오기」 같은 동작 칸(IsAction)이다.</summary>
public sealed record TrayIconOption(string Style, string Label, ImageSource? Preview, bool IsSelected, bool IsCustom, bool IsAction);

/// <summary>그리기 칸의 붓 색 하나. 투명이면 지우개다.</summary>
public sealed record IconPen(string Label, System.Drawing.Color Color, Brush Swatch, bool IsSelected)
{
    public bool IsEraser => Color.A == 0;
}

/// <summary>16×16 그리기 칸의 한 픽셀.</summary>
public sealed partial class IconPixel : ObservableObject
{
    public IconPixel(System.Drawing.Color color) => SetColor(color);

    public System.Drawing.Color Color { get; private set; }

    [ObservableProperty]
    private Brush fill = Brushes.Transparent;

    public void SetColor(System.Drawing.Color color)
    {
        Color = color;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        Fill = brush;
    }
}
