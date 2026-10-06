using System.Windows;
using System.Windows.Controls;

namespace costats.App.Views;

public partial class ClaudeMultiPanelView : UserControl
{
    public ClaudeMultiPanelView()
    {
        InitializeComponent();
    }

    // 계약: 카드의 「연동」 — 연동 흐름은 팝업 창이 쥐고 있으므로 창에 넘긴다
    private void OnLinkAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string providerId } && Window.GetWindow(this) is GlassWidgetWindow window)
        {
            window.LinkAccount(providerId);
        }
    }
}
