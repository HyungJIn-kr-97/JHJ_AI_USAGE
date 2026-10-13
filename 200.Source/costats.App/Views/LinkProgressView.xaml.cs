using System.Windows;
using System.Windows.Controls;
using costats.App.ViewModels;

namespace costats.App.Views;

public partial class LinkProgressView : UserControl
{
    public LinkProgressView()
    {
        InitializeComponent();
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.AccountsMessage = string.Empty;
            viewModel.InstallLog = string.Empty;
        }
    }

    private void OnInstallLogChanged(object sender, TextChangedEventArgs e) => (sender as TextBox)?.ScrollToEnd();
}
