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
        public const string ExeName = "JHJ_AI-Usage-Monitor.exe";

        // 왜: 옛 버전이 깐 설치 폴더에는 이 이름들만 있다 — 기존 설치를 알아보고 종료시키는 데만 쓴다
        // TODO: 옛 이름 사본을 꾸러미에서 빼는 릴리스에서 함께 지운다
        public static readonly string[] LegacyExeNames = { "AI-Usage-Monitor_JHJ.exe", "AiUsageMonitor.exe" };
        // 계약: costats.App 의 SelfInstaller.ShortcutName 과 같은 값이어야 한다
        private const string ShortcutName = "JHJ AI 통합 사용량 모니터.lnk";

        // 계약: 옛 이름 바로가기 — 설치 폴더를 찾을 때 보고, 제거할 때 함께 지운다
        private static readonly string[] LegacyShortcutNames = { "AI 통합 사용량 모니터.lnk" };

        // 계약: 설정·이력·동의가 사는 자료 폴더 — 설치 폴더를 어디로 골라도 여기는 고정이다
        public static string DataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JHJ_AI-Usage-Monitor");

        public static string DefaultInstallDir { get; } = Path.Combine(DataDir, "app");

        // 계약: 앱은 .NET 10 데스크톱 런타임이 있어야 뜬다 — 런타임을 품지 않는 대신 꾸러미가 64MB 에서 4MB 로 줄었다
        public const string RuntimeName = ".NET 10 데스크톱 런타임";

        public const string RuntimeUrl = "https://dotnet.microsoft.com/download/dotnet/10.0/runtime";

        /// <summary>이 PC 에 앱이 요구하는 런타임이 깔려 있나.</summary>
        // 함정: dotnet --list-runtimes 를 부르면 PATH 에 없을 때 틀린 답이 된다 — 폴더를 직접 본다
        public static bool IsRuntimeInstalled()
        {
            try
            {
                foreach (var root in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                })
                {
                    var dir = Path.Combine(root, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                    if (Directory.Exists(dir) &&
                        Directory.GetDirectories(dir).Any(d => Path.GetFileName(d).StartsWith("10.", StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return false;
        }

        // 계약: 사용자가 고른 설치 폴더는 install-dir.txt 에 남긴다 — 앱의 「이 버전 설치」(--silent)와 「제거」가 같은 자리를 쓴다
        private static readonly string InstallDirFile = Path.Combine(DataDir, "install-dir.txt");

        // 계약: 「프로그램 추가/제거」 등록 — winget 이 설치 확인·업그레이드·제거에 쓴다. 제거 명령은 DataDir 에 복사해 둔 이 설치 관리자다
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\JHJ_AI-Usage-Monitor";
        public static string SetupCopyPath => Path.Combine(DataDir, "JHJ_AI-Usage-Monitor-Setup.exe");
        private static string SelfPath => Path.GetFullPath(typeof(Installer).Assembly.Location);

        private static void KeepSetupCopy()
        {
            try
            {
                if (!string.Equals(SelfPath, Path.GetFullPath(SetupCopyPath), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(DataDir);
                    File.Copy(SelfPath, SetupCopyPath, true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void RegisterUninstall()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (key == null)
                {
                    return;
                }

                key.SetValue("DisplayName", "JHJ AI 통합 사용량 모니터");
                key.SetValue("DisplayVersion", InstalledVersion() ?? string.Empty);
                key.SetValue("Publisher", "HyungJin Ju");
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("DisplayIcon", InstalledExe);
                key.SetValue("UninstallString", "\"" + SetupCopyPath + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + SetupCopyPath + "\" --uninstall --silent");
                key.SetValue("URLInfoAbout", "https://github.com/HyungJIn-kr-97/JHJ_AI_USAGE");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(DirectorySize(InstallDir) / 1024), Microsoft.Win32.RegistryValueKind.DWord);
            }
        }

        private static long DirectorySize(string dir) =>
            Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

        // 왜: 실행 중인 자기 exe 는 못 지운다 — 종료 뒤 cmd 가 지우고, 폴더가 비면 폴더도 없앤다
        private static void ScheduleSelfDelete(string file, string dirToRemove)
        {
            var script = "/c ping -n 3 127.0.0.1 >nul & del /q \"" + file + "\"" + (dirToRemove != null ? " & rmdir \"" + dirToRemove + "\"" : string.Empty);
            Process.Start(new ProcessStartInfo("cmd.exe", script) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }

        private static void DeleteDataDir()
        {
            var self = SelfPath;
            var root = Path.GetFullPath(DataDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var selfInside = self.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            foreach (var entry in Directory.EnumerateFileSystemEntries(DataDir).ToList())
            {
                if (selfInside && string.Equals(Path.GetFullPath(entry), self, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, true);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            if (selfInside)
            {
                ScheduleSelfDelete(self, DataDir);
            }
            else
            {
                Directory.Delete(DataDir, true);
            }
        }
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
                    var run = key?.GetValue("JHJ_AI-Usage-Monitor") as string;
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
                var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                var shortcut = new[] { ShortcutName }.Concat(LegacyShortcutNames)
                    .Select(name => Path.Combine(programs, name))
                    .FirstOrDefault(File.Exists) ?? Path.Combine(programs, ShortcutName);
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
            var found = candidates.FirstOrDefault(dir => !string.IsNullOrEmpty(dir)
                && (File.Exists(Path.Combine(dir, ExeName)) || LegacyExeNames.Any(n => File.Exists(Path.Combine(dir, n)))));
            _installDir = found ?? _installDir ?? ReadSavedInstallDir() ?? DefaultInstallDir;
        }
        private static void SaveInstallDir()
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(InstallDirFile, InstallDir);
        }

        // 계약: 새 이름이 없으면 1.1.0 이전이 깐 옛 이름을 가리킨다 — 버전 표시·제거가 기존 설치에서도 되게 한다
        public static string InstalledExe
        {
            get
            {
                var current = Path.Combine(InstallDir, ExeName);
                if (File.Exists(current))
                {
                    return current;
                }

                var legacy = LegacyExeNames.Select(n => Path.Combine(InstallDir, n)).FirstOrDefault(File.Exists);
                return legacy ?? current;
            }
        }

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
            KeepSetupCopy();
            RegisterUninstall();
        }

        public static void Launch() =>
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });

        // 왜: 앱이 떠 있으면 exe 가 잠겨 폴더를 옮길 수 없다
        private static void StopRunningApp()
        {
            // 계약: 옛 이름으로 떠 있는 기존 설치본도 함께 내린다 — 안 내리면 폴더 교체가 막힌다
            foreach (var process in new[] { ExeName }.Concat(LegacyExeNames)
                .SelectMany(name => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name))))
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

            // 계약: 옛 이름 바로가기도 함께 지운다 — 안 지우면 죽은 링크가 시작 메뉴에 남는다
            foreach (var name in new[] { ShortcutName }.Concat(LegacyShortcutNames))
            {
                var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), name);
                if (File.Exists(shortcut))
                {
                    File.Delete(shortcut);
                }
            }

            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key != null && key.GetValue("JHJ_AI-Usage-Monitor") != null)
                {
                    key.DeleteValue("JHJ_AI-Usage-Monitor", false);
                }
            }

            if (File.Exists(InstallDirFile))
            {
                File.Delete(InstallDirFile);
            }

            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            if (!keepData && Directory.Exists(DataDir))
            {
                DeleteDataDir();
            }
            else if (File.Exists(SetupCopyPath))
            {
                if (string.Equals(SelfPath, Path.GetFullPath(SetupCopyPath), StringComparison.OrdinalIgnoreCase))
                {
                    ScheduleSelfDelete(SetupCopyPath, null);
                }
                else
                {
                    File.Delete(SetupCopyPath);
                }
            }
        }
        public static string FindArg(string[] args, string name) =>
            args.SkipWhile(a => !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
    }
}
