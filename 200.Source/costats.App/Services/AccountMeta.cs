using System.IO;
using costats.App.Localization;
using costats.Application.Settings;

namespace costats.App.Services;

/// <summary>
/// 계정 명칭·유형(AppSettings.Accounts)을 읽고 쓰는 곳. 설정 화면과 팝업이 같은 규칙을 쓴다.
/// 계약: 키는 providerId 이고 도구(kind)는 ':' 앞이다 — "codex" 는 Codex 의 이 PC 로그인이다.
/// </summary>
public static class AccountMeta
{
    // 계약: 영어 원문이 저장 키가 아니라 표시 문구다 — 화면 언어로 바꿔 보이고, 사용자가 고르면 그 문구가 그대로 저장된다
    public static readonly string[] SuggestedTypes = ["Default", "Personal", "Work", "Team", "Test"];

    public static string KindOf(string id) => id.Split(':')[0].ToLowerInvariant();

    public static bool IsDefaultType(string? type) =>
        type?.Trim() is { Length: > 0 } t &&
        (t.Equals("Default", StringComparison.OrdinalIgnoreCase) || t == "기본" || t == Loc.T("Default"));

    public static string? NameOf(AppSettings settings, string id) =>
        Find(settings, id)?.Name?.Trim() is { Length: > 0 } name ? name : null;

    public static string TypeOf(AppSettings settings, string id) => Find(settings, id)?.Type?.Trim() ?? string.Empty;

    public static string DisplayNameOf(AppSettings settings, string id) => NameOf(settings, id) ?? AutoNameOf(id);

    // 계약: 명칭을 안 정했으면 로그인 메일의 @ 앞("atisys.ai") — 로그인 전이면 추가할 때 적은 이름, 이 PC 로그인은 "이 PC"
    public static string AutoNameOf(string id)
    {
        var folder = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : string.Empty;
        var isMain = folder.Length == 0 || folder.Equals(AccountProfileStore.DefaultName, StringComparison.OrdinalIgnoreCase);
        var isCodex = KindOf(id) == "codex";
        var account = isCodex
            ? AccountIdentityReader.ReadCodex(isMain ? null : CodexAccountStore.DirOf(folder))
            : AccountIdentityReader.ReadClaude(isMain ? null : Path.Combine(AccountProfileStore.RootDir, folder));
        var mail = account.Split(" · ")[0];
        if (mail.IndexOf('@') is > 0 and var at)
        {
            return mail[..at];
        }

        if (isMain)
        {
            return Loc.T("This PC");
        }

        return isCodex ? CodexAccountStore.LabelOf(folder) : AccountProfileStore.LabelOf(folder);
    }

    public static string? DefaultIdOf(AppSettings settings, string kind) =>
        settings.Accounts.FirstOrDefault(pair => KindOf(pair.Key) == kind && IsDefaultType(pair.Value.Type)).Key;

    /// <returns>유형이 「기본」이 되면서 기본을 빼앗긴 같은 도구의 다른 계정 id</returns>
    public static IReadOnlyList<string> Set(AppSettings settings, string id, string? name, string? type)
    {
        var cleared = new List<string>();
        if (IsDefaultType(type))
        {
            foreach (var (otherId, info) in settings.Accounts)
            {
                if (!otherId.Equals(id, StringComparison.OrdinalIgnoreCase) && KindOf(otherId) == KindOf(id) && IsDefaultType(info.Type))
                {
                    info.Type = null;
                    cleared.Add(otherId);
                }
            }
        }

        var entry = Find(settings, id) ?? (settings.Accounts[id] = new AccountInfo());
        // 왜: 칸에 보이던 자동 명칭을 그대로 두면 저장하지 않는다 — 저장하면 로그인 메일이 바뀌어도 옛 이름이 남는다
        entry.Name = string.IsNullOrWhiteSpace(name) || name.Trim() == AutoNameOf(id) ? null : name.Trim();
        entry.Type = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        return cleared;
    }

    public static void Remove(AppSettings settings, string id)
    {
        foreach (var key in settings.Accounts.Keys.Where(k => k.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            settings.Accounts.Remove(key);
        }
    }

    // 함정: 설정 파일에서 읽은 사전은 대소문자를 가린다 — 키를 직접 꺼내지 않고 여기서만 찾는다
    private static AccountInfo? Find(AppSettings settings, string id) =>
        settings.Accounts.FirstOrDefault(pair => pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Value;
}
