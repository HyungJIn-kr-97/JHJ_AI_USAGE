using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using costats.App.ViewModels;

namespace costats.App.Views;

public partial class LinkModeMenu : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(LinkModeMenu), new PropertyMetadata(Loc.T("Link")));

    public static readonly DependencyProperty ShowDesktopAppProperty =
        DependencyProperty.Register(nameof(ShowDesktopApp), typeof(bool), typeof(LinkModeMenu), new PropertyMetadata(true));

    public static readonly DependencyProperty IsBusyProperty =
        DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(LinkModeMenu), new PropertyMetadata(false));

    public static readonly DependencyProperty FlatProperty =
        DependencyProperty.Register(nameof(Flat), typeof(bool), typeof(LinkModeMenu), new PropertyMetadata(false));

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(LinkMenuState), typeof(LinkModeMenu), new PropertyMetadata(null));

    public LinkModeMenu()
    {
        InitializeComponent();
        // 왜: WPF 는 한글을 글자 사이 아무 데서나 꺾어 「창\n에」가 된다 — 어절 안에 WORD JOINER 를 넣어 띄어쓰기에서만 꺾이게 한다(061 §7-1 어절 줄바꿈)
        foreach (var detail in (TextBlock[])[CliDetail, AppDetail, WebDetail])
        {
            detail.Text = KeepWords(detail.Text);
        }
        // 계약: 열릴 때마다 호스트가 State 를 새로 채운다(Opening) — 표지를 붙인 뒤에 폭을 재야 오른쪽 끝이 맞는다
        // 왜: 단추가 카드·줄의 오른쪽 끝에 있어 왼쪽 정렬로 펼치면 메뉴가 창 밖으로 삐져나간다 — 열린 뒤 오른쪽 끝을 단추에 맞춘다
        Menu.Opened += (_, _) =>
        {
            Opening?.Invoke(this, EventArgs.Empty);
            ApplyState();
            if (Menu.Child is FrameworkElement body)
            {
                body.UpdateLayout();
                if (body.ActualWidth > 0)
                {
                    Menu.HorizontalOffset = Toggle.ActualWidth - body.ActualWidth;
                }
            }
        };
    }

    /// <summary>단추 글자 — 「연동」 또는 「다시 연동」.</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>「데스크톱 앱」 줄을 보일지 — Claude 자리만 true.</summary>
    public bool ShowDesktopApp { get => (bool)GetValue(ShowDesktopAppProperty); set => SetValue(ShowDesktopAppProperty, value); }

    /// <summary>연동 중이면 true — 단추가 흐려지고 눌리지 않는다.</summary>
    public bool IsBusy { get => (bool)GetValue(IsBusyProperty); set => SetValue(IsBusyProperty, value); }

    /// <summary>설정 창의 네모 단추 모양으로 그릴지.</summary>
    public bool Flat { get => (bool)GetValue(FlatProperty); set => SetValue(FlatProperty, value); }

    /// <summary>줄 표지의 근거 — null 이면 표지 없이 세 줄만 보인다.</summary>
    public LinkMenuState? State { get => (LinkMenuState?)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>메뉴가 열리는 순간 — 호스트가 State 를 채우는 자리.</summary>
    public event EventHandler? Opening;

    /// <summary>고른 방식 — SettingsViewModel.LinkModeCli · LinkModeApp · LinkModeWeb 중 하나.</summary>
    public event EventHandler<string>? ModeChosen;

    private void ApplyState()
    {
        var state = State;
        ApplyRow(CliRow, CliBadge, CliBadgeText, state?.CliLinked == true, state?.Recommended == SettingsViewModel.LinkModeCli, state?.CliBroken == true);
        ApplyRow(AppRow, AppBadge, AppBadgeText, state?.AppLinked == true, state?.Recommended == SettingsViewModel.LinkModeApp, false);
        ApplyRow(WebRow, WebBadge, WebBadgeText, state?.WebLinked == true, state?.Recommended == SettingsViewModel.LinkModeWeb, false);
        var install = state is { CliInstalled: false } ? Loc.T("Not installed yet — {0} CLI is installed first (winget), then sign-in continues.", state.CliName) : string.Empty;
        ShowNote(CliNote, install);
        ShowNote(WebNote, install);
        ShowNote(AppNote, state is { DesktopSignedIn: false } ? Loc.T("The Claude desktop app is not signed in on this PC.") : string.Empty);
    }

    // 계약: 연동된 줄은 「연동됨」 표지와 함께 눌리지 않고, 추천 줄은 강조색 테의 「추천」 표지를, 끊긴 줄(추천이 아닐 때)은 「끊김」 표지를 단다 — 끊긴 줄은 다시 고를 수 있다
    private void ApplyRow(Button row, Border badge, TextBlock badgeText, bool linked, bool recommended, bool broken)
    {
        row.IsEnabled = !linked;
        badge.Visibility = linked || recommended || broken ? Visibility.Visible : Visibility.Collapsed;
        badgeText.Text = linked ? Loc.T("Linked") : recommended ? Loc.T("Recommended") : Loc.T("Disconnected");
        var accent = TryFindResource("AccentBrush") as Brush;
        badge.BorderBrush = recommended ? accent : Brushes.Transparent;
        badge.Background = recommended ? Brushes.Transparent : TryFindResource("SubtleOverlayBrush") as Brush;
        badgeText.Foreground = recommended ? accent : TryFindResource("TextMutedBrush") as Brush;
    }

    private static void ShowNote(TextBlock note, string text)
    {
        note.Text = KeepWords(text);
        note.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 계약: 공백이 아닌 글자 사이마다 U+2060 을 끼운다 — 줄은 공백(어절 경계)에서만 꺾인다. 표시 전용이라 저장·비교에는 쓰지 않는다
    private static string KeepWords(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            builder.Append(text[i]);
            if (i + 1 < text.Length && !char.IsWhiteSpace(text[i]) && !char.IsWhiteSpace(text[i + 1]))
            {
                builder.Append('\u2060');
            }
        }

        return builder.ToString();
    }

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        Toggle.IsChecked = false;
        if (sender is FrameworkElement { Tag: string mode })
        {
            ModeChosen?.Invoke(this, mode);
        }
    }
}
