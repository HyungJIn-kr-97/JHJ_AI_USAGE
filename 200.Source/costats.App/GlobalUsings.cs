// 계약: 아래 이름들은 이제 JHJ_CS_CORE 가 갖는다 — 호출부를 그대로 두려고 별칭으로 잇는다.
// 왜: 같은 코드가 JHJ_GIT_CENTER·JHJ_VPN 과 세 벌로 갈려 한쪽만 고쳐지고 있었다(JHJ_CS_CORE/300.Docs/010 §4).
global using DiagnosticsConsent = Jhj.Core.Diagnostics.DiagnosticsConsent;
global using DiagnosticsLog = Jhj.Core.Diagnostics.DiagnosticsLog;
global using GithubUpdateService = Jhj.Core.Updates.GithubUpdateService;
global using GlassBackdropService = Jhj.Core.Wpf.Shell.GlassBackdrop;
global using IGlassBackdropService = Jhj.Core.Wpf.Shell.IGlassBackdrop;
global using HotkeyRules = Jhj.Core.Wpf.Hotkeys.HotkeyRules;
global using HotkeyVerdict = Jhj.Core.Wpf.Hotkeys.HotkeyVerdict;
global using LegacyMigration = Jhj.Core.Windows.Install.LegacyMigration;
global using Loc = Jhj.Core.Wpf.Localization.Loc;
global using ReleaseInfo = Jhj.Core.Updates.ReleaseInfo;
global using ScheduledUpdateService = Jhj.Core.Wpf.Updates.ScheduledUpdateService;
global using SelfInstaller = Jhj.Core.Windows.Install.SelfInstaller;
global using SingleInstance = Jhj.Core.Wpf.Boot.SingleInstance;
global using StartupRegistry = Jhj.Core.Windows.Startup.StartupRegistry;
global using TaskbarPositionService = Jhj.Core.Wpf.Tray.TaskbarAnchor;
global using ThemeManager = Jhj.Core.Wpf.Theme.JhjTheme;
global using TrayPinService = Jhj.Core.Windows.Tray.TrayPin;
global using UpdateCheckResult = Jhj.Core.Updates.UpdateCheckResult;
global using UpdateProgress = Jhj.Core.Updates.UpdateProgress;
global using UpdateStage = Jhj.Core.Updates.UpdateStage;

// 계약: 옛 이름으로 남아 있던 업데이트 조정자 — 참조부가 많아 이름만 잇는다
global using StartupUpdateCoordinator = Jhj.Core.Updates.GithubUpdateService;
