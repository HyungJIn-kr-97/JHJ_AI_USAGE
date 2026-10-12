using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using costats.App.ViewModels;
using costats.Application.Alerts;
using costats.Application.Settings;

namespace costats.App.Views;

/// <summary>
/// 미니 위젯 — 계정 하나(또는 전체)의 세션·주간·모델별 주간 막대만 작게 띄워 둔다.
/// 계약: 고정(IsPinned)이면 Topmost, 아니면 보통 창처럼 다른 창 뒤로 간다. 보임·고정·계정·자리는 AppSettings 에 저장된다.
/// 계약: 값은 PulseViewModel.Providers 의 카드를 그대로 읽는다 — 갱신마다 카드가 통째로 바뀌므로 CollectionChanged 에 다시 그린다.
/// </summary>
public partial class UsageMiniWidgetWindow : Window
{
    private readonly PulseViewModel _pulse;
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly DispatcherTimer _saveTimer;
    private bool _suppressSelection;

    public ObservableCollection<MiniAccount> Accounts { get; } = [];
    public ObservableCollection<MiniCard> Cards { get; } = [];

    public static readonly DependencyProperty SelectedAccountProperty = DependencyProperty.Register(
        nameof(SelectedAccount), typeof(MiniAccount), typeof(UsageMiniWidgetWindow), new PropertyMetadata(null, (d, _) => ((UsageMiniWidgetWindow)d).OnAccountChanged()));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(string), typeof(UsageMiniWidgetWindow), new PropertyMetadata(string.Empty));

    public MiniAccount? SelectedAccount
    {
        get => (MiniAccount?)GetValue(SelectedAccountProperty);
        set => SetValue(SelectedAccountProperty, value);
    }

    public string Footer
    {
        get => (string)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool IsPinned => _settings.MiniWidgetPinned;

    /// <summary>보임·고정이 창 쪽(📌·✕)에서 바뀌었다 — 설정 탭이 칸을 다시 읽는다.</summary>
    public event Action? SettingsTouched;

    public UsageMiniWidgetWindow(PulseViewModel pulse, AppSettings settings, ISettingsStore store)
    {
        _pulse = pulse;
        _settings = settings;
        _store = store;
        InitializeComponent();
        DataContext = pulse;
        // 왜: 끌 때마다 저장하면 파일을 초당 수십 번 쓴다 — 멈춘 뒤 1초에 한 번
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _ = _store.SaveAsync(_settings, CancellationToken.None); };

        Topmost = _settings.MiniWidgetPinned;
        PinButton.IsChecked = _settings.MiniWidgetPinned;
        _pulse.Providers.CollectionChanged += OnProvidersChanged;
        _pulse.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PulseViewModel.UpdatedLabel)) Rebuild(); };
        Loc.LanguageChanged += Rebuild;
        LocationChanged += (_, _) => { if (IsVisible) { _settings.MiniWidgetLeft = Left; _settings.MiniWidgetTop = Top; _saveTimer.Stop(); _saveTimer.Start(); } };
        Rebuild();
    }

    /// <summary>보이기 — 저장된 자리가 없거나 화면 밖이면 작업 영역 우측 위에 둔다.</summary>
    public void ShowWidget()
    {
        var area = SystemParameters.WorkArea;
        var left = _settings.MiniWidgetLeft ?? (area.Right - Width - 12);
        var top = _settings.MiniWidgetTop ?? (area.Top + 12);
        // 함정: 주 모니터의 WorkArea 로 가두면 둘째 모니터에 둔 자리가 다음 실행에 끌려온다 — 가상 화면 전체로 가둔다
        var vLeft = SystemParameters.VirtualScreenLeft;
        var vTop = SystemParameters.VirtualScreenTop;
        var vRight = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(left, vLeft, Math.Max(vLeft, vRight - Width));
        Top = Math.Clamp(top, vTop, Math.Max(vTop, vBottom - 80));
        _settings.MiniWidgetVisible = true;
        Show();
        Rebuild();
        _saveTimer.Stop(); _saveTimer.Start();
        SettingsTouched?.Invoke();
    }

    public void HideWidget()
    {
        _settings.MiniWidgetVisible = false;
        Hide();
        _saveTimer.Stop(); _saveTimer.Start();
        SettingsTouched?.Invoke();
    }

    public void Toggle()
    {
        if (IsVisible) HideWidget(); else ShowWidget();
    }

    public void SetPinned(bool pinned)
    {
        _settings.MiniWidgetPinned = pinned;
        Topmost = pinned;
        PinButton.IsChecked = pinned;
        _saveTimer.Stop(); _saveTimer.Start();
        SettingsTouched?.Invoke();
    }

    private void OnProvidersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 왜: Clear + Add 로 통째로 바뀐다 — 마지막 Add 뒤 한 번만 그리면 되므로 Background 우선순위로 미룬다
        Dispatcher.BeginInvoke(Rebuild, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        var providers = _pulse.Providers
            // 왜: 한도 정보가 없는 공급자("--")는 막대가 비어 자리만 차지한다 — 값이 있는 것만 싣는다
            .Where(p => p.SessionPercentText != "--" || p.WeekPercentText != "--")
            .Where(p => !(p.ProviderKind == "claude" && p.ProviderId.Equals("claude", StringComparison.OrdinalIgnoreCase) && _pulse.Providers.Any(q => q.ProviderId.StartsWith("claude:", StringComparison.OrdinalIgnoreCase))))
            .ToList();

        _suppressSelection = true;
        try
        {
            Accounts.Clear();
            Accounts.Add(new MiniAccount(null, Loc.T("All accounts")));
            foreach (var p in providers)
            {
                var label = p.ProviderKind == "claude" ? p.DisplayName : $"{p.DisplayName} · {Capital(p.ProviderKind)}";
                Accounts.Add(new MiniAccount(p.ProviderId, label));
            }

            SelectedAccount = Accounts.FirstOrDefault(a => string.Equals(a.Id, _settings.MiniWidgetAccount, StringComparison.OrdinalIgnoreCase)) ?? Accounts[0];
        }
        finally
        {
            _suppressSelection = false;
        }

        var wanted = _settings.MiniWidgetAccount;
        var shown = wanted is null ? providers : providers.Where(p => p.ProviderId.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        Cards.Clear();
        foreach (var p in shown)
        {
            var bars = new List<MiniBar>
            {
                new(UsageWindowKind.Session, Loc.T(p.ProviderKind == "gemini" ? "Pro models · daily" : "Session"), p.SessionProgress, p.SessionPercentText, p.SessionResetText, p.SessionResetAtText, p.SessionPercentColor),
                new(UsageWindowKind.Weekly, Loc.T(p.ProviderKind == "gemini" ? "Flash models · daily" : "Weekly"), p.WeekProgress, p.WeekPercentText, p.WeekResetText, p.WeekResetAtText, p.WeekPercentColor),
            };
            // 왜: 세 칸이 한 줄이라 「주간 · Fable」 은 모델 이름만, 초기화 문구는 상대·절대 두 줄로 나눈다
            bars.AddRange(p.ModelWeeks.Where(m => m.UsageLabel != "--")
                .Select(m => new MiniBar(UsageWindowKind.ModelWeek, Tail(m.Title), m.Progress, $"{(int)Math.Round(m.Progress * 100)}%", Head(m.ResetText), Tail(m.ResetText), ColorOf(m.Progress * 100))));
            Cards.Add(new MiniCard(p.DisplayName, bars, ShowName: wanted is null || shown.Count > 1, Columns: Math.Clamp(bars.Count, 1, 3), ShowDivider: Cards.Count > 0));
        }

        Footer = _pulse.UpdatedLabel;
    }

    private void OnAccountChanged()
    {
        if (_suppressSelection || SelectedAccount is not { } account)
        {
            return;
        }

        _settings.MiniWidgetAccount = account.Id;
        _saveTimer.Stop(); _saveTimer.Start();
        Rebuild();
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { } el && (el == AccountBox || AccountBox.IsAncestorOf(el)))
        {
            return;
        }

        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void OnPinClick(object sender, RoutedEventArgs e) => SetPinned(PinButton.IsChecked == true);

    private void OnCloseClick(object sender, RoutedEventArgs e) => HideWidget();

    private static string Head(string text) => text.Split(" · ", 2)[0];

    private static string Tail(string text) { var parts = text.Split(" · ", 2); return parts.Length > 1 ? parts[1] : text; }

    private static string Capital(string s) => s.Length > 1 ? char.ToUpperInvariant(s[0]) + s[1..] : s;

    // 왜: 팝업의 숫자 색(ProviderPulseViewModel.GetPercentTextColor)과 같은 네 색
    private static string ColorOf(double percent) => percent switch
    {
        >= 95 => "#DC2626",
        >= 80 => "#C2410C",
        >= 50 => "#B45309",
        _ => "#047857",
    };
}

public sealed record MiniAccount(string? Id, string Label);

/// <summary>계정 카드 하나. ShowDivider 는 「전체 계정」에서 두 번째 카드부터 — 카드 사이 가는 금.</summary>
public sealed record MiniCard(string Name, IReadOnlyList<MiniBar> Bars, bool ShowName, int Columns, bool ShowDivider);

/// <summary>막대 한 칸. Kind 는 아이콘 선택용, Progress 는 0~1, Reset 은 상대시간, ResetAt 은 초기화 예상 일시, Color 는 "#RRGGBB".</summary>
public sealed record MiniBar(UsageWindowKind Kind, string Title, double Progress, string Percent, string Reset, string ResetAt, string Color)
{
    public double Maximum => 1;
}
