using System.IO;

namespace costats.App.Services;

/// <summary>
/// 로그인에 쓸 공식 CLI(claude · codex)의 실행 파일을 찾는다. 못 찾으면 null.
/// </summary>
public static class CliLocator
{
    private static readonly string[] Extensions = [".exe", ".cmd", ".bat"];

    public static string? FindClaude() => FindOnPath("claude");

    // 왜: Codex 를 VS Code 확장으로만 쓰는 PC 는 PATH 에 codex 가 없다 — 확장에 든 codex.exe 로 로그인할 수 있다
    public static string? FindCodex() => FindOnPath("codex") ?? FindInVsCodeExtension();

    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in Extensions)
            {
                try
                {
                    var candidate = Path.Combine(dir, name + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // 함정: PATH 에 깨진 항목이 섞여 있으면 Path.Combine 이 던진다 — 그 항목만 건너뛴다
                }
            }
        }

        return null;
    }

    private static string? FindInVsCodeExtension()
    {
        var extensions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
        if (!Directory.Exists(extensions))
        {
            return null;
        }

        return Directory.GetDirectories(extensions, "openai.chatgpt-*")
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .Select(dir => Path.Combine(dir, "bin", "windows-x86_64", "codex.exe"))
            .FirstOrDefault(File.Exists);
    }
}
