using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using costats.App.Localization;
using costats.App.Services;
using costats.Application.Pulse;
using costats.Application.Settings;
using costats.Core.Pulse;

namespace costats.App.ViewModels;

public sealed partial class PulseViewModel : ObservableObject, IObserver<PulseState>, IDisposable
{
    private readonly IPulseOrchestrator _orchestrator;
    private readonly AppSettings _settings;
    private readonly IDisposable _subscription;
    private Dictionary<string, string> _displayNames;

    // 계약: 실행 중에 더한 계정 소스의 표시 이름을 알린다 — 사전을 통째로 바꿔 끼워 읽는 쪽과 부딪히지 않는다
    public void RegisterSource(ProviderProfile profile) =>
        _displayNames = new Dictionary<string, string>(_displayNames, StringComparer.OrdinalIgnoreCase) { [profile.ProviderId] = profile.DisplayName };

    private readonly ISettingsStore _settingsStore;

    // 계약: 접힘 상태는 계정 VM 이 아니라 여기에 둔다 — 계정 VM 은 갱신마다 새로 만들어져 상태를 못 지킨다
    [ObservableProperty]
    private bool isChartExpanded;

    [ObservableProperty]
    private bool isModelsExpanded;

    [ObservableProperty]
    private bool isTokenTypesExpanded;

    partial void OnIsChartExpandedChanged(bool value) => SaveSection("chart", value);

    partial void OnIsModelsExpandedChanged(bool value) => SaveSection("models", value);

    partial void OnIsTokenTypesExpandedChanged(bool value) => SaveSection("tokenTypes", value);

    private void SaveSection(string name, bool expanded)
    {
        _settings.CollapsedSections.Remove(name);
        if (!expanded)
        {
            _settings.CollapsedSections.Add(name);
        }

        _ = _settingsStore.SaveAsync(_settings, CancellationToken.None);
    }

    public PulseViewModel(IPulseOrchestrator orchestrator, AppSettings settings, IEnumerable<ISignalSource> sources, ISettingsStore settingsStore)
    {
        _orchestrator = orchestrator;
        _settings = settings;
        _settingsStore = settingsStore;
        isChartExpanded = !settings.CollapsedSections.Contains("chart");
        isModelsExpanded = !settings.CollapsedSections.Contains("models");
        isTokenTypesExpanded = !settings.CollapsedSections.Contains("tokenTypes");
        isCopilotEnabled = settings.CopilotEnabled;
        isGeminiEnabled = settings.GeminiEnabled;
        _pendingClaudeDefault = AccountMeta.DefaultIdOf(settings, "claude");
        _pendingCodexDefault = AccountMeta.DefaultIdOf(settings, "codex");
        _displayNames = sources
            .Select(source => source.Profile)
            .GroupBy(profile => profile.ProviderId)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);

        Providers = new ObservableCollection<ProviderPulseViewModel>();
        _subscription = orchestrator.PulseStream.Subscribe(this);
        Loc.LanguageChanged += OnLanguageChanged;

