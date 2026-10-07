using System.IO;

namespace costats.App.Services;

/// <summary>
/// 사용 정보 수집 동의 — %LOCALAPPDATA%\AiUsageMonitor\consent.txt 가 있으면 동의한 것이다.
/// 계약: 동의는 웹 설치 관리자(costats.Setup)의 체크박스가 받고 이 파일을 쓴다. 앱은 묻지 않고 진단 보고서에 동의 시각만 적는다.
/// </summary>
public static class DiagnosticsConsent
{
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor", "consent.txt");

    public static bool IsGiven => File.Exists(Path);

    public static string? GivenText
    {
        get
        {
            try { return IsGiven ? File.ReadAllText(Path).Trim() : null; }
            catch (IOException) { return null; }
        }
    }

    public static void Give(string version)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} v{version}");
        }
        catch (IOException)
        {
        }
    }

}
