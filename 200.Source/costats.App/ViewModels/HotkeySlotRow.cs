using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using costats.App.Localization;
using costats.App.Services;

namespace costats.App.ViewModels;

/// <summary>설정 › 일반의 「팝업 단축키 n」 한 줄. 0번은 「기본」, 나머지는 「삭제」 버튼을 단다.</summary>
public sealed partial class HotkeySlotRow : ObservableObject
{
    private readonly Action<HotkeySlotRow> _onButton;

    public HotkeySlotRow(int index, string text, Action<HotkeySlotRow> onButton)
    {
        Index = index;
        this.text = text;
        _onButton = onButton;
    }

    public int Index { get; }

    public string Label => Loc.T("Popup shortcut {0}", Index + 1);

    public string ButtonText => Loc.T(Index == 0 ? "Default" : "Remove");

    // 계약: 빈 칸은 아직 키를 받지 않은 새 줄이다 — 저장·등록되지 않는다
    [ObservableProperty]
    private string text;

    [ObservableProperty]
    private string message = string.Empty;

    // 계약: Recommended·Allowed 는 초록, Caution 은 노랑, Blocked 는 빨강으로 그린다
    [ObservableProperty]
    private HotkeyVerdict verdict = HotkeyVerdict.Allowed;

    [RelayCommand]
    private void Button() => _onButton(this);
}
