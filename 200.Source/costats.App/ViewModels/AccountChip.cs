using CommunityToolkit.Mvvm.ComponentModel;

namespace costats.App.ViewModels;

/// <summary>
/// 선택 목록의 한 항목 — Claude 계정 드롭다운과 기간 칩이 함께 쓴다.
/// 계약: 계정 목록에서 Id 가 null 이면 "All"(전 계정 쌓아 보기)이다. Detail 은 그 계정의 메일 주소 같은 보조 설명이다.
/// </summary>
public sealed partial class AccountChip : ObservableObject
{
    public AccountChip(string? id, string label, string detail = "")
    {
        Id = id;
        Label = label;
        Detail = detail;
    }

    public string? Id { get; }

    public string Label { get; }

    public string Detail { get; }

    [ObservableProperty]
    private bool isSelected;
}
