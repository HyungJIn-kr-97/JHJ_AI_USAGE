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
        Current(settings, id)?.Name?.Trim() is { Length: > 0 } name ? name : null;

    public static string TypeOf(AppSettings settings, string id) => Current(settings, id)?.Type?.Trim() ?? string.Empty;

    public static decimal? FeeOf(AppSettings settings, string id) => Current(settings, id)?.MonthlyFeeUsd;

    public static string DisplayNameOf(AppSettings settings, string id) => NameOf(settings, id) ?? AutoNameOf(id);

    private static bool IsMainFolder(string folder) =>
        folder.Length == 0 || folder.Equals(AccountProfileStore.DefaultName, StringComparison.OrdinalIgnoreCase);

    private static string FolderOf(string id) => id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : string.Empty;

    /// <summary>이 id 에 지금 로그인된 메일. 로그인 전이면 null.</summary>
    public static string? MailOf(string id)
    {
        var folder = FolderOf(id);
        var isMain = IsMainFolder(folder);
        var account = KindOf(id) == "codex"
            ? AccountIdentityReader.ReadCodex(isMain ? null : CodexAccountStore.DirOf(folder))
            : AccountIdentityReader.ReadClaude(isMain ? null : Path.Combine(AccountProfileStore.RootDir, folder));
        var mail = account.Split(" · ")[0];
        return mail.IndexOf('@') > 0 ? mail : null;
    }

    /// <summary>이 id 에 지금 로그인된 Claude 계정 UUID. Claude 가 아니거나 로그인 전이면 null.</summary>
    // 계약: 기본 자리는 ClaudeHomeIdentity.Read() 를 거친다 — 다른 세션이 덮어쓴 이름을 거른 신원이어야 메일과 짝이 맞는다
    public static string? UuidOf(string id)
    {
        if (KindOf(id) != "claude")
        {
            return null;
        }

        var folder = FolderOf(id);
        var path = AccountIdentityReader.ClaudeAccountFile(
            IsMainFolder(folder) ? null : Path.Combine(AccountProfileStore.RootDir, folder));
        return string.Equals(path, costats.Infrastructure.Providers.ClaudeHomeIdentity.HomeFile, StringComparison.OrdinalIgnoreCase)
            ? costats.Infrastructure.Providers.ClaudeHomeIdentity.Read()?.Uuid
            : costats.Infrastructure.Providers.ClaudeHomeIdentity.ReadFile(path)?.Uuid;
    }

    // 계약: 명칭을 안 정했으면 로그인 메일의 @ 앞("atisys.ai") — 로그인 전이면 추가할 때 적은 이름, 이 PC 로그인은 "이 PC"
    public static string AutoNameOf(string id)
    {
        if (MailOf(id) is { } mail)
        {
            return mail[..mail.IndexOf('@')];
        }

        var folder = FolderOf(id);
        if (IsMainFolder(folder))
        {
            return Loc.T("This PC");
        }

        return KindOf(id) == "codex" ? CodexAccountStore.LabelOf(folder) : AccountProfileStore.LabelOf(folder);
    }

    public static string? DefaultIdOf(AppSettings settings, string kind) =>
        settings.Accounts.Keys.FirstOrDefault(id => KindOf(id) == kind && IsDefaultType(Current(settings, id)?.Type));

    // 왜: 「이 PC 로그인」 칸에 다른 계정으로 다시 로그인하면 옛 계정의 명칭·유형이 새 계정에 붙어 보였다
    // 함정: Email 이 없던 옛 항목은 명칭이 메일 모양일 때만 그 메일을 주인으로 본다
    private static AccountInfo? Current(AppSettings settings, string id)
    {
        var entry = Find(settings, id);
        if (entry is null)
        {
            return null;
        }

        var owner = entry.Email ?? (entry.Name?.Trim() is { } n && n.Contains('@') ? n : null);
        var mail = MailOf(id);
        return owner is null || mail is null || owner.Equals(mail, StringComparison.OrdinalIgnoreCase) ? entry : null;
    }

    /// <returns>유형이 「기본」이 되면서 기본을 빼앗긴 같은 도구의 다른 계정 id</returns>
    public static IReadOnlyList<string> Set(AppSettings settings, string id, string? name, string? type, decimal? monthlyFee = null)
    {
        var cleared = new List<string>();
        if (IsDefaultType(type))
        {
            foreach (var (otherId, info) in settings.Accounts)
            {
                if (!otherId.Equals(id, StringComparison.OrdinalIgnoreCase) && KindOf(otherId) == KindOf(id) && IsDefaultType(Current(settings, otherId)?.Type))
                {
                    info.Type = null;
                    cleared.Add(otherId);
                }
            }
        }

        var entry = Find(settings, id) ?? (settings.Accounts[id] = new AccountInfo());
        // 왜: 칸에 보이던 자동 명칭을 그대로 두면 저장하지 않는다 — 저장하면 로그인 메일이 바뀌어도 옛 이름이 남는다
        var trimmed = name?.Trim() ?? string.Empty;
        entry.Name = trimmed.Length == 0 || trimmed == AutoNameOf(id) ? null
            : trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
        entry.Type = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
        entry.MonthlyFeeUsd = monthlyFee is > 0 ? monthlyFee : null;
        entry.Email = MailOf(id);
        return cleared;
    }

    // 계약: 계정 명칭 최대 글자 수 — 팝업 계정 카드 머리에 요금제·비용과 함께 들어가는 길이
    public const int MaxNameLength = 8;

    /// <summary>정한 순서대로 정렬한다 — 순서에 없는 id 는 뒤에 표시 이름순.</summary>
    public static IEnumerable<T> Ordered<T>(AppSettings settings, IEnumerable<T> items, Func<T, string> idOf, Func<T, string> nameOf) =>
        items.OrderBy(item => OrderOf(settings, idOf(item)))
            .ThenBy(nameOf, StringComparer.OrdinalIgnoreCase);

    private static int OrderOf(AppSettings settings, string id)
    {
        var index = settings.AccountOrder.FindIndex(o => o.Equals(Canonical(id), StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    // 왜: 팝업은 기본 자리를 "claude" 로도 부른다 — 순서 목록에는 설정 화면의 id("claude:default")로 둔다
    private static string Canonical(string id) =>
        id.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "claude:" + AccountProfileStore.DefaultName : id;

    /// <summary>한 도구(kind)의 순서를 통째로 바꾼다 — 다른 도구의 순서는 그대로 둔다.</summary>
    public static void SetOrder(AppSettings settings, string kind, IEnumerable<string> ids)
    {
        var others = settings.AccountOrder.Where(id => KindOf(id) != kind);
        settings.AccountOrder = others.Concat(ids.Select(Canonical)).ToList();
    }

    public static void Remove(AppSettings settings, string id)
    {
        settings.AccountOrder.RemoveAll(o => o.Equals(id, StringComparison.OrdinalIgnoreCase));
        foreach (var key in settings.Accounts.Keys.Where(k => k.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            settings.Accounts.Remove(key);
        }
    }

    // 함정: 설정 파일에서 읽은 사전은 대소문자를 가린다 — 키를 직접 꺼내지 않고 여기서만 찾는다
    private static AccountInfo? Find(AppSettings settings, string id) =>
        settings.Accounts.FirstOrDefault(pair => pair.Key.Equals(id, StringComparison.OrdinalIgnoreCase)).Value;
}
