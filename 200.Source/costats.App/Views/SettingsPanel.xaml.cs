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

        private void OnIconPixelDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Paint(sender);
            e.Handled = true;
        }

        private void OnIconPixelEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                Paint(sender);
            }
        }

        private void Paint(object sender)
        {
            if (sender is FrameworkElement { DataContext: IconPixel pixel } && DataContext is SettingsViewModel viewModel)
            {
                viewModel.PaintPixel(pixel);
            }
        }

        // 왜: 편집을 열면 바로 타이핑할 수 있게 명칭 칸에 초점을 주고 전체 선택한다
        private void OnNameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox { IsVisible: true } box)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    box.Focus();
                    box.SelectAll();
                });
            }
        }

        // 함정: Alt 가 눌리면 e.Key 는 System, 한글 IME 가 켜지면 ImeProcessed 로 온다 — 실제 키는 따로 꺼낸다
        private void OnHotkeyBoxKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key switch
            {
                System.Windows.Input.Key.System => e.SystemKey,
                System.Windows.Input.Key.ImeProcessed => e.ImeProcessedKey,
                _ => e.Key,
            };
            if (sender is FrameworkElement { DataContext: HotkeySlotRow row })
            {
                (DataContext as SettingsViewModel)?.CaptureHotkey(row, key, System.Windows.Input.Keyboard.Modifiers);
            }
        }

        // 계약: 수정키만 눌렀다 떼면 「Alt+…」 미리보기를 지운다 — 고른 조합(없으면 저장된 값)으로 돌아간다
        private void OnHotkeyBoxKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None &&
                sender is FrameworkElement { DataContext: HotkeySlotRow row } && row.Text.EndsWith('…'))
            {
                (DataContext as SettingsViewModel)?.CancelHotkeyPreview(row);
            }
        }

        // 왜: 「+」로 생긴 줄은 입력 중 상태로 태어난다 — 바로 키를 받게 초점을 준다
        private void OnHotkeyBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: HotkeySlotRow { IsEditing: true } } box)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => box.Focus());
            }
        }

        // 왜: 「변경」을 누르면 입력란이 풀린다 — 다시 클릭하지 않아도 키를 받게 초점을 준다
        private void OnHotkeyBoxEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox { IsEnabled: true, IsLoaded: true } box)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => box.Focus());
            }
        }

        private void OnHotkeyBoxFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
            (DataContext as SettingsViewModel)?.BeginHotkeyCapture();

        private void OnHotkeyBoxBlur(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
            (DataContext as SettingsViewModel)?.EndHotkeyCapture();

        // 왜: 초점을 쥔 채 팝업이 숨으면 LostKeyboardFocus 가 안 와 단축키가 내려간 채 남는다
        // 함정: 줄을 더하면 목록이 다시 그려지며 옛 입력란도 「안 보임」이 된다 — 설정 화면 자체가 숨을 때만 입력을 닫는다
        // 함정: 이 신호 안에서 목록을 바꾸면 다시 그리는 도중에 또 바뀌어 줄이 통째로 비었다 — 한 박자 뒤에 닫는다
        private void OnHotkeyBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is UIElement { IsVisible: false } && !IsVisible)
            {
                Dispatcher.BeginInvoke(() => (DataContext as SettingsViewModel)?.CloseHotkeyEditing());
            }
        }

        private Point _dragStart;
        private ExtraAccountRow? _dragRow;

        private void OnAccountDragHandleDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(this);
            _dragRow = (sender as FrameworkElement)?.DataContext as ExtraAccountRow;
            // 왜: 팝업 창은 빈 곳을 누르면 창을 끌어 옮긴다 — 손잡이 누름이 거기까지 가지 않게 막는다
            e.Handled = true;
        }

        private void OnAccountDragHandleMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragRow is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                return;
            }

            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance)
            {
                return;
            }

            var row = _dragRow;
            _dragRow = null;
            DragDrop.DoDragDrop((DependencyObject)sender, row, DragDropEffects.Move);
        }

        private void OnAccountRowDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetData(typeof(ExtraAccountRow)) is ExtraAccountRow ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnAccountRowDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(ExtraAccountRow)) is ExtraAccountRow dragged &&
                (sender as FrameworkElement)?.DataContext is ExtraAccountRow target &&
                DataContext is SettingsViewModel viewModel)
            {
                viewModel.MoveRow(dragged, target);
            }

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
