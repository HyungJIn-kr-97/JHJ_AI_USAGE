using System.Windows;
using System.Windows.Controls;
using costats.App.ViewModels;

namespace costats.App.Views;

public partial class ClaudeMultiPanelView : UserControl
{
    public ClaudeMultiPanelView()
    {
        InitializeComponent();
    }

    // 계약: 카드의 「연동 ▾」 — 연동 흐름은 팝업 창이 쥐고 있으므로 자리와 방식을 창에 넘긴다
    private void OnLinkModeChosen(object? sender, string mode)
    {
        if (sender is FrameworkElement { DataContext: ProviderPulseViewModel card } && Window.GetWindow(this) is GlassWidgetWindow window)
        {
            window.LinkAccount(card.ProviderId, mode);
        }
    }
}
