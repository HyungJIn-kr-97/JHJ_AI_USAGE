using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using costats.App.Localization;
using costats.App.Services;

namespace costats.App.ViewModels;

/// <summary>
/// 설정 › 일반의 「팝업 단축키 n」 한 줄. 상태가 둘이다 — 입력 중(저장 · 삭제)과 완료(변경 · 삭제).
/// 계약: 완료 상태의 입력란은 키를 받지 않는다. 0번은 지울 수 없어 둘째 버튼이 입력 중 「기본」, 완료 「+」(줄 추가)다.
/// </summary>
public sealed partial class HotkeySlotRow : ObservableObject
{
    private readonly Action<HotkeySlotRow> _onPrimary;
    private readonly Action<HotkeySlotRow> _onSecondary;

    public HotkeySlotRow(int index, string text, Action<HotkeySlotRow> onPrimary, Action<HotkeySlotRow> onSecondary, bool editing = false)
    {
        Index = index;
        this.text = text;
        _onPrimary = onPrimary;
        _onSecondary = onSecondary;
        isEditing = editing;
    }

    public int Index { get; }

    public string Label => Loc.T("Popup shortcut {0}", Index + 1);

    public string PrimaryText => Loc.T(IsEditing ? "Save" : "Change");

    public string SecondaryText => Index > 0 ? Loc.T("Remove") : IsEditing ? Loc.T("Default") : "+";

    // 계약: 입력 중에 누른, 아직 저장하지 않은 조합 — 「저장」이 이 값을 등록한다. 막힌 조합이면 null 이다
    public (Key Key, ModifierKeys Modifiers)? Pending { get; set; }

    // 계약: 수정키만 누른 미리보기(「Alt+…」)를 지울 때 돌아갈 글자
    public string SettledText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText), nameof(SecondaryText))]
    private bool isEditing;

    [ObservableProperty]
    private string text;

    [ObservableProperty]
    private string message = string.Empty;

    // 계약: Recommended·Allowed 는 초록, Caution 은 노랑, Blocked 는 빨강으로 그린다
    [ObservableProperty]
    private HotkeyVerdict verdict = HotkeyVerdict.Allowed;

    [RelayCommand]
    private void Primary() => _onPrimary(this);

    [RelayCommand]
    private void Secondary() => _onSecondary(this);
}
