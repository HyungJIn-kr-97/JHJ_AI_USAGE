using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace costats.Setup
{
    /// <summary>
    /// 받은 zip 을 앱 설치 폴더에 풀고 시작 메뉴 바로가기를 만든다.
    /// 계약: 설치 폴더·바로가기 이름은 costats.App/Services/SelfInstaller.cs 와 같다 — 어느 쪽으로 깔아도 같은 자리에 놓인다.
    /// 함정: 설치 폴더는 이 앱 전용이라 통째로 비우고 다시 푼다 — 다른 파일을 두는 곳이 아니다.
    /// </summary>
    internal static class Installer
    {
        public const string ExeName = "AiUsageMonitor.exe";
        private const string ShortcutName = "AI 통합 사용량 모니터.lnk";

        // 계약: 설정·이력·동의가 사는 자료 폴더 — 설치 폴더를 어디로 골라도 여기는 고정이다
        public static string DataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor");

        public static string DefaultInstallDir { get; } = Path.Combine(DataDir, "app");

        // 계약: 사용자가 고른 설치 폴더는 install-dir.txt 에 남긴다 — 앱의 「이 버전 설치」(--silent)와 「제거」가 같은 자리를 쓴다
        private static readonly string InstallDirFile = Path.Combine(DataDir, "install-dir.txt");
        private static string _installDir;

        public static string InstallDir
        {
            get => _installDir ?? (_installDir = ReadSavedInstallDir() ?? DefaultInstallDir);
            set => _installDir = value;
        }

        private static string ReadSavedInstallDir()
        {
            try
            {
                var saved = File.Exists(InstallDirFile) ? File.ReadAllText(InstallDirFile).Trim() : null;
                return string.IsNullOrEmpty(saved) ? null : saved;
            }
            catch (IOException)
            {
                return null;
            }
        }

        // 계약: 설치본 찾기 — 저장값 · 시작 프로그램 등록(Run) · 시작 메뉴 바로가기 · 기본 폴더 순으로 exe 가 있는 첫 자리를 InstallDir 로 삼는다
        public static void DetectInstallDir()
        {
            var candidates = new System.Collections.Generic.List<string> { ReadSavedInstallDir() };
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                {
                    var run = key?.GetValue("AiUsageMonitor") as string;
                    if (!string.IsNullOrEmpty(run))
                    {
                        candidates.Add(Path.GetDirectoryName(run.Trim().Trim('"')));
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is IOException)
            {
            }

            try
            {
                var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
                var shellType = File.Exists(shortcut) ? Type.GetTypeFromProgID("WScript.Shell") : null;
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    string target = shell.CreateShortcut(shortcut).TargetPath;
                    if (!string.IsNullOrEmpty(target))
                    {
                        candidates.Add(Path.GetDirectoryName(target));
                    }
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
            }

            candidates.Add(DefaultInstallDir);
            var found = candidates.FirstOrDefault(dir => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, ExeName)));
            _installDir = found ?? _installDir ?? ReadSavedInstallDir() ?? DefaultInstallDir;
        }
        private static void SaveInstallDir()
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(InstallDirFile, InstallDir);
        }

        public static string InstalledExe => Path.Combine(InstallDir, ExeName);

        /// <returns>설치돼 있으면 "1.0.0.20261006"(옛 설치본은 "1.0.3"), 없으면 null</returns>
        public static string InstalledVersion()
        {
            if (!File.Exists(InstalledExe))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(InstalledExe);
            // 왜: 날짜는 FileVersion 에 못 담아 ProductVersion 에만 있다 — "+커밋" 꼬리는 뗀다
            var product = (info.ProductVersion ?? string.Empty).Split('+')[0];
            if (Version.TryParse(product, out var dated) && dated.Revision > 0)
            {
                return dated.ToString(4);
            }

            return Version.TryParse(info.FileVersion, out var v) ? v.ToString(3) : info.ProductVersion;
        }

        public static void Install(string zipPath)
        {
            StopRunningApp();

            // 왜: 풀기에 실패해도 옛 버전으로 돌아갈 수 있게 지우지 않고 옆으로 옮겨 둔다
            var backup = InstallDir + ".__backup";
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }

            if (Directory.Exists(InstallDir))
            {
                Directory.Move(InstallDir, backup);
            }

            try
            {
                Directory.CreateDirectory(InstallDir);
                ZipFile.ExtractToDirectory(zipPath, InstallDir);
                if (!File.Exists(InstalledExe))
                {
                    throw new FileNotFoundException("받은 파일에 " + ExeName + " 이 없습니다.");
                }
            }
            catch
            {
                if (Directory.Exists(InstallDir))
                {
                    Directory.Delete(InstallDir, recursive: true);
                }

                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, InstallDir);
                }

                throw;
            }

            if (Directory.Exists(backup))
            {
                try { Directory.Delete(backup, recursive: true); } catch (IOException) { }
            }

            CreateShortcut();
            SaveInstallDir();
        }

        public static void Launch() =>
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });

        // 왜: 앱이 떠 있으면 exe 가 잠겨 폴더를 옮길 수 없다
        private static void StopRunningApp()
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path != null && path.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                {
                    // 다른 사용자의 프로세스 등 읽을 수 없는 것은 건너뛴다
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void CreateShortcut()
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }

            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(Path.Combine(programs, ShortcutName));
            shortcut.TargetPath = InstalledExe;
            shortcut.WorkingDirectory = InstallDir;
            shortcut.Save();
        }

        // 계약: keepData 면 앱 폴더·바로가기·시작 프로그램만 지우고 설정·이력(상위 폴더)은 남긴다
        public static void Uninstall(bool keepData)
        {
            StopRunningApp();
            if (Directory.Exists(InstallDir))
            {
                Directory.Delete(InstallDir, true);
            }

            var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            if (File.Exists(shortcut))
            {
                File.Delete(shortcut);
            }

            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key != null && key.GetValue("AiUsageMonitor") != null)
                {
                    key.DeleteValue("AiUsageMonitor", false);
                }
            }

            if (File.Exists(InstallDirFile))
            {
                File.Delete(InstallDirFile);
            }

            if (!keepData && Directory.Exists(DataDir))
            {
                Directory.Delete(DataDir, true);
            }
        }
        public static string FindArg(string[] args, string name) =>
            args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
    }
}
