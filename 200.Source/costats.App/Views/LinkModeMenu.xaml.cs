using System.Windows;
using System.Windows.Controls;
using costats.App.ViewModels;

namespace costats.App.Views;

public partial class LinkModeMenu : UserControl
{
    public LinkModeMenu()
    {
        InitializeComponent();
    }

    // 계약: 고른 방식(cli · app · web)과 카드의 providerId 를 팝업 창에 넘긴다 — 진행은 그 카드 밑의 LinkProgressView 가 보인다
    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        Toggle.IsChecked = false;
        if (sender is FrameworkElement { Tag: string mode } && DataContext is ProviderPulseViewModel card
            && Window.GetWindow(this) is GlassWidgetWindow window)
        {
            window.LinkAccount(card.ProviderId, mode);
        }
    }
}
