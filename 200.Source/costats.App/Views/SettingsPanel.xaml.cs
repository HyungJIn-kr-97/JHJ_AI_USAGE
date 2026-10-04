using System.Windows;
using System.Windows.Controls;
using costats.App.ViewModels;

namespace costats.App.Views
{
    /// <summary>
    /// 설정 화면. 팝업 창(GlassWidgetWindow) 안에 끼워 쓴다 — DataContext 는 SettingsViewModel 이다.
    /// 계약: 안쪽 바인딩의 조상 찾기는 Window 가 아니라 UserControl 이다 — 팝업 창의 DataContext 는 PulseViewModel 이기 때문이다.
    /// </summary>
    public partial class SettingsPanel : UserControl
    {
        public SettingsPanel()
        {
            InitializeComponent();
        }

        private void OnLinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void OnSaveCopilotTokenClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel viewModel)
            {
                await viewModel.SaveCopilotTokenAsync(CopilotTokenBox.Password);
            }
        }
    }
}
