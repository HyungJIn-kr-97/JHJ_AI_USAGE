namespace costats.Application.Settings;

public sealed class AppSettings
{
    public int RefreshMinutes { get; set; } = 5;
    public string Hotkey { get; set; } = "Ctrl+Alt+Y";
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
}

public sealed class AccountInfo
{
    public string? Name { get; set; }

    // 계약: 자유 입력이다 — "기본"·"Default" 인 계정이 팝업을 열 때 먼저 보인다(도구마다 하나)
    public string? Type { get; set; }
}
