using CommunityToolkit.Mvvm.ComponentModel;

namespace costats.App.ViewModels;

/// <summary>
/// 설정 › 계정 › Claude 의 「프로그램 연동」 한 줄 — 이 PC 의 프로그램(entrypoint) 하나와 그 기록의 주인 계정.
/// </summary>
public sealed partial class ProgramLinkRow : ObservableObject
{
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
