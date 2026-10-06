using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
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
    private readonly HashSet<string> _startupClaudeSlots;
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
        // 왜: 이 VM 이 HotkeyService 보다 먼저 만들어진다 — 설정값으로 줄을 세운다
        RebuildHotkeySlots();
        pinTrayIcon = settings.PinTrayIcon;

        multiccDetected = _multiccDiscovery?.IsDetected ?? false;
        multiccEnabled = settings.MulticcEnabled;
        multiccSelectedProfile = settings.MulticcSelectedProfile;
        multiccProfileNames = _multiccDiscovery?.Profiles.Select(p => p.Name).ToList() ?? [];
        multiccProfileCount = multiccProfileNames.Count;
        // 계약: 시작 때 소스로 등록된 Claude 자리 — 여기 있는 자리는 다시 연동해도 재시작이 필요 없다(App.xaml.cs 의 등록 조건과 같다)
        _startupClaudeSlots = ClaudeProgramRouter.IsActive && _multiccDiscovery is { } startup
            ? startup.Profiles
                .Where(p => settings.MulticcSelectedProfile is null || p.Name.Equals(settings.MulticcSelectedProfile, StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetFullPath(p.ConfigDir).TrimEnd('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        copilotEnabled = settings.CopilotEnabled;
        geminiEnabled = settings.GeminiEnabled;
        _ = LoadCopilotTokenStatusAsync();
        RefreshAccounts();
    }

    [ObservableProperty]
    private string claudeSourceText = string.Empty;

    [ObservableProperty]
    private string codexSourceText = string.Empty;

    // 계약: 이 PC 로그인(IsMain)과 추가 계정이 사용자가 정한 순서(AppSettings.AccountOrder)로 선다
    [ObservableProperty]
    private IReadOnlyList<ExtraAccountRow> claudeAccounts = [];

    [ObservableProperty]
    private IReadOnlyList<ExtraAccountRow> codexAccounts = [];

    [ObservableProperty]
    private string accountsMessage = string.Empty;

    [ObservableProperty]
    private bool accountsRestartPending;

    // 계약: AccountsMessage 가 ✓ 로 시작하면 완료 안내라 초록으로 그린다 — 오류·진행 중 문구는 ✓ 없이 쓴다
    [ObservableProperty]
    private bool accountsMessageIsSuccess;

    partial void OnAccountsMessageChanged(string value)
    {
        AccountsMessageIsSuccess = value.StartsWith('✓');
        OnPropertyChanged(nameof(ShowLinkStrip));
    }

    partial void OnLoginInProgressChanged(bool value) => OnPropertyChanged(nameof(ShowLinkStrip));

    // 계약: 팝업 아래 연동 띠 — 연동 중이거나 결과 문구가 있을 때 보인다
    public bool ShowLinkStrip => LoginInProgress || AccountsMessage.Length > 0;

    // 계약: 유형 칸의 추천 목록 — 화면 언어로 보이고, 자유 입력도 된다
    public IReadOnlyList<string> TypeSuggestions => AccountMeta.SuggestedTypes.Select(t => Loc.T(t)).ToList();

    /// <summary>
    /// 계약: 설정 창이 보일 때마다 불러, 그사이 바뀐 로그인 계정을 다시 읽는다.
    /// </summary>
    public void RefreshAccounts()
    {
        RebuildPalettes();
        RebuildIconOptions();
        RebuildHotkeySlots();
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

        ClaudeAccounts = InOrder(claudeRows);
        ProgramLinks = BuildProgramLinks(ClaudeAccounts);

        var codexRows = new List<ExtraAccountRow> { NewRow(CodexMainId, string.Empty, AccountIdentityReader.ReadCodex(), null) };
        codexRows.AddRange(CodexAccountStore.List()
            .Select(name => NewRow("codex:" + name, name, AccountIdentityReader.ReadCodex(CodexAccountStore.DirOf(name)), CodexAccountStore.DirOf(name))));
        CodexAccounts = InOrder(codexRows);

        // 왜: 실행 한 번에 GitHub 를 한 번만 읽는다 — 설정을 열 때마다 부르면 비로그인 한도(시간당 60회)를 깎는다
        if (Releases.Count == 0 && !_releasesRequested && _updateCoordinator is not null)
        {
            _releasesRequested = true;
            _ = LoadReleasesAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        }
    }

    private bool _releasesRequested;

    // 계약: 이 PC 에서 Claude 를 쓰는 프로그램 — 키는 로그 줄의 entrypoint 값이다
    private static readonly (string Program, string Label)[] ClaudePrograms =
    [
        ("claude-desktop", "Desktop app"),
        ("claude-vscode", "VS Code"),
        ("cli", "Terminal CLI"),
    ];

    [ObservableProperty]
    private IReadOnlyList<ProgramLinkRow> programLinks = [];

    private IReadOnlyList<ProgramLinkRow> BuildProgramLinks(IReadOnlyList<ExtraAccountRow> rows)
    {
        var options = rows.Select(row =>
        {
            var email = row.AccountText.Split(" · ")[0];
            var name = AccountMeta.DisplayNameOf(_settings, row.Id);
            return new ProgramAccountOption(row.Id, email.Contains('@') && !name.Equals(email, StringComparison.OrdinalIgnoreCase) ? $"{name} · {email}" : name);
        }).ToList();

        return ClaudePrograms
            .Select(p => new ProgramLinkRow(p.Program, Loc.T(p.Label), options,
                _settings.ProgramAccounts.GetValueOrDefault(p.Program, ClaudeMainId), OnProgramLinkChanged))
            .ToList();
    }

    private void OnProgramLinkChanged(ProgramLinkRow row)
    {
        if (row.SelectedAccount.Id.Equals(ClaudeMainId, StringComparison.OrdinalIgnoreCase))
        {
            _settings.ProgramAccounts.Remove(row.Program);
        }
        else
        {
            _settings.ProgramAccounts[row.Program] = row.SelectedAccount.Id;
        }

        // 왜: 소스는 갱신마다 연동표를 다시 읽는다 — 재시작 없이 다음 갱신부터 기록의 주인이 바뀐다
        if (ClaudeProgramRouter.IsActive && _multiccDiscovery is { } discovery)
        {
            ClaudeProgramRouter.Configure(ClaudeMainId, _settings.ProgramAccounts, discovery.Profiles);
        }

        _ = SaveSettingsAsync();
        _ = _pulseOrchestrator.RefreshOnceAsync(RefreshTrigger.Manual, CancellationToken.None);
    }

    // 계약: 정한 순서로 줄을 세우고 줄마다 위·아래 이동 가능 여부를 단다
    private List<ExtraAccountRow> InOrder(IEnumerable<ExtraAccountRow> rows)
    {
        var list = AccountMeta.Ordered(_settings, rows, r => r.Id, r => r.DisplayNameText).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            list[i].CanMoveUp = i > 0;
            list[i].CanMoveDown = i < list.Count - 1;
        }

        return list;
    }

    [RelayCommand]
    private void MoveRowUp(ExtraAccountRow? row) => MoveRowBy(row, -1);

    [RelayCommand]
    private void MoveRowDown(ExtraAccountRow? row) => MoveRowBy(row, 1);

    private void MoveRowBy(ExtraAccountRow? row, int delta)
    {
        if (row is not null && RowsOf(row) is { } rows)
        {
            MoveRow(row, rows[Math.Clamp(rows.IndexOf(row) + delta, 0, rows.Count - 1)]);
        }
    }

    /// <summary>끌어서 놓기 — row 를 target 자리로 옮긴다(같은 도구의 줄끼리만).</summary>
    public void MoveRow(ExtraAccountRow row, ExtraAccountRow target)
    {
        if (ReferenceEquals(row, target) || RowsOf(row) is not { } rows || !rows.Contains(target))
        {
            return;
        }

        var reordered = rows.ToList();
        reordered.Remove(row);
        reordered.Insert(rows.IndexOf(target), row);
        AccountMeta.SetOrder(_settings, AccountMeta.KindOf(row.Id), reordered.Select(r => r.Id));
        if (AccountMeta.KindOf(row.Id) == "codex")
        {
            CodexAccounts = InOrder(reordered);
        }
        else
        {
            ClaudeAccounts = InOrder(reordered);
            ProgramLinks = BuildProgramLinks(ClaudeAccounts);
        }

        _ = _settingsStore.SaveAsync(_settings, CancellationToken.None);
        _pulseViewModel?.ApplyAccountOrder();
    }

    private List<ExtraAccountRow>? RowsOf(ExtraAccountRow row) =>
        ClaudeAccounts.Contains(row) ? ClaudeAccounts.ToList() : CodexAccounts.Contains(row) ? CodexAccounts.ToList() : null;

    private ExtraAccountRow NewRow(string id, string name, string account, string? dir)
    {
        var signedIn = IsSignedIn(account);
        var text = Loc.T(account);
        // 왜: Claude 는 메일(.claude.json)만 남고 토큰(.credentials.json)이 없을 수 있다 — 그러면 한도를 못 읽는데 로그인된 것처럼 보였다
        if (signedIn && AccountMeta.KindOf(id) == "claude" && !HasClaudeToken(dir))
        {
            signedIn = false;
            text = account.Split(" · ")[0] + " · " + Loc.T("Signed in, but no token on this PC — sign in again");
        }

        return new(id, name, text, dir, signedIn,
            AccountMeta.DisplayNameOf(_settings, id), AccountMeta.TypeOf(_settings, id), OnRowEdited);
    }

    private static bool HasClaudeToken(string? configDir) => File.Exists(Path.Combine(
        configDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), ".credentials.json"));

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
        if (row is not null)
        {
            LinkSlot(row.Id, row.DisplayNameText, row.ConfigDir);
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

    // 계약: 계정은 「자리」다 — 「계정 추가」는 자리를 하나 만들고 바로 연동(브라우저 로그인)을 띄운다. 연동이 실패해도 자리는 남고 「연동」으로 다시 한다
    // 왜: 어느 계정이 붙는지는 브라우저에서 고른 계정이 정한다 — 메일을 미리 받지 않고, 연동 뒤 실제 메일을 확인해 보인다
    private static string NewSlotName() => "acct-" + DateTime.Now.ToString("yyMMddHHmmss");

    [RelayCommand]
    private void AddAccount()
    {
        var name = NewSlotName();
        var dir = AccountProfileStore.Add(name, Loc.T("New account"));
        RefreshAccounts();
        LinkSlot("claude:" + name, Loc.T("New account"), dir);
    }

    [RelayCommand]
    private void AddCodexAccount()
    {
        var name = NewSlotName();
        var dir = CodexAccountStore.Add(name, Loc.T("New account"));
        RefreshAccounts();
        LinkSlot("codex:" + name, Loc.T("New account"), dir);
    }

    /// <summary>팝업이 부르는 연동 — providerId("claude" · "claude:default" · "claude:&lt;이름&gt;" · "codex" · "codex:&lt;이름&gt;")를 자리로 바꿔 LinkSlot 에 넘긴다.</summary>
    // 계약: 지금 연동 중(또는 결과를 보이는 중)인 자리 — 팝업은 이 자리의 카드 밑에 진행 칸을 펼친다
    [ObservableProperty]
    private string linkingId = string.Empty;

    /// <summary>카드의 providerId 가 지금 연동 중인 자리인가 — "claude" 와 "claude:default" 는 같은 자리다.</summary>
    public bool IsLinkingFor(string providerId)
    {
        static string Norm(string id) => id.Equals("claude", StringComparison.OrdinalIgnoreCase) ? ClaudeMainId : id;
        return ShowLinkStrip && LinkingId.Length > 0 && Norm(providerId).Equals(Norm(LinkingId), StringComparison.OrdinalIgnoreCase);
    }

    public void LinkAccount(string providerId)
    {
        var kind = AccountMeta.KindOf(providerId);
        var name = providerId.Contains(':') ? providerId[(providerId.IndexOf(':') + 1)..] : string.Empty;
        var isMain = name.Length == 0 || name.Equals(AccountProfileStore.DefaultName, StringComparison.OrdinalIgnoreCase);
        var id = isMain ? (kind == "codex" ? CodexMainId : ClaudeMainId) : providerId;
        var dir = isMain ? null : kind == "codex" ? CodexAccountStore.DirOf(name) : Path.Combine(AccountProfileStore.RootDir, name);
        LinkSlot(id, AccountMeta.DisplayNameOf(_settings, id), dir);
    }

    // 계약: 자리 하나에 계정을 연동한다(이미 연동돼 있으면 다시 연동 = 다른 계정으로 바꾸기). 연동 뒤 VerifyLink 가 결과를 확인한다
    private void LinkSlot(string id, string label, string? dir)
    {
        var codex = AccountMeta.KindOf(id) == "codex";
        var exe = codex ? CliLocator.FindCodex() : CliLocator.FindClaude();
        if (exe is null)
        {
            AccountsMessage = Loc.T("{0} CLI was not found on this PC. Install it, then try again.", codex ? "Codex" : "Claude Code");
            return;
        }

        LinkingId = id;
        var before = AccountMeta.MailOf(id);
        _ = RunLoginAsync(label, exe, codex ? "login" : "auth login",
            dir is null ? null : (codex ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR", dir),
            () => codex ? AccountIdentityReader.ReadCodex(dir) : AccountIdentityReader.ReadClaude(dir),
            acceptsCode: !codex,
            verify: account => VerifyLink(id, label, account, codex ? HasCodexToken(dir) : HasClaudeToken(dir), before));
    }

    private static bool HasCodexToken(string? codexHome) => File.Exists(Path.Combine(
        codexHome ?? Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "auth.json"));

    // 계약: 연동 확인 세 가지 — 실제 연동된 메일 · 토큰 저장 · 다른 자리와 같은 메일인가. 통과하면 true(자리는 실패해도 지우지 않는다)
    private bool VerifyLink(string id, string label, string account, bool hasToken, string? before)
    {
        var mail = account.Split(" · ")[0];
        if (!hasToken)
        {
            AccountsMessage = Loc.T("Signed in as {0}, but no token was saved. Link again.", mail);
            return false;
        }

        // 함정: 브라우저가 이미 로그인된 계정으로 바로 넘어가면 다른 자리의 계정이 한 번 더 붙는다
        var kind = AccountMeta.KindOf(id);
        var twin = ClaudeAccounts.Concat(CodexAccounts).FirstOrDefault(r =>
            !r.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && AccountMeta.KindOf(r.Id) == kind &&
            string.Equals(AccountMeta.MailOf(r.Id), mail, StringComparison.OrdinalIgnoreCase));
        if (twin is not null)
        {
            AccountsMessage = Loc.T("{0} is already linked to \"{1}\". Use Relink on this slot and pick another account (the login window helps).", mail, twin.DisplayNameText);
            return false;
        }

        AccountsMessage = before is not null && !before.Equals(mail, StringComparison.OrdinalIgnoreCase)
            ? Loc.T("✓ \"{0}\" relinked: {1} → {2} (token saved).", label, before, mail)
            : Loc.T("✓ \"{0}\" linked to {1} (token saved).", label, mail);
        return true;
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

    private System.Diagnostics.Process? _loginProcess;

    // 계약: 로그인 CLI 가 뒤에서 도는 동안 true — 화면에 취소 버튼(과 코드 입력 칸)이 보인다
    [ObservableProperty]
    private bool loginInProgress;

    // 계약: 브라우저가 코드를 보여 줄 수 있는 로그인(Claude)일 때만 true — Codex 는 브라우저만으로 끝난다
    [ObservableProperty]
    private bool loginAcceptsCode;

    [ObservableProperty]
    private string loginCodeInput = string.Empty;

    // 계약: 로그인 중 CLI 가 내보낸 로그인 페이지 주소 — 브라우저가 안 열렸을 때 화면 링크로 연다
    [ObservableProperty]
    private string loginUrl = string.Empty;

    [ObservableProperty]
    private string loginOutput = string.Empty;

    private static readonly System.Text.RegularExpressions.Regex AnsiEscape = new(@"\x1B\[[0-9;?]*[A-Za-z]");
    private static readonly System.Text.RegularExpressions.Regex UrlPattern = new(@"https://\S+");

    private System.Diagnostics.Process? _loginBrowser;
    private string? _loginBrowserDir;

    // 계약: 연동 전용 Chrome 창 — 로그인 기록이 없는 별도 프로필이라 계정을 새로 고르고, 연동이 끝나면 이 창만 닫는다
    // 왜: 이미 열린 Chrome 의 탭은 다른 프로그램이 안전하게 닫을 수 없다 — 앱이 띄운 별도 프로세스라야 닫을 수 있다
    private bool OpenDedicatedWindow(string url)
    {
        if (FindChrome() is not { } chrome)
        {
            return false;
        }

        try
        {
            CloseLoginBrowser();
            _loginBrowserDir = Path.Combine(Path.GetTempPath(), "AiUsageMonitor-login-" + Guid.NewGuid().ToString("N")[..8]);
            var info = new System.Diagnostics.ProcessStartInfo(chrome) { UseShellExecute = false };
            info.ArgumentList.Add("--user-data-dir=" + _loginBrowserDir);
            info.ArgumentList.Add("--no-first-run");
            info.ArgumentList.Add("--no-default-browser-check");
            info.ArgumentList.Add("--new-window");
            info.ArgumentList.Add(url);
            _loginBrowser = System.Diagnostics.Process.Start(info);
            return _loginBrowser is not null;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LoginOutput = Loc.T("Could not open the browser: {0}", ex.Message);
            return false;
        }
    }

    private void CloseLoginBrowser()
    {
        try
        {
            if (_loginBrowser is { HasExited: false } browser)
            {
                browser.Kill(entireProcessTree: true);
                browser.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 사용자가 먼저 닫았다
        }

        _loginBrowser = null;
        if (_loginBrowserDir is { } dir && Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 함정: 창이 막 닫혀 파일이 잠겨 있을 수 있다 — 임시 폴더라 남아도 해가 없다
            }
        }

        _loginBrowserDir = null;
    }

    // 계약: mode — "dedicated"(연동 전용 Chrome 창 · 끝나면 닫힘) · "default"(기본 브라우저) · "copy"(주소 복사)
    [RelayCommand]
    private void OpenLoginPage(string? mode)
    {
        if (LoginUrl.Length == 0)
        {
            return;
        }

        switch (mode)
        {
            case "copy":
                Clipboard.SetText(LoginUrl);
                LoginOutput = Loc.T("Copied the sign-in address. Paste it into any browser.");
                return;
            case "dedicated":
                if (!OpenDedicatedWindow(LoginUrl))
                {
                    LoginOutput = Loc.T("Chrome was not found. Use Copy address and open a private window yourself.");
                }

                return;
            default:
                OpenInBrowser(LoginUrl);
                return;
        }
    }

    private static string? FindChrome()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key?.GetValue(null) is string path && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LoginOutput = Loc.T("Could not open the browser: {0}", ex.Message);
        }
    }

    private async Task PumpLoginOutputAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } raw)
            {
                var line = AnsiEscape.Replace(raw, string.Empty).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var url = UrlPattern.Match(line);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (url.Success)
                    {
                        // 왜: 창 없이 띄운 CLI 는 브라우저를 열지 않고 주소만 출력한다 — 첫 주소를 앱이 평소 쓰는 기본 브라우저로 연다
                        // 계약: 전용 로그인 창은 자동으로 띄우지 않는다 — 다른 계정이 잡힐 때 「전용 로그인 창」 버튼으로만 연다
                        var first = LoginUrl.Length == 0;
                        LoginUrl = url.Value;
                        if (first)
                        {
                            OpenInBrowser(url.Value);
                        }
                    }
                    else
                    {
                        LoginOutput = line.Length > 160 ? line[..160] + "…" : line;
                    }
                });
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // CLI 가 끝나며 파이프가 닫혔다
        }
    }

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

    private static string SlotKey(string dir) => Path.GetFullPath(dir).TrimEnd('\\', '/');

    // 왜: 전체 갱신은 모든 계정의 로그를 차례로 읽어 수십 초 걸린다 — 그동안 방금 연동한 줄이 「연동 필요」로 남았다
    // 계약: 연동한 계정 하나를 먼저 읽어 한도를 띄우고, 이어서 전체를 갱신한다(다른 갱신이 도는 중이면 앞 단계는 건너뛴다)
    private async Task RefreshAfterLinkAsync(string providerId)
    {
        await _pulseOrchestrator.RefreshProviderAsync(providerId, CancellationToken.None);
        await _pulseOrchestrator.RefreshOnceAsync(RefreshTrigger.Manual, CancellationToken.None);
    }

    // 계약: 연동한 Claude 자리를 재시작 없이 쓰게 한다 — 등록된 자리는 계정 UUID 표만 다시 세우고, 새 자리는 소스를 실행 중에 더한다
    // 왜: fetcher 가 토큰·메일을 갱신마다 파일에서 다시 읽는다 — 소스만 있으면 다음 갱신에 새 계정이 뜬다
    /// <returns>붙인 자리의 providerId. 실행 중에 붙일 수 없으면 null</returns>
    private string? TryAttachClaudeSlot(string dir)
    {
        var key = SlotKey(dir);
        if (_multiccDiscovery is not { } discovery || !key.StartsWith(SlotKey(AccountProfileStore.RootDir), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!_startupClaudeSlots.Contains(key))
        {
            if (!_settings.MulticcEnabled || _settings.MulticcSelectedProfile is not null)
            {
                return null;
            }

            var missing = discovery.Refresh().Where(p => !_startupClaudeSlots.Contains(SlotKey(p.ConfigDir))).ToList();
            if (!missing.Any(p => SlotKey(p.ConfigDir).Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            // 함정: 추가 계정이 하나도 없던 실행은 기본 자리를 ClaudeLogSource("claude") 하나로 읽고 있다 — 자리별 소스로 갈아 끼운다
            var sources = missing.Select(p => new MulticcClaudeLogSource(p)).ToList();
            _pulseOrchestrator.ReplaceSources(sources, _startupClaudeSlots.Count == 0 ? source => source is ClaudeLogSource : null);
            foreach (var source in sources)
            {
                _pulseViewModel?.RegisterSource(source.Profile);
            }

            _startupClaudeSlots.UnionWith(missing.Select(p => SlotKey(p.ConfigDir)));
        }

        ClaudeProgramRouter.Configure(ClaudeMainId, _settings.ProgramAccounts, discovery.Profiles);
        var slot = discovery.Profiles.FirstOrDefault(p => SlotKey(p.ConfigDir).Equals(key, StringComparison.OrdinalIgnoreCase));
        return "claude:" + (slot?.Name ?? Path.GetFileName(key));
    }

    private string? TryAttachCodexSlot(string dir)
    {
        var name = CodexAccountStore.List().FirstOrDefault(n => SlotKey(CodexAccountStore.DirOf(n)).Equals(SlotKey(dir), StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return null;
        }

        // 계약: 같은 providerId 가 이미 있으면 ReplaceSources 가 더하지 않는다 — 등록된 자리의 다시 연동은 갱신만 돈다
        var source = new CodexLogSource(name, CodexAccountStore.DirOf(name));
        _pulseOrchestrator.ReplaceSources([source]);
        _pulseViewModel?.RegisterSource(source.Profile);
        return source.Profile.ProviderId;
    }

    // 계약: 로그인은 공식 CLI 가 연 브라우저에서 사용자가 직접 한다 — 앱은 CLI 를 창 없이 돌리고, 코드가 필요하면 입력 칸의 값을 넘겨 준다
    // 함정: Claude 토큰은 CLAUDE_CONFIG_DIR 아래 .credentials.json 에 남는다 — 앱은 값을 읽지 않고 로그인 여부만 본다
    private async Task RunLoginAsync(string label, string exe, string arguments, (string Name, string Dir)? homeEnv, Func<string> readAccount, bool acceptsCode,
        Func<string, bool> verify)
    {
        // 왜: 멈춘 이전 로그인이 「진행 중」으로 남아 새 연동을 막았다 — 새로 연동하면 이전 것을 끝내고 시작한다
        if (LoginInProgress)
        {
            CancelLogin();
            for (var i = 0; i < 50 && LoginInProgress; i++)
            {
                await Task.Delay(100);
            }
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
                // 함정: 출력을 읽어 주지 않으면 파이프가 차서 CLI 가 멈춘다
                // 왜: 브라우저가 안 열리면 로그인 주소는 CLI 출력에만 있다 — 주소와 마지막 안내 줄을 화면에 올린다
                LoginUrl = string.Empty;
                LoginOutput = string.Empty;
                _ = PumpLoginOutputAsync(process.StandardOutput);
                _ = PumpLoginOutputAsync(process.StandardError);

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
            CloseLoginBrowser();
        }

        RefreshAccounts();
        var account = readAccount();
        if (account is "Not signed in" or "Unable to read account")
        {
            AccountsMessage = Loc.T("Linking \"{0}\" was not completed. Press Link to try again.", label);
            return;
        }

        if (!verify(account))
        {
            return;
        }

        if (homeEnv is null)
        {
            // 왜: 기본 자리의 토큰은 fetcher 가 매번 파일에서 다시 읽는다 — 재시작 없이 갱신만 돌리면 새 계정 사용량이 뜬다
            // 계약: 코드를 받는 로그인은 Claude 다 — 기본 자리의 providerId 는 계정별 보기일 때만 "claude:default" 다
            _ = RefreshAfterLinkAsync(!acceptsCode ? CodexMainId : ClaudeProgramRouter.IsActive ? ClaudeMainId : "claude");
            return;
        }

        if ((TryAttachClaudeSlot(homeEnv.Value.Dir) ?? TryAttachCodexSlot(homeEnv.Value.Dir)) is { } linkedId)
        {
            _ = RefreshAfterLinkAsync(linkedId);
            return;
        }

        // 함정: 실행 중에 붙이지 못한 구성(단일 프로필 모드·함께 보기 꺼짐)은 시작 때만 등록된다 — 그때만 스스로 다시 띄운다
        AccountsMessage += " " + Loc.T("Restarting to show its usage...");
        await Task.Delay(TimeSpan.FromSeconds(2));
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
        RebuildIconOptions();
        TrayIconRenderer.NotifyChanged();
    }

    // 계약: 「아이콘」 탭 — 기본 모양 4종은 팔레트 색으로 그리고, 불러온 그림·직접 그린 그림은 있을 때만 보인다
    [ObservableProperty]
    private IReadOnlyList<TrayIconOption> iconOptions = [];

    [ObservableProperty]
    private string iconMessage = string.Empty;

    private void RebuildIconOptions()
    {
        var palette = ThemeManager.Palette;
        var options = TrayIconRenderer.Presets
            .Select(style => new TrayIconOption(style, Loc.T(PresetLabelOf(style)), TrayIconRenderer.Preview(style, palette, 32),
                style == _settings.TrayIconStyle, IsCustom: false, IsAction: false))
            .ToList();

        int drawings = 0, images = 0;
        foreach (var style in TrayIconRenderer.CustomStyles())
        {
            var label = TrayIconRenderer.IsDrawing(style) ? Loc.T("Drawing {0}", ++drawings) : Loc.T("Image {0}", ++images);
            options.Add(new TrayIconOption(style, label, TrayIconRenderer.Preview(style, palette, 32),
                style == _settings.TrayIconStyle, IsCustom: true, IsAction: false));
        }

        options.Add(new TrayIconOption(DrawAction, Loc.T("+ Draw"), null, false, IsCustom: false, IsAction: true));
        options.Add(new TrayIconOption(ImportAction, Loc.T("+ Image"), null, false, IsCustom: false, IsAction: true));
        IconOptions = options;
    }

    private const string DrawAction = "+draw";
    private const string ImportAction = "+image";

    // 계약: 모양 id 「j」는 기본값이고 화면 이름은 JHJ 다
    private static string PresetLabelOf(string style) => style switch
    {
        "bars" => "Bars",
        "ring" => "Ring",
        "spark" => "Sparkle",
        _ => "JHJ",
    };

    [RelayCommand]
    private void SelectTrayIcon(string? style)
    {
        switch (style)
        {
            case DrawAction:
                OpenIconEditor();
                return;
            case ImportAction:
                ImportTrayIcon();
                return;
        }

        _settings.TrayIconStyle = string.IsNullOrEmpty(style) ? "j" : style;
        _ = SaveSettingsAsync();
        RebuildIconOptions();
        TrayIconRenderer.NotifyChanged();
    }

    [RelayCommand]
    private void DeleteTrayIcon(string? style)
    {
        if (!TrayIconRenderer.IsCustom(style))
        {
            return;
        }

        try
        {
            TrayIconRenderer.Delete(style!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IconMessage = Loc.T("Could not delete that icon: {0}", ex.Message);
            return;
        }

        if (style == _settings.TrayIconStyle)
        {
            SelectTrayIcon("j");
            return;
        }

        RebuildIconOptions();
    }

    private void ImportTrayIcon()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("Images") + " (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var style = TrayIconRenderer.ImportFile(dialog.FileName);
            IconMessage = string.Empty;
            SelectTrayIcon(style);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or OutOfMemoryException)
        {
            // 함정: GDI+ 는 그림이 아닌 파일을 OutOfMemoryException 으로 알린다
            IconMessage = Loc.T("Could not read that image: {0}", ex.Message);
        }
    }

    [ObservableProperty]
    private bool isIconEditorOpen;

    public System.Collections.ObjectModel.ObservableCollection<IconPixel> IconPixels { get; } = [];

    [ObservableProperty]
    private IReadOnlyList<IconPen> iconPens = [];

    private System.Drawing.Color _penColor;

    private void RebuildPens()
    {
        var c = TrayIconRenderer.ColorsOf(ThemeManager.Palette);
        (string Label, System.Drawing.Color Color)[] pens =
        [
            ("Letter", c[2]), ("Accent", c[3]), ("Background top", c[0]), ("Background bottom", c[1]),
            ("Black", System.Drawing.Color.Black), ("Eraser", System.Drawing.Color.Transparent),
        ];
        if (_penColor.IsEmpty)
        {
            _penColor = c[2];
        }

        IconPens = pens.Select(p => new IconPen(Loc.T(p.Label), p.Color, ToBrush(p.Color), p.Color.ToArgb() == _penColor.ToArgb())).ToList();
    }

    private static System.Windows.Media.Brush ToBrush(System.Drawing.Color c)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private void LoadCanvas(string? style)
    {
        var pixels = TrayIconRenderer.PixelsOf(style, ThemeManager.Palette);
        if (IconPixels.Count != pixels.Length)
        {
            IconPixels.Clear();
            foreach (var color in pixels)
            {
                IconPixels.Add(new IconPixel(color));
            }

            return;
        }

        for (var i = 0; i < pixels.Length; i++)
        {
            IconPixels[i].SetColor(pixels[i]);
        }
    }

    // 계약: 그리기는 언제나 새 아이콘을 하나 더한다 — 바탕만 깔린 칸에서 시작하고, 기존 아이콘은 고치지 않는다
    private void OpenIconEditor()
    {
        LoadCanvas(TrayIconRenderer.TileStyle);
        RebuildPens();
        IconMessage = string.Empty;
        IsIconEditorOpen = true;
    }

    [RelayCommand]
    private void SelectIconPen(IconPen? pen)
    {
        if (pen is not null)
        {
            _penColor = pen.Color;
            RebuildPens();
        }
    }

    /// <summary>칸 하나를 지금 붓 색으로 칠한다 — 화면 코드가 누르기·끌기에서 부른다.</summary>
    public void PaintPixel(IconPixel pixel) => pixel.SetColor(_penColor);

    [RelayCommand]
    private void ResetIconCanvas(string? style) => LoadCanvas(style);

    [RelayCommand]
    private void ClearIconCanvas()
    {
        foreach (var pixel in IconPixels)
        {
            pixel.SetColor(System.Drawing.Color.Transparent);
        }
    }

    [RelayCommand]
    private void SaveIconDrawing()
    {
        try
        {
            var style = TrayIconRenderer.SaveDrawing(IconPixels.Select(p => p.Color).ToList());
            IsIconEditorOpen = false;
            IconMessage = string.Empty;
            SelectTrayIcon(style);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            IconMessage = Loc.T("Could not save the drawing: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private void CancelIconEditor() => IsIconEditorOpen = false;

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

    // 규칙(막는 키·권장 키)은 Services/HotkeyRules.cs
    [ObservableProperty]
    private IReadOnlyList<HotkeySlotRow> hotkeySlots = [];

    public bool CanAddHotkey => HotkeySlots.Count < HotkeyService.MaxCount && HotkeySlots.All(r => r.Text.Length > 0);

    partial void OnHotkeySlotsChanged(IReadOnlyList<HotkeySlotRow> value) => OnPropertyChanged(nameof(CanAddHotkey));

    // 왜: 이 VM 이 HotkeyService 보다 먼저 만들어진다 — 처음엔 설정값으로, 입력란을 떠날 때 실제 등록값으로 다시 세운다
    private void RebuildHotkeySlots(IEnumerable<string>? texts = null)
    {
        var list = (texts ?? HotkeyService.Current?.Texts ?? new[] { _settings.Hotkey }.Concat(_settings.ExtraHotkeys)).ToList();
        if (list.Count == 0)
        {
            list.Add(HotkeyRules.Default);
        }

        HotkeySlots = list.Select((t, i) => new HotkeySlotRow(i, t, OnHotkeySlotButton)).ToList();
    }

    [RelayCommand]
    private void AddHotkey()
    {
        if (CanAddHotkey)
        {
            HotkeySlots = [.. HotkeySlots, new HotkeySlotRow(HotkeySlots.Count, string.Empty, OnHotkeySlotButton)];
        }
    }

    /// <summary>입력란에서 누른 키. 수정키만 눌린 동안은 미리보기만 하고 적용하지 않는다.</summary>
    public void CaptureHotkey(HotkeySlotRow row, Key key, ModifierKeys modifiers)
    {
        if (HotkeyRules.IsModifierKey(key))
        {
            row.Text = HotkeyRules.Format(Key.None, modifiers) + "+…";
            return;
        }

        ApplyHotkey(row, key, modifiers);
    }

    public void BeginHotkeyCapture() => HotkeyService.Current?.Suspend();

    // 계약: 키를 못 받은 새 줄은 이때 사라진다
    public void EndHotkeyCapture()
    {
        if (HotkeyService.Current is not { } service)
        {
            return;
        }

        service.Resume();
        var texts = service.Texts;
        foreach (var row in HotkeySlots)
        {
            if (row.Index < texts.Count)
            {
                row.Text = texts[row.Index];
            }
        }

        if (HotkeySlots.Count > texts.Count)
        {
            HotkeySlots = HotkeySlots.Take(texts.Count).ToList();
        }
    }

    private void OnHotkeySlotButton(HotkeySlotRow row)
    {
        if (row.Index == 0)
        {
            HotkeyRules.TryParse(HotkeyRules.Default, out var key, out var modifiers);
            ApplyHotkey(row, key, modifiers);
            return;
        }

        HotkeyService.Current?.RemoveAt(row.Index);
        var texts = HotkeySlots.Where(r => r != row && r.Text.Length > 0 && !r.Text.EndsWith('…')).Select(r => r.Text).ToList();
        RebuildHotkeySlots(HotkeyService.Current?.Texts ?? texts);
        SaveHotkeys();
    }

    private void ApplyHotkey(HotkeySlotRow row, Key key, ModifierKeys modifiers)
    {
        var (verdict, message) = HotkeyRules.Check(key, modifiers);
        var text = HotkeyRules.Format(key, modifiers);
        var service = HotkeyService.Current;
        var texts = service?.Texts ?? [];
        var registered = row.Index < texts.Count ? texts[row.Index] : string.Empty;

        if (verdict != HotkeyVerdict.Blocked && texts.Select((t, i) => (t, i)).Any(x => x.i != row.Index && x.t == text))
        {
            (verdict, message) = (HotkeyVerdict.Blocked, "Already set as another popup shortcut.");
        }
        else if (verdict != HotkeyVerdict.Blocked && service is not null && !service.TrySet(Math.Min(row.Index, texts.Count), key, modifiers))
        {
            (verdict, message) = (HotkeyVerdict.Blocked, "Another program already uses this shortcut. Pick another one.");
        }

        row.Verdict = verdict;
        if (verdict == HotkeyVerdict.Blocked)
        {
            row.Message = $"{text} — {Loc.T(message)}";
            row.Text = registered;
            return;
        }

        row.Text = text;
        row.Message = $"✓ {text} — {Loc.T(message)}";
        OnPropertyChanged(nameof(CanAddHotkey));
        SaveHotkeys();
    }

    private void SaveHotkeys()
    {
        var texts = HotkeyService.Current?.Texts ?? HotkeySlots.Select(r => r.Text).Where(t => t.Length > 0).ToList();
        _settings.Hotkey = texts.FirstOrDefault() ?? HotkeyRules.Default;
        _settings.ExtraHotkeys = texts.Skip(1).ToList();
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
        UpdateStatusText = Loc.T("Downloading {0}...", "v" + StartupUpdateCoordinator.Display(release.Version));
        try
        {
            var result = await Task.Run(() => _updateCoordinator.StageReleaseAsync(release.Version, cts.Token), cts.Token);
            if (result == UpdateCheckResult.UpdateStaged &&
                await Task.Run(() => _updateCoordinator.TryApplyPendingUpdateAsync(cts.Token, manualTrigger: true), cts.Token))
            {
                UpdateStatusText = Loc.T("Installing {0}. The app will restart.", "v" + StartupUpdateCoordinator.Display(release.Version));
                _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown(0));
                return;
            }

            UpdateStatusText = Loc.T("Could not install {0}.", "v" + StartupUpdateCoordinator.Display(release.Version));
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
