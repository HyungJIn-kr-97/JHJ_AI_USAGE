namespace costats.Application.Settings;

public sealed class AppSettings
{
    public int RefreshMinutes { get; set; } = 5;
    public string Hotkey { get; set; } = "Ctrl+Alt+2";

    // 계약: 「팝업 단축키 2」부터 — 기본은 비어 있어 단축키가 하나다
    public List<string> ExtraHotkeys { get; set; } = [];
    public bool StartAtLogin { get; set; } = false;

    // 왜: 백그라운드가 주기적으로 갱신하므로 팝업을 열 때마다 다시 조회하는 것은 기본으로 끈다
    public bool RefreshOnOpen { get; set; } = false;

    // 계약: 켜면 트레이 아이콘을 숨겨진 아이콘(^) 밖 작업 표시줄에 늘 둔다 — 한 번 클릭으로 팝업을 열 수 있게
    public bool PinTrayIcon { get; set; } = true;

    // 계약: 팝업에서 접어 둔 구역의 이름("chart" · "models" · "tokenTypes") — 없으면 펼친 것이다
    public List<string> CollapsedSections { get; set; } = [];

    /// <summary>
    /// "system" | "light" | "dark". system 은 시작할 때의 Windows 앱 모드를 따른다.
    /// </summary>
    public string Theme { get; set; } = "system";

    /// <summary>
    /// "bull" | "navy" | "emerald" | "violet" | "slate". 색상 팔레트.
    /// </summary>
    public string Palette { get; set; } = "bull";

    // 계약: 트레이 아이콘 모양 — "ai"(기본) · "j"(화면 이름 JHJ) · "bars" · "ring" · "spark"(팔레트 색으로 그림) · "custom:<파일 이름>"(사용자 아이콘)
    public string TrayIconStyle { get; set; } = "ai";

    /// <summary>
    /// "ko" | "en". 화면 문구의 언어.
    /// </summary>
    public string Language { get; set; } = "ko";

    /// <summary>
    /// Whether multicc integration is enabled. Default true when multicc is detected.
    /// </summary>
    public bool MulticcEnabled { get; set; } = true;

    /// <summary>
    /// When set, only show this single profile instead of all profiles stacked.
    /// Null means "show all profiles" (stacked mode).
    /// </summary>
    public string? MulticcSelectedProfile { get; set; }

    /// <summary>
    /// Override path for multicc config directory. Null means auto-detect (~/.multicc or $MULTICC_DIR).
    /// </summary>
    public string? MulticcConfigPath { get; set; }

    /// <summary>
    /// Whether the GitHub Copilot personal usage provider is enabled.
    /// </summary>
    public bool CopilotEnabled { get; set; } = false;

    // 계약: 켜야 Gemini 탭이 보이고 ~/.gemini 를 읽는다 — Gemini CLI 를 쓰지 않는 PC 에서 빈 탭이 뜨지 않게 기본은 꺼 둔다
    public bool GeminiEnabled { get; set; } = false;

    // 계약: 키는 providerId("claude:default" · "claude:<이름>" · "codex" · "codex:<이름>") — 사용자가 정한 계정 명칭·유형
    public Dictionary<string, AccountInfo> Accounts { get; set; } = [];

    // 계약: Claude 기본 폴더 기록의 주인 — 키는 프로그램(entrypoint: "claude-desktop" · "claude-vscode" · "cli"), 값은 providerId. 없으면 기본 계정(claude:default)이다
    public Dictionary<string, string> ProgramAccounts { get; set; } = [];

    // 계약: 설정 「계정」에서 정한 계정 순서(providerId) — 팝업 칩·카드도 이 순서다. 목록에 없는 계정은 뒤에 이름순으로 붙는다
    public List<string> AccountOrder { get; set; } = [];

    // 계약: 「활용도」 등급 경계 4개(오름차순, 배수) — 토큰 환산 비용 ÷ 구독료가 1번째 미만이면 1등급, 4번째 이상이면 5등급
    public List<decimal> ValueGradeBounds { get; set; } = [1m, 3m, 10m, 20m];

    // 계약: 로컬 이벤트 기록(갱신·오류·연동)을 남길지 — DiagnosticsLog. 어디로도 보내지 않는다
    public bool DiagnosticsEnabled { get; set; } = true;
}

public sealed class AccountInfo
{
    public string? Name { get; set; }

    // 계약: 자유 입력이다 — "기본"·"Default" 인 계정이 팝업을 열 때 먼저 보인다(도구마다 하나)
    public string? Type { get; set; }

    // 계약: 명칭·유형을 정할 때 로그인돼 있던 메일 — 같은 칸에 다른 계정으로 다시 로그인하면 이 값이 달라 명칭·유형을 쓰지 않는다
    public string? Email { get; set; }

    // 계약: 사용자가 적은 월 구독료(USD) — null 이면 플랜 등급에서 자동으로 정한다(SubscriptionPlans)
    public decimal? MonthlyFeeUsd { get; set; }
}
