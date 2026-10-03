using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Navigation;
using costats.App.ViewModels;
using costats.Application.Shell;

namespace costats.App
{
    public partial class GlassWidgetWindow : Window
    {
        private readonly IGlassBackdropService _backdropService;
        private readonly SettingsWindow _settingsWindow;

        private readonly costats.Application.Settings.AppSettings _settings;
        private readonly costats.Application.Settings.ISettingsStore _settingsStore;

        public GlassWidgetWindow(
            PulseViewModel viewModel,
            SettingsWindow settingsWindow,
            IGlassBackdropService backdropService,
            costats.Application.Settings.AppSettings settings,
            costats.Application.Settings.ISettingsStore settingsStore)
        {
            _settings = settings;
            _settingsStore = settingsStore;
            InitializeComponent();
            DataContext = viewModel;
            _backdropService = backdropService;
            _settingsWindow = settingsWindow;
            SourceInitialized += OnSourceInitialized;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            Deactivated += OnDeactivated;
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Hide();
                }
            };

            // Subscribe to ViewModel property changes for dynamic height
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            // Skip backdrop - we use AllowsTransparency with custom Border for rounded corners
            // Applying DWM backdrop creates a conflicting layer with different corner radius
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window, but only if clicking on the background (not on buttons/controls)
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is System.Windows.Controls.Border or System.Windows.Controls.Grid or Window)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // DragMove can throw if called at wrong time
                }
            }
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            // Hide window when it loses focus (like a popup)
            Hide();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PulseViewModel.IsMulticcActive) or
                nameof(PulseViewModel.SelectedTabIndex) or
                nameof(PulseViewModel.ShowClaudeStacked) or
                nameof(PulseViewModel.SelectedProvider))
            {
                UpdateWindowHeight();
            }
        }

        private void UpdateWindowHeight()
        {
            // Defer to Loaded priority so the height change renders in the same frame
            // as panel visibility changes from MultiDataTrigger bindings.
            // Without this, the window resizes before panels swap, causing a visible flash.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                var vm = DataContext as PulseViewModel;
                if (vm is null) return;

                // 계약: 모델별 주간 한도(Fable 등)는 한 줄에 66 을 더 쓴다
                var stacked = vm.ShowClaudeStacked && vm.SelectedTabIndex == 1;
                var targetHeight = stacked ? 746.0 : 816.0 + vm.SelectedProvider.ModelWeeks.Count * 66.0;
                if (Math.Abs(Height - targetHeight) > 1.0)
                {
                    Height = targetHeight;
                }
            });
        }

        private void OnAccountItemClick(object sender, RoutedEventArgs e)
        {
            AccountToggle.IsChecked = false;
        }

        private void OnAddAccountClick(object sender, RoutedEventArgs e)
        {
            AccountToggle.IsChecked = false;
            OnSettingsClick(sender, e);
        }

        private async void OnThemeToggleClick(object sender, RoutedEventArgs e)
        {
            var dark = !costats.App.Services.ThemeManager.IsDark;
            costats.App.Services.ThemeManager.Apply(dark);
            _settings.Theme = dark ? costats.App.Services.ThemeManager.Dark : costats.App.Services.ThemeManager.Light;
            try
            {
                await _settingsStore.SaveAsync(_settings, CancellationToken.None);
            }
            catch (Exception)
            {
                // 저장 실패는 이번 실행의 전환을 막지 않는다
            }
        }

        private void OnQuitClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var workArea = SystemParameters.WorkArea;
            _settingsWindow.Left = (workArea.Width - _settingsWindow.Width) / 2 + workArea.Left;
            _settingsWindow.Top = (workArea.Height - _settingsWindow.Height) / 2 + workArea.Top;

            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
            }

            _settingsWindow.Activate();
        }

        private void OnUsageLinkNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
