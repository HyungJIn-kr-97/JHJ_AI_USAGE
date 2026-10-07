using System.IO;

namespace costats.App.Services;

/// <summary>
/// 로컬 이벤트 기록 — %LOCALAPPDATA%\AiUsageMonitor\diagnostics\events.log 한 줄씩(시각 | 분류 | 내용).
/// 계약: Enabled 가 false 면 아무것도 쓰지 않는다. 2,000줄을 넘으면 뒤 1,000줄만 남긴다. 어디로도 보내지 않는다.
/// </summary>
public static class DiagnosticsLog
{
    private const int KeepLines = 1000;
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor", "diagnostics", "events.log");

    public static bool Enabled { get; set; } = true;

    public static void Record(string category, string message)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {category} | {message.ReplaceLineEndings(" ")}{Environment.NewLine}");
                if (File.ReadLines(Path).Count() > KeepLines * 2)
                {
                    var tail = File.ReadAllLines(Path).TakeLast(KeepLines);
                    File.WriteAllLines(Path, tail);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static IReadOnlyList<string> Tail(int lines)
    {
        lock (Gate)
        {
            try
            {
                return File.Exists(Path) ? File.ReadAllLines(Path).TakeLast(lines).ToList() : [];
            }
            catch (IOException)
            {
                return [];
            }
        }
    }
}
