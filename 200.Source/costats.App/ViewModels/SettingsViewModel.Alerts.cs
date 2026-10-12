using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using costats.App.Services;
using costats.Application.Settings;

namespace costats.App.ViewModels;

// 계약: 설정 「사용률 알림」 탭의 값 — 첫 값은 SettingsViewModel 생성자가 필드에 넣고, ApplyResetToView 가 Alerts 묶음으로 되돌린다
public sealed partial class SettingsViewModel
{
    private UsageAlertService? _usageAlerts;

    public IReadOnlyList<AlertStyleOption> UsageAlertStyleOptions { get; } =
        new[] { "toast", "balloon", "both" }.Select(code => new AlertStyleOption(code)).ToArray();

    private Views.UsageMiniWidgetWindow? _miniWidget;

    public IReadOnlyList<SecondsOption> UsageAlertToastSecondsOptions { get; } =
        new[] { 3, 5, 8, 12, 20, 30 }.Select(s => new SecondsOption(s)).ToArray();

    public IReadOnlyList<AlertPositionOption> UsageAlertPositionOptions { get; } =
        new[] { "bottom-right", "top-right", "bottom-left", "top-left" }.Select(code => new AlertPositionOption(code)).ToArray();

    // 계약: 미니 위젯 칸 — 창 쪽(📌·✕·Ctrl+휠)에서 바꾼 것은 창이 설정에 직접 쓰고, 여기는 탭을 열 때 다시 읽는다
    [ObservableProperty] private bool miniWidgetVisible;
    [ObservableProperty] private bool miniWidgetPinned;
    [ObservableProperty] private bool miniWidgetOnDesktop;
    [ObservableProperty] private double miniWidgetScale = 1.0;

    public IReadOnlyList<ScaleOption> MiniWidgetScaleOptions { get; } =
        Enumerable.Range(7, 14).Select(i => new ScaleOption(i * 10)).ToArray();

    public ScaleOption? SelectedMiniWidgetScale
    {
        get => MiniWidgetScaleOptions.FirstOrDefault(o => Math.Abs(o.Value - MiniWidgetScale) < 0.005);
        set
        {
            if (value is not null && Math.Abs(value.Value - MiniWidgetScale) >= 0.005)
            {
                MiniWidgetScale = value.Value;
                OnPropertyChanged();
            }
        }
    }

    public void SyncMiniWidgetFromSettings()
    {
        MiniWidgetVisible = _settings.MiniWidgetVisible;
        MiniWidgetPinned = _settings.MiniWidgetPinned;
        MiniWidgetOnDesktop = _settings.MiniWidgetOnDesktop;
        MiniWidgetScale = _settings.MiniWidgetScale;
        OnPropertyChanged(nameof(SelectedMiniWidgetScale));
    }

    // 계약: 글자로 적는 단축키 — Core NamedHotkeys 가 해석·검사·등록하고 실패 사유를 돌려준다. 실패하면 옛 등록이 남고 칸만 빨갛다
    [ObservableProperty] private string miniWidgetHotkeyText = string.Empty;
    [ObservableProperty] private string miniWidgetHotkeyError = string.Empty;

    partial void OnMiniWidgetHotkeyTextChanged(string value)
    {
        var text = value.Trim();
        var error = HotkeyService.Current?.SetMiniWidgetHotkey(text);
        MiniWidgetHotkeyError = error is null ? string.Empty : Loc.T(error);
        if (error is null)
        {
            _settings.MiniWidgetHotkey = text;
            _ = SaveSettingsAsync();
        }
    }

    partial void OnMiniWidgetVisibleChanged(bool value)
    {
        if (_miniWidget is null || value == _miniWidget.IsVisible)
        {
            _settings.MiniWidgetVisible = value;
            _ = SaveSettingsAsync();
            return;
        }

        if (value) _miniWidget.ShowWidget(); else _miniWidget.HideWidget();
    }

    partial void OnMiniWidgetPinnedChanged(bool value)
    {
        if (_miniWidget is not null) _miniWidget.SetPinned(value);
        else { _settings.MiniWidgetPinned = value; _ = SaveSettingsAsync(); }
    }

    partial void OnMiniWidgetOnDesktopChanged(bool value)
    {
        if (_miniWidget is not null) _miniWidget.SetOnDesktop(value);
        else { _settings.MiniWidgetOnDesktop = value; _ = SaveSettingsAsync(); }
    }

