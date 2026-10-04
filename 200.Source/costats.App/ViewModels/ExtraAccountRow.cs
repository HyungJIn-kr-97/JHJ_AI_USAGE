using CommunityToolkit.Mvvm.ComponentModel;

namespace costats.App.ViewModels;

/// <summary>
/// 설정 창 「계정」의 한 줄 — 이 PC 로그인(IsMain)과 추가 계정이 같은 모양으로 그려진다.
/// Id 는 팝업이 쓰는 providerId, Name 은 추가 계정의 폴더 이름(이 PC 로그인은 빈 값), AccountText 는 실제 로그인된 계정이다.
/// 계약: DisplayName·AccountType 은 사용자가 고치는 칸이다 — 바뀌면 onEdited 가 설정에 저장한다.
/// </summary>
public sealed partial class ExtraAccountRow : ObservableObject
{
    private readonly Action<ExtraAccountRow>? _onEdited;
    private bool _silent;

    public ExtraAccountRow(string id, string name, string accountText, string? configDir, bool isSignedIn,
        string displayName, string accountType, Action<ExtraAccountRow>? onEdited)
    {
        Id = id;
        Name = name;
        AccountText = accountText;
        ConfigDir = configDir;
        IsSignedIn = isSignedIn;
        displayNameText = displayName;
        this.accountType = accountType;
        _onEdited = onEdited;
    }

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

    // 계약: 다른 줄이 「기본」을 가져가서 이 줄의 유형을 비울 때 쓴다 — 저장을 다시 부르지 않는다
    public void ClearTypeSilently()
    {
        _silent = true;
        AccountType = string.Empty;
        _silent = false;
    }

    partial void OnDisplayNameTextChanged(string value) => Edited();

    partial void OnAccountTypeChanged(string value) => Edited();

    private void Edited()
    {
        if (!_silent)
        {
            _onEdited?.Invoke(this);
        }
    }
}

/// <summary>
/// 설정 창의 색상 팔레트 견본 하나.
/// </summary>
public sealed record PaletteOption(string Name, System.Windows.Media.Brush Swatch, bool IsSelected);
