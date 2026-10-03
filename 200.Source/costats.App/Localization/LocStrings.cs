using System.Text.RegularExpressions;

namespace costats.App.Localization;

/// <summary>
/// 한국어 문안. 키는 화면에 쓰이던 영어 원문 그대로다(Footer* 만 이름 키).
/// 계약: 새 문구를 화면에 더하면 여기에 한 줄을 더한다 — 빠뜨리면 한국어 모드에서도 영어로 나올 뿐 깨지지는 않는다.
/// </summary>
internal static class LocStrings
{
    // 이름 키(영어 원문이 아닌 것)의 영어 문안
    public static readonly Dictionary<string, string> EnglishOverrides = new()
    {
        ["FooterCodex"] = " - OpenAI's AI coding assistant",
        ["FooterClaude"] = " - Anthropic's Claude Code CLI",
        ["FooterCopilot"] = " - GitHub's AI coding assistant",
    };

    public static readonly Dictionary<string, string> Korean = new()
    {
        // 팝업
        ["Unified AI Usage"] = "AI 통합 사용량",
        ["FooterCodex"] = " - OpenAI 의 AI 코딩 도구",
        ["FooterClaude"] = " - Anthropic 의 Claude Code CLI",
        ["FooterCopilot"] = " - GitHub 의 AI 코딩 도구",
        ["Session"] = "세션",
        ["Weekly"] = "주간",
        ["Premium requests"] = "프리미엄 요청",
        ["Chat requests"] = "채팅 요청",
        ["Cost"] = "비용",
        ["Today:"] = "오늘:",
        ["Today"] = "오늘",
        ["Last 30 days:"] = "최근 30일:",
        ["This week:"] = "이번 주:",
        [" tokens"] = " 토큰",
        ["All Profiles"] = "전체 계정",
        ["Chart range"] = "차트 기간",
        ["Settings"] = "설정",
        ["Switch light / dark"] = "라이트 / 다크 전환",
        ["Refresh now"] = "지금 새로고침",
        ["View Claude usage"] = "Claude 사용량 보기",
        ["View Codex usage on ChatGPT"] = "ChatGPT 에서 Codex 사용량 보기",
        ["View Copilot usage on GitHub"] = "GitHub 에서 Copilot 사용량 보기",
        ["+ Add account…"] = "+ 계정 추가…",
        ["This PC"] = "이 PC",
        ["All"] = "전체",
        ["Every account, stacked"] = "모든 계정을 한 화면에",
        ["All · {0} accounts"] = "전체 · 계정 {0}개",
        ["Weekly · {0}"] = "주간 · {0}",
        ["Theme"] = "테마",
        ["Appearance"] = "화면",
        ["General"] = "일반",
        ["About"] = "정보",
        ["Daily cost"] = "일별 비용",
        ["Weekly cost"] = "주별 비용",
        ["Monthly cost"] = "월별 비용",
        ["total {0} · peak {1}"] = "합계 {0} · 최고 {1}",
        ["Models · {0} days"] = "모델 · {0}일",
        ["Models · 1 year"] = "모델 · 1년",
        ["Others ({0})"] = "그 외 ({0})",
        ["{0} tokens"] = "{0} 토큰",
        ["unknown"] = "알 수 없음",
        ["Updated {0}"] = "{0} 갱신",
        ["Updated never"] = "갱신 전",
        ["Never"] = "없음",
        ["1y"] = "1년",
        ["{0}d"] = "{0}일",

        // 상태
        ["No data"] = "데이터 없음",
        ["No Claude usage data available"] = "Claude 사용량 데이터 없음",
        ["No Codex usage data available"] = "Codex 사용량 데이터 없음",
        ["No Copilot usage data available"] = "Copilot 사용량 데이터 없음",
        ["Copilot disabled in Settings"] = "설정에서 Copilot 이 꺼져 있음",
        ["Copilot usage unavailable"] = "Copilot 사용량을 가져올 수 없음",
        ["Not signed in"] = "로그인되지 않음",
        ["Unable to read account"] = "계정을 읽을 수 없음",
        ["API key"] = "API 키",

        // 설정 창
        ["Accounts"] = "계정",
        ["Extra Claude accounts (usage only)"] = "추가 Claude 계정 (사용량 조회 전용)",
        ["Remove"] = "삭제",
        ["Sign in"] = "로그인",
        ["Add account"] = "계정 추가",
        ["Account name (letters, digits, - or _)"] = "계정 이름 (영문, 숫자, - 또는 _)",
        ["Restart app to apply"] = "앱을 다시 시작해 적용",
        ["SYSTEM"] = "시스템",
        ["Start at login"] = "로그인 시 자동 실행",
        ["Automatically launch costats when Windows starts."] = "Windows 를 시작할 때 이 앱을 자동으로 실행합니다.",
        ["Language"] = "언어",
        ["AUTOMATION"] = "자동화",
        ["Refresh interval"] = "새로고침 주기",
        ["How often costats polls for usage data in the background."] = "백그라운드에서 사용량을 가져오는 주기입니다.",
        ["CLAUDE CODE"] = "CLAUDE CODE",
        ["Use multicc profiles"] = "여러 계정 함께 보기",
        ["Automatically detect and display all multicc profiles in the Claude tab."] = "등록된 계정을 Claude 탭에 모두 표시합니다.",
        ["Profiles found:"] = "등록된 계정:",
        ["multicc not detected. Install multicc to manage multiple Claude Code accounts."] = "등록된 추가 계정이 없습니다. 위 「계정」에서 추가할 수 있습니다.",
        ["COPILOT"] = "COPILOT",
        ["Enable GitHub Copilot usage"] = "GitHub Copilot 사용량 표시",
        ["Connect a GitHub personal access token to fetch usage data."] = "GitHub 개인 액세스 토큰을 연결해 사용량을 가져옵니다.",
        ["Personal access token"] = "개인 액세스 토큰",
        ["Save token"] = "토큰 저장",
        ["Clear token"] = "토큰 지우기",
        ["UPDATES"] = "업데이트",
        ["Check for updates"] = "업데이트 확인",
        ["Version"] = "버전",
        ["Changes are saved automatically."] = "변경 사항은 자동으로 저장됩니다.",
        ["Made by"] = "제작",
        ["Save"] = "저장",
        ["Clear"] = "지우기",
        ["Based on costats by fmdz (MIT License)"] = "fmdz 의 costats 기반 (MIT 라이선스)",
        ["Claude Code login on this PC · {0}"] = "이 PC 의 Claude Code 로그인 · {0}",
        ["Codex CLI login on this PC · {0}"] = "이 PC 의 Codex CLI 로그인 · {0}",
        ["Name: 1-24 letters, digits, - or _ (\"default\" is reserved)."] = "이름은 영문·숫자·-·_ 1~24자입니다 (\"default\" 는 쓸 수 없습니다).",
        ["\"{0}\" already exists."] = "\"{0}\" 은(는) 이미 있습니다.",
        ["Added \"{0}\". Sign in in the terminal that opened, then restart."] = "\"{0}\" 을(를) 추가했습니다. 열린 터미널에서 로그인한 뒤 앱을 다시 시작해 주십시오.",
        ["Sign in to \"{0}\" in the terminal that opened."] = "열린 터미널에서 \"{0}\" 계정으로 로그인해 주십시오.",
        ["Removed \"{0}\". Restart to apply."] = "\"{0}\" 을(를) 삭제했습니다. 앱을 다시 시작하면 적용됩니다.",
        ["Could not remove \"{0}\": {1}"] = "\"{0}\" 을(를) 삭제하지 못했습니다: {1}",
        ["Restart required to apply changes."] = "앱을 다시 시작해야 적용됩니다.",
        ["Copilot token is required."] = "Copilot 토큰을 입력해 주십시오.",
        ["Copilot token saved."] = "Copilot 토큰을 저장했습니다.",
        ["Could not save Copilot token."] = "Copilot 토큰을 저장하지 못했습니다.",
        ["Copilot token cleared."] = "Copilot 토큰을 지웠습니다.",
        ["Could not clear Copilot token."] = "Copilot 토큰을 지우지 못했습니다.",
        ["Copilot token not set."] = "Copilot 토큰이 설정되지 않았습니다.",
        ["Could not load Copilot token."] = "Copilot 토큰을 불러오지 못했습니다.",
        ["Copilot token not configured."] = "Copilot 토큰이 설정되지 않았습니다.",
        ["Copilot token rejected."] = "Copilot 토큰이 거부되었습니다.",
        ["Copilot access denied for this token."] = "이 토큰은 Copilot 접근 권한이 없습니다.",
        ["Copilot usage endpoint unavailable or token lacks Copilot access."] = "Copilot 사용량을 조회할 수 없거나 토큰에 권한이 없습니다.",
        ["Copilot usage rate limited."] = "Copilot 사용량 조회가 일시적으로 제한되었습니다.",
        ["Copilot usage request failed."] = "Copilot 사용량 요청에 실패했습니다.",
        ["Copilot usage response could not be parsed."] = "Copilot 사용량 응답을 해석하지 못했습니다.",
        ["Updates are not available."] = "업데이트를 사용할 수 없습니다.",
        ["Checking for updates..."] = "업데이트 확인 중...",
        ["Update found. Restarting..."] = "업데이트를 찾았습니다. 다시 시작합니다...",
        ["Update staged. Restart to apply."] = "업데이트가 준비되었습니다. 다시 시작하면 적용됩니다.",
        ["You're up to date."] = "최신 버전입니다.",
        ["Update check already in progress."] = "이미 업데이트를 확인하는 중입니다.",
        ["Could not check for updates."] = "업데이트를 확인하지 못했습니다.",
        ["Update check timed out. Try again."] = "업데이트 확인 시간이 초과되었습니다. 다시 시도해 주십시오.",

        // 트레이 메뉴
        ["Show Widget"] = "열기",
        ["Refresh Now"] = "지금 새로고침",
        ["Settings..."] = "설정...",
        ["Exit"] = "종료",
    };

