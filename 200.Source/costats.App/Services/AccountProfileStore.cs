using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace costats.App.Services;

/// <summary>
/// 사용량 조회용 Claude 계정 목록. multicc 와 같은 형식의 config.json 을 이 앱 전용 폴더에 둔다.
/// 계약: 각 계정은 자기 CLAUDE_CONFIG_DIR 폴더를 갖고, 로그인은 사용자가 그 폴더로 띄운 claude 에서 직접 한다.
/// 함정: 목록은 앱 시작 때 한 번 읽히므로 추가·삭제는 재시작해야 반영된다.
/// </summary>
public static class AccountProfileStore
{
    public const string DefaultName = "default";

    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor", "accounts");

    private static string ConfigPath => Path.Combine(RootDir, "config.json");

    private static string DefaultClaudeDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,24}$") &&
        !name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 사용자가 적은 이메일·별명을 폴더 이름으로 접는다 — "jhj@atisys.co.kr" → "jhj-atisys-co-kr". 못 접으면 빈 문자열.
    /// </summary>
    public static string ToFolderName(string? label)
    {
        var folded = Regex.Replace((label ?? string.Empty).Trim().ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-');
        if (folded.Length > 24)
        {
            folded = folded[..24].TrimEnd('-');
        }

        return IsValidName(folded) ? folded : string.Empty;
    }

    public static string LabelOf(string name) =>
        LoadProfiles()[name]?["label"]?.GetValue<string>() is { Length: > 0 } label ? label : name;

    /// <returns>새 계정의 설정 폴더 경로</returns>
    public static string Add(string name, string label)
    {
        var profiles = LoadProfiles();

        // 왜: 계정 목록이 생기면 기본 Claude 소스가 빠지므로, 지금 로그인된 계정을 default 로 함께 넣어야 사라지지 않는다
        if (profiles[DefaultName] is null && Directory.Exists(DefaultClaudeDir))
        {
            profiles[DefaultName] = NewProfile(DefaultClaudeDir, "This PC's Claude Code login");
        }

        var dir = Path.Combine(RootDir, name);
        Directory.CreateDirectory(dir);
        var profile = NewProfile(dir, "Usage-only account");
        profile["label"] = label;
        profiles[name] = profile;
        Save(profiles);
        return dir;
    }

    public static void Remove(string name)
    {
        var profiles = LoadProfiles();
        profiles.Remove(name);

        // 왜: default 만 남으면 계정 목록이 필요 없다 — 파일을 지워 단일 계정 표시로 돌아간다
        if (profiles.Count == 0 || (profiles.Count == 1 && profiles[DefaultName] is not null))
        {
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }
        }
        else
        {
            Save(profiles);
        }

        // 함정: 로그인 토큰이 든 폴더라 남기면 안 된다 — 단, 이 앱이 만든 폴더(RootDir 아래)만 지운다
        var dir = Path.GetFullPath(Path.Combine(RootDir, name));
        if (dir.StartsWith(Path.GetFullPath(RootDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static bool Exists(string name) => LoadProfiles()[name] is not null;

    private static JsonObject LoadProfiles()
    {
        try
        {
            if (File.Exists(ConfigPath) &&
                JsonNode.Parse(File.ReadAllText(ConfigPath)) is JsonObject root &&
                root["profiles"] is JsonObject profiles)
            {
                root.Remove("profiles");
                return profiles;
            }
        }
        catch (JsonException)
        {
            // 깨진 파일은 빈 목록으로 본다 — 다음 저장이 덮어쓴다
        }

        return new JsonObject();
    }

    private static void Save(JsonObject profiles)
    {
        Directory.CreateDirectory(RootDir);
        var root = new JsonObject { ["version"] = 1, ["profiles"] = profiles };
        File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonObject NewProfile(string configDir, string description) => new()
    {
        ["authType"] = "oauth",
        ["configDir"] = configDir,
        ["description"] = description
    };
}
