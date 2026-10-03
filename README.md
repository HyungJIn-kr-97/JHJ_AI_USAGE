# JHJ_AI_USAGE — AI 통합 사용량 모니터 (Windows 트레이 앱)

Claude Code · Codex 의 세션·주간 한도와 일별 비용을 트레이 팝업으로 보여 주는 개인 도구입니다.
[fmdz387/costats](https://github.com/fmdz387/costats) (MIT) 의 포크이고, 원본 README 는
[900.Archive/upstream/README.upstream.md](900.Archive/upstream/README.upstream.md) 에 그대로 있습니다. **이 저장소가 원본입니다** — `JHJ_DEV` 에는 지도 한 줄만 있습니다.

## 설치

[Releases](https://github.com/HyungJIn-kr-97/JHJ_AI_USAGE/releases) 의 최신 `AiUsageMonitor-win-x64-v<버전>.zip` 을 받아 풀고
`AiUsageMonitor.exe` 를 실행합니다. .NET 설치는 필요 없습니다. 설치 여부를 묻는 창에서 「예」를 고르면
`%LOCALAPPDATA%\AiUsageMonitor\app` 에 복사되고 시작 메뉴 바로가기(`AI 통합 사용량 모니터`)가 생긴 뒤 설치본으로 다시 뜹니다.

- 단축키 `Ctrl+Alt+Y` 로 팝업을 열고 닫습니다. 설정 창은 팝업 우측 아래 톱니입니다.
- 설치본은 시작할 때와 6시간마다 이 저장소의 최신 릴리스를 보고, 더 높은 버전이면 받아서 다음 실행 때 갈아 끼웁니다.
  설정 창 「일반」의 `업데이트 확인` 은 바로 받아 다시 시작합니다.
- 원본 `costats` 와 함께 띄워도 서로 덮어쓰지 않습니다(데이터 폴더·실행 이름·단축키가 다릅니다).

## 빌드 · 릴리스

`.NET 10 SDK` 가 필요합니다(`net10.0-windows`, WPF).

| 하려는 것 | 방법 |
|---|---|
| 개발 빌드 | `dotnet build 200.Source\costats.sln -c Release` → `200.Source\costats.App\bin\Release\…\AiUsageMonitor.exe`. 개발 실행이라 설치·업데이트는 동작하지 않습니다 |
| 설치용 exe · 업데이트 꾸러미 | `800.Deploy\Build-Exe.bat` 더블클릭(버전을 올리려면 `Build-Exe.bat 1.0.1`) → `800.Deploy\publish\win-x64\AiUsageMonitor.exe` 와 `800.Deploy\publish\AiUsageMonitor-win-x64-v<버전>.zip` · `.zip.sha256` |
| 릴리스 | 태그 `v<버전>` 으로 GitHub Release 를 만들고 **zip 과 .sha256 두 파일**을 올립니다. 예: `gh release create v1.0.1 800.Deploy\publish\AiUsageMonitor-win-x64-v1.0.1.zip 800.Deploy\publish\AiUsageMonitor-win-x64-v1.0.1.zip.sha256` |

- `800.Deploy\publish\` 는 git 에 올라가지 않습니다.
- 함정: 설치 폴더는 업데이트 때 **통째로 교체**됩니다. 그래서 설치 위치가 아닌 곳에서 띄운 exe 는 스스로 업데이트하지 않습니다.
- 함정: 이 저장소는 **공개**여야 합니다 — 앱이 토큰 없이 `api.github.com/repos/HyungJIn-kr-97/JHJ_AI_USAGE/releases/latest` 를 읽습니다.
  업데이트 저장소를 원본(`fmdz387/costats`)으로 적으면 원본 릴리스가 이 포크를 덮어씁니다.
- 빌드 중에는 이 폴더에서 띄운 개발 실행본을 bat 가 먼저 종료합니다(`bin\` 잠금). 설치본은 건드리지 않습니다.
- 원본의 GitHub Actions(`.github/`)는 뺐습니다 — 릴리스는 위 명령으로 사람이 올립니다. 원본에서 쓰지 않는 것(문서·MSIX 패키징·insights-cli·install.ps1)은 `900.Archive\upstream\` 에 참고용으로만 둡니다.

## 원본과 다른 점

| 무엇 | 원본 | 이 포크 | 고친 파일 |
|---|---|---|---|
| 탭 배치 | Codex · Claude | Claude · Codex | `200.Source/costats.App/GlassWidgetWindow.xaml` |
| 처음 선택되는 탭 | Codex | Claude | `200.Source/costats.App/ViewModels/PulseViewModel.cs` |
| 이름 | `costats` · `costats.App.exe` | `AI 통합 사용량 모니터` / `AI Usage Monitor` · `AiUsageMonitor.exe`(프로젝트·네임스페이스는 `costats.*` 유지) | `costats.App.csproj` · `LocStrings.cs` · `TrayHost.cs` |
| 데이터 폴더 | `%LOCALAPPDATA%\costats` | `%LOCALAPPDATA%\AiUsageMonitor`(1.0.0 의 `costats-jhj` 는 첫 실행 때 자동 이동 — `Services/LegacyMigration.cs`) | `JsonSettingsStore.cs` · `JsonPulseSnapshotWriter.cs` · `ClaudeOAuthUsageFetcher.cs` · `App.xaml.cs` · `appsettings.json` |
| 단일 실행 이름 · 시작프로그램 이름 | `costats` | `AiUsageMonitor` | `App.xaml.cs` · `SettingsViewModel.cs` |
| 기본 단축키 | `Ctrl+Alt+U` | `Ctrl+Alt+Y` | `AppSettings.cs` · `appsettings.json` |
| 아이콘 | costats 로고 | JHJ favicon(불꽃 황소) | `200.Source/costats.App/Resources/tray-icon.ico` |
| 설치 | zip + `install.ps1` | exe 를 실행하면 `%LOCALAPPDATA%\AiUsageMonitor\app` 에 스스로 설치 | `Services/SelfInstaller.cs` · `App.xaml.cs` |
| 자동 업데이트 | 원본 저장소 릴리스 | **이 저장소 릴리스만**(`Costats:Update:Repository`). 비어 있으면 꺼짐. 상태 폴더는 `%LOCALAPPDATA%\AiUsageMonitor\updates`, 설정 창 「일반」에 `업데이트 확인` 버튼 | `UpdateOptions.cs` · `StartupUpdateCoordinator.cs` · `apply-update.ps1` · `appsettings.json` · `SettingsWindow.xaml` |
| 색·글꼴 | 라벤더 | JHJ 팔레트 5종(bull·navy·emerald·violet·slate) × 라이트·다크 · Pretendard. 팔레트는 설정 창 견본으로 고른다 | `Services/ThemeManager.cs` · `App.xaml.cs` · 창 XAML 3개 |
| 모델별 주간 한도 | 없음 | 주간 아래에 `주간 · Fable` 막대(응답 `limits[]` 의 `weekly_scoped`) | `ClaudeOAuthUsageFetcher.cs` · `UsagePulse.cs` · `ProviderPulseViewModel.cs` · `ModelWeekRow.cs` |
| 설정 창 구성 | 구분선 나열 | 카드 4장(계정·화면·일반·Copilot) + 두 줄 하단. 설명은 툴팁으로, 콤보는 테마 색 템플릿 | `SettingsWindow.xaml` |
| 로그인 계정 표시 | 없음 | 설정 창 맨 위 「계정」에 Claude·Codex 계정 | `Services/AccountIdentityReader.cs` · `SettingsViewModel.cs` · `SettingsWindow.xaml` |
| 버전 · 제작자 | 1.4.6 · fmdz | 1.0.0 · `제작 HyungJin Ju (메일 주소)` + GitHub 링크, 원저작자 표기 유지 | `200.Source/Directory.Build.props` · `costats.App.csproj` · `SettingsWindow.xaml` |
| 제목 줄 · 테마 버튼 | 없음 | 상단 `AI 통합 사용량 모니터`(영어 `AI Usage Monitor`) 제목, 하단 반달 버튼으로 라이트/다크 전환(설정에 저장) | `GlassWidgetWindow.xaml` · `Services/ThemeManager.cs` · `AppSettings.cs` |
| 기간 선택 | 30일 고정 | Cost 옆 `7일`~`1년` 칩 6개(차트·모델 비중에 적용, 재시작하면 30일). 90·180일은 주 단위, 1년은 월 단위 막대 | `PulseViewModel.cs` · `ProviderPulseViewModel.cs` · `Services/UsageHistoryStore.cs` · `ExpenseAnalyzer.cs` |
| 모델별 사용량 | 없음 | 차트 아래 도넛 + 목록 4줄, 막대 툴팁에 그날의 모델 | `ProviderPulseViewModel.cs` · `ModelUsageRow.cs` |
| 사용량 조회용 계정 | multicc 설정 파일을 손으로 작성 | 설정 창에서 추가·로그인·삭제, 메인 팝업 우측 상단 드롭다운으로 계정 전환(All 포함). 계정이 하나면 그 메일 주소만 표시 | `Services/AccountProfileStore.cs` · `SettingsViewModel.cs` · `PulseViewModel.cs` · `AccountChip.cs` · `ExtraAccountRow.cs` |
| 언어 | 영어만 | 한국어 기본, 설정 창에서 한국어/English 전환(즉시 반영, 설정에 저장) | `Localization/Loc.cs` · `Localization/LocStrings.cs` · 창 XAML 3개 · 뷰모델 3개 · `TrayHost.cs` |
| 열 때 새로고침 | 선택 탭만 표시 없이 | 전체를 스피너와 함께 | `Services/TrayHost.cs` |
| 일별 비용 차트 | 없음 | Cost 아래 막대(마우스를 올리면 날짜·비용·토큰) | `ProviderPulseViewModel.cs` · `DailyUsageBar.cs` · `GlassWidgetWindow.xaml` |
| 창 높이 · 닫기 | 580 | 690 · `Esc` 로 닫힘 | `GlassWidgetWindow.xaml` · `GlassWidgetWindow.xaml.cs` |

- 색 정본은 `JHJ_OPS/200.Source/100.Web/src/styles.css` 의 bull 팔레트입니다. 라이트·다크는 **앱을 시작할 때의 Windows 앱 모드**로 정해지고, 바꾸려면 앱을 다시 띄워야 합니다.
- 추가 계정은 `%LOCALAPPDATA%\AiUsageMonitor\accounts\<이름>\` 폴더를 `CLAUDE_CONFIG_DIR` 로 쓰고, 목록은 같은 폴더의 `config.json`(multicc 형식)에 둡니다. **로그인은 앱이 띄운 터미널에서 사용자가 직접** 하고, 추가·삭제는 앱을 다시 띄워야 반영됩니다. 삭제하면 그 폴더(로그인 토큰 포함)도 지웁니다.
- 일별 사용량은 `%LOCALAPPDATA%\AiUsageMonitor\history\<계정>.json` 에 쌓습니다. Claude Code 가 오래된 대화 기록을 지워도 여기 쌓인 날은 남습니다 — **이 폴더를 지우면 로그에 없는 과거는 되살릴 수 없습니다.**
- 계정은 `~/.claude.json` 의 `oauthAccount` 와 `~/.codex/auth.json` 의 `id_token` 에서 이메일만 읽습니다.
- 차트의 날짜는 원본 집계를 그대로 써서 **UTC 기준**입니다. 한국 시간 오전 9시 이전 사용분은 전날 막대에 들어갑니다.
- 탭 번호(0 = Codex, 1 = Claude)는 원본 그대로입니다. 원본을 다시 받아 합칠 때 위 표의 파일만 다시 고치면 됩니다.
