using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace costats.App.Services;

/// <summary>
/// 창 밖 마우스 클릭 감시 — 팝업이 포커스 없이 떠 있을 때(설치·업데이트 뒤 자동 실행 · 다른 프로세스가 띄움) Deactivated 가 오지 않아 닫히지 않는 구멍을 메운다.
/// 계약: 저수준 마우스 훅(WH_MOUSE_LL)은 Start~Stop 사이에만 걸린다. 이 프로세스의 창(팝업 · 콤보 드롭다운 · 미니 위젯)을 누른 것은 바깥으로 치지 않는다.
/// 함정: 훅 콜백은 빨리 돌아와야 한다 — 판정만 하고 실제 닫기는 Dispatcher 로 미룬다.
/// </summary>
public sealed class OutsideClickWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    private readonly Window _owner;
    private readonly Action _onOutsideClick;
    // 왜: 델리게이트를 필드에 붙들지 않으면 GC 가 걷어가 훅이 조용히 죽는다
    private readonly HookProc _proc;
    private readonly uint _pid = (uint)Environment.ProcessId;
    private IntPtr _hook;

    public OutsideClickWatcher(Window owner, Action onOutsideClick)
    {
        _owner = owner;
        _onOutsideClick = onOutsideClick;
        _proc = Callback;
    }

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var target = WindowFromPoint(info.pt);
            GetWindowThreadProcessId(target, out var pid);
            if (pid != _pid)
            {
                _owner.Dispatcher.BeginInvoke(_onOutsideClick);
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
