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
        private readonly SettingsViewModel _settingsViewModel;
        private bool _settingsOpen;

        private readonly costats.Application.Settings.AppSettings _settings;
        private readonly costats.Application.Settings.ISettingsStore _settingsStore;

        public GlassWidgetWindow(
            PulseViewModel viewModel,
            SettingsViewModel settingsViewModel,
            IGlassBackdropService backdropService,
            costats.Application.Settings.AppSettings settings,
            costats.Application.Settings.ISettingsStore settingsStore)
        {
            _settings = settings;
            _settingsStore = settingsStore;
            InitializeComponent();
            DataContext = viewModel;
            _backdropService = backdropService;
            _settingsViewModel = settingsViewModel;
            SettingsView.DataContext = settingsViewModel;
            viewModel.LinkContext = settingsViewModel;
            _viewModel = viewModel;
            // 왜: 카드는 갱신 때마다 새로 만들어진다 — 연동 상태가 바뀌거나 카드가 바뀔 때마다 진행 칸을 펼칠 카드를 다시 고른다
            settingsViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SettingsViewModel.LinkingId) or nameof(SettingsViewModel.ShowLinkStrip))
                {
                    ApplyLinking();
                }
            };
            viewModel.ClaudeProfiles.CollectionChanged += (_, _) => ApplyLinking();
            SourceInitialized += OnSourceInitialized;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            Deactivated += OnDeactivated;
            KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape)
                {
                    return;
                }

                // 왜: 설정이 열려 있을 때의 Esc 는 「설정에서 나가기」다 — 팝업까지 닫으면 한 번에 두 단계를 건너뛴다
                if (_settingsOpen)
                {
                    SetSettingsOpen(false);
                }
                else
                {
                    Hide();
                }
            };

            // Subscribe to ViewModel property changes for dynamic height
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private readonly PulseViewModel _viewModel;

        private void ApplyLinking()
        {
            var cards = _viewModel.ClaudeProfiles
                .Concat([_viewModel.Claude, _viewModel.Codex])
                .Append(_viewModel.SelectedProvider)
                .OfType<ProviderPulseViewModel>();
            foreach (var card in cards)
            {
                card.IsLinking = _settingsViewModel.IsLinkingFor(card.ProviderId);
            }
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
            // 왜: 연동 중에는 브라우저로 포커스가 넘어간다 — 그때 닫으면 코드 칸이 사라져 붙여 넣을 곳이 없다
            if (_settingsViewModel.LoginInProgress)
            {
                return;
            }

            // Hide window when it loses focus (like a popup)
            Hide();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PulseViewModel.SelectedProvider) or nameof(PulseViewModel.Claude) or nameof(PulseViewModel.Codex))
            {
                ApplyLinking();
            }

            if (e.PropertyName is nameof(PulseViewModel.IsMulticcActive) or
                nameof(PulseViewModel.SelectedTabIndex) or
                nameof(PulseViewModel.ShowClaudeStacked) or
                nameof(PulseViewModel.SelectedProvider) or
                nameof(PulseViewModel.Claude) or
                nameof(PulseViewModel.Codex) or
                nameof(PulseViewModel.Copilot) or
                nameof(PulseViewModel.Gemini) or
                nameof(PulseViewModel.IsChartExpanded) or
                nameof(PulseViewModel.IsModelsExpanded) or
                nameof(PulseViewModel.IsTokenTypesExpanded) or
                nameof(PulseViewModel.IsProgramsExpanded))
            {
                UpdateWindowHeight();
            }
        }

        // 왜: 탭·계정·접힘에 따라 창 높이가 바뀌면 화면이 흔들린다 — 창은 작업 영역 높이(100%)로 고정하고 본문만 스크롤한다
        // 계약: 위아래 여백 12px 씩은 TrayHost 의 위치 계산과 맞춘 값이다
        public void FitToWorkArea()
        {
            var target = SystemParameters.WorkArea.Height - 24;
            if (Math.Abs(Height - target) > 1.0)
            {
                Height = target;
            }
        }

        private void UpdateWindowHeight() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitToWorkArea);

        private void OnAccountItemClick(object sender, RoutedEventArgs e)
        {
            AccountToggle.IsChecked = false;
        }

        private void OnAddAccountClick(object sender, RoutedEventArgs e)
        {
            AccountToggle.IsChecked = false;
            OnSettingsClick(sender, e);
        }

        private void OnLinkAccountClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string providerId })
            {
                LinkAccount(providerId);
            }
        }

        // 계약: 팝업에서 바로 연동한다 — 진행(브라우저 버튼·코드 칸·결과)은 누른 카드·머리글 바로 밑(LinkProgressView)에 보인다
        public void LinkAccount(string providerId) => _settingsViewModel.LinkAccount(providerId);

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

        private void OnCloseWindowClick(object sender, RoutedEventArgs e) => Hide();

        // 계약: 톱니와 제목 줄의 「설정 ✕」는 같은 토글이다 — 열려 있으면 닫고 닫혀 있으면 연다
        private void OnSettingsClick(object sender, RoutedEventArgs e) => SetSettingsOpen(!_settingsOpen);

        /// <summary>트레이 메뉴의 「설정」이 부른다 — 팝업을 띄운 뒤 설정 화면으로 넘긴다.</summary>
        public void OpenSettings() => SetSettingsOpen(true);
        // 계약: 설정은 제목 줄 아래 ~ 하단 줄 위를 덮고, 오른쪽에서 밀려 들어왔다가 오른쪽으로 빠진다
        private void SetSettingsOpen(bool open)
        {
            if (_settingsOpen == open)
            {
                return;
            }

            _settingsOpen = open;
            // 왜: 설정 화면에서는 계정 전환이 쓸모없다 — 제목에 " · 설정" 을 잇고 그 자리에는 돌아가기만 둔다
            SettingsTitleChip.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            AccountToggle.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            TitleSuffix.Text = open ? " · " + costats.App.Localization.Loc.T("Settings") : string.Empty;

            var width = Math.Max(1, ActualWidth);
            if (open)
            {
                _settingsViewModel.RefreshAccounts();
                SettingsHost.Visibility = Visibility.Visible;
                SettingsSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(width, 0, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    });
                return;
            }

            var slideOut = new System.Windows.Media.Animation.DoubleAnimation(0, width, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            slideOut.Completed += (_, _) =>
            {
                // 함정: 빠지는 도중에 다시 열면 이 콜백이 늦게 온다 — 그때 접어 버리면 방금 연 설정이 사라진다
                if (!_settingsOpen)
                {
                    SettingsHost.Visibility = Visibility.Collapsed;
                }
            };
            SettingsSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideOut);
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
