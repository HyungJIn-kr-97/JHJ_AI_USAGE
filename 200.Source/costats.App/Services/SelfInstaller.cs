using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace costats.App.Services;

/// <summary>
/// 받은 exe 를 그대로 실행하면 고정 위치(%LOCALAPPDATA%\AiUsageMonitor\app)에 스스로 설치하고 거기서 다시 뜬다.
/// 계약: 설치 폴더는 이 앱 전용이다 — 업데이트가 폴더를 통째로 갈아 끼우므로 다른 파일을 두지 않는다.
/// </summary>
public static class SelfInstaller
{
    private const string AppName = "AiUsageMonitor";
    private const string ShortcutName = "AI 통합 사용량 모니터.lnk";
    private static readonly string[] SiblingFiles = ["appsettings.json", "apply-update.ps1"];

    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "app");

    private static string BaseDir => AppContext.BaseDirectory.TrimEnd('\\', '/');

    public static bool IsInstalledLocation =>
        string.Equals(Path.GetFullPath(BaseDir), Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase);

    /// <returns>설치본을 띄웠으면 true — 호출자는 지금 프로세스를 끝낸다.</returns>
    public static bool TryInstallAndRelaunch()
    {
        var exePath = Environment.ProcessPath;
        if (IsInstalledLocation || exePath is null || IsDevOrPackagedRun())
        {
            return false;
        }

        var answer = System.Windows.MessageBox.Show(
            $"이 PC 에 설치하고 실행할까요?\n(Install to this PC and run?)\n\n{InstallDir}\n\n「아니요」를 고르면 설치 없이 이 위치에서 실행합니다.",
            "AI 통합 사용량 모니터",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return false;
        }

        try
        {
            StopInstalledInstances();
            Directory.CreateDirectory(InstallDir);

            var installedExe = Path.Combine(InstallDir, Path.GetFileName(exePath));
            File.Copy(exePath, installedExe, overwrite: true);
            foreach (var name in SiblingFiles)
            {
                var source = Path.Combine(BaseDir, name);
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(InstallDir, name), overwrite: true);
                }
            }

            CreateStartMenuShortcut(installedExe);
            RepointStartupEntry(installedExe);

            Process.Start(new ProcessStartInfo(installedExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"설치하지 못했습니다. 이 위치에서 그대로 실행합니다.\n\n{ex.Message}",
                "AI 통합 사용량 모니터",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }
    }

    private static bool IsDevOrPackagedRun()
    {
        var dir = BaseDir;
        return dir.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
               (dir.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) &&
                dir.Contains(@"\200.Source\", StringComparison.OrdinalIgnoreCase));
    }

    // 왜: 설치본이 떠 있으면 exe 가 잠겨 덮어쓸 수 없다
    private static void StopInstalledInstances()
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Environment.ProcessPath!)))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (process.Id != Environment.ProcessId && path is not null &&
                    path.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch
            {
                // 다른 사용자의 프로세스 등 읽을 수 없는 것은 건너뛴다
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>시작 메뉴 바로가기가 지금 exe 를 가리키지 않으면 다시 쓴다.</summary>
    // 왜: 자동 업데이트는 설치 폴더를 통째로 갈아 끼우기만 한다 — 실행 파일 이름이 바뀌면 바로가기가 없는 파일을 가리킨다
    // TODO: 옛 이름 사본을 꾸러미에서 빼는 릴리스 다음에 지운다 — 300.Docs\실행파일-이름-전환.md
    public static void RefreshShortcutIfStale()
    {
        var exePath = Environment.ProcessPath;
        if (!IsInstalledLocation || exePath is null)
        {
            return;
        }

        try
        {
            var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            if (!File.Exists(shortcutPath))
            {
                return;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            string target = shell.CreateShortcut(shortcutPath).TargetPath;
            if (!string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase))
            {
                CreateStartMenuShortcut(exePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // 바로가기를 못 고쳐도 앱은 뜬다 — 트레이·시작 프로그램으로 열 수 있다
        }
    }

    private static void CreateStartMenuShortcut(string targetExe)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(Path.Combine(programs, ShortcutName));
        shortcut.TargetPath = targetExe;
        shortcut.WorkingDirectory = InstallDir;
        shortcut.Save();
    }

    // 왜: 빌드 폴더 등 옛 경로로 등록된 자동 실행이 있으면 설치본으로 돌려야 다음 로그인에 설치본이 뜬다
    private static void RepointStartupEntry(string targetExe)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key?.GetValue(AppName) is not null)
        {
            key.SetValue(AppName, $"\"{targetExe}\"");
        }
    }
}