    partial void OnMiniWidgetScaleChanged(double value)
    {
        if (_miniWidget is not null) _miniWidget.SetScale(value);
        else { _settings.MiniWidgetScale = value; _ = SaveSettingsAsync(); }
        OnPropertyChanged(nameof(SelectedMiniWidgetScale));
    }

    public IReadOnlyList<RefreshOption> UsageAlertRelaxOptions { get; } =
        new[] { 15, 30, 45, 60, 90, 120 }.Select(m => new RefreshOption(m)).ToArray();

    public ObservableCollection<UsageAlertStatusRow> UsageAlertStatus => _usageAlerts?.Status ?? [];

    [ObservableProperty] private bool usageAlertEnabled;
    [ObservableProperty] private string usageAlertStepsText = string.Empty;
    [ObservableProperty] private bool usageAlertStepsInvalid;
    [ObservableProperty] private string usageAlertStyle = "toast";
    [ObservableProperty] private int usageAlertToastSeconds;
    [ObservableProperty] private string usageAlertPosition = "bottom-right";
    [ObservableProperty] private bool usageAlertSession;
    [ObservableProperty] private bool usageAlertWeekly;
    [ObservableProperty] private bool usageAlertModelWeek;
    [ObservableProperty] private bool usageAlertRecommend;
    [ObservableProperty] private int usageAlertRelaxMinutes;

    private void ApplyAlertsResetToView()
    {
        UsageAlertEnabled = _settings.UsageAlertEnabled;
        UsageAlertStepsText = string.Join(", ", _settings.UsageAlertSteps);
        UsageAlertStyle = _settings.UsageAlertStyle;
        UsageAlertToastSeconds = _settings.UsageAlertToastSeconds;
        UsageAlertPosition = _settings.UsageAlertPosition;
        UsageAlertSession = _settings.UsageAlertSession;
        UsageAlertWeekly = _settings.UsageAlertWeekly;
        UsageAlertModelWeek = _settings.UsageAlertModelWeek;
        UsageAlertRecommend = _settings.UsageAlertRecommend;
        UsageAlertRelaxMinutes = _settings.UsageAlertRelaxMinutes;
        MiniWidgetPinned = _settings.MiniWidgetPinned;
        MiniWidgetOnDesktop = _settings.MiniWidgetOnDesktop;
        MiniWidgetScale = _settings.MiniWidgetScale;
        MiniWidgetHotkeyText = _settings.MiniWidgetHotkey;
        OnPropertyChanged(nameof(SelectedUsageAlertStyle));
        OnPropertyChanged(nameof(SelectedUsageAlertToastSeconds));
        OnPropertyChanged(nameof(SelectedUsageAlertPosition));
        OnPropertyChanged(nameof(SelectedUsageAlertRelax));
    }

    private void NotifyAlertOptionsLanguageChanged()
    {
        foreach (var o in UsageAlertStyleOptions) o.NotifyLanguageChanged();
        foreach (var o in UsageAlertToastSecondsOptions) o.NotifyLanguageChanged();
        foreach (var o in UsageAlertPositionOptions) o.NotifyLanguageChanged();
        foreach (var o in UsageAlertRelaxOptions) o.NotifyLanguageChanged();
    }

    public AlertStyleOption SelectedUsageAlertStyle
    {
        get => UsageAlertStyleOptions.FirstOrDefault(o => o.Code == UsageAlertStyle) ?? UsageAlertStyleOptions[0];
        set
        {
            if (value is not null && UsageAlertStyle != value.Code)
            {
                UsageAlertStyle = value.Code;
                OnPropertyChanged();
            }
        }
    }

    public AlertPositionOption SelectedUsageAlertPosition
    {
        get => UsageAlertPositionOptions.FirstOrDefault(o => o.Code == UsageAlertPosition) ?? UsageAlertPositionOptions[0];
        set
        {
            if (value is not null && UsageAlertPosition != value.Code)
            {
                UsageAlertPosition = value.Code;
                OnPropertyChanged();
            }
        }
    }

    public SecondsOption SelectedUsageAlertToastSeconds
    {
        get => UsageAlertToastSecondsOptions.FirstOrDefault(o => o.Seconds == UsageAlertToastSeconds) ?? UsageAlertToastSecondsOptions[2];
        set
        {
            if (value is not null && UsageAlertToastSeconds != value.Seconds)
            {
                UsageAlertToastSeconds = value.Seconds;
                OnPropertyChanged();
            }
        }
    }

