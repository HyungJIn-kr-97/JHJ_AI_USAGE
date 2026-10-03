using System.IO;
using System.Text;
using System.Text.Json;

namespace costats.App.Services;

/// <summary>
/// Claude Code · Codex CLI 가 로컬에 남긴 로그인 정보에서 계정 표시 문자열만 읽는다.
/// 계약: 토큰 값은 읽어서 버리고 이메일·조직명만 반환한다. 읽지 못하면 "Not signed in" 계열 문구를 돌려준다.
/// </summary>
public static class AccountIdentityReader
{
    private const string NotSignedIn = "Not signed in";

    /// <param name="configDir">CLAUDE_CONFIG_DIR 로 쓰는 폴더. null 이면 기본 계정(~/.claude.json).</param>
    public static string ReadClaude(string? configDir = null)
    {
        try
        {
            var path = ClaudeAccountFile(configDir);
            if (!File.Exists(path))
            {
                return NotSignedIn;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("oauthAccount", out var account) ||
                account.ValueKind != JsonValueKind.Object)
            {
                return NotSignedIn;
            }

            var email = GetString(account, "emailAddress");
            var organization = GetString(account, "organizationName");
            if (string.IsNullOrWhiteSpace(email))
            {
                return NotSignedIn;
            }

            // 왜: 개인 계정의 조직명은 "<이메일>'s Organization" 이라 붙이면 같은 주소가 두 번 나온다
            var isPersonalOrg = organization?.StartsWith(email, StringComparison.OrdinalIgnoreCase) == true;
            return string.IsNullOrWhiteSpace(organization) || isPersonalOrg ? email : $"{email} · {organization}";
        }
        catch (Exception)
        {
            return "Unable to read account";
        }
    }

    /// <summary>
    /// 함정: 기본 계정의 .claude.json 은 ~/.claude 안이 아니라 홈 폴더 바로 아래에 있다.
    /// </summary>
    public static string ClaudeAccountFile(string? configDir = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeFile = Path.Combine(home, ".claude.json");
        if (configDir is null)
        {
            return homeFile;
        }

        var inDir = Path.Combine(configDir, ".claude.json");
        var isDefaultDir = Path.GetFullPath(configDir).TrimEnd('\\')
            .Equals(Path.Combine(home, ".claude"), StringComparison.OrdinalIgnoreCase);
        return isDefaultDir && !File.Exists(inDir) ? homeFile : inDir;
    }

    public static string CodexAuthFile()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        return Path.Combine(codexHome, "auth.json");
    }

    public static string ReadCodex()
    {
        try
        {
            var path = CodexAuthFile();
            if (!File.Exists(path))
            {
                return NotSignedIn;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
            {
                var email = ReadEmailFromJwt(GetString(tokens, "id_token"));
                if (!string.IsNullOrWhiteSpace(email))
                {
                    return email;
                }
            }

            return string.IsNullOrWhiteSpace(GetString(root, "OPENAI_API_KEY")) ? NotSignedIn : "API key";
        }
        catch (Exception)
        {
            return "Unable to read account";
        }
    }

    private static string? ReadEmailFromJwt(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        // 왜: JWT payload 는 패딩 없는 base64url 이라 표준 base64 로 되돌려야 디코딩된다
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return GetString(doc.RootElement, "email");
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
