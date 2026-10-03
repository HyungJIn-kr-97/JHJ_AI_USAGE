namespace costats.Application.Settings;

public sealed class AppSettings
{
    public int RefreshMinutes { get; set; } = 5;
    public string Hotkey { get; set; } = "Ctrl+Alt+Y";
    public bool StartAtLogin { get; set; } = false;

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
}
