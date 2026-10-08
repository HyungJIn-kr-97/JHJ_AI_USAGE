using System.IO;
using Microsoft.Win32;

namespace costats.App.Services;

/// <summary>
/// 1.0.0 까지 쓰던 이름 costats-jhj 의 데이터 폴더와 자동 실행 등록을 AiUsageMonitor 로 옮긴다.
/// 계약: 로거·설정보다 먼저 한 번 부른다 — 새 폴더가 이미 있으면 아무것도 하지 않는다.
/// TODO: 모든 PC 가 1.0.1 이상이 되면 지운다.
/// </summary>
public static class LegacyMigration
{
    private const string OldName = "costats-jhj";
    private const string NewName = "AiUsageMonitor";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void Run()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var oldDir = Path.Combine(local, OldName);
        var newDir = Path.Combine(local, NewName);
        try
        {
            if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
            {
                Directory.Move(oldDir, newDir);
            }
        }
        catch
        {
            // 옮기지 못하면 새 폴더에서 빈 상태로 시작한다 — 옛 폴더는 남는다
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(OldName) is string command && key.GetValue(NewName) is null)
            {
                key.SetValue(NewName, command);
                key.DeleteValue(OldName, throwOnMissingValue: false);
            }

            // 왜: 원본 costats · 옛 이름이 남긴 작업 관리자 「시작 앱」 켬/끔 기록은 Run 값이 없으면 고아다 — 목록에 유령 항목으로 남으니 지운다
            using var approved = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", writable: true);
            foreach (var orphan in new[] { OldName, "costats" })
            {
                if (approved?.GetValue(orphan) is not null && key?.GetValue(orphan) is null)
                {
                    approved.DeleteValue(orphan, throwOnMissingValue: false);
                }
            }
        }
        catch
        {
            // 레지스트리 접근 실패는 무시한다 — 설정 창에서 다시 켤 수 있다
        }
    }
}
