using System.Windows;
using System.Windows.Controls;

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

    public LinkModeMenu()
    {
        InitializeComponent();
        // 왜: 단추가 카드·줄의 오른쪽 끝에 있어 왼쪽 정렬로 펼치면 메뉴가 창 밖으로 삐져나간다 — 열린 뒤 오른쪽 끝을 단추에 맞춘다
        Menu.Opened += (_, _) =>
        {
            if (Menu.Child is FrameworkElement body && body.ActualWidth > 0)
            {
                Menu.HorizontalOffset = Toggle.ActualWidth - body.ActualWidth;
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

    /// <summary>고른 방식 — SettingsViewModel.LinkModeCli · LinkModeApp · LinkModeWeb 중 하나.</summary>
    public event EventHandler<string>? ModeChosen;

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        Toggle.IsChecked = false;
        if (sender is FrameworkElement { Tag: string mode })
        {
            ModeChosen?.Invoke(this, mode);
        }
    }
}
