using System.IO;
using Microsoft.Win32;

namespace costats.App.Services;

/// <summary>
/// 옛 이름으로 쌓인 데이터 폴더와 자동 실행 등록을 지금 이름(JHJ_AI-Usage-Monitor)으로 옮긴다.
/// 계약: 로거·설정보다 먼저 한 번 부른다 — 새 폴더가 이미 있으면 그 세대는 건너뛴다.
/// 계약: 세대는 오래된 것부터 차례로 옮긴다 — costats-jhj 로 머문 PC 도 한 번에 지금 이름까지 온다.
/// TODO: 모든 PC 가 1.1.0 이상이 되면 지운다.
/// </summary>
public static class LegacyMigration
{
    // 계약: 왼쪽이 옛 이름, 오른쪽이 그다음 이름 — 순서대로 이어 옮긴다
    private static readonly (string From, string To)[] Generations =
    [
        ("costats-jhj", "AiUsageMonitor"),
        ("AiUsageMonitor", "AI-Usage-Monitor_JHJ"),
        ("AI-Usage-Monitor_JHJ", "JHJ_AI-Usage-Monitor"),
    ];

    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private const string ApprovedKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static void Run()
    {
        foreach (var (from, to) in Generations)
        {
            MoveDataDir(from, to);
            MoveStartupEntry(from, to);
        }

        CleanOrphanStartupApproved();
    }

    // 함정: 자료 폴더 안에 설치 폴더(app)가 있다 — 지금 돌고 있는 exe 가 그 안이라 통째로 옮기면 잠겨서 전부 실패한다
    // 계약: app 만 남기고 항목별로 옮긴다. 옛 자리의 app 은 다음 설치·업데이트가 새 자리로 데려간다
    private const string InstallFolder = "app";

    private static void MoveDataDir(string from, string to)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var oldDir = Path.Combine(local, from);
        var newDir = Path.Combine(local, to);
        if (!Directory.Exists(oldDir))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(oldDir))
        {
            var name = Path.GetFileName(path);
            if (name.Equals(InstallFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var target = Path.Combine(newDir, name);
                if (File.Exists(target) || Directory.Exists(target))
                {
                    // 왜: 새 자리에 이미 있으면 그쪽이 지금 값이다 — 덮어쓰면 이번 실행의 설정이 옛 값으로 돌아간다
                    continue;
                }

                Directory.CreateDirectory(newDir);
                if (Directory.Exists(path))
                {
                    Directory.Move(path, target);
                }
                else
                {
                    File.Move(path, target);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 하나를 못 옮겨도 나머지는 옮긴다 — 다음 실행에 다시 시도된다
            }
        }

        RepointAbsolutePaths(newDir, oldDir);

        try
        {
            // 왜: 다 옮기고 나면 빈 껍데기가 남는다 — 비었을 때만 지운다(app 이 남아 있으면 그대로 둔다)
            if (!Directory.EnumerateFileSystemEntries(oldDir).Any())
            {
                Directory.Delete(oldDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 빈 폴더가 남아도 동작에는 지장이 없다
        }
    }

    // 함정: 계정 목록(accounts\config.json)은 계정 폴더를 절대경로로 들고 있다 — 폴더만 옮기면 없는 곳을 가리켜 계정이 통째로 사라진다
    // 계약: 루트와 계정 폴더 바로 아래의 json 만 고친다 — 이력·표시 데이터는 경로를 담지 않는다
    private static readonly string[] PathHolders = ["", "accounts", "accounts-codex"];

    private static void RepointAbsolutePaths(string newDir, string oldDir)
    {
        foreach (var sub in PathHolders)
        {
            var dir = sub.Length == 0 ? newDir : Path.Combine(newDir, sub);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    if (!text.Contains(oldDir, StringComparison.OrdinalIgnoreCase) &&
                        !text.Contains(JsonEscaped(oldDir), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    text = text
                        .Replace(JsonEscaped(oldDir), JsonEscaped(newDir), StringComparison.OrdinalIgnoreCase)
                        .Replace(oldDir, newDir, StringComparison.OrdinalIgnoreCase);
                    File.WriteAllText(file, text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 못 고치면 그 계정만 안 보인다 — 설정 › 계정에서 다시 더하면 된다
                }
            }
        }
    }

    // 계약: JSON 문자열 안의 경로는 역슬래시가 두 번이다
    private static string JsonEscaped(string path) => path.Replace("\\", "\\\\");

    private static void MoveStartupEntry(string from, string to)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(from) is not string command || run.GetValue(to) is not null)
            {
                return;
            }

            run.SetValue(to, command);
            run.DeleteValue(from, throwOnMissingValue: false);

            // 왜: 작업 관리자 「시작 앱」의 켬/끔 기록은 Run 값 이름으로 묶인다 — 같이 옮기지 않으면 꺼 둔 상태를 잃는다
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            if (approved?.GetValue(from) is byte[] flags && approved.GetValue(to) is null)
            {
                approved.SetValue(to, flags, RegistryValueKind.Binary);
                approved.DeleteValue(from, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 레지스트리 접근 실패는 무시한다 — 설정 창에서 다시 켤 수 있다
        }
    }

    // 왜: Run 값이 없는 「시작 앱」 기록은 고아다 — 목록에 유령 항목으로 남으니 지운다
    private static void CleanOrphanStartupApproved()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            foreach (var orphan in new[] { "costats", "costats-jhj", "AiUsageMonitor", "AI-Usage-Monitor_JHJ" })
            {
                if (approved?.GetValue(orphan) is not null && run?.GetValue(orphan) is null)
                {
                    approved.DeleteValue(orphan, throwOnMissingValue: false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 유령 항목이 남아도 동작에는 지장이 없다
        }
    }
}