        // 왜: 남은 시간은 초 단위로 줄어든다 — 갱신 주기와 따로 1초마다 문구만 다시 쓴다
        _nextRefreshTicker = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => UpdateNextRefreshText(),
            System.Windows.Application.Current.Dispatcher);
    }

    private readonly System.Windows.Threading.DispatcherTimer _nextRefreshTicker;

    // 계약: "다음 갱신 10:45 · 4:12 후" — 예약이 아직 없으면 빈 문자열
    [ObservableProperty]
    private string nextRefreshText = string.Empty;

    // 계약: 쌓기 카드 요약 띠용 짧은 판 — "다음 16:50 · 2:13 후"
    [ObservableProperty]
    private string nextRefreshShortText = string.Empty;

    private void UpdateNextRefreshText()
    {
        if (_orchestrator.NextRefreshAt is not { } next)
        {
            NextRefreshText = string.Empty;
            NextRefreshShortText = string.Empty;
            return;
        }

        var left = next - DateTimeOffset.Now;
        if (left < TimeSpan.Zero)
        {
            left = TimeSpan.Zero;
        }

        var countdown = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        NextRefreshText = Loc.T("Next refresh {0} · in {1}", next.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), countdown);
        NextRefreshShortText = Loc.T("Next {0} · in {1}", next.ToLocalTime().ToString("HH:mm"), countdown);
    }

    // 왜: 칩 라벨과 계정 VM 의 문장은 만들 때의 언어로 굳는다 — 언어가 바뀌면 다시 만들어야 한다
    private void OnLanguageChanged()
    {
        RangeChips.Clear();
        foreach (var days in ProviderPulseViewModel.RangeChoices)
        {
            RangeChips.Add(new AccountChip(days.ToString(), days == 365 ? Loc.T("1y") : Loc.T("{0}d", days))
            {
                IsSelected = days == ProviderPulseViewModel.RangeDays
            });
        }

        if (_lastState is not null)
        {
            OnNext(_lastState);
        }
    }

    public ObservableCollection<ProviderPulseViewModel> Providers { get; }

    [ObservableProperty]
    private string lastUpdated = "Never";

    // 계약: 카드 밑 연동 진행 칸(LinkProgressView)의 DataContext — 팝업 창이 SettingsViewModel 을 넣는다
    [ObservableProperty]
    private object? linkContext;

    [ObservableProperty]
    private ProviderPulseViewModel claude = new();

    [ObservableProperty]
    private ProviderPulseViewModel codex = new();

    [ObservableProperty]
    private ProviderPulseViewModel copilot = new();

    [ObservableProperty]
    private ProviderPulseViewModel gemini = new();

    [ObservableProperty]
    private string updatedLabel = "Updated never";

    [ObservableProperty]
    // 왜: 탭 번호(0=Codex, 1=Claude)는 원본 그대로 두고 초기 선택만 Claude 로 바꾼다
    private int selectedTabIndex = 1;

    [ObservableProperty]
    private bool isRefreshing = true; // Start true to show spinner on initial load

    [ObservableProperty]
    private bool isMulticcActive;

    [ObservableProperty]
    private bool isCopilotEnabled;

    // 계약: 탭 번호는 0 Codex · 1 Claude · 2 Copilot · 3 Gemini 로 고정이다 — 꺼진 탭은 폭 0 으로 숨긴다
    [ObservableProperty]
    private bool isGeminiEnabled;

    [ObservableProperty]
    private string multiccSummary = string.Empty;

    // Aggregate cost/token totals across all multicc profiles
    [ObservableProperty]
    private string multiccTotalTodayCost = "--";

    [ObservableProperty]
    private string multiccTotalTodayTokens = "--";

    [ObservableProperty]
    private string multiccTotalWeekCost = "--";

    [ObservableProperty]
    private string multiccTotalWeekTokens = "--";

    [ObservableProperty]
    private bool hasMulticcTotals;

    public ObservableCollection<ProviderPulseViewModel> ClaudeProfiles { get; } = new();

    // 계약: 첫 칩은 "All"(Id=null, 쌓아 보기)이고 나머지는 계정마다 하나다
    public ObservableCollection<AccountChip> ClaudeAccountChips { get; } = new();

    // 계약: null 이면 전 계정을 쌓아 보이고, 값이 있으면 그 계정(providerId)만 상세로 보인다
    [ObservableProperty]
    private string? selectedClaudeAccountId;

    /// <summary>
    /// 계정이 여럿이고 "All" 이 선택돼 있을 때만 쌓아 보기 패널을 쓴다.
    /// </summary>
    public bool ShowClaudeStacked => IsMulticcActive && SelectedClaudeAccountId is null;

    // 계약: Claude 탭에서 계정 카드를 쌓아 보이는 중인지 — 통계 구역 위에 「어느 계정의 통계인지」를 적을 때 쓴다
    public bool IsStackedClaudeView => ShowClaudeStacked && SelectedTabIndex == 1;

    // 계약: 「전체」의 통계 구역은 모든 계정을 (날짜, 모델)로 합친 판이다 — 계정이 하나면 그 계정 그대로
    private static ProviderPulseViewModel AllStats(IReadOnlyList<ProviderPulseViewModel> profiles) =>
        profiles.Count == 1 ? profiles[0] : ProviderPulseViewModel.Combine(profiles, Loc.T("All"), "claude:*all");

    /// <summary>
    /// 계약: 제목 줄의 계정 드롭다운은 Claude 탭에서 계정이 여럿일 때만 열린다.
    /// </summary>
    public bool CanSwitchAccount => SelectedTabIndex is 0 or 1;

    private const string CodexMainId = "codex";

    // 계약: 키는 providerId("codex" 또는 "codex:<이름>") — Codex 탭은 이 중 선택된 계정 하나만 보인다
    private readonly Dictionary<string, ProviderPulseViewModel> _codexAccounts = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<AccountChip> CodexAccountChips { get; } = new();

    [ObservableProperty]
    private string selectedCodexAccountId = CodexMainId;

    // 계약: 제목 줄 드롭다운은 지금 탭의 도구 계정만 보인다 — 도구마다 계정을 따로 고른다
    public ObservableCollection<AccountChip> ActiveAccountChips => SelectedTabIndex == 0 ? CodexAccountChips : ClaudeAccountChips;

    // 함정: 첫 갱신 전에는 계정 칩이 없다 — 고를 칩이 생길 때까지 기본 계정을 들고 있다가 동기화 뒤에 고른다
    private string? _pendingClaudeDefault;
    private string? _pendingCodexDefault;

    // 계약: 팝업이 새로 열릴 때 부른다 — 유형이 「기본」인 계정이 없으면 지금 선택을 그대로 둔다
    public void SelectDefaultAccounts()
    {
        _pendingClaudeDefault = AccountMeta.DefaultIdOf(_settings, "claude");
        _pendingCodexDefault = AccountMeta.DefaultIdOf(_settings, "codex");
        ApplyPendingDefaults();
    }

    // 계약: 설정에서 계정 명칭·유형을 고친 뒤 부른다 — 칩 라벨·제목을 다시 만들고 기본 계정을 다시 고른다
    public void ApplyAccountMeta()
    {
        if (_lastState is not null)
        {
            OnNext(_lastState);
        }

        SelectDefaultAccounts();
    }

    // 계약: 설정에서 계정 순서를 바꾼 뒤 부른다 — 칩·카드만 다시 세우고 지금 고른 계정은 그대로 둔다
    public void ApplyAccountOrder()
    {
        if (_lastState is not null)
        {
            OnNext(_lastState);
        }
    }

    private void ApplyPendingDefaults()
    {
        if (_pendingClaudeDefault is { } claudeId &&
            ClaudeAccountChips.FirstOrDefault(c => string.Equals(c.Id, claudeId, StringComparison.OrdinalIgnoreCase)) is { } claudeChip)
        {
            _pendingClaudeDefault = null;
            SelectedClaudeAccountId = claudeChip.Id;
        }

        if (_pendingCodexDefault is { } codexId &&
            CodexAccountChips.FirstOrDefault(c => string.Equals(c.Id, codexId, StringComparison.OrdinalIgnoreCase)) is { Id: { } codexChipId })
        {
            _pendingCodexDefault = null;
            SelectedCodexAccountId = codexChipId;
        }
    }

    [RelayCommand]
    private void SelectAccount(string? providerId)
    {
        if (SelectedTabIndex == 0)
        {
            SelectedCodexAccountId = providerId ?? CodexMainId;
        }
        else
        {
            SelectedClaudeAccountId = providerId;
        }
    }

    partial void OnSelectedCodexAccountIdChanged(string value)
    {
        ApplySelectedCodexAccount();
        RefreshActiveAccount();
        foreach (var chip in CodexAccountChips)
        {
            chip.IsSelected = chip.Id == value;
        }
    }

    private void ApplySelectedCodexAccount()
    {
        if (_codexAccounts.TryGetValue(SelectedCodexAccountId, out var selected) ||
            _codexAccounts.TryGetValue(CodexMainId, out selected))
        {
            Codex = selected;
            OnPropertyChanged(nameof(SelectedProvider));
            OnPropertyChanged(nameof(SelectedProviderId));
        }
    }

    private void SyncCodexAccountChips()
    {
        var ids = AccountMeta.Ordered(_settings, _codexAccounts.Keys.Append(CodexMainId).Distinct(StringComparer.OrdinalIgnoreCase),
            id => id, id => AccountMeta.DisplayNameOf(_settings, id));
        var wanted = ids
            .Select(id => (Id: id, Label: AccountMeta.DisplayNameOf(_settings, id),
                Detail: WithType(id, ShortAccount(AccountIdentityReader.ReadCodex(CodexDirOf(id))))))
            .ToList();

        if (wanted.SequenceEqual(CodexAccountChips.Select(c => (c.Id!, c.Label, c.Detail))))
        {
            return;
        }

        if (wanted.All(w => w.Id != SelectedCodexAccountId))
        {
            SelectedCodexAccountId = CodexMainId;
        }

        CodexAccountChips.Clear();
        foreach (var (id, label, detail) in wanted)
        {
            CodexAccountChips.Add(new AccountChip(id, label, detail) { IsSelected = id == SelectedCodexAccountId });
        }
    }

    private static string CodexNameOf(string providerId) => providerId[(providerId.IndexOf(':') + 1)..];

    // 계약: "codex" 는 이 PC 의 기본 로그인(null), "codex:<이름>" 은 CodexAccountStore 가 만든 폴더다
    private static string? CodexDirOf(string providerId) =>
        providerId.Contains(':') ? CodexAccountStore.DirOf(CodexNameOf(providerId)) : null;

    partial void OnIsMulticcActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowClaudeStacked));
        OnPropertyChanged(nameof(IsStackedClaudeView));
        OnPropertyChanged(nameof(CanSwitchAccount));
    }

    partial void OnSelectedClaudeAccountIdChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowClaudeStacked));
        OnPropertyChanged(nameof(IsStackedClaudeView));
        ApplySelectedClaudeAccount();
        RefreshActiveAccount();
        foreach (var chip in ClaudeAccountChips)
        {
            chip.IsSelected = chip.Id == value;
        }
    }

    [RelayCommand]
    private void SelectClaudeAccount(string? providerId)
    {
        SelectedClaudeAccountId = providerId;
    }

    private void ApplySelectedClaudeAccount()
    {
        if (!IsMulticcActive || ClaudeProfiles.Count == 0)
        {
            return;
        }

        Claude = ClaudeProfiles.FirstOrDefault(p => p.ProviderId == SelectedClaudeAccountId) ?? AllStats(ClaudeProfiles);
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(SelectedProviderId));
    }

    private void SyncClaudeAccountChips()
    {
        // 왜: 계정이 하나뿐이어도 드롭다운을 열 수 있어야 "Add account" 로 가는 길이 보인다
        var wanted = new List<(string? Id, string Label, string Detail)>
        {
            (null, Loc.T(ClaudeProfiles.Count == 0 ? "This PC" : "All"),
                ClaudeProfiles.Count == 0 ? ShortAccount(AccountIdentityReader.ReadClaude()) : Loc.T("Every account, stacked"))
        };
        wanted.AddRange(ClaudeProfiles
            .Select(p => ((string?)p.ProviderId, p.DisplayName,
                WithType(p.ProviderId, ShortAccount(AccountIdentityReader.ReadClaude(AccountDirOf(p.ProviderId)))))));

        // 왜: 갱신마다 칩을 새로 만들면 깜빡이고 마우스 오버가 풀린다 — 목록이 달라졌을 때만 다시 만든다
        if (wanted.SequenceEqual(ClaudeAccountChips.Select(c => (c.Id, c.Label, c.Detail))))
        {
            return;
        }

        if (SelectedClaudeAccountId is not null && wanted.All(w => w.Id != SelectedClaudeAccountId))
        {
            SelectedClaudeAccountId = null;
        }

        ClaudeAccountChips.Clear();
        foreach (var (id, label, detail) in wanted)
        {
            ClaudeAccountChips.Add(new AccountChip(id, label, detail) { IsSelected = id == SelectedClaudeAccountId });
        }
    }

    // 계약: 드롭다운 보조 줄은 「유형 · 메일」 — 유형이 비어 있으면 메일만
    private string WithType(string id, string account) =>
        AccountMeta.TypeOf(_settings, id) is { Length: > 0 } type ? $"{type} · {account}" : account;

    /// <summary>
    /// Returns the currently selected provider based on tab index.
    /// </summary>
    public ProviderPulseViewModel SelectedProvider => SelectedTabIndex switch
    {
        0 => Codex,
        1 => Claude,
        3 => IsGeminiEnabled ? Gemini : Codex,
        _ => IsCopilotEnabled ? Copilot : Codex
    };

    /// <summary>
    /// Returns the provider ID for the currently selected tab.
    /// </summary>
    public string SelectedProviderId
    {
        get
        {
            if (SelectedTabIndex == 0)
                return _codexAccounts.ContainsKey(SelectedCodexAccountId) ? SelectedCodexAccountId : CodexMainId;

            if (SelectedTabIndex == 1)
            {
                // For multicc, return the first (worst-case) profile's ID for targeted refresh
                if (IsMulticcActive && ClaudeProfiles.Count > 0)
                    return SelectedClaudeAccountId ?? ClaudeProfiles[0].ProviderId;

                return "claude";
            }

            if (SelectedTabIndex == 3)
                return IsGeminiEnabled ? "gemini" : "codex";

            return IsCopilotEnabled ? "copilot" : "codex";
        }
    }

    // 계약: 제목 줄 오른쪽에 보이는, 지금 탭·계정에 연동된 로그인 계정의 메일 주소
    [ObservableProperty]
    private string activeAccountText = string.Empty;

    public void RefreshActiveAccount()
    {
        ActiveAccountText = SelectedTabIndex switch
        {
            0 => ShortAccount(AccountIdentityReader.ReadCodex(CodexDirOf(SelectedCodexAccountId))),
            1 when IsMulticcActive && SelectedClaudeAccountId is null => Loc.T("All · {0} accounts", ClaudeProfiles.Count),
            1 when IsMulticcActive => ShortAccount(AccountIdentityReader.ReadClaude(AccountDirOf(SelectedClaudeAccountId!))),
            1 => ShortAccount(AccountIdentityReader.ReadClaude()),
            3 => ShortAccount(AccountIdentityReader.ReadGemini()),
            _ => string.Empty
        };
    }

    // 왜: 제목 줄이 좁아 조직명까지는 못 싣는다 — "메일 · 조직" 에서 메일만 남긴다
    private static string ShortAccount(string text) => Loc.T(text.Split(" · ")[0]);

    // 계약: "claude:default" 는 이 PC 의 기본 로그인(null), 나머지는 AccountProfileStore 가 만든 폴더다
    private static string? AccountDirOf(string providerId)
    {
        var name = providerId[(providerId.IndexOf(':') + 1)..];
        return name.Equals(AccountProfileStore.DefaultName, StringComparison.OrdinalIgnoreCase)
            ? null
            : System.IO.Path.Combine(AccountProfileStore.RootDir, name);
    }

    // 계약: 차트·모델 비중이 보는 기간 선택 칩(7 · 14 · 30일). 선택은 이번 실행 동안만 유지된다
    public ObservableCollection<AccountChip> RangeChips { get; } = new(
        ProviderPulseViewModel.RangeChoices.Select(days =>
            new AccountChip(days.ToString(), days == 365 ? Loc.T("1y") : Loc.T("{0}d", days)) { IsSelected = days == ProviderPulseViewModel.RangeDays }));

    private PulseState? _lastState;

    [RelayCommand]
    private void SelectRange(string? days)
    {
        if (!int.TryParse(days, out var value) || value == ProviderPulseViewModel.RangeDays)
        {
            return;
        }

        ProviderPulseViewModel.RangeDays = value;
        foreach (var chip in RangeChips)
        {
            chip.IsSelected = chip.Id == days;
        }

        // 왜: 계정 VM 은 갱신 때만 새로 만들어진다 — 마지막 상태를 다시 흘려 새 기간으로 즉시 다시 그린다
        if (_lastState is not null)
        {
            OnNext(_lastState);
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsStackedClaudeView));
        RefreshActiveAccount();
        OnPropertyChanged(nameof(CanSwitchAccount));
        OnPropertyChanged(nameof(ActiveAccountChips));
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(SelectedProviderId));
    }

    /// <summary>
    /// Silently refresh the currently selected provider (no loading indicator).
    /// </summary>
    public async Task RefreshSelectedProviderSilentlyAsync()
    {
        try
        {
            await _orchestrator.RefreshProviderAsync(SelectedProviderId, CancellationToken.None);
        }
        catch
        {
            // Silent refresh failures are non-blocking
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Show loading indicator immediately for responsive UX
        IsRefreshing = true;
        try
        {
            await _orchestrator.RefreshOnceAsync(RefreshTrigger.Manual, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Log but don't crash - refresh failures should not take down the app
            System.Diagnostics.Debug.WriteLine($"Refresh failed: {ex.Message}");
        }
        finally
        {
            // Ensure loading indicator is hidden even if orchestrator doesn't publish
            IsRefreshing = false;
        }
    }

    public void OnNext(PulseState value)
    {
        _lastState = value;
        // Use BeginInvoke (async) instead of Invoke to avoid blocking the UI thread
        // This allows window deactivation to work even during data updates
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsCopilotEnabled = _settings.CopilotEnabled;
            IsGeminiEnabled = _settings.GeminiEnabled;
            if ((!IsCopilotEnabled && SelectedTabIndex == 2) || (!IsGeminiEnabled && SelectedTabIndex == 3))
            {
                SelectedTabIndex = 1;
            }

            IsRefreshing = value.IsRefreshing;

            // Only update provider data if we have providers (keep last state during refresh)
            if (value.Providers.Count > 0)
            {
                // ── Build all data in local variables first (no UI mutations yet) ──
                var newProviders = new List<ProviderPulseViewModel>();
                var claudeProfileList = new List<ProviderPulseViewModel>();
                var codexAccounts = new Dictionary<string, ProviderPulseViewModel>(StringComparer.OrdinalIgnoreCase);
                ProviderPulseViewModel? newCodex = null;
                ProviderPulseViewModel? newClaude = null;
                ProviderPulseViewModel? newCopilot = null;
                ProviderPulseViewModel? newGemini = null;

                // Aggregate cost/token totals across multicc profiles
                decimal totalTodayCost = 0;
                long totalTodayTokens = 0;
                decimal totalWeekCost = 0;
                long totalWeekTokens = 0;
                var today = DateOnly.FromDateTime(DateTime.Now);
                // 왜: 달력 주(월~일)로 세면 월요일에는 「오늘」과 같은 값이 된다 — 오늘 포함 최근 7일로 센다
                var weekStart = today.AddDays(-6);

                foreach (var (providerId, reading) in value.Providers)
                {
                    // 왜: 계정 줄의 제목은 사용자가 정한 명칭이다 — 폴더 이름("jhj-atisys-co-kr")을 보이지 않는다
                    var displayName = providerId.Contains(':') ? AccountMeta.DisplayNameOf(_settings, providerId)
                        : _displayNames.TryGetValue(providerId, out var name) ? name : providerId;
                    var vm = ProviderPulseViewModel.FromReading(reading, displayName, providerId);
                    if (vm.ProviderKind is "claude" or "codex")
                    {
                        var metaId = providerId.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "claude:default" : providerId;
                        vm.AccountType = AccountMeta.TypeOf(_settings, metaId);
                        vm.AccountEmail = AccountMeta.MailOf(metaId) ?? string.Empty;
                    }

                    if ((providerId.Equals("copilot", StringComparison.OrdinalIgnoreCase) && !IsCopilotEnabled) ||
                        (providerId.Equals("gemini", StringComparison.OrdinalIgnoreCase) && !IsGeminiEnabled))
                    {
                        continue;
                    }

                    newProviders.Add(vm);

                    if (providerId.Equals("codex", StringComparison.OrdinalIgnoreCase) ||
                        providerId.StartsWith("codex:", StringComparison.OrdinalIgnoreCase))
                    {
                        codexAccounts[providerId] = vm;
                    }
                    else if (providerId.Equals("claude", StringComparison.OrdinalIgnoreCase))
                    {
                        newClaude = vm;
                    }
                    else if (providerId.Equals("copilot", StringComparison.OrdinalIgnoreCase))
                    {
                        newCopilot = vm;
                    }
                    else if (providerId.Equals("gemini", StringComparison.OrdinalIgnoreCase))
                    {
                        newGemini = vm;
                    }
                    else if (providerId.StartsWith("claude:", StringComparison.OrdinalIgnoreCase))
                    {
                        claudeProfileList.Add(vm);

                        // Accumulate totals from raw reading data
                        if (reading.Usage?.Consumption is { } c)
                        {
                            totalTodayCost += c.TodayCostUsd;
                            totalTodayTokens += c.TodayTokens.TotalConsumed;

                            foreach (var slice in c.DailyBreakdown)
                            {
                                if (slice.Period >= weekStart && slice.Period <= today)
                                {
                                    totalWeekCost += slice.ComputedCostUsd;
                                    totalWeekTokens += slice.Tokens.TotalConsumed;
                                }
                            }
                        }
                    }
                }

                // 계약: 카드 순서는 설정 「계정」에서 정한 순서다(AppSettings.AccountOrder)
                claudeProfileList = AccountMeta.Ordered(_settings, claudeProfileList, p => p.ProviderId, p => p.DisplayName).ToList();

                // 함정: 부분 갱신은 한 계정만 실어 온다 — 지난 계정 목록에 덮어써야 다른 계정이 사라지지 않는다
                foreach (var (id, vm) in codexAccounts)
                {
                    _codexAccounts[id] = vm;
                }

                if (!_codexAccounts.TryGetValue(SelectedCodexAccountId, out newCodex))
                {
                    _codexAccounts.TryGetValue(CodexMainId, out newCodex);
                }

                var isMulticc = claudeProfileList.Count > 0;

                // Build summary text
                var summaryText = string.Empty;
                if (isMulticc)
                {
                    newClaude = claudeProfileList.FirstOrDefault(p => p.ProviderId == SelectedClaudeAccountId)
                        ?? AllStats(claudeProfileList);

                    var total = claudeProfileList.Count;
                    var critical = claudeProfileList.Count(p => p.SessionProgress >= 0.95);
                    var warning = claudeProfileList.Count(p => p.SessionProgress >= 0.80 && p.SessionProgress < 0.95);
                    // 왜: 한도를 못 받은 자리는 0% 로 세여 「모두 정상」이 떴다 — 연동 필요를 먼저 알린다
                    var unlinked = claudeProfileList.Count(p => p.NeedsLink);

                    if (unlinked > 0)
                        summaryText = $"{total} profiles  ·  {unlinked} need linking";
                    else if (critical > 0)
                        summaryText = $"{total} profiles  ·  {critical} at limit, {warning} warning";
                    else if (warning > 0)
                        summaryText = $"{total} profiles  ·  {warning} near limit";
                    else
                        summaryText = $"{total} profiles  ·  All healthy";
                }

                // ── Apply to observable state (batched, single render frame) ──

                // Set scalar properties before collection changes to prevent layout thrash.
                // IsMulticcActive controls panel visibility — setting it first ensures the
                // correct panel stays visible while collections are swapped.
                if (newCodex is not null) Codex = newCodex;
                if (newClaude is not null) Claude = newClaude;
                if (newCopilot is not null) Copilot = newCopilot;
                if (newGemini is not null) Gemini = newGemini;
                IsMulticcActive = isMulticc;
                MulticcSummary = Loc.Tr(summaryText);

                // Multicc aggregate totals
                if (isMulticc && (totalTodayTokens > 0 || totalWeekTokens > 0))
                {
                    MulticcTotalTodayCost = UsageFormatter.FormatCurrency(totalTodayCost);
                    MulticcTotalTodayTokens = UsageFormatter.FormatTokenCount(totalTodayTokens);
                    MulticcTotalWeekCost = UsageFormatter.FormatCurrency(totalWeekCost);
                    MulticcTotalWeekTokens = UsageFormatter.FormatTokenCount(totalWeekTokens);
                    HasMulticcTotals = true;
                }
                else
                {
                    HasMulticcTotals = false;
                }

                // Swap collection contents (single clear + add, no double-clear)
                Providers.Clear();
                foreach (var p in newProviders) Providers.Add(p);

                ClaudeProfiles.Clear();
                foreach (var p in claudeProfileList) ClaudeProfiles.Add(p);
                SyncClaudeAccountChips();
                SyncCodexAccountChips();
                ApplyPendingDefaults();
            }

            RefreshActiveAccount();

            // Only notify SelectedProvider if the reference actually changed
            OnPropertyChanged(nameof(SelectedProvider));

            LastUpdated = value.LastRefresh.ToLocalTime().ToString("g");
            UpdatedLabel = Loc.T("Updated {0}", value.LastRefresh.ToLocalTime().ToString("t"));
        });
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    public void Dispose()
    {
        _nextRefreshTicker.Stop();
        _subscription.Dispose();
    }
}