    /// <summary>
    /// 코어·인프라 층이 만든 가변 문장 — 원문 형식은 costats.Core/Pulse/UsageFormatter.cs 가 정한다.
    /// </summary>
    public static readonly (Regex Pattern, Func<Match, string> Replace)[] KoreanPatterns =
    [
        (new Regex(@"^Resets now$"), _ => "곧 초기화"),
        (new Regex(@"^Resets in (.+)$"), m => $"{Loc.Duration(m.Groups[1].Value)} 후 초기화"),
        (new Regex(@"^Pace: (.+?)(?: · (.+))?$"), m => m.Groups[2].Success
            ? $"페이스: {Pace(m.Groups[1].Value)} · {Pace(m.Groups[2].Value)}"
            : $"페이스: {Pace(m.Groups[1].Value)}"),
        (new Regex(@"^(\d+)% used$"), m => $"{m.Groups[1].Value}% 사용"),
        (new Regex(@"^Updated just now$"), _ => "방금 갱신"),
        (new Regex(@"^Updated less than a minute ago$"), _ => "1분 이내 갱신"),
        (new Regex(@"^Updated (\d+[mhd]) ago$"), m => $"{Loc.Duration(m.Groups[1].Value)} 전 갱신"),
        (new Regex(@"^No data for (.+)$"), m => $"{m.Groups[1].Value} 데이터 없음"),
        (new Regex(@"^Overage: (.+) / (.+)$"), m => $"초과 사용: {m.Groups[1].Value} / {m.Groups[2].Value}"),
        (new Regex(@"^Balance: (.+) remaining$"), m => $"잔액: {m.Groups[1].Value}"),
        (new Regex(@"^(.+) today  ·  (.+) / 30d$"), m => $"오늘 {m.Groups[1].Value}  ·  30일 {m.Groups[2].Value}"),
        (new Regex(@"^(\d+) profiles  ·  (\d+) at limit, (\d+) warning$"), m => $"계정 {m.Groups[1].Value}개  ·  한도 도달 {m.Groups[2].Value}, 주의 {m.Groups[3].Value}"),
        (new Regex(@"^(\d+) profiles  ·  (\d+) near limit$"), m => $"계정 {m.Groups[1].Value}개  ·  한도 근접 {m.Groups[2].Value}"),
        (new Regex(@"^(\d+) profiles  ·  All healthy$"), m => $"계정 {m.Groups[1].Value}개  ·  모두 정상"),
    ];

    private static string Pace(string part)
    {
        var match = Regex.Match(part, @"^(\d+)% in (deficit|reserve)$");
        if (match.Success)
        {
            return match.Groups[2].Value == "deficit" ? $"{match.Groups[1].Value}% 초과" : $"{match.Groups[1].Value}% 여유";
        }

        match = Regex.Match(part, @"^Runs out in (.+)$");
        if (match.Success)
        {
            return $"{Loc.Duration(match.Groups[1].Value)} 후 소진";
        }

        return part switch
        {
            "On pace" => "적정",
            "Unknown" => "알 수 없음",
            "Lasts until reset" => "초기화까지 충분",
            "Runs out now" => "곧 소진",
            _ => part
        };
    }
}
