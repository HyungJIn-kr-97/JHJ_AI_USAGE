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
    private readonly Dictionary<string, string> _displayNames;

    public PulseViewModel(IPulseOrchestrator orchestrator, AppSettings settings, IEnumerable<ISignalSource> sources)
    {
        _orchestrator = orchestrator;
        _settings = settings;
        isCopilotEnabled = settings.CopilotEnabled;
        _displayNames = sources
            .Select(source => source.Profile)
            .GroupBy(profile => profile.ProviderId)
            .ToDictionary(group => group.Key, group => group.First().DisplayName, StringComparer.OrdinalIgnoreCase);

        Providers = new ObservableCollection<ProviderPulseViewModel>();
        _subscription = orchestrator.PulseStream.Subscribe(this);
        Loc.LanguageChanged += OnLanguageChanged;
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

    [ObservableProperty]
    private ProviderPulseViewModel claude = new();

    [ObservableProperty]
    private ProviderPulseViewModel codex = new();

    [ObservableProperty]
    private ProviderPulseViewModel copilot = new();

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

    /// <summary>
    /// 계약: 제목 줄의 계정 드롭다운은 Claude 탭에서 계정이 여럿일 때만 열린다.
    /// </summary>
    public bool CanSwitchAccount => SelectedTabIndex == 1;

    partial void OnIsMulticcActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowClaudeStacked));
        OnPropertyChanged(nameof(CanSwitchAccount));
    }

    partial void OnSelectedClaudeAccountIdChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowClaudeStacked));
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

        Claude = ClaudeProfiles.FirstOrDefault(p => p.ProviderId == SelectedClaudeAccountId) ?? ClaudeProfiles[0];
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(SelectedProviderId));
    }

    private void SyncClaudeAccountChips()
    {
        // 왜: 계정이 하나뿐이어도 드롭다운을 열 수 있어야 "Add account" 로 가는 길이 보인다
        var wanted = new List<(string? Id, string Label)> { (null, Loc.T(ClaudeProfiles.Count == 0 ? "This PC" : "All")) };
        wanted.AddRange(ClaudeProfiles
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(p => ((string?)p.ProviderId, p.DisplayName)));

        // 왜: 갱신마다 칩을 새로 만들면 깜빡이고 마우스 오버가 풀린다 — 목록이 달라졌을 때만 다시 만든다
        if (wanted.Select(w => (w.Id, w.Label)).SequenceEqual(ClaudeAccountChips.Select(c => (c.Id, c.Label))))
        {
            return;
        }

        if (SelectedClaudeAccountId is not null && wanted.All(w => w.Id != SelectedClaudeAccountId))
        {
            SelectedClaudeAccountId = null;
        }

        ClaudeAccountChips.Clear();
        foreach (var (id, label) in wanted)
        {
            var detail = id is null
                ? (ClaudeProfiles.Count == 0 ? ShortAccount(AccountIdentityReader.ReadClaude()) : Loc.T("Every account, stacked"))
                : ShortAccount(AccountIdentityReader.ReadClaude(AccountDirOf(id)));
            ClaudeAccountChips.Add(new AccountChip(id, label, detail) { IsSelected = id == SelectedClaudeAccountId });
        }
    }

    /// <summary>
    /// Returns the currently selected provider based on tab index.
    /// </summary>
    public ProviderPulseViewModel SelectedProvider => SelectedTabIndex switch
    {
        0 => Codex,
        1 => Claude,
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
                return "codex";

            if (SelectedTabIndex == 1)
            {
                // For multicc, return the first (worst-case) profile's ID for targeted refresh
                if (IsMulticcActive && ClaudeProfiles.Count > 0)
                    return SelectedClaudeAccountId ?? ClaudeProfiles[0].ProviderId;

                return "claude";
            }

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
            0 => ShortAccount(AccountIdentityReader.ReadCodex()),
            1 when IsMulticcActive && SelectedClaudeAccountId is null => Loc.T("All · {0} accounts", ClaudeProfiles.Count),
            1 when IsMulticcActive => ShortAccount(AccountIdentityReader.ReadClaude(AccountDirOf(SelectedClaudeAccountId!))),
            1 => ShortAccount(AccountIdentityReader.ReadClaude()),
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
        RefreshActiveAccount();
        OnPropertyChanged(nameof(CanSwitchAccount));
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
            if (!IsCopilotEnabled && SelectedTabIndex > 1)
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
                ProviderPulseViewModel? newCodex = null;
                ProviderPulseViewModel? newClaude = null;
                ProviderPulseViewModel? newCopilot = null;

                // Aggregate cost/token totals across multicc profiles
                decimal totalTodayCost = 0;
                long totalTodayTokens = 0;
                decimal totalWeekCost = 0;
                long totalWeekTokens = 0;
                var today = DateOnly.FromDateTime(DateTime.Now);
                var weekStart = today.AddDays(-((int)today.DayOfWeek == 0 ? 6 : (int)today.DayOfWeek - 1)); // Monday

                foreach (var (providerId, reading) in value.Providers)
                {
                    var displayName = _displayNames.TryGetValue(providerId, out var name) ? name : providerId;
                    var vm = ProviderPulseViewModel.FromReading(reading, displayName);

                    if (providerId.Equals("copilot", StringComparison.OrdinalIgnoreCase) && !IsCopilotEnabled)
                    {
                        continue;
                    }

                    newProviders.Add(vm);

                    if (providerId.Equals("codex", StringComparison.OrdinalIgnoreCase))
                    {
                        newCodex = vm;
                    }
                    else if (providerId.Equals("claude", StringComparison.OrdinalIgnoreCase))
                    {
                        newClaude = vm;
                    }
                    else if (providerId.Equals("copilot", StringComparison.OrdinalIgnoreCase))
                    {
                        newCopilot = vm;
                    }
                    else if (providerId.StartsWith("claude:", StringComparison.OrdinalIgnoreCase))
                    {
                        claudeProfileList.Add(vm);

                        // Accumulate totals from raw reading data
                        if (reading.Usage?.Consumption is { } c)
                        {
                            totalTodayCost += c.TodayCostUsd;
                            totalTodayTokens += c.TodayTokens.TotalConsumed;

                            // Compute this week from daily breakdown (Mon-Sun)
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

                // Sort claude profiles by session utilization descending (worst-first)
                claudeProfileList.Sort((a, b) => b.SessionProgress.CompareTo(a.SessionProgress));

                var isMulticc = claudeProfileList.Count > 0;

                // Build summary text
                var summaryText = string.Empty;
                if (isMulticc)
                {
                    newClaude = claudeProfileList.FirstOrDefault(p => p.ProviderId == SelectedClaudeAccountId)
                        ?? claudeProfileList[0]; // worst-case for backward compat

                    var total = claudeProfileList.Count;
                    var critical = claudeProfileList.Count(p => p.SessionProgress >= 0.95);
                    var warning = claudeProfileList.Count(p => p.SessionProgress >= 0.80 && p.SessionProgress < 0.95);

                    if (critical > 0)
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
        _subscription.Dispose();
    }
}
