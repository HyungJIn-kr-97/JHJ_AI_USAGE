using System.IO;

namespace costats.App.Services;

/// <summary>
/// 사용량 조회용 추가 Codex 계정. 계정마다 폴더 하나를 CODEX_HOME 으로 쓴다.
/// 계약: 기본 계정(~/.codex)은 여기에 없다 — 여기 있는 것은 앱이 만든 폴더뿐이다.
/// 함정: 로그인 토큰(auth.json)이 그 폴더에 남는다 — 앱은 로그인 여부와 사용량 조회에만 쓴다.
/// </summary>
public static class CodexAccountStore
{
    private const string LabelFile = ".label";

    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI-Usage-Monitor_JHJ", "accounts-codex");

    public static string DirOf(string name) => Path.Combine(RootDir, name);

    public static bool Exists(string name) => AccountProfileStore.IsValidName(name) && Directory.Exists(DirOf(name));

    public static IReadOnlyList<string> List() => Directory.Exists(RootDir)
        ? Directory.GetDirectories(RootDir)
            .Select(dir => Path.GetFileName(dir)!)
            .Where(AccountProfileStore.IsValidName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()
        : [];

    public static string LabelOf(string name)
    {
        var path = Path.Combine(DirOf(name), LabelFile);
        return File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } label ? label : name;
    }

    /// <returns>새 계정의 CODEX_HOME 폴더 경로</returns>
    public static string Add(string name, string label)
    {
        var dir = DirOf(name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, LabelFile), label);
        return dir;
    }

    public static void Remove(string name)
    {
        // 함정: 이름이 규칙을 벗어나면 RootDir 밖을 가리킬 수 있다 — 검증된 이름의 폴더만 지운다
        if (Exists(name))
        {
            Directory.Delete(DirOf(name), recursive: true);
        }
    }
}
