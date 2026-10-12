using System.IO;
using costats.App.Services;
using costats.Infrastructure.Providers;

namespace costats.App.ViewModels;

// 계정 줄의 연동 유형 표지(CLI · APP · WEB)를 채운다 — 규칙은 JHJ\AGENTS.md §4 「계정 정보는 세 출처」.
// 계약: 찾은 출처만 표지로 낸다 — 못 찾은 출처를 흐린 표지로 그리지 않는다.
// 계약: Info 에는 토큰 값을 싣지 않고, 계정 UUID 는 앞 8자만 싣는다(사용자가 그대로 복사해 붙인다).
// 계약: WEB 은 공식 로그인 창(브라우저)으로 연동한 자리다 — 브라우저의 쿠키·프로필은 읽지 않는다.
// TODO: Codex 전용 앱 출처는 아직 읽지 않는다.
public sealed partial class SettingsViewModel
{
    /// <param name="account">AccountIdentityReader 가 돌려준 원문 — 번역·덧붙임 전의 값</param>
    private static IReadOnlyList<AccountSourceBadge> SourcesOf(string id, string? dir, string account)
    {
        var badges = new List<AccountSourceBadge>();
        var isClaude = AccountMeta.KindOf(id) == "claude";
        if (IsSignedIn(account))
        {
            // 계약: 추가 자리는 이 앱의 「브라우저 연동」(공식 로그인 창)으로만 생긴다 — 그래서 WEB 이고, 이 PC 로그인은 CLI 다
            var viaBrowser = dir is not null;
            var lines = new List<string>
            {
                Loc.T(viaBrowser ? "Browser sign-in · {0}" : "CLI login · {0}", isClaude ? "Claude Code" : "Codex CLI"),
                Loc.T("Account: {0}", account),
                Loc.T("File: {0}", isClaude ? AccountIdentityReader.ClaudeAccountFile(dir) : AccountIdentityReader.CodexAuthFile(dir))
            };
            if (isClaude)
            {
                lines.Add(Loc.T(HasClaudeToken(dir) ? "Token on this PC: found" : "Token on this PC: missing"));
            }

            badges.Add(new AccountSourceBadge(viaBrowser ? "WEB" : "CLI", string.Join(Environment.NewLine, lines)));
        }

        if (isClaude && DesktopLoginOf(dir) is var (uuid, login) && login is not null)
        {
            var lines = new List<string>
            {
                Loc.T("Desktop app login · {0}", "Claude Desktop"),
                Loc.T("Account ID: {0}…", uuid[..Math.Min(8, uuid.Length)]),
                Loc.T(login.IsCurrent ? "Signed in to the app now" : "Signed in to the app before")
            };
            if (login.Latest is { } sample)
            {
                lines.Add(Loc.T("Last usage record: 5h {0}% · 7d {1}% ({2})",
                    sample.FiveHour?.ToString("0") ?? "–", sample.SevenDay?.ToString("0") ?? "–", sample.At.ToLocalTime().ToString("g")));
            }

            badges.Add(new AccountSourceBadge("APP", string.Join(Environment.NewLine, lines)));
        }

        return badges;
    }

    private static (string Uuid, ClaudeDesktopUsage.AppLogin? Login) DesktopLoginOf(string? dir)
    {
        var identity = dir is null
            ? ClaudeHomeIdentity.Read()
            : ClaudeHomeIdentity.ReadFile(AccountIdentityReader.ClaudeAccountFile(dir));
        return identity?.Uuid is { Length: > 0 } uuid
            ? (uuid, ClaudeDesktopUsage.LoginOf(uuid, identity.OrganizationUuid))
            : (string.Empty, null);
    }
}
