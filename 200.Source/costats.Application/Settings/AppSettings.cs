using Jhj.Core.Settings;

namespace costats.Application.Settings;

// 계약: 예약 업데이트가 읽는 세 칸(AutoUpdate*)은 JHJ_CS_CORE 의 IScheduledUpdateSettings 다
public sealed class AppSettings : IScheduledUpdateSettings
{
    public int RefreshMinutes { get; set; } = 5;
    /// 계약: 전역 단축키 자리표는 JHJ_DEV/000.AGENTS_MD/060.단축키/060.단축키-대장.md 다 — AI Usage 는 2번이다.
    // 왜: App 이 부팅 때 이 값을 HotkeyRules.Default 에도 넣는다 — 참조 방향이 반대라 Core 가 이 상수를 못 읽는다
    public const string DefaultHotkey = "Ctrl+Alt+2";

    public string Hotkey { get; set; } = DefaultHotkey;

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

    // 계약: 트레이 아이콘 모양 — "jhj-ai"(기본 · JHJ + AI 표지) · "ai"(두 글자) · "j"(화면 이름 JHJ) · "bars" · "ring" · "spark"(팔레트 색으로 그림) · "custom:<파일 이름>"(사용자 아이콘)
    // 계약: costats.App 의 TrayIconRenderer.DefaultStyle 과 같은 값이어야 한다 — 참조 방향이 반대라 상수를 공유하지 못한다
    public string TrayIconStyle { get; set; } = "jhj-ai";

    // 계약: 옛 기본값 "ai" 를 새 기본으로 옮겼다는 표시 — 켜진 뒤에 고른 "ai" 는 사용자의 선택이라 건드리지 않는다
    public bool TrayIconDefaultMoved { get; set; }

    /// <summary>옛 기본 모양("ai")이 저장된 설정을 새 기본으로 한 번만 옮긴다. 바뀐 것이 있으면 true — 부른 쪽이 저장한다.</summary>
    public bool MoveLegacyTrayIconDefault()
    {
        if (TrayIconDefaultMoved)
        {
            return false;
        }

        if (TrayIconStyle == "ai")
        {
            TrayIconStyle = new AppSettings().TrayIconStyle;
        }

        TrayIconDefaultMoved = true;
        return true;
    }

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

    // 계약: 연동 이력(ClaudeProgramLinks)이 시작되기 전 줄의 주인 — 키는 프로그램(entrypoint: "claude-desktop" · "claude-vscode" · "cli"), 값은 providerId
    // 함정: 화면은 더 이상 이 칸을 쓰지 않는다 — 옛 기록의 주인이 바뀌지 않게 그대로 둔다. 지금의 연동은 ProgramAccountPins 와 연동 이력이 정한다
    public Dictionary<string, string> ProgramAccounts { get; set; } = [];

    // 계약: 「프로그램 연동」에서 손으로 고정한 줄 — 키는 프로그램, 값은 providerId. 없는 프로그램은 자동(지금 이 PC 에 로그인한 계정)이다
    public Dictionary<string, string> ProgramAccountPins { get; set; } = [];

    // 계약: 설정 「계정」에서 정한 계정 순서(providerId) — 팝업 칩·카드도 이 순서다. 목록에 없는 계정은 뒤에 이름순으로 붙는다
    public List<string> AccountOrder { get; set; } = [];

    // 계약: 「활용도」 등급 경계 4개(오름차순, 배수) — 토큰 환산 비용 ÷ 구독료가 1번째 미만이면 1등급, 4번째 이상이면 5등급
    public List<decimal> ValueGradeBounds { get; set; } = [1m, 3m, 10m, 20m];

    // 계약: 로컬 이벤트 기록(갱신·오류·연동)을 남길지 — DiagnosticsLog. 어디로도 보내지 않는다
    public bool DiagnosticsEnabled { get; set; } = true;

    // 계약: 끄면 시작할 때의 자동 적용·받아 두기와 예약 적용이 모두 멈춘다 — 「업데이트 확인」·「이 버전 설치」는 그대로 된다
    // 계약: 팝업의 확대 비율(%) — 폭과 글자가 함께 커진다. 0 은 「자동」(화면 작업영역 높이로 정한다)
    // 왜: 기본값은 100 이다 — 「자동」은 큰 화면에서 1.5배까지 키워 처음 보는 사람에게 너무 크다(2026-10-09)
    public int PopupScalePercent { get; set; } = 100;

    public bool AutoUpdateEnabled { get; set; } = true;

    // 계약: 받아 둔 업데이트를 적용하는 시각 — 이 PC 의 시간 기준 "HH:mm"
    public string AutoUpdateTime { get; set; } = "04:00";

    // 계약: 켜면 적용 1분 전에 트레이 알림을 낸다 — 그 1분 안에 자동 업데이트를 끄면 적용하지 않는다
    public bool AutoUpdateNotify { get; set; } = true;

    /// <summary>이 묶음의 설정만 기본값으로 되돌린다. 계정 관련 값은 건드리지 않는다.</summary>
    // 함정: 새 설정을 더하면 여기에도 적어야 한다 — 빠뜨리면 「기본값으로」가 그 칸만 조용히 남긴다
    public void ResetToDefaults(SettingsGroup group)
    {
        var d = new AppSettings();

        if (group is SettingsGroup.General or SettingsGroup.All)
        {
            RefreshMinutes = d.RefreshMinutes;
            Hotkey = d.Hotkey;
            ExtraHotkeys = [];
            StartAtLogin = d.StartAtLogin;
            RefreshOnOpen = d.RefreshOnOpen;
            PinTrayIcon = d.PinTrayIcon;
            MulticcEnabled = d.MulticcEnabled;
            PopupScalePercent = d.PopupScalePercent;
            ValueGradeBounds = [.. d.ValueGradeBounds];
            DiagnosticsEnabled = d.DiagnosticsEnabled;
            AutoUpdateEnabled = d.AutoUpdateEnabled;
            AutoUpdateTime = d.AutoUpdateTime;
            AutoUpdateNotify = d.AutoUpdateNotify;
        }

        if (group is SettingsGroup.Display or SettingsGroup.All)
        {
            Theme = d.Theme;
            Palette = d.Palette;
            Language = d.Language;
            CollapsedSections = [];
        }

        if (group is SettingsGroup.Icon or SettingsGroup.All)
        {
            TrayIconStyle = d.TrayIconStyle;
        }

        if (group is SettingsGroup.All)
        {
            CopilotEnabled = d.CopilotEnabled;
            GeminiEnabled = d.GeminiEnabled;
            MulticcSelectedProfile = d.MulticcSelectedProfile;
            MulticcConfigPath = d.MulticcConfigPath;
        }
    }
}

/// <summary>설정 묶음 — 설정 창의 탭과 같다.</summary>
/// 계약: 「계정」은 되돌리지 않는다 — 명칭·유형·요금은 사용자가 손으로 적은 값이라 지우면 되살릴 수 없다.
public enum SettingsGroup
{
    General,
    Display,
    Icon,
    All
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
