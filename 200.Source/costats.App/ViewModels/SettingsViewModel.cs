using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using costats.App.Localization;
using costats.App.Services;
using costats.App.Services.Updates;
using costats.Application.Pulse;
using costats.Application.Security;
using costats.Application.Settings;
using costats.Core.Pulse;
using costats.Infrastructure.Providers;
using Microsoft.Win32;
using System.Linq;

namespace costats.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly IPulseOrchestrator _pulseOrchestrator;
    private readonly ICredentialVault _credentialVault;
    private readonly CopilotUsageFetcher _copilotFetcher;
    private readonly StartupUpdateCoordinator? _updateCoordinator;
    private readonly IMulticcDiscovery? _multiccDiscovery;
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AiUsageMonitor";

    public SettingsViewModel(
        ISettingsStore settingsStore,
        AppSettings settings,
        IPulseOrchestrator pulseOrchestrator,
        ICredentialVault credentialVault,
        CopilotUsageFetcher copilotFetcher,
        StartupUpdateCoordinator? updateCoordinator = null,
        IMulticcDiscovery? multiccDiscovery = null)
    {
        _settingsStore = settingsStore;
        _settings = settings;
        _pulseOrchestrator = pulseOrchestrator;
        _credentialVault = credentialVault;
        _copilotFetcher = copilotFetcher;
        _updateCoordinator = updateCoordinator;
        _multiccDiscovery = multiccDiscovery;

        refreshMinutes = settings.RefreshMinutes;
        startAtLogin = GetStartupRegistryValue();

        multiccDetected = _multiccDiscovery?.IsDetected ?? false;
        multiccEnabled = settings.MulticcEnabled;
        multiccSelectedProfile = settings.MulticcSelectedProfile;
        multiccProfileNames = _multiccDiscovery?.Profiles.Select(p => p.Name).ToList() ?? [];
        multiccProfileCount = multiccProfileNames.Count;

        copilotEnabled = settings.CopilotEnabled;
        geminiEnabled = settings.GeminiEnabled;
        _ = LoadCopilotTokenStatusAsync();
        RefreshAccounts();
    }

    [ObservableProperty]
    private string claudeAccountText = string.Empty;

    [ObservableProperty]
    private string codexAccountText = string.Empty;

    [ObservableProperty]
    private string claudeSourceText = string.Empty;

    [ObservableProperty]
    private string codexSourceText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ExtraAccountRow> extraAccounts = [];

    [ObservableProperty]
    private string newAccountName = string.Empty;

    [ObservableProperty]
    private string accountsMessage = string.Empty;

    [ObservableProperty]
    private bool accountsRestartPending;

    /// <summary>
    /// 계약: 설정 창이 보일 때마다 불러, 그사이 바뀐 로그인 계정을 다시 읽는다.
    /// </summary>
    public void RefreshAccounts()
    {
        RebuildPalettes();
        ClaudeAccountText = Loc.T(AccountIdentityReader.ReadClaude());
        ClaudeSourceText = Loc.T("Claude Code login on this PC · {0}", AccountIdentityReader.ClaudeAccountFile());
        CodexAccountText = Loc.T(AccountIdentityReader.ReadCodex());
        CodexSourceText = Loc.T("Codex CLI login on this PC · {0}", AccountIdentityReader.CodexAuthFile());
        GeminiAccountText = Loc.T(AccountIdentityReader.ReadGemini());

        var root = AccountProfileStore.RootDir;
        ExtraAccounts = Directory.Exists(root)
            ? Directory.GetDirectories(root)
                .Select(dir => Path.GetFileName(dir)!)
                .Where(AccountProfileStore.Exists)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new ExtraAccountRow(
                    name,
                    AccountProfileStore.LabelOf(name),
                    Loc.T(AccountIdentityReader.ReadClaude(Path.Combine(root, name))),
                    Path.Combine(root, name)))
                .ToList()
            : [];
    }

    [RelayCommand]
    private void AddAccount()
    {
        var label = NewAccountName.Trim();
        var name = AccountProfileStore.ToFolderName(label);
        if (name.Length == 0)
        {
            AccountsMessage = Loc.T("Enter the account's email or a short name.");
            return;
        }

        if (AccountProfileStore.Exists(name))
        {
            AccountsMessage = Loc.T("\"{0}\" already exists.", label);
            return;
        }

        var dir = AccountProfileStore.Add(name, label);
        NewAccountName = string.Empty;
        AccountsMessage = Loc.T("Added \"{0}\". Finish signing in in the terminal that opened — this window updates by itself.", label);
        OpenLoginTerminal(dir);
        RefreshAccounts();
        _ = WatchSignInAsync(dir, label);
    }

    [RelayCommand]
    private void SignInAccount(ExtraAccountRow? row)
    {
        if (row is not null)
        {
            OpenLoginTerminal(row.ConfigDir);
            AccountsMessage = Loc.T("Sign in to \"{0}\" in the terminal that opened.", row.Label);
            _ = WatchSignInAsync(row.ConfigDir, row.Label);
        }
    }

    // 왜: 로그인은 브라우저·터미널에서 끝나므로, 토큰 파일이 생기는 것을 지켜봐야 사용자가 창을 다시 열지 않아도 된다
    private async Task WatchSignInAsync(string configDir, string label)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var account = AccountIdentityReader.ReadClaude(configDir);
            if (account is "Not signed in" or "Unable to read account")
            {
                continue;
            }

            RefreshAccounts();
            AccountsRestartPending = true;
            AccountsMessage = Loc.T("\"{0}\" signed in as {1}. Restart to show its usage.", label, account);
            return;
        }
    }

    [RelayCommand]
    private void RemoveAccount(ExtraAccountRow? row)
    {
        if (row is null)
        {
            return;
        }

        try
        {
            AccountProfileStore.Remove(row.Name);
            AccountsRestartPending = true;
            AccountsMessage = Loc.T("Removed \"{0}\". Restart to apply.", row.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AccountsMessage = Loc.T("Could not remove \"{0}\": {1}", row.Name, ex.Message);
        }

        RefreshAccounts();
    }

    [RelayCommand]
    private void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        // 함정: 단일 실행 뮤텍스 때문에 새 프로세스를 먼저 띄우면 바로 종료된다 — 이 프로세스가 끝난 뒤 뜨게 미룬다
        var info = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add($"Wait-Process -Id {Environment.ProcessId} -Timeout 15 -ErrorAction SilentlyContinue; Start-Process -FilePath '{exe.Replace("'", "''")}'");
        System.Diagnostics.Process.Start(info);
        System.Windows.Application.Current.Shutdown();
    }

    // 계약: 로그인은 사용자가 이 터미널(브라우저로 넘어감)에서 직접 한다 — 앱은 폴더만 지정해 `claude auth login` 을 띄운다
    // 함정: 토큰은 CLAUDE_CONFIG_DIR 아래 .credentials.json 에 남는다 — 앱은 값을 읽지 않고 로그인 여부만 본다
    private static void OpenLoginTerminal(string configDir)
    {
        var info = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false
        };
        info.ArgumentList.Add("/k");
        info.ArgumentList.Add("claude");
        info.ArgumentList.Add("auth");
        info.ArgumentList.Add("login");
        info.Environment["CLAUDE_CONFIG_DIR"] = configDir;
        System.Diagnostics.Process.Start(info);
    }

    [ObservableProperty]
    private int refreshMinutes;

    [ObservableProperty]
    private bool startAtLogin;

    [ObservableProperty]
    private bool isCheckingForUpdates;

    [ObservableProperty]
    private string updateStatusText = string.Empty;

    [ObservableProperty]
    private bool multiccDetected;

    [ObservableProperty]
    private bool multiccEnabled;

    [ObservableProperty]
    private string? multiccSelectedProfile;

    [ObservableProperty]
    private IReadOnlyList<string> multiccProfileNames = [];

    [ObservableProperty]
    private int multiccProfileCount;

    [ObservableProperty]
    private string multiccRestartMessage = string.Empty;

    [ObservableProperty]
    private bool copilotEnabled;

    [ObservableProperty]
    private bool geminiEnabled;

    [ObservableProperty]
    private string geminiAccountText = string.Empty;

    partial void OnGeminiEnabledChanged(bool value)
    {
        _settings.GeminiEnabled = value;
        _ = SaveSettingsAsync();
        _ = _pulseOrchestrator.RefreshOnceAsync(RefreshTrigger.Silent, CancellationToken.None);
    }

    [ObservableProperty]
    private bool hasCopilotToken;

    [ObservableProperty]
    private string copilotTokenStatus = string.Empty;

    [ObservableProperty]
    private bool isCopilotTokenBusy;

    public bool IsMulticcAllProfiles => MulticcSelectedProfile is null;

    public string Version { get; } =
        (Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown")
        .Split('+')[0];

    // 함정: 언어가 바뀔 때 목록을 갈아 끼우면 콤보가 선택을 비운다 — 목록은 그대로 두고 항목의 Label 만 다시 알린다
    public IReadOnlyList<RefreshOption> RefreshOptions { get; } =
        new[] { 1, 2, 3, 5, 10, 15 }.Select(m => new RefreshOption(m)).ToArray();

    // 계약: 설정 창의 색 견본 목록. 견본 색은 지금 명암의 대표색이라 명암이 바뀌면 다시 만든다
    [ObservableProperty]
    private IReadOnlyList<PaletteOption> palettes = [];

    private void RebuildPalettes()
    {
        Palettes = ThemeManager.PaletteNames
            .Select(name => new PaletteOption(name, ThemeManager.SwatchOf(name), name == ThemeManager.Palette))
            .ToList();
    }

    [RelayCommand]
    private void SelectPalette(string? name)
    {
        ThemeManager.Apply(name, ThemeManager.IsDark);
        _settings.Palette = ThemeManager.Palette;
        _ = SaveSettingsAsync();
        RebuildPalettes();
    }

    public string LanguageLabel =>costats.App.Localization.Loc.IsKorean ? "한국어" : "English";

    [RelayCommand]
    private void SetLanguage(string? language)
    {
        costats.App.Localization.Loc.SetLanguage(language);
        _settings.Language = costats.App.Localization.Loc.Language;
        _ = SaveSettingsAsync();
        OnPropertyChanged(nameof(LanguageLabel));
        foreach (var option in RefreshOptions)
        {
            option.NotifyLanguageChanged();
        }
        _ = LoadCopilotTokenStatusAsync();
        RefreshAccounts();
    }

    public RefreshOption SelectedRefreshOption
    {
        get => RefreshOptions.FirstOrDefault(o => o.Minutes == RefreshMinutes) ?? RefreshOptions[3];
        set
        {
            if (value is not null && RefreshMinutes != value.Minutes)
            {
                RefreshMinutes = value.Minutes;
                OnPropertyChanged();
            }
        }
    }

    partial void OnRefreshMinutesChanged(int value)
    {
        _settings.RefreshMinutes = value;
        _pulseOrchestrator.UpdateRefreshInterval(TimeSpan.FromMinutes(value));
        _ = SaveSettingsAsync();
        OnPropertyChanged(nameof(SelectedRefreshOption));
    }

    partial void OnStartAtLoginChanged(bool value)
    {
        _settings.StartAtLogin = value;
        SetStartupRegistryValue(value);
        _ = SaveSettingsAsync();
    }

    partial void OnMulticcEnabledChanged(bool value)
    {
        _settings.MulticcEnabled = value;
        MulticcRestartMessage = Loc.T("Restart required to apply changes.");
        _ = SaveSettingsAsync();
    }

    partial void OnMulticcSelectedProfileChanged(string? value)
    {
        _settings.MulticcSelectedProfile = value;
        MulticcRestartMessage = Loc.T("Restart required to apply changes.");
        OnPropertyChanged(nameof(IsMulticcAllProfiles));
        _ = SaveSettingsAsync();
    }

    partial void OnCopilotEnabledChanged(bool value)
    {
        _settings.CopilotEnabled = value;
        _ = SaveSettingsAsync();
        _ = _pulseOrchestrator.RefreshOnceAsync(RefreshTrigger.Silent, CancellationToken.None);
    }

    public async Task SaveCopilotTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            CopilotTokenStatus = Loc.T("Copilot token is required.");
            return;
        }

        IsCopilotTokenBusy = true;
        try
        {
            var trimmedToken = token.Trim();
            await _credentialVault.SaveAsync(CredentialKeys.CopilotToken, trimmedToken, CancellationToken.None);
            var validation = await _copilotFetcher.FetchAsync(trimmedToken, CancellationToken.None);
            HasCopilotToken = true;
            CopilotTokenStatus = validation.Status == CopilotFetchStatus.Success
                ? "Copilot token saved."
                : Loc.Tr(validation.StatusSummary);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copilot token save failed: {ex.Message}");
            CopilotTokenStatus = Loc.T("Could not save Copilot token.");
        }
        finally
        {
            IsCopilotTokenBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearCopilotTokenAsync()
    {
        IsCopilotTokenBusy = true;
        try
        {
            await _credentialVault.SaveAsync(CredentialKeys.CopilotToken, string.Empty, CancellationToken.None);
            HasCopilotToken = false;
            CopilotTokenStatus = Loc.T("Copilot token cleared.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copilot token clear failed: {ex.Message}");
            CopilotTokenStatus = Loc.T("Could not clear Copilot token.");
        }
        finally
        {
            IsCopilotTokenBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (_updateCoordinator is null)
        {
            UpdateStatusText = Loc.T("Updates are not available.");
            return;
        }

        // Cancel any previous in-flight check before starting a new one
        _updateCheckCts?.Cancel();
        _updateCheckCts?.Dispose();
        _updateCheckCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var ct = _updateCheckCts.Token;

        IsCheckingForUpdates = true;
        UpdateStatusText = Loc.T("Checking for updates...");

        try
        {
            var result = await Task.Run(() => _updateCoordinator.CheckAndStageUpdateAsync(ct, forceCheck: true), ct);

            switch (result)
            {
                case UpdateCheckResult.UpdateStaged:
                case UpdateCheckResult.UpdateAlreadyStaged:
                    UpdateStatusText = Loc.T("Update found. Restarting...");
                    if (await Task.Run(() => _updateCoordinator.TryApplyPendingUpdateAsync(ct, manualTrigger: true), ct))
                    {
                        // Use BeginInvoke to avoid any potential deadlock with synchronous Invoke
                        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                            System.Windows.Application.Current.Shutdown(0));
                    }
                    else
                    {
                        UpdateStatusText = Loc.T("Update staged. Restart to apply.");
                        IsCheckingForUpdates = false;
                    }
                    break;

                case UpdateCheckResult.UpToDate:
                case UpdateCheckResult.Skipped:
                    UpdateStatusText = Loc.T("You're up to date.");
                    IsCheckingForUpdates = false;
                    break;

                case UpdateCheckResult.Disabled:
                    UpdateStatusText = Loc.T("Updates are not available.");
                    IsCheckingForUpdates = false;
                    break;

                case UpdateCheckResult.AlreadyRunning:
                    UpdateStatusText = Loc.T("Update check already in progress.");
                    IsCheckingForUpdates = false;
                    break;

                case UpdateCheckResult.CheckFailed:
                default:
                    UpdateStatusText = Loc.T("Could not check for updates.");
                    IsCheckingForUpdates = false;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText = Loc.T("Update check timed out. Try again.");
            IsCheckingForUpdates = false;
        }
        catch
        {
            UpdateStatusText = Loc.T("Could not check for updates.");
            IsCheckingForUpdates = false;
        }
    }

    private CancellationTokenSource? _updateCheckCts;

    private async Task LoadCopilotTokenStatusAsync()
    {
        try
        {
            var token = await _credentialVault.LoadAsync(CredentialKeys.CopilotToken, CancellationToken.None);
            HasCopilotToken = !string.IsNullOrWhiteSpace(token);
            CopilotTokenStatus = HasCopilotToken ? string.Empty : Loc.T("Copilot token not set.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copilot token load failed: {ex.Message}");
            CopilotTokenStatus = Loc.T("Could not load Copilot token.");
        }
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(_settings, CancellationToken.None);
    }

    private static bool GetStartupRegistryValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return key?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartupRegistryValue(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key is null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch
        {
            // Silently ignore registry errors
        }
    }
}

public sealed class RefreshOption(int minutes) : ObservableObject
{
    public int Minutes { get; } = minutes;

    public string Label => costats.App.Localization.Loc.IsKorean
        ? $"{Minutes}분"
        : Minutes == 1 ? "1 minute" : $"{Minutes} minutes";

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(Label));
}
