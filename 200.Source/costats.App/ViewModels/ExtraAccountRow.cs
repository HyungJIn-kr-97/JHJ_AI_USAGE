namespace costats.App.ViewModels;

/// <summary>
/// 설정 창 ACCOUNTS 의 추가 계정 한 줄. AccountText 는 그 폴더에 로그인된 계정(없으면 "Not signed in").
/// </summary>
public sealed record ExtraAccountRow(string Name, string AccountText, string ConfigDir);

/// <summary>
/// 설정 창의 색상 팔레트 견본 하나.
/// </summary>
public sealed record PaletteOption(string Name, System.Windows.Media.Brush Swatch, bool IsSelected);
