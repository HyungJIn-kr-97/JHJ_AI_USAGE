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

    // 계약: 팝업 고정 — 켜면 바깥을 눌러도·포커스를 잃어도 닫히지 않는다. 기본은 꺼짐
    public bool PopupPinned { get; set; }

    // 계약: 켜면 트레이 아이콘을 숨겨진 아이콘(^) 밖 작업 표시줄에 늘 둔다 — 한 번 클릭으로 팝업을 열 수 있게
    public bool PinTrayIcon { get; set; } = true;

    // 계약: 팝업에서 접어 둔 구역의 이름("chart" · "models" · "tokenTypes") — 없으면 펼친 것이다
    public List<string> CollapsedSections { get; set; } = [];

    /// <summary>"system" | "light" | "dark". system 은 시작할 때의 Windows 앱 모드를 따른다.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>"bull" | "navy" | "emerald" | "violet" | "slate". 색상 팔레트.</summary>
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

    /// <summary>"en" | "ko" | "zh". 화면 문구의 언어.</summary>
    /// 계약: 기본값은 Core 의 Loc.Default 와 같은 글자다 — 이 층은 Jhj.Core.Wpf 를 못 물어 글자로 적는다.
    public string Language { get; set; } = "en";

    public bool MulticcEnabled { get; set; } = true;

    /// <summary>계약: 값이 있으면 그 프로필 하나만 보인다 — null 은 전체를 쌓아 보인다.</summary>
    public string? MulticcSelectedProfile { get; set; }

    /// <summary>계약: null 은 자동 찾기다(~/.multicc 또는 $MULTICC_DIR).</summary>
    public string? MulticcConfigPath { get; set; }

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

    // 계약: 이 설치의 장비 ID(GUID) — 처음 실행 때 한 번 만들고 「기본값으로」로도 지우지 않는다(여러 장비의 기록을 합칠 때의 키다)
    public string DeviceId { get; set; } = string.Empty;

    // 계약: 사용자가 붙인 장비 명칭 — 비어 있으면 PC 이름을 쓴다. 「기본값으로」로 지우지 않는다
    public string DeviceLabel { get; set; } = string.Empty;

    // 계약: 팝업의 확대 비율(%) — 폭과 글자가 함께 커진다. 0 은 「자동」(화면 작업영역 높이로 정한다)
    // 왜: 기본값은 100 이다 — 「자동」은 큰 화면에서 1.5배까지 키워 처음 보는 사람에게 너무 크다
    public int PopupScalePercent { get; set; } = 100;

    // 계약: 끄면 시작할 때의 자동 적용·받아 두기와 예약 적용이 모두 멈춘다 — 「업데이트 확인」·「이 버전 설치」는 그대로 된다
    public bool AutoUpdateEnabled { get; set; } = true;

    // 계약: 받아 둔 업데이트를 적용하는 시각 — 이 PC 의 시간 기준 "HH:mm"
    public string AutoUpdateTime { get; set; } = "04:00";

    // 계약: 켜면 적용 1분 전에 트레이 알림을 낸다 — 그 1분 안에 자동 업데이트를 끄면 적용하지 않는다
    public bool AutoUpdateNotify { get; set; } = true;

    // 계약: 사용률 임계 알림 묶음 — 설정 「사용률 알림」 탭. 판정 규칙은 Alerts/UsageAlertEvaluator.cs
    public bool UsageAlertEnabled { get; set; } = true;

    // 계약: 알림 단계(%) 오름차순 — 사용률이 한 단계를 처음 넘을 때 한 번만 알린다. 창이 초기화되면 다시 센다
    public List<int> UsageAlertSteps { get; set; } = [80, 85, 90, 95, 98, 99, 100];

    /// <summary>"toast" | "balloon" | "both". toast 는 화면 우측 작은 창, balloon 은 Windows 알림 센터다.</summary>
    public string UsageAlertStyle { get; set; } = "toast";

    // 계약: 토스트가 떠 있는 시간(초) — 마우스를 올리면 멈춘다. 기본 3초는 2026-10-12 사용자 지정
    public int UsageAlertToastSeconds { get; set; } = 3;

    /// <summary>"bottom-right" | "top-right" | "bottom-left" | "top-left" — 토스트가 뜨는 작업 영역의 모서리. 기본은 작업 표시줄 쪽 우측 아래.</summary>
    public string UsageAlertPosition { get; set; } = "bottom-right";

    // 계약: 미니 위젯 — 사용률 막대만 작게 띄워 두는 창. Account 는 providerId, null 은 전체 계정. 위치는 끌어 놓은 자리(null 은 화면 우측 위)
    public bool MiniWidgetVisible { get; set; }
    public bool MiniWidgetPinned { get; set; } = true;
    public string? MiniWidgetAccount { get; set; }
    public double? MiniWidgetLeft { get; set; }
    public double? MiniWidgetTop { get; set; }

    // 계약: 미니 위젯 켬/끔 전역 단축키 — 팝업 단축키(Ctrl+Alt+2)에 Shift 를 더한 것. 비우면 등록하지 않는다
    public const string DefaultMiniWidgetHotkey = "Ctrl+Alt+Shift+2";
    public string MiniWidgetHotkey { get; set; } = DefaultMiniWidgetHotkey;

    // 계약: 유형별 켜고 끄기 — 세션(5시간) · 주간 · 모델별 주간(Fable 등)
    public bool UsageAlertSession { get; set; } = true;
    public bool UsageAlertWeekly { get; set; } = true;
    public bool UsageAlertModelWeek { get; set; } = true;

    // 계약: 켜면 알림에 소진 예상·권장 모델 한 줄을 붙인다
    public bool UsageAlertRecommend { get; set; } = true;

    // 계약: 세션 초기화까지 이 분 이하로 남았으면 사용률이 높아도 고토큰 모델을 말리지 않는다
    public int UsageAlertRelaxMinutes { get; set; } = 30;

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
            PopupPinned = d.PopupPinned;
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

        if (group is SettingsGroup.Alerts or SettingsGroup.All)
        {
            UsageAlertEnabled = d.UsageAlertEnabled;
            UsageAlertSteps = [.. d.UsageAlertSteps];
            UsageAlertStyle = d.UsageAlertStyle;
            UsageAlertToastSeconds = d.UsageAlertToastSeconds;
            UsageAlertPosition = d.UsageAlertPosition;
            UsageAlertSession = d.UsageAlertSession;
            UsageAlertWeekly = d.UsageAlertWeekly;
            UsageAlertModelWeek = d.UsageAlertModelWeek;
            UsageAlertRecommend = d.UsageAlertRecommend;
            UsageAlertRelaxMinutes = d.UsageAlertRelaxMinutes;
            MiniWidgetPinned = d.MiniWidgetPinned;
            MiniWidgetAccount = d.MiniWidgetAccount;
            MiniWidgetLeft = d.MiniWidgetLeft;
            MiniWidgetTop = d.MiniWidgetTop;
            MiniWidgetHotkey = d.MiniWidgetHotkey;
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
    Alerts,
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
