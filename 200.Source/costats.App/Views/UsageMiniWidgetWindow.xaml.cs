using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using costats.App.ViewModels;
using costats.Application.Alerts;
using costats.Application.Settings;

namespace costats.App.Views;

/// <summary>
/// 미니 위젯 — 계정 하나(또는 전체)의 세션·주간·모델별 주간 막대만 작게 띄워 둔다.
/// 계약: 고정(IsPinned)이면 Topmost, 아니면 보통 창처럼 다른 창 뒤로 간다. 보임·고정·계정·자리(화면 픽셀)·배율은 AppSettings 에 저장된다.
/// 계약: 바탕화면 모드(MiniWidgetOnDesktop)는 창의 주인을 Progman 으로 두고 z 순서 변경을 늘 맨 아래로 돌린다 — 모든 창 아래, 바탕화면 보기(Win+D)에도 남는다.
/// 계약: 값은 PulseViewModel.Providers 의 카드를 그대로 읽는다 — 갱신마다 카드가 통째로 바뀌므로 CollectionChanged 에 다시 그린다.
/// </summary>
public partial class UsageMiniWidgetWindow : Window
{
    private readonly PulseViewModel _pulse;
    private readonly AppSettings _settings;
    private readonly ISettingsStore _store;
    private readonly DispatcherTimer _saveTimer;
    private readonly ScaleTransform _scale = new(1, 1);
    private bool _suppressSelection;

    private const double MinScale = 0.7;
    private const double MaxScale = 2.0;
    private static readonly IntPtr HwndBottom = new(1);
    private const int GwlpHwndParent = -8;
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpMoveOnly = 0x0001 | SwpNoZOrder | 0x0010;

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos { public IntPtr Hwnd; public IntPtr HwndInsertAfter; public int X, Y, Cx, Cy; public uint Flags; }

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

    public double Scale => _scale.ScaleX;

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

