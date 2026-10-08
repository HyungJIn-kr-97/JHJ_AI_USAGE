using Microsoft.Win32;
using Serilog;

namespace costats.App.Services;

/// <summary>
/// Windows 11 작업 표시줄의 「숨겨진 아이콘(^)」 밖으로 트레이 아이콘을 꺼내 둔다.
/// 계약: Windows 가 아이콘마다 HKCU\Control Panel\NotifyIconSettings\&lt;번호&gt; 를 만들고, IsPromoted=1 이면 항상 보인다.
/// 함정: 그 키는 아이콘이 한 번 표시된 뒤에야 생긴다 — 시작 직후에 부르면 못 찾으므로 잠시 뒤 한 번 더 부른다.
/// 함정: ExecutablePath 는 알려진 폴더 GUID 로 줄여 적히기도 한다 — 전체 경로 대신 파일 이름으로 맞춘다.
/// </summary>
public static class TrayPinService
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <returns>바꾼 항목 수 — 0 이면 아직 Windows 가 이 아이콘을 등록하지 않은 것이다</returns>
    public static int SetPromoted(bool promoted)
    {
        var exeName = System.IO.Path.GetFileName(Environment.ProcessPath ?? "AI-Usage-Monitor_JHJ.exe");
        var changed = 0;
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (root is null)
            {
                return 0;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                using var item = root.OpenSubKey(name, writable: true);
                if (item?.GetValue("ExecutablePath") is string path &&
                    path.EndsWith("\\" + exeName, StringComparison.OrdinalIgnoreCase))
                {
                    item.SetValue("IsPromoted", promoted ? 1 : 0, RegistryValueKind.DWord);
                    changed++;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Log.Warning(ex, "Could not update tray icon visibility");
        }

        return changed;
    }

    public static void ApplyAfterIconShown(bool promoted)
    {
        if (SetPromoted(promoted) == 0)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => SetPromoted(promoted), TaskScheduler.Default);
        }
    }
}
