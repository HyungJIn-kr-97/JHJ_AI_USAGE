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
    private readonly PulseViewModel? _pulseViewModel;
    private const string ClaudeMainId = "claude:" + AccountProfileStore.DefaultName;
    private const string CodexMainId = "codex";
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AiUsageMonitor";

    public SettingsViewModel(
        ISettingsStore settingsStore,
        AppSettings settings,
        IPulseOrchestrator pulseOrchestrator,
        ICredentialVault credentialVault,
        CopilotUsageFetcher copilotFetcher,
        StartupUpdateCoordinator? updateCoordinator = null,
        IMulticcDiscovery? multiccDiscovery = null,
        PulseViewModel? pulseViewModel = null)
    {
        _pulseViewModel = pulseViewModel;
        _settingsStore = settingsStore;
        _settings = settings;
        _pulseOrchestrator = pulseOrchestrator;
        _credentialVault = credentialVault;
        _copilotFetcher = copilotFetcher;
        _updateCoordinator = updateCoordinator;
        _multiccDiscovery = multiccDiscovery;

        refreshMinutes = settings.RefreshMinutes;
        startAtLogin = GetStartupRegistryValue();
        refreshOnOpen = settings.RefreshOnOpen;
        pinTrayIcon = settings.PinTrayIcon;

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
    private string claudeSourceText = string.Empty;

    [ObservableProperty]
    private string codexSourceText = string.Empty;

    // 계약: 첫 줄이 이 PC 로그인(IsMain), 그 뒤가 추가 계정이다
    [ObservableProperty]
    private IReadOnlyList<ExtraAccountRow> claudeAccounts = [];

    [ObservableProperty]
    private string newAccountName = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ExtraAccountRow> codexAccounts = [];

    [ObservableProperty]
    private string newCodexAccountName = string.Empty;

    [ObservableProperty]
    private string accountsMessage = string.Empty;

    [ObservableProperty]
    private bool accountsRestartPending;

    // 계약: AccountsMessage 가 ✓ 로 시작하면 완료 안내라 초록으로 그린다 — 오류·진행 중 문구는 ✓ 없이 쓴다
    [ObservableProperty]
    private bool accountsMessageIsSuccess;

    partial void OnAccountsMessageChanged(string value) => AccountsMessageIsSuccess = value.StartsWith('✓');

    // 계약: 유형 칸의 추천 목록 — 화면 언어로 보이고, 자유 입력도 된다
    public IReadOnlyList<string> TypeSuggestions => AccountMeta.SuggestedTypes.Select(t => Loc.T(t)).ToList();

    /// <summary>
    /// 계약: 설정 창이 보일 때마다 불러, 그사이 바뀐 로그인 계정을 다시 읽는다.
    /// </summary>
    public void RefreshAccounts()
    {
        RebuildPalettes();
        ClaudeSourceText = Loc.T("Claude Code login on this PC · {0}", AccountIdentityReader.ClaudeAccountFile());
        CodexSourceText = Loc.T("Codex CLI login on this PC · {0}", AccountIdentityReader.CodexAuthFile());
        GeminiAccountText = Loc.T(AccountIdentityReader.ReadGemini());
        OnPropertyChanged(nameof(TypeSuggestions));

        var root = AccountProfileStore.RootDir;
        var claudeRows = new List<ExtraAccountRow> { NewRow(ClaudeMainId, string.Empty, AccountIdentityReader.ReadClaude(), null) };
        if (Directory.Exists(root))
        {
            claudeRows.AddRange(Directory.GetDirectories(root)
                .Select(dir => Path.GetFileName(dir)!)
                .Where(AccountProfileStore.Exists)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => NewRow("claude:" + name, name, AccountIdentityReader.ReadClaude(Path.Combine(root, name)), Path.Combine(root, name))));
        }

        ClaudeAccounts = claudeRows;

        var codexRows = new List<ExtraAccountRow> { NewRow(CodexMainId, string.Empty, AccountIdentityReader.ReadCodex(), null) };
        codexRows.AddRange(CodexAccountStore.List()
            .Select(name => NewRow("codex:" + name, name, AccountIdentityReader.ReadCodex(CodexAccountStore.DirOf(name)), CodexAccountStore.DirOf(name))));
        CodexAccounts = codexRows;

        // 왜: 실행 한 번에 GitHub 를 한 번만 읽는다 — 설정을 열 때마다 부르면 비로그인 한도(시간당 60회)를 깎는다
        if (Releases.Count == 0 && !_releasesRequested && _updateCoordinator is not null)
        {
            _releasesRequested = true;
            _ = LoadReleasesAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        }
    }

    private bool _releasesRequested;

    private ExtraAccountRow NewRow(string id, string name, string account, string? dir) =>
        new(id, name, Loc.T(account), dir, IsSignedIn(account),
            AccountMeta.DisplayNameOf(_settings, id), AccountMeta.TypeOf(_settings, id), OnRowEdited);

    // 계약: AccountIdentityReader 의 두 실패 문구 말고는 로그인된 것으로 본다
    private static bool IsSignedIn(string account) => account is not ("Not signed in" or "Unable to read account");

    private System.Windows.Threading.DispatcherTimer? _saveTimer;

    private void OnRowEdited(ExtraAccountRow row)
    {
        var cleared = AccountMeta.Set(_settings, row.Id, row.DisplayNameText, row.AccountType);
        foreach (var other in ClaudeAccounts.Concat(CodexAccounts).Where(r => cleared.Contains(r.Id, StringComparer.OrdinalIgnoreCase)))
        {
            other.ClearTypeSilently();
        }

        // 왜: 유형 칸은 글자마다 바뀐다 — 저장과 팝업 갱신은 입력이 멈춘 뒤 한 번만 한다
        _saveTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(600), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => { _saveTimer!.Stop(); SaveAccountMeta(); }, System.Windows.Application.Current.Dispatcher);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveAccountMeta()
    {
        _ = _settingsStore.SaveAsync(_settings, CancellationToken.None);
        _pulseViewModel?.ApplyAccountMeta();
    }

    [RelayCommand]
    private void SignInRow(ExtraAccountRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (AccountMeta.KindOf(row.Id) == "codex")
        {
            SignInCodexInto(row.DisplayNameText, row.ConfigDir);
        }
        else
        {
            SignInClaudeInto(row.DisplayNameText, row.ConfigDir);
        }
    }

    [RelayCommand]
    private Task SignOutRow(ExtraAccountRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        return AccountMeta.KindOf(row.Id) == "codex"
            ? SignOutAsync(row.DisplayNameText, "Codex", CliLocator.FindCodex(), "logout", row.ConfigDir is null ? null : ("CODEX_HOME", row.ConfigDir), row.IsMain)
            : SignOutAsync(row.DisplayNameText, "Claude", CliLocator.FindClaude(), "auth logout", row.ConfigDir is null ? null : ("CLAUDE_CONFIG_DIR", row.ConfigDir), row.IsMain);
    }

    [RelayCommand]
    private void RemoveRow(ExtraAccountRow? row)
    {
        if (row is null || row.IsMain)
        {
            return;
        }

        if (AccountMeta.KindOf(row.Id) == "codex")
        {
            RemoveCodexAccount(row);
        }
        else
        {
            RemoveAccount(row);
        }
    }

    // 계약: 로그아웃도 공식 CLI 가 한다 — 앱은 토큰 파일을 직접 지우지 않는다
    // 함정: 이 PC 기본 로그인은 터미널의 claude·codex 와 같은 것이라 로그아웃하면 CLI 도 함께 풀린다 — 그래서 한 번 묻는다
    private async Task SignOutAsync(string label, string tool, string? exe, string arguments, (string Name, string Dir)? homeEnv, bool isMain)
    {
        if (exe is null)
        {
            AccountsMessage = Loc.T("{0} CLI was not found on this PC. Install it, then try again.", tool);
            return;
        }

        if (isMain && MessageBox.Show(
                Loc.T("Sign out of \"{0}\" on this PC? The {0} CLI in your terminal will be signed out too.", tool),
                "AI Usage Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(CreateCliStartInfo(exe, arguments, homeEnv));
            if (process is not null)
            {
                process.StandardInput.Close();
                _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
                _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or IOException)
        {
            AccountsMessage = Loc.T("Could not sign out of \"{0}\": {1}", label, ex.Message);
            return;
        }

        RefreshAccounts();
        AccountsMessage = Loc.T("✓ Signed out of \"{0}\".", label);
        _ = _pulseOrchestrator.RefreshOnceAsync(RefreshTrigger.Manual, CancellationToken.None);
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
        RefreshAccounts();
        SignInClaudeInto(label, dir);
    }

    [RelayCommand]
    private void AddCodexAccount()
    {
        var label = NewCodexAccountName.Trim();
        var name = AccountProfileStore.ToFolderName(label);
        if (name.Length == 0)
        {
            AccountsMessage = Loc.T("Enter the account's email or a short name.");
            return;
        }

        if (CodexAccountStore.Exists(name))
        {
            AccountsMessage = Loc.T("\"{0}\" already exists.", label);
            return;
        }

        var dir = CodexAccountStore.Add(name, label);
        NewCodexAccountName = string.Empty;
        RefreshAccounts();
        SignInCodexInto(label, dir);
    }

    private void RemoveCodexAccount(ExtraAccountRow row)
    {
        try
        {
            CodexAccountStore.Remove(row.Name);
            AccountMeta.Remove(_settings, row.Id);
            _ = _settingsStore.SaveAsync(_settings, CancellationToken.None);
            AccountsRestartPending = true;
            AccountsMessage = Loc.T("Removed \"{0}\". Restart to apply.", row.DisplayNameText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AccountsMessage = Loc.T("Could not remove \"{0}\": {1}", row.DisplayNameText, ex.Message);
        }

        RefreshAccounts();
    }

    private void SignInCodexInto(string label, string? codexHome)
    {
        var codex = CliLocator.FindCodex();
        if (codex is null)
        {
            AccountsMessage = Loc.T("{0} CLI was not found on this PC. Install it, then try again.", "Codex");
            return;
        }

        _ = RunLoginAsync(label, codex, "login", codexHome is null ? null : ("CODEX_HOME", codexHome),
            () => AccountIdentityReader.ReadCodex(codexHome), acceptsCode: false);
    }

    private void SignInClaudeInto(string label, string? configDir)
    {
        var claude = CliLocator.FindClaude();
        if (claude is null)
        {
            AccountsMessage = Loc.T("{0} CLI was not found on this PC. Install it, then try again.", "Claude Code");
            return;
        }

        _ = RunLoginAsync(label, claude, "auth login", configDir is null ? null : ("CLAUDE_CONFIG_DIR", configDir),
            () => AccountIdentityReader.ReadClaude(configDir), acceptsCode: true);
    }

    private System.Diagnostics.Process? _loginProcess;

    // 계약: 로그인 CLI 가 뒤에서 도는 동안 true — 화면에 취소 버튼(과 코드 입력 칸)이 보인다
    [ObservableProperty]
    private bool loginInProgress;

    // 계약: 브라우저가 코드를 보여 줄 수 있는 로그인(Claude)일 때만 true — Codex 는 브라우저만으로 끝난다
    [ObservableProperty]
    private bool loginAcceptsCode;

    [ObservableProperty]
    private string loginCodeInput = string.Empty;

    // 계약: 붙여넣은 코드를 CLI 의 입력으로 넘긴다 — 토큰 교환과 저장은 CLI 가 한다
    [RelayCommand]
    private void SubmitLoginCode()
    {
        var code = LoginCodeInput.Trim();
        if (code.Length == 0 || _loginProcess is not { HasExited: false } process)
        {
            return;
        }

        try
        {
            process.StandardInput.WriteLine(code);
            process.StandardInput.Flush();
            LoginCodeInput = string.Empty;
            AccountsMessage = Loc.T("Code sent. Waiting for sign-in to finish...");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            AccountsMessage = Loc.T("Could not start sign-in: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private void CancelLogin()
    {
        try
        {
            _loginProcess?.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 이미 끝난 프로세스다 — 아래 대기가 풀리며 정리된다
        }
    }

    // 계약: 로그인은 공식 CLI 가 연 브라우저에서 사용자가 직접 한다 — 앱은 CLI 를 창 없이 돌리고, 코드가 필요하면 입력 칸의 값을 넘겨 준다
    // 함정: Claude 토큰은 CLAUDE_CONFIG_DIR 아래 .credentials.json 에 남는다 — 앱은 값을 읽지 않고 로그인 여부만 본다
    private async Task RunLoginAsync(string label, string exe, string arguments, (string Name, string Dir)? homeEnv, Func<string> readAccount, bool acceptsCode)
    {
        if (LoginInProgress)
        {
            AccountsMessage = Loc.T("Another sign-in is still in progress. Finish or cancel it first.");
            return;
        }

        var info = CreateCliStartInfo(exe, arguments, homeEnv);
        AccountsMessage = acceptsCode
            ? Loc.T("Finish signing in to \"{0}\" in the browser. If it shows a code, paste it below.", label)
            : Loc.T("Finish signing in to \"{0}\" in the browser.", label);
        try
        {
            using var process = System.Diagnostics.Process.Start(info);
            if (process is not null)
            {
                _loginProcess = process;
                LoginAcceptsCode = acceptsCode;
                LoginCodeInput = string.Empty;
                LoginInProgress = true;
                // 함정: 출력을 읽어 주지 않으면 파이프가 차서 CLI 가 멈춘다 — 내용은 쓰지 않고 흘려보낸다
                _ = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
                _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null);

                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AccountsMessage = Loc.T("Could not start sign-in: {0}", ex.Message);
            return;
        }
        finally
        {
            _loginProcess = null;
            LoginInProgress = false;
        }

        RefreshAccounts();
        var account = readAccount();
        if (account is "Not signed in" or "Unable to read account")
        {
            AccountsMessage = Loc.T("Sign-in to \"{0}\" was not completed.", label);
            return;
        }

        if (homeEnv is null)
        {
            // 왜: 기본 계정의 토큰은 fetcher 가 매번 파일에서 다시 읽는다 — 재시작 없이 갱신만 돌리면 새 계정 사용량이 뜬다
            AccountsMessage = Loc.T("✓ \"{0}\" signed in as {1}. Refreshing usage now.", label, account);
            _ = _pulseOrchestrator.RefreshOnceAsync(RefreshTrigger.Manual, CancellationToken.None);
            return;
        }

        // 함정: 추가 계정의 로그 소스는 App 시작 때 등록된다 — 재시작 없이는 팝업 계정 목록에 안 나타나므로 스스로 다시 띄운다
        AccountsMessage = Loc.T("✓ \"{0}\" signed in as {1}. Restarting to show its usage...", label, account);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        RestartApp();
    }

    // 왜: 터미널 창을 띄우면 CLI 의 프롬프트가 그대로 보여 앱 밖으로 끌려 나간다 — 창 없이 돌리고 입출력을 앱이 쥔다
    private static System.Diagnostics.ProcessStartInfo CreateCliStartInfo(string exe, string arguments, (string Name, string Dir)? homeEnv)
    {
        var isBatch = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var info = new System.Diagnostics.ProcessStartInfo(isBatch ? "cmd.exe" : exe)
        {
            Arguments = isBatch ? $"/c \"\"{exe}\" {arguments}\"" : arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (homeEnv is { } env)
        {
            info.Environment[env.Name] = env.Dir;
        }

        return info;
    }

    private void RemoveAccount(ExtraAccountRow row)
    {
        try
        {
            AccountProfileStore.Remove(row.Name);
            AccountMeta.Remove(_settings, row.Id);
            _ = _settingsStore.SaveAsync(_settings, CancellationToken.None);
            AccountsRestartPending = true;
            AccountsMessage = Loc.T("Removed \"{0}\". Restart to apply.", row.DisplayNameText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AccountsMessage = Loc.T("Could not remove \"{0}\": {1}", row.DisplayNameText, ex.Message);
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


    [ObservableProperty]
    private int refreshMinutes;

    [ObservableProperty]
    private bool startAtLogin;

    [ObservableProperty]
    private bool refreshOnOpen;

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

    partial void OnRefreshOnOpenChanged(bool value)
    {
        _settings.RefreshOnOpen = value;
        _ = SaveSettingsAsync();
    }

    [ObservableProperty]
    private bool pinTrayIcon;

    partial void OnPinTrayIconChanged(bool value)
    {
        _settings.PinTrayIcon = value;
        TrayPinService.SetPromoted(value);
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

    // 계약: GitHub 릴리스 목록(최신 순) — 버전 비교와 「이 버전 설치」 콤보가 쓴다
    [ObservableProperty]
    private IReadOnlyList<ReleaseInfo> releases = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedReleaseCommand))]
    private ReleaseInfo? selectedRelease;

    // 계약: "GitHub 최신 v1.0.3 · 2026-10-03" — 아직 못 읽었으면 빈 문자열
    [ObservableProperty]
    private string latestReleaseText = string.Empty;

    [ObservableProperty]
    private string versionCompareText = string.Empty;

    public bool CanInstallRelease => _updateCoordinator?.CanInstall ?? false;

    public string ReleasesUrl => _updateCoordinator?.ReleasesPageUrl ?? "https://github.com/" + costats.App.Services.Updates.UpdateOptions.DefaultRepository + "/releases";

    /// <returns>읽기에 성공했으면 true</returns>
    private async Task<bool> LoadReleasesAsync(CancellationToken ct)
    {
        if (_updateCoordinator is null)
        {
            return false;
        }

        try
        {
            var list = await Task.Run(() => _updateCoordinator.GetReleasesAsync(ct), ct);
            Releases = list;
            var current = _updateCoordinator.CurrentVersion;
            SelectedRelease = list.FirstOrDefault(r => r.Version != current && r.HasPackage) ?? list.FirstOrDefault();
            var latest = list.FirstOrDefault(r => !r.Prerelease);
            if (latest is null)
            {
                LatestReleaseText = Loc.T("No release on GitHub yet");
                VersionCompareText = string.Empty;
                return true;
            }

            LatestReleaseText = Loc.T("GitHub latest {0}", latest.Label);
            VersionCompareText = latest.Version.CompareTo(current) switch
            {
                > 0 => Loc.T("A newer version is available."),
                < 0 => Loc.T("This build is newer than the latest release."),
                _ => Loc.T("You're up to date.")
            };
            return true;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            LatestReleaseText = Loc.T("Could not read GitHub releases.");
            VersionCompareText = string.Empty;
            return false;
        }
    }

    [RelayCommand]
    private void OpenReleases() =>
        Process.Start(new ProcessStartInfo(SelectedRelease?.HtmlUrl ?? ReleasesUrl) { UseShellExecute = true });

    // 계약: 낮은 버전도 설치된다(되돌리기) — 받은 뒤 앱이 닫히고 그 버전으로 다시 뜬다
    [RelayCommand(CanExecute = nameof(CanInstallSelected))]
    private async Task InstallSelectedReleaseAsync()
    {
        if (_updateCoordinator is null || SelectedRelease is not { } release)
        {
            return;
        }

        if (!CanInstallRelease)
        {
            UpdateStatusText = Loc.T("Installing a version works only in the installed app, not in a development build.");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        IsCheckingForUpdates = true;
        UpdateStatusText = Loc.T("Downloading {0}...", "v" + release.Version.ToString(3));
        try
        {
            var result = await Task.Run(() => _updateCoordinator.StageReleaseAsync(release.Version, cts.Token), cts.Token);
            if (result == UpdateCheckResult.UpdateStaged &&
                await Task.Run(() => _updateCoordinator.TryApplyPendingUpdateAsync(cts.Token, manualTrigger: true), cts.Token))
            {
                UpdateStatusText = Loc.T("Installing {0}. The app will restart.", "v" + release.Version.ToString(3));
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown(0));
                return;
            }

            UpdateStatusText = Loc.T("Could not install {0}.", "v" + release.Version.ToString(3));
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText = Loc.T("Update check timed out. Try again.");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private bool CanInstallSelected() =>
        SelectedRelease is { HasPackage: true } r && _updateCoordinator is not null && r.Version != _updateCoordinator.CurrentVersion;

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (_updateCoordinator is null)
        {
            UpdateStatusText = Loc.T("Updates are not available.");
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatusText = Loc.T("Checking for updates...");
        using (var listCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await LoadReleasesAsync(listCts.Token);
        }

        // 왜: 개발 빌드·설치 폴더 밖 실행은 스스로 교체할 수 없다 — 비교 결과만 보이고 멈춘다
        if (!CanInstallRelease)
        {
            UpdateStatusText = Loc.T("Installing a version works only in the installed app, not in a development build.");
            IsCheckingForUpdates = false;
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

    // 계약: 켜면 「지금 실행 중인 exe」 경로로 HKCU Run 에 등록한다 — App 시작 때도 불러 경로가 낡았거나 빠진 등록을 바로잡는다
    internal static void SetStartupRegistryValue(bool enable)
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