        Root.LayoutTransform = _scale;
        _scale.ScaleX = _scale.ScaleY = Math.Clamp(_settings.MiniWidgetScale, MinScale, MaxScale);
        Topmost = _settings.MiniWidgetPinned && !_settings.MiniWidgetOnDesktop;
        PinButton.IsChecked = _settings.MiniWidgetPinned;
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnMessage);
            ApplyDesktopMode();
        };
        _pulse.Providers.CollectionChanged += OnProvidersChanged;
        _pulse.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PulseViewModel.UpdatedLabel)) Rebuild(); };
        Loc.LanguageChanged += Rebuild;
        LocationChanged += (_, _) => RememberPlace();
        // 왜: 해상도를 낮추거나 모니터를 떼면 저장된 자리가 화면 밖이 된다 — 그때 주 모니터로 되돌린다
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        Rebuild();
    }

    /// <summary>보이기 — 저장된 자리가 없거나 어느 모니터에도 안 걸리면 주 모니터 작업 영역 우측 위에 둔다.</summary>
    public void ShowWidget()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (!PlaceSaved(hwnd))
        {
            PlaceDefault(hwnd);
        }

        _settings.MiniWidgetVisible = true;
        Show();
        ApplyDesktopMode();
        Rebuild();
        _saveTimer.Stop(); _saveTimer.Start();
        SettingsTouched?.Invoke();
    }

    // 함정: 배율이 다른 모니터가 섞이면 WPF 의 Left·Top 은 창이 지금 놓인 화면의 배율로 환산돼 다시 켤 때 자리가 밀린다 — 자리는 화면 픽셀로만 저장하고 되돌린다
    private void RememberPlace()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!IsVisible || hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        _settings.MiniWidgetLeftPx = rect.Left;
        _settings.MiniWidgetTopPx = rect.Top;
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private bool PlaceSaved(IntPtr hwnd)
    {
        // 계약: SystemParameters 와 옛 설정(MiniWidgetLeft·Top)은 주 모니터 배율의 DIP 이다 — 픽셀은 × PrimaryScale
        var x = _settings.MiniWidgetLeftPx ?? (_settings.MiniWidgetLeft is { } l ? (int)Math.Round(l * PrimaryScale) : (int?)null);
        var y = _settings.MiniWidgetTopPx ?? (_settings.MiniWidgetTop is { } t ? (int)Math.Round(t * PrimaryScale) : (int?)null);
        if (x is null || y is null || !IsOnScreen(x.Value, y.Value))
        {
            return false;
        }

        SetWindowPos(hwnd, IntPtr.Zero, x.Value, y.Value, 0, 0, SwpMoveOnly);
        return true;
    }

    private void PlaceDefault(IntPtr hwnd)
    {
        var area = SystemParameters.WorkArea;
        // 왜: SizeToContent 라 Width 는 NaN 이다 — 내용 폭(312) × 위젯 배율로 어림한다
        var x = (area.Right - 312 * Scale - 12) * PrimaryScale;
        var y = (area.Top + 12) * PrimaryScale;
        SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, SwpMoveOnly);
    }

    private static double PrimaryScale => GetDpiForSystem() / 96.0;

    // 계약: 머리(끌기 자리) 한 조각이라도 어느 모니터에 걸려야 「화면 안」이다 — 못 끌어오는 자리를 막는다
    private static bool IsOnScreen(int x, int y)
    {
        var head = new NativeRect { Left = x, Top = y, Right = x + 120, Bottom = y + 24 };
        return MonitorFromRect(ref head, 0) != IntPtr.Zero;
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (IsVisible && hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect) && !IsOnScreen(rect.Left, rect.Top))
        {
            PlaceDefault(hwnd);
        }
    });

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
        Topmost = pinned && !_settings.MiniWidgetOnDesktop;
        PinButton.IsChecked = pinned;
        _saveTimer.Stop(); _saveTimer.Start();
        SettingsTouched?.Invoke();
    }

    public void SetOnDesktop(bool onDesktop)
    {
        _settings.MiniWidgetOnDesktop = onDesktop;
        ApplyDesktopMode();
        _saveTimer.Stop(); _saveTimer.Start();
        SettingsTouched?.Invoke();
    }

    /// <summary>배율 0.7~2.0 — 넘치면 자른다. 설정 콤보와 Ctrl+휠이 같이 부른다.</summary>
    public void SetScale(double scale)
    {
        scale = Math.Round(Math.Clamp(scale, MinScale, MaxScale), 2);
        if (Math.Abs(scale - Scale) < 0.001)
        {
            return;
        }

        _scale.ScaleX = _scale.ScaleY = scale;
        _settings.MiniWidgetScale = scale;
        _saveTimer.Stop(); _saveTimer.Start();
        SettingsTouched?.Invoke();
    }

    private void ApplyDesktopMode()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_settings.MiniWidgetOnDesktop)
        {
            Topmost = false;
            // 함정: 주인(GWLP_HWNDPARENT)이 Progman 이어야 「바탕화면 보기」가 이 창을 숨기지 않는다 — Owner 속성은 다른 프로세스 창을 못 받는다
            SetWindowLongPtr(hwnd, GwlpHwndParent, FindWindow("Progman", null));
            SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }
        else
        {
            SetWindowLongPtr(hwnd, GwlpHwndParent, IntPtr.Zero);
            Topmost = _settings.MiniWidgetPinned;
        }
    }

    // 왜: 바탕화면 모드에서 창을 누르면 활성화되며 위로 올라온다 — z 순서를 바꾸는 모든 이동을 맨 아래로 돌린다
    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmWindowPosChanging && _settings.MiniWidgetOnDesktop)
        {
            var pos = Marshal.PtrToStructure<WindowPos>(lParam);
            if ((pos.Flags & SwpNoZOrder) == 0 && pos.HwndInsertAfter != HwndBottom)
            {
                pos.HwndInsertAfter = HwndBottom;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }

        return IntPtr.Zero;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        SetScale(Scale + (e.Delta > 0 ? 0.1 : -0.1));
        e.Handled = true;
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
                Accounts.Add(new MiniAccount(p.ProviderId, LabelOf(p)));
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
            Cards.Add(new MiniCard(LabelOf(p), bars, ShowName: wanted is null || shown.Count > 1, Columns: Math.Clamp(bars.Count, 1, 3), ShowDivider: Cards.Count > 0));
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

    // 계약: 계정 표기는 「앱 · 계정 명칭」(Claude · atisys.ai) — 계정이 없는 도구(Copilot·Gemini)는 앱 이름만이다
    // 함정: 이 PC 로그인("claude"·"codex")의 DisplayName 은 앱 이름이다 — 계정 명칭은 AccountMeta 에서 따로 읽는다
    private string LabelOf(ProviderPulseViewModel p)
    {
        if (p.ProviderKind is not ("claude" or "codex"))
        {
            return p.DisplayName;
        }

        var metaId = p.ProviderId.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "claude:" + Services.AccountProfileStore.DefaultName : p.ProviderId;
        return $"{Capital(p.ProviderKind)} · {Services.AccountMeta.DisplayNameOf(_settings, metaId)}";
    }

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
