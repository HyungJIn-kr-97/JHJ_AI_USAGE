using CommunityToolkit.Mvvm.ComponentModel;

namespace costats.App.ViewModels;

/// <summary>
/// 설정 창 「계정」의 한 줄 — 이 PC 로그인(IsMain)과 추가 계정이 같은 모양으로 그려진다.
/// Id 는 팝업이 쓰는 providerId, Name 은 추가 계정의 폴더 이름(이 PC 로그인은 빈 값), AccountText 는 실제 로그인된 계정이다.
/// 계약: DisplayName·AccountType 은 「편집」을 누른 동안만 고칠 수 있고, 「저장」을 눌러야 onEdited 가 설정에 저장한다.
/// </summary>
public sealed partial class ExtraAccountRow : ObservableObject
{
    private readonly Action<ExtraAccountRow>? _onEdited;
    private string _savedName = string.Empty;
    private string _savedType = string.Empty;
    private string _savedFee = string.Empty;

    public ExtraAccountRow(string id, string name, string accountText, string? configDir, bool isSignedIn,
        string displayName, string accountType, Action<ExtraAccountRow>? onEdited, decimal? monthlyFee = null)
    {
        Id = id;
        Name = name;
        AccountText = accountText;
        ConfigDir = configDir;
        IsSignedIn = isSignedIn;
        displayNameText = displayName;
        this.accountType = accountType;
        monthlyFeeText = monthlyFee is { } fee ? fee.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        _onEdited = onEdited;
    }

    // 계약: 월 구독료 입력칸(USD) — 비우면 플랜 등급에서 자동. 숫자가 아니면 저장 때 null 로 간다
    [ObservableProperty]
    private string monthlyFeeText;

    public decimal? MonthlyFee =>
        decimal.TryParse(MonthlyFeeText.Trim().TrimStart('$'), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var fee) && fee > 0 ? fee : null;

    public string Id { get; }

    public string Name { get; }

    public string AccountText { get; }

    public string? ConfigDir { get; }

    public bool IsSignedIn { get; }

    public bool IsMain => Name.Length == 0;

    [ObservableProperty]
    private string displayNameText;

    [ObservableProperty]
    private string accountType;

    [ObservableProperty]
    private bool isEditing;

    // 계약: 설정 화면이 줄을 세울 때 단다 — 맨 위·맨 아래 줄은 그쪽 이동 버튼이 꺼진다
    [ObservableProperty]
    private bool canMoveUp;

    [ObservableProperty]
    private bool canMoveDown;

    public int MaxNameLength => costats.App.Services.AccountMeta.MaxNameLength;

    public bool HasType => AccountType.Trim().Length > 0;

    partial void OnAccountTypeChanged(string value) => OnPropertyChanged(nameof(HasType));

    // 계약: 다른 줄이 「기본」을 가져가서 이 줄의 유형을 비울 때 쓴다 — 저장은 그 줄이 이미 했다
    public void ClearTypeSilently() => AccountType = string.Empty;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void BeginEdit()
    {
        _savedName = DisplayNameText;
        _savedType = AccountType;
        _savedFee = MonthlyFeeText;
        IsEditing = true;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SaveEdit()
    {
        IsEditing = false;
        _onEdited?.Invoke(this);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CancelEdit()
    {
        DisplayNameText = _savedName;
        AccountType = _savedType;
        MonthlyFeeText = _savedFee;
        IsEditing = false;
    }
}

/// <summary>
/// 설정 창의 색상 팔레트 견본 하나.
/// </summary>
public sealed record PaletteOption(string Name, System.Windows.Media.Brush Swatch, bool IsSelected);
