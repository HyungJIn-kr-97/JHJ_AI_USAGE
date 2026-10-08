using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using costats.App.Localization;
using costats.App.ViewModels;
using costats.Application.Pulse;
using costats.Application.Settings;
using costats.Core.Pulse;
using Microsoft.Win32;
using Serilog;

namespace costats.App.Services
{
    public sealed class TrayHost : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        private readonly TaskbarIcon _taskbarIcon;
        private readonly GlassWidgetWindow _widgetWindow;
        private readonly IPulseOrchestrator _pulseOrchestrator;
        private readonly PulseViewModel _viewModel;
        private readonly TaskbarPositionService _taskbarPosition;
        private readonly AppSettings _settings;

        public TrayHost(
            PulseViewModel viewModel,
            GlassWidgetWindow widgetWindow,
            IPulseOrchestrator pulseOrchestrator,
            TaskbarPositionService taskbarPosition,
            AppSettings settings)
        {
            _settings = settings;
            _viewModel = viewModel;
            _widgetWindow = widgetWindow;
            _pulseOrchestrator = pulseOrchestrator;
            _taskbarPosition = taskbarPosition;

            _taskbarIcon = new TaskbarIcon();
            _taskbarIcon.Icon = CreateIcon();
            TrayIconRenderer.Changed += OnTrayIconChanged;
            _taskbarIcon.ToolTipText = Loc.T("AI Usage Monitor");
            _taskbarIcon.ContextMenu = BuildContextMenu();
            Loc.LanguageChanged += () => _taskbarIcon.ContextMenu = BuildContextMenu();
            _taskbarIcon.TrayLeftMouseUp += OnTrayLeftClick;
            _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
            TrayPinService.ApplyAfterIconShown(_settings.PinTrayIcon);

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _widgetWindow.SizeChanged += OnWidgetSizeChanged;
        }

        private void OnTrayLeftClick(object? sender, EventArgs e)
        {
            ToggleWidget();
        }

        private void OnTrayIconChanged()
        {
            _taskbarIcon.Dispatcher.Invoke(() =>
            {
                var old = _taskbarIcon.Icon;
                _taskbarIcon.Icon = CreateIcon();
                old?.Dispose();
            });
        }

        // 계약: 모양은 설정 「아이콘」 탭(TrayIconStyle), 색은 지금 팔레트 — TrayIconRenderer 가 그린다
        private Icon CreateIcon()
        {
            // 왜: 트레이 칸 크기(16px × 화면 배율)로 바로 그려야 줄여 그리며 흐려지지 않는다
            var size = (int)Math.Round(16 * GetDpiForSystem() / 96.0);
            using var bitmap = TrayIconRenderer.Render(_settings.TrayIconStyle, ThemeManager.Palette, size);
            return TrayIconRenderer.ToIcon(bitmap);
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();

            var showItem = new MenuItem { Header = Loc.T("Show Widget"), FontWeight = FontWeights.SemiBold };
            showItem.Click += (_, _) => ShowWidget();

            var refreshItem = new MenuItem { Header = Loc.T("Refresh Now") };
            refreshItem.Click += async (_, _) => await _pulseOrchestrator.RefreshOnceAsync(RefreshTrigger.Manual, CancellationToken.None);

            var settingsItem = new MenuItem { Header = Loc.T("Settings...") };
            settingsItem.Click += (_, _) => ShowSettings();

            // 왜: 창을 끌다 보면 화면 밖이나 다른 모니터로 가 버린다 — 띄우지 않고도 트레이에서 되돌린다
            var resetPositionItem = new MenuItem { Header = Loc.T("Move to default position") };
            resetPositionItem.Click += (_, _) => ShowWidget();

            var resetSettingsItem = new MenuItem { Header = Loc.T("Reset settings...") };
            resetSettingsItem.Click += (_, _) => ResetAllSettings();

            var exitItem = new MenuItem { Header = Loc.T("Exit") };
            exitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

            menu.Items.Add(showItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(resetPositionItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(resetSettingsItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(exitItem);
            return menu;
        }

        // 계약: 계정(명칭·유형·요금·연동)과 사용량 이력은 남긴다 — 되살릴 수 없는 값이라서다
        private void ResetAllSettings()
        {
            var answer = System.Windows.MessageBox.Show(
                Loc.T("Restore every setting to its default?") + Environment.NewLine + Environment.NewLine +
                Loc.T("Accounts, linking and usage history are kept."),
                Loc.T("Reset settings..."),
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning);
            if (answer != System.Windows.MessageBoxResult.OK)
            {
                return;
            }

            // 계약: 설정값 되돌리기와 저장은 창이 한다 — 설정 VM·저장소를 가진 쪽이 거기다
            _widgetWindow.ResetAllSettings();
            _taskbarIcon.ContextMenu = BuildContextMenu();
            TrayPinService.ApplyAfterIconShown(_settings.PinTrayIcon);
            ShowWidget();
        }

        /// <summary>트레이 풍선 알림 — 실패해도 앱 동작에는 영향이 없다.</summary>
        public void ShowBalloon(string title, string message)
        {
            try
            {
                _taskbarIcon.ShowNotification(title, message);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Tray notification failed");
            }
        }

        public void ShowSettings()
        {
            // 왜: 설정은 팝업 안의 화면이다 — 팝업을 띄운 뒤 그 안에서 설정으로 넘긴다
            ShowWidget();
            _widgetWindow.OpenSettings();
        }

        public void ShowWidget()
        {
            _widgetWindow.FitToWorkArea();
            PositionWidget();

            var wasVisible = _widgetWindow.IsVisible;

            if (!wasVisible)
            {
                _viewModel.SelectDefaultAccounts();
                _widgetWindow.Show();
            }

            _widgetWindow.Activate();

            if (!wasVisible && _settings.RefreshOnOpen)
            {
                _ = RefreshSelectedProviderAsync().ContinueWith(
                    t => Log.Warning(t.Exception!.GetBaseException(), "Silent provider refresh failed"),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        private Task RefreshSelectedProviderAsync()
        {
            // 왜: 원본은 선택된 탭만 표시 없이 갱신해 열어도 갱신 여부를 알 수 없었다 — 전체를 스피너와 함께 갱신한다
            var refresh = _viewModel.RefreshCommand;
            return refresh.CanExecute(null)
                ? refresh.ExecuteAsync(null)
                : _viewModel.RefreshSelectedProviderSilentlyAsync();
        }

        public void HideWidget()
        {
            _widgetWindow.Hide();
        }

        public void ToggleWidget()
        {
            if (_widgetWindow.IsVisible)
            {
                HideWidget();
            }
            else
            {
                ShowWidget();
            }
        }

        public void Dispose()
        {
            TrayIconRenderer.Changed -= OnTrayIconChanged;
            _widgetWindow.SizeChanged -= OnWidgetSizeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _taskbarIcon.Dispose();
            _widgetWindow.Close();
        }

        private void OnWidgetSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_widgetWindow.IsVisible)
            {
                PositionWidget();
            }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_widgetWindow.IsVisible)
            {
                PositionWidget();
            }
        }

        private void PositionWidget()
        {
            var position = _taskbarPosition.GetWidgetPosition(_widgetWindow.Width, _widgetWindow.Height, 12);
            _widgetWindow.Left = position.X;
            _widgetWindow.Top = position.Y;
        }
    }
}
