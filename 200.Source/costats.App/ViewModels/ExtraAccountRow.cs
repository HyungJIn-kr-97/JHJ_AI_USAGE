namespace costats.App.ViewModels;

/// <summary>
/// 설정 창 「계정」의 추가 계정 한 줄. Name 은 폴더 이름, Label 은 사용자가 적은 이메일·별명,
/// AccountText 는 그 폴더에 실제 로그인된 계정(없으면 "Not signed in").
/// </summary>
public sealed record ExtraAccountRow(string Name, string Label, string AccountText, string ConfigDir);

/// <summary>
/// 설정 창의 색상 팔레트 견본 하나.
/// </summary>
public sealed record PaletteOption(string Name, System.Windows.Media.Brush Swatch, bool IsSelected);
