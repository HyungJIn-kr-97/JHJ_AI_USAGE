using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using costats.Application.Alerts;

namespace costats.App.Views;

/// <summary>
/// 사용률 알림 토스트 창. 항목이 하나라도 있으면 보이고, 모두 사라지면 숨는다.
/// 계약: 포커스를 가져가지 않는다(ShowActivated=false) — 작업 중인 창을 방해하지 않는다.
/// 계약: 자리는 TaskbarAnchor 가 정한 작업 영역 우측 아래, 팝업이 떠 있으면 그 왼쪽이다.
/// </summary>
public partial class UsageAlertToastWindow : Window
{
    private readonly TaskbarPositionService _anchor;
    private readonly Func<Window?> _widget;
    private readonly Func<string> _corner;

    public ObservableCollection<UsageToastItem> Items { get; } = [];

    /// <param name="corner">AppSettings.UsageAlertPosition — "bottom-right" | "top-right" | "bottom-left" | "top-left"</param>
    public UsageAlertToastWindow(TaskbarPositionService anchor, Func<Window?> widget, Func<string> corner)
    {
        _anchor = anchor;
        _widget = widget;
        _corner = corner;
        InitializeComponent();
        SizeChanged += (_, _) => Reposition();
        Items.CollectionChanged += (_, _) =>
        {
            if (Items.Count == 0)
            {
                Hide();
            }
        };
    }

    /// <summary>항목을 올리고 seconds 뒤에 스스로 내린다. 같은 Key 가 이미 떠 있으면 내용을 바꾸고 시간을 다시 센다.</summary>
    public void Push(UsageToastItem item, int seconds)
    {
        var existing = Items.FirstOrDefault(i => i.Key == item.Key);
        if (existing is not null)
        {
            Items.Remove(existing);
            existing.Timer?.Stop();
        }

        // 왜: 한 번에 넷을 넘으면 화면을 가린다 — 가장 오래된 것을 내린다
        while (Items.Count >= 4)
        {
            Dismiss(Items[0]);
        }

        item.Timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(Math.Max(3, seconds)) };
        item.Timer.Tick += (_, _) => Dismiss(item);
        item.Timer.Start();
        Items.Add(item);

        if (!IsVisible)
        {
            Show();
        }

        Reposition();
    }

    public void Reposition()
    {
        var height = ActualHeight > 0 ? ActualHeight : Height;
        // 왜: SystemParameters.WorkArea 는 앱이 뜰 때의 배율로 굳는다 — 원격 접속으로 화면이 바뀌면 어긋난다
        var area = TaskbarPositionService.WorkAreaFor(this);
        const double margin = 12;
        var corner = _corner();
        // 계약: 우측 아래(기본)만 작업 표시줄 위치를 따르고 팝업을 피한다 — 나머지 셋은 작업 영역 모서리 고정
        var position = corner switch
        {
            "top-right" => new Point(area.Right - Width - margin, area.Top + margin),
            "top-left" => new Point(area.Left + margin, area.Top + margin),
            "bottom-left" => new Point(area.Left + margin, area.Bottom - height - margin),
            _ => _anchor.GetWidgetPosition(Width, height, margin, this),
        };
        var widget = _widget();
        if (corner is not ("top-right" or "top-left" or "bottom-left") && widget is { IsVisible: true })
        {
            // 왜: 팝업과 같은 구석을 쓰면 토스트가 팝업 뒤에 숨는다 — 팝업 왼쪽에 바닥을 맞춰 둔다
            var left = widget.Left - Width - 8;
            if (left >= area.Left)
            {
                position = new Point(left, Math.Max(area.Top, widget.Top + widget.ActualHeight - height));
            }
        }

        Left = position.X;
        Top = position.Y;
    }

    private void Dismiss(UsageToastItem item)
    {
        item.Timer?.Stop();
        item.Timer = null;
        Items.Remove(item);
    }

    private static UsageToastItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as UsageToastItem;

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ItemOf(sender) is { } item)
        {
            Dismiss(item);
        }
    }

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            item.Open?.Invoke();
            Dismiss(item);
        }
    }

    // 왜: 읽는 동안 사라지면 안 된다 — 마우스가 올라가 있으면 시간을 멈춘다
    private void OnItemEnter(object sender, MouseEventArgs e) => ItemOf(sender)?.Timer?.Stop();

    private void OnItemLeave(object sender, MouseEventArgs e) => ItemOf(sender)?.Timer?.Start();
}

/// <summary>토스트 한 장의 내용. Kind 는 제목 앞 아이콘, Percent 는 0~100, BarColor 는 "#RRGGBB".</summary>
public sealed class UsageToastItem
{
    public required string Key { get; init; }
    public required UsageWindowKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string Advice { get; init; } = string.Empty;
    public required int Percent { get; init; }
    public double Maximum => 100;
    public required string BarColor { get; init; }
    public Action? Open { get; init; }
    internal DispatcherTimer? Timer { get; set; }
}
