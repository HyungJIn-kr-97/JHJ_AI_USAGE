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
        ["FooterGemini"] = " - Google's Gemini CLI",
    };

    public static readonly Dictionary<string, string> Korean = new()
    {
        // 팝업
        ["AI Usage Monitor"] = "AI 통합 사용량 모니터",
        ["FooterCodex"] = " - OpenAI 의 AI 코딩 도구",
        ["FooterClaude"] = " - Anthropic 의 Claude Code CLI",
        ["FooterCopilot"] = " - GitHub 의 AI 코딩 도구",
        ["FooterGemini"] = " - Google 의 Gemini CLI",
        ["Pro models · daily"] = "Pro 모델 · 일일",
        ["Flash models · daily"] = "Flash 모델 · 일일",
        ["No Gemini usage data available"] = "Gemini 사용량 데이터 없음",
        ["Gemini disabled in Settings"] = "설정에서 Gemini 가 꺼져 있음",
        ["Show Gemini CLI usage"] = "Gemini CLI 사용량 표시",
        ["Reads ~/.gemini: quota from the signed-in Code Assist seat, tokens and cost from session logs."] = "~/.gemini 를 읽습니다 — 한도는 로그인된 Code Assist 좌석에서, 토큰·비용은 세션 로그에서 가져옵니다.",
        ["Quota needs a Code Assist Standard/Enterprise seat. Personal Google sign-in was closed on 2026-06-18, so only token and cost history shows for it. Cost is an API-price estimate."] = "한도는 Code Assist Standard/Enterprise 좌석이 있어야 보입니다. 개인 Google 로그인은 2026-06-18 에 막혀 토큰·비용 이력만 나옵니다. 비용은 API 단가 환산 추정입니다.",
        ["Gemini CLI login on this PC · {0}"] = "이 PC 의 Gemini CLI 로그인 · {0}",
        ["Session"] = "세션",
        ["Weekly"] = "주간",
        ["Premium requests"] = "프리미엄 요청",
        ["Chat requests"] = "채팅 요청",
        ["Cost"] = "비용",
        ["Today:"] = "오늘:",
        ["Today"] = "오늘",
        ["Last 30 days:"] = "최근 30일:",
        ["Daily avg:"] = "일 평균:",
        ["Close settings"] = "설정 닫기",
        ["Close"] = "닫기",
        ["Back"] = "돌아가기",
        ["Cancel"] = "취소",
        ["Apply code"] = "코드 적용",
        ["Paste the code the browser shows after signing in"] = "로그인 뒤 브라우저가 보여 주는 코드를 붙여넣습니다",
        ["Finish signing in to \"{0}\" in the browser. If it shows a code, paste it below."] = "브라우저에서 \"{0}\" 로그인을 마쳐 주십시오. 브라우저가 코드를 보여 주면 아래 칸에 붙여넣습니다.",
        ["Finish signing in to \"{0}\" in the browser."] = "브라우저에서 \"{0}\" 로그인을 마쳐 주십시오.",
        ["Code sent. Waiting for sign-in to finish..."] = "코드를 보냈습니다. 로그인이 끝나기를 기다리는 중입니다…",
        ["Another sign-in is still in progress. Finish or cancel it first."] = "다른 로그인이 아직 진행 중입니다. 먼저 마치거나 취소해 주십시오.",
        ["Next refresh {0} · in {1}"] = "다음 갱신 {0} · {1} 후",
        ["Weekly · per model"] = "주간 · 모델별",
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
        ["Display"] = "화면",
        ["General"] = "일반",
        ["About"] = "정보",
        ["Daily cost"] = "일별 비용",
        ["Weekly cost"] = "주별 비용",
        ["Monthly cost"] = "월별 비용",
        ["total {0} · peak {1}"] = "합계 {0} · 최고 {1}",
        ["total {0} · best day {1}"] = "합계 {0} · 일 최고 {1}",
        ["total {0} · best week {1}"] = "합계 {0} · 주 최고 {1}",
        ["total {0} · best month {1}"] = "합계 {0} · 월 최고 {1}",
        ["Token types · {0} days"] = "토큰 유형 · {0}일",
        ["Token types · 1 year"] = "토큰 유형 · 1년",
        ["Input"] = "입력",
        ["Output"] = "출력",
        ["Cache read"] = "캐시 읽기",
        ["Cache write"] = "캐시 쓰기",
        ["Unknown"] = "유형 미상",
        ["Fresh input sent to the model (not cached)"] = "모델에 새로 보낸 입력(캐시 아님)",
        ["Text the model generated"] = "모델이 생성한 출력",
        ["Input re-read from the prompt cache (cheapest)"] = "프롬프트 캐시에서 다시 읽은 입력(가장 저렴)",
        ["Input stored into the prompt cache"] = "프롬프트 캐시에 저장한 입력",
        ["Older history saved before token types were recorded"] = "토큰 유형을 기록하기 전에 쌓인 옛 이력",
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
        ["Email or a short name for the account"] = "계정 이메일 또는 짧은 이름",
        ["Enter the account's email or a short name."] = "계정 이메일이나 짧은 이름을 적어 주십시오.",
        ["✓ \"{0}\" signed in as {1}. Restart to show its usage."] = "✓ \"{0}\" 이(가) {1} 로 로그인되었습니다. 다시 시작하면 사용량이 보입니다.",
        ["✓ \"{0}\" signed in as {1}. Restarting to show its usage..."] = "✓ \"{0}\" 이(가) {1} 로 로그인되었습니다. 계정 목록에 넣으려고 앱을 다시 시작합니다...",
        ["Sign out"] = "로그아웃",
        ["Default"] = "기본",
        ["Personal"] = "개인",
        ["Work"] = "회사",
        ["Team"] = "팀",
        ["Test"] = "테스트",
        ["Name · Type"] = "명칭 · 유형",
        ["account stats"] = "계정의 통계",
        ["Current"] = "현재",
        ["GitHub releases"] = "GitHub 릴리스",
        ["GitHub latest {0}"] = "GitHub 최신 {0}",
        ["No release on GitHub yet"] = "GitHub 에 아직 릴리스가 없습니다",
        ["A newer version is available."] = "새 버전이 있습니다.",
        ["This build is newer than the latest release."] = "지금 버전이 공개된 최신 릴리스보다 높습니다(개발 빌드).",
        ["Could not read GitHub releases."] = "GitHub 릴리스를 읽지 못했습니다.",
        ["Install this version"] = "이 버전 설치",
        ["Installing a version works only in the installed app, not in a development build."] = "버전 설치는 설치된 앱에서만 됩니다 — 지금은 개발 빌드라 비교만 합니다.",
        ["Downloading {0}..."] = "{0} 을(를) 받는 중입니다...",
        ["Installing {0}. The app will restart."] = "{0} 을(를) 설치합니다. 앱이 다시 시작됩니다.",
        ["Could not install {0}."] = "{0} 설치에 실패했습니다.",
        ["App settings"] = "앱 설정",
        ["Accounts & AI tools"] = "계정 · AI 도구",
        ["Always show icon on taskbar"] = "작업 표시줄에 아이콘 항상 표시",
        ["Keep the icon on the taskbar instead of the hidden icons (^), so one click opens the popup."] = "숨겨진 아이콘(^) 안이 아니라 작업 표시줄에 아이콘을 늘 둡니다 — 한 번 클릭으로 팝업이 열립니다.",
        ["All accounts today:"] = "전체 계정 오늘:",
        ["All accounts this week:"] = "전체 계정 이번 주:",
        ["Account name shown in the popup"] = "팝업에 보일 계정 명칭",
        ["Account type — type freely or pick one. Default is shown first when the popup opens."] = "계정 유형 — 직접 적거나 골라 주십시오. 「기본」인 계정이 팝업을 열 때 먼저 보입니다.",
        ["Sign out of \"{0}\" on this PC? The {0} CLI in your terminal will be signed out too."] = "이 PC 의 \"{0}\" 로그인을 해제할까요? 터미널의 {0} CLI 도 함께 로그아웃됩니다.",
        ["Could not sign out of \"{0}\": {1}"] = "\"{0}\" 로그아웃에 실패했습니다: {1}",
        ["✓ Signed out of \"{0}\"."] = "✓ \"{0}\" 에서 로그아웃했습니다.",
        ["✓ \"{0}\" signed in as {1}. Refreshing usage now."] = "✓ \"{0}\" 이(가) {1} 로 로그인되었습니다. 사용량을 새로 읽는 중입니다.",
        ["Restart app to apply"] = "앱을 다시 시작해 적용",
        ["SYSTEM"] = "시스템",
        ["Start at login"] = "로그인 시 자동 실행",
        ["Refresh when opened"] = "열 때마다 새로고침",
        ["Fetch usage again every time the popup opens. Off: only the background interval refreshes."] = "팝업을 열 때마다 사용량을 다시 조회합니다. 끄면 새로고침 주기에 맞춰서만 갱신합니다.",
        ["Automatically launch AI Usage Monitor when Windows starts."] = "Windows 를 시작할 때 이 앱을 자동으로 실행합니다.",
        ["Language"] = "언어",
        ["AUTOMATION"] = "자동화",
        ["Refresh interval"] = "새로고침 주기",
        ["How often AI Usage Monitor polls for usage data in the background."] = "백그라운드에서 사용량을 가져오는 주기입니다.",
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
        ["\"{0}\" already exists."] = "\"{0}\" 은(는) 이미 있습니다.",
        ["Finish signing in to \"{0}\" in the browser. If no browser opens, use the minimized terminal on the taskbar."] = "브라우저에서 \"{0}\" 로그인을 마쳐 주십시오. 브라우저가 열리지 않으면 작업 표시줄에 최소화된 터미널을 여십시오.",
        ["Extra Claude account (usage only) — email or a short name"] = "추가 Claude 계정(사용량 조회 전용) — 이메일 또는 짧은 이름",
        ["Extra Codex account (usage only) — email or a short name"] = "추가 Codex 계정(사용량 조회 전용) — 이메일 또는 짧은 이름",
        ["Sign-in to \"{0}\" was not completed."] = "\"{0}\" 로그인이 완료되지 않았습니다.",
        ["Could not start sign-in: {0}"] = "로그인을 시작하지 못했습니다: {0}",
        ["{0} CLI was not found on this PC. Install it, then try again."] = "이 PC 에서 {0} CLI 를 찾지 못했습니다. 설치한 뒤 다시 시도해 주십시오.",
        ["Opens the official CLI sign-in in your browser. Signing in again switches the account."] = "공식 CLI 로그인을 브라우저로 엽니다. 다시 로그인하면 계정이 바뀝니다.",
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