    public RefreshOption SelectedUsageAlertRelax
    {
        get => UsageAlertRelaxOptions.FirstOrDefault(o => o.Minutes == UsageAlertRelaxMinutes) ?? UsageAlertRelaxOptions[1];
        set
        {
            if (value is not null && UsageAlertRelaxMinutes != value.Minutes)
            {
                UsageAlertRelaxMinutes = value.Minutes;
                OnPropertyChanged();
            }
        }
    }

    partial void OnUsageAlertEnabledChanged(bool value)
    {
        _settings.UsageAlertEnabled = value;
        _ = SaveSettingsAsync();
        // 왜: 켜는 순간 지금 넘어 있는 단계를 바로 알린다 — 다음 갱신까지 기다리게 하지 않는다
        _usageAlerts?.Reevaluate(restart: value);
    }

    // 계약: 쉼표로 나눈 1~100 의 정수, 오름차순 — 모양이 틀리면 저장하지 않고 칸만 빨갛게 둔다
    partial void OnUsageAlertStepsTextChanged(string value)
    {
        var parts = value.Split([',', '·', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var steps = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part.TrimEnd('%'), out var step) || step is < 1 or > 100)
            {
                UsageAlertStepsInvalid = true;
                return;
            }

            steps.Add(step);
        }

        if (steps.Count == 0 || steps.Zip(steps.Skip(1)).Any(pair => pair.First >= pair.Second))
        {
            UsageAlertStepsInvalid = true;
            return;
        }

        UsageAlertStepsInvalid = false;
        _settings.UsageAlertSteps = steps;
        _ = SaveSettingsAsync();
    }

    partial void OnUsageAlertStyleChanged(string value) { _settings.UsageAlertStyle = value; _ = SaveSettingsAsync(); }
    partial void OnUsageAlertToastSecondsChanged(int value) { _settings.UsageAlertToastSeconds = value; _ = SaveSettingsAsync(); }
    partial void OnUsageAlertPositionChanged(string value) { _settings.UsageAlertPosition = value; _ = SaveSettingsAsync(); _usageAlerts?.RepositionToast(); }
    partial void OnUsageAlertSessionChanged(bool value) { _settings.UsageAlertSession = value; SaveAndReevaluate(); }
    partial void OnUsageAlertWeeklyChanged(bool value) { _settings.UsageAlertWeekly = value; SaveAndReevaluate(); }
    partial void OnUsageAlertModelWeekChanged(bool value) { _settings.UsageAlertModelWeek = value; SaveAndReevaluate(); }
    partial void OnUsageAlertRecommendChanged(bool value) { _settings.UsageAlertRecommend = value; SaveAndReevaluate(); }
    partial void OnUsageAlertRelaxMinutesChanged(int value) { _settings.UsageAlertRelaxMinutes = value; SaveAndReevaluate(); }

    private void SaveAndReevaluate()
    {
        _ = SaveSettingsAsync();
        _usageAlerts?.Reevaluate(restart: false);
    }

    [RelayCommand]
    private void TestUsageAlert() => _usageAlerts?.Test();
}

public sealed class AlertStyleOption(string code) : ObservableObject
{
    public string Code { get; } = code;

    public string Label => Code switch
    {
        "balloon" => Loc.T("Windows notification"),
        "both" => Loc.T("Both"),
        _ => Loc.T("Toast on screen"),
    };

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(Label));
}

public sealed class AlertPositionOption(string code) : ObservableObject
{
    public string Code { get; } = code;

    public string Label => Code switch
    {
        "top-right" => Loc.T("Top right"),
        "bottom-left" => Loc.T("Bottom left"),
        "top-left" => Loc.T("Top left"),
        _ => Loc.T("Bottom right"),
    };

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(Label));
}

/// <summary>미니 위젯 배율 콤보 한 줄 — Percent 70~200, Value 는 0.7~2.0.</summary>
public sealed record ScaleOption(int Percent)
{
    public double Value => Percent / 100.0;
    public string Label => $"{Percent}%";
}

public sealed class SecondsOption(int seconds) : ObservableObject
{
    public int Seconds { get; } = seconds;

    public string Label => Loc.Language switch
    {
        Loc.Korean => $"{Seconds}초",
        Loc.Chinese => $"{Seconds} 秒",
        _ => $"{Seconds} s",
    };

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(Label));
}
