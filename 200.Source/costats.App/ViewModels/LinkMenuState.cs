namespace costats.App.ViewModels;

/// <summary>「연동 ▾」 줄 표지의 근거(SettingsViewModel.LinkMenuStateFor) — *Linked 는 잠글 줄(연동됨), Recommended 는 「추천」 줄(cli · web · 빈 값), CliInstalled 가 false 면 CLI·웹 줄에 「먼저 설치」가 붙는다.</summary>
public sealed record LinkMenuState(bool CliLinked, bool AppLinked, bool WebLinked, string Recommended, bool CliInstalled, bool DesktopSignedIn, string CliName);
