using CommunityToolkit.Mvvm.ComponentModel;

namespace costats.App.ViewModels;

/// <summary>
/// 설정 › 계정 › Claude 의 「프로그램 연동」 한 줄 — 이 PC 의 프로그램(entrypoint) 하나와 그 기록의 주인 계정.
/// </summary>
public sealed partial class ProgramLinkRow : ObservableObject
{
    /// <summary>첫 칸 「자동」의 id — 고르면 고정을 풀고 지금 이 PC 에 로그인한 계정을 따라간다.</summary>
    public const string AutoId = "auto";

    private readonly Action<ProgramLinkRow> _changed;

    public ProgramLinkRow(string program, string label, IReadOnlyList<ProgramAccountOption> options, string selectedId,
        Action<ProgramLinkRow> changed)
    {
        Program = program;
        Label = label;
        Options = options;
        selectedAccount = options.FirstOrDefault(o => o.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        _changed = changed;
    }

    public string Program { get; }

    public string Label { get; }

    public IReadOnlyList<ProgramAccountOption> Options { get; }

    [ObservableProperty]
    private ProgramAccountOption selectedAccount;

    partial void OnSelectedAccountChanged(ProgramAccountOption value) => _changed(this);
}

public sealed record ProgramAccountOption(string Id, string Label);

/// <summary>
/// 설정 › 계정 › Claude 의 「연동 이력」 한 줄 — 언제 · 어느 프로그램이 · 어느 계정으로 바뀌었나.
/// 계약: 최근 것이 위다. 값은 ClaudeProgramLinks 가 쌓아 둔 그대로이고 화면에서 고칠 수 없다.
/// </summary>
public sealed record ProgramLinkHistoryRow(string When, string Program, string Account, string Tooltip);
