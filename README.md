# JHJ_AI_USAGE — AI 통합 사용량 모니터 (Windows 트레이 앱)

Claude Code · Codex 의 세션·주간 한도와 일별 비용을 트레이 팝업으로 보여 주는 개인 도구입니다.
[fmdz387/costats](https://github.com/fmdz387/costats) (MIT) 에서 출발했습니다. **이 저장소가 원본입니다** — `JHJ_DEV` 에는 지도 한 줄만 있습니다.

## 라이선스

MIT 입니다([LICENSE](LICENSE)). 원저작자 고지(`Copyright (c) 2026 fmdz`)는 MIT 가 요구하는 대로 **배포 꾸러미에 따라갑니다** —
업데이트 zip 안의 `LICENSE.txt` 와 exe 파일 속성의 「저작권」. 앱 화면에는 표시하지 않습니다(MIT 는 화면 표시를 요구하지 않습니다).

## 설치

[Releases](https://github.com/HyungJIn-kr-97/JHJ_AI_USAGE/releases) 에서 둘 중 하나를 받아 실행합니다. .NET 설치는 필요 없습니다.

| 파일 | 종류 | 하는 일 |
|---|---|---|
| `AiUsageMonitor-Setup.exe` (약 60KB) | **웹 설치 관리자** | 실행하면 GitHub 릴리스 목록을 읽어 버전을 고르게 하고(기본은 최신), 그 버전을 받아 설치한 뒤 실행합니다. 낮은 버전을 고르면 되돌리기가 됩니다. `--version 1.0.0` · `--silent` 인자를 받습니다. Windows 기본 .NET Framework 4.8 로 돕니다 |
| `AiUsageMonitor-win-x64-v<버전>.exe` (약 65MB) | **오프라인 설치 파일** | 인터넷 없이 그 버전 하나로 설치합니다. 설치 여부를 묻는 창에서 「예」를 고르면 설치본으로 다시 뜹니다 |

어느 쪽이든 `%LOCALAPPDATA%\AiUsageMonitor\app` 에 놓이고 시작 메뉴 바로가기(`AI 통합 사용량 모니터`)가 생깁니다.

- 단축키 `Ctrl+Alt+Y` 로 팝업을 열고 닫습니다. 설정 › 일반 「팝업 단축키 1」에서 바꾸고 「+ 단축키 추가」로 4개까지 늘릴 수 있습니다(권장 `Ctrl+Alt+F1~F12`, `Ctrl+C`·`Alt+F4` 같은 수정키 하나짜리·Win 조합·편집 키는 막습니다). 설정 창은 팝업 우측 아래 톱니입니다.
- 설치본은 시작할 때와 6시간마다 이 저장소의 최신 릴리스를 보고, 더 높은 버전이면 받아서 다음 실행 때 갈아 끼웁니다.
  설정 창 「일반」에서 지금 버전과 GitHub 최신 버전을 비교하고, `업데이트 확인` 으로 바로 받거나 목록에서 고른 버전을 `이 버전 설치` 로 깝니다(설치본에서만).
- 원본 `costats` 와 함께 띄워도 서로 덮어쓰지 않습니다(데이터 폴더·실행 이름·단축키가 다릅니다).

## 빌드 · 릴리스

`.NET 10 SDK` 가 필요합니다(`net10.0-windows`, WPF).

| 하려는 것 | 방법 |
|---|---|
| 개발 빌드 | `dotnet build 200.Source\costats.sln -c Release` → `200.Source\costats.App\bin\Release\…\AiUsageMonitor.exe`. 개발 실행이라 설치·업데이트는 동작하지 않습니다 |
| 설치 파일 · 업데이트 꾸러미 | `800.Deploy\Build-Exe.bat` 더블클릭(배포 버전을 올리려면 `Build-Exe.bat 1.0.1` — 날짜 자리는 빌드가 붙인다) → `800.Deploy\publish\` 에 `AiUsageMonitor-win-x64-v<버전>.zip`(업데이트용) · `AiUsageMonitor-win-x64-v<버전>.exe`(오프라인 설치) · `AiUsageMonitor-Setup.exe`(웹 설치 관리자)와 각각의 `.sha256` |
| 릴리스 | 태그 `v<버전>` 으로 GitHub Release 를 만들고 위 파일을 올립니다. 예: `gh release create v1.0.0.20261006 800.Deploy\publish\AiUsageMonitor-win-x64-v1.0.0.20261006.zip 800.Deploy\publish\AiUsageMonitor-win-x64-v1.0.0.20261006.zip.sha256 800.Deploy\publish\AiUsageMonitor-win-x64-v1.0.0.20261006.exe 800.Deploy\publish\AiUsageMonitor-Setup.exe`. 태그는 `v<배포 버전>.<빌드 날짜>` 이고 `publish.ps1` 이 마지막에 출력합니다(아래 「버전 체계」). 올린 뒤 `800.Deploy\prune-releases.ps1`(기본은 목록만, `-Apply` 로 삭제)로 같은 배포 버전의 옛 날짜 릴리스를 지웁니다. 함정: 앱의 업데이트와 웹 설치 관리자는 **zip 이름**으로 버전을 찾으므로 zip 은 빠뜨리면 안 됩니다 |

- `800.Deploy\publish\` 는 git 에 올라가지 않습니다.

## 버전 체계

**배포 버전은 앞 세 자리(`1.0.0`)이고, 릴리스·개발 빌드 모두 그 뒤에 빌드 날짜를 붙인 `1.0.0.20261004` 로 나갑니다.** 2026-10-04 에 1.0.x 계열을 정리하고 `1.0.0` 부터 다시 셉니다.

- **날짜는 빌드가 붙이고, 세 자리는 사람이 정합니다** — 세 자리를 올릴 때는 어느 자리를 올릴지 먼저 정하고 올립니다.
- **같은 배포 버전에는 가장 늦은 날짜의 릴리스 하나만 둡니다.** `1.0.0` 을 다시 내면 옛 `1.0.0.<날짜>` 는 지우고, `1.0.1` 로 올라가면 `1.0.0` 의 마지막 날짜판이 남습니다. 앱과 웹 설치 관리자의 버전 목록도 배포 버전마다 최신 날짜 하나만 보입니다.

| 자리 | 뜻 | 어디에 쓰이나 |
|---|---|---|
| `1.0.0` (Major.Minor.Patch) | **배포 버전** — 호환·기능 단위. 업데이트 비교의 앞자리 | `Directory.Build.props` 의 `VersionPrefix` 한 곳 |
| `.20261004` | **빌드 날짜**(`yyyyMMdd`) — 같은 배포 버전 안에서 어느 빌드인지 가르고, 업데이트 비교의 뒷자리가 된다 | GitHub Release 태그 `v1.0.0.20261004`, zip·exe 이름, 설정 창 「현재 v…」·하단 버전, exe 속성의 「제품 버전」 |

배포 버전을 올리는 기준은 [SemVer 2.0](https://semver.org/lang/ko/) 을 따릅니다 — 설정 파일·데이터 폴더가 호환되지 않게 바뀌면 Major, 기능을 더하면 Minor, 고치기만 했으면 Patch.

**일반 규칙과 비교 — 이 방식이 괜찮은 이유와 지키는 선**

| 규칙 | 4번째 자리를 어떻게 보나 | 이 저장소 |
|---|---|---|
| SemVer 2.0 | 세 자리만 정식. 빌드 정보는 `1.0.0+20261004` 처럼 `+` 뒤 「빌드 메타데이터」로 붙이고 **비교에서 무시**한다 | 다르다 — 날짜를 비교에 쓴다. 번호를 안 올리고도 고친 빌드를 업데이트로 내보내기 위해서다 |
| .NET / Windows 4자리 (`Major.Minor.Build.Revision`) | 4번째는 빌드·리비전 번호. 단 **각 자리는 0~65534** 라 `20261004` 를 못 담는다 | 그래서 `AssemblyVersion`·`FileVersion` 은 `1.0.0.0` 으로 두고, 날짜는 자유 문자열인 `InformationalVersion` 에만 넣는다 |
| CalVer (`2026.10.04`) | 날짜가 곧 버전 | 쓰지 않는다 — 배포 의미(호환 깨짐 여부)를 못 싣는다. 날짜는 개발 표시로만 |

- 함정: 하루에 여러 번 빌드하면 같은 날짜가 붙습니다 — 구분은 「제품 버전」 끝의 `+<커밋 해시>` 로 합니다(.NET SDK 가 자동으로 붙이고, 화면에는 `+` 앞만 보입니다).
- 계약: 업데이트 비교는 세 자리 다음에 날짜를 봅니다. **날짜만 바뀐 빌드도 업데이트로 배포됩니다.** 날짜가 없는 옛 릴리스(`v1.0.3`)는 같은 번호의 날짜판보다 낮게 봅니다.
- 함정: 같은 날 두 번 릴리스하면 태그가 같아집니다 — 앞선 릴리스를 지우고 다시 올립니다.
- 함정: 번호를 1.0.0 으로 되돌렸으므로 **이미 1.0.3 이 설치된 PC 는 1.0.0 을 「낮은 버전」으로 보고 자동 업데이트하지 않습니다.** 설정 「일반」에서 `1.0.0` 을 골라 `이 버전 설치`(되돌리기)하거나 웹 설치 관리자로 다시 설치합니다.
- 함정: 설치 폴더는 업데이트 때 **통째로 교체**됩니다. 그래서 설치 위치가 아닌 곳에서 띄운 exe 는 스스로 업데이트하지 않습니다.
- 함정: 이 저장소는 **공개**여야 합니다 — 앱이 토큰 없이 `api.github.com/repos/HyungJIn-kr-97/JHJ_AI_USAGE/releases/latest` 를 읽습니다.
  업데이트 저장소를 원본(`fmdz387/costats`)으로 적으면 원본 릴리스가 이 포크를 덮어씁니다.
- 빌드 중에는 이 폴더에서 띄운 개발 실행본을 bat 가 먼저 종료합니다(`bin\` 잠금). 설치본은 건드리지 않습니다.
- 원본의 GitHub Actions(`.github/`)는 뺐습니다 — 릴리스는 위 명령으로 사람이 올립니다. 원본에서 쓰지 않는 것(문서·MSIX 패키징·insights-cli·install.ps1)은 지웠습니다 — 필요하면 원본 저장소에서 봅니다.

## 원본과 다른 점

| 무엇 | 원본 | 이 포크 | 고친 파일 |
|---|---|---|---|
| 탭 배치 | Codex · Claude | Claude · Codex | `200.Source/costats.App/GlassWidgetWindow.xaml` |
| 처음 선택되는 탭 | Codex | Claude | `200.Source/costats.App/ViewModels/PulseViewModel.cs` |
| 이름 | `costats` · `costats.App.exe` | `AI 통합 사용량 모니터` / `AI Usage Monitor` · `AiUsageMonitor.exe`(프로젝트·네임스페이스는 `costats.*` 유지) | `costats.App.csproj` · `LocStrings.cs` · `TrayHost.cs` |
| 데이터 폴더 | `%LOCALAPPDATA%\costats` | `%LOCALAPPDATA%\AiUsageMonitor`(1.0.0 의 `costats-jhj` 는 첫 실행 때 자동 이동 — `Services/LegacyMigration.cs`) | `JsonSettingsStore.cs` · `JsonPulseSnapshotWriter.cs` · `ClaudeOAuthUsageFetcher.cs` · `App.xaml.cs` · `appsettings.json` |
| 단일 실행 이름 · 시작프로그램 이름 | `costats` | `AiUsageMonitor` | `App.xaml.cs` · `SettingsViewModel.cs` |
| 기본 단축키 | `Ctrl+Alt+U` | `Ctrl+Alt+Y` | `AppSettings.cs` · `appsettings.json` |
| 아이콘 | costats 로고 | JHJ 공통 아이콘(bull 색 바탕 + JHJ 모노그램) 위에 앱 유형 표지 — 앱은 `gauge`(사용량 막대), 웹 설치 관리자는 `install`(내려받기 화살표). 트레이 아이콘은 설정 「앱 설정 › 아이콘」에서 바꿉니다 — 기본 모양 4종(JHJ(기본) · 막대 · 링 · 반짝임, 테마 색을 따름)에 「+ 그리기」(16×16, 바탕만 깔린 칸에서 시작) · 「+ 불러오기」(가운데 정사각형으로 잘라 저장)로 **사용자 아이콘을 하나씩 더하고** ✕ 로 지웁니다. 기본 모양은 앱이 트레이 크기(16px × 화면 배율)로 정수 픽셀에 맞춰 직접 그리고, 사용자 그림은 `%LOCALAPPDATA%\AiUsageMonitor\icons\` 에 둡니다(`Services/TrayIconRenderer.cs`). 규격·생성기는 `JHJ_DEV/000.AGENTS_MD/070.아이콘/` (`python make_jhj_icon.py gauge <out.ico>`) | `200.Source/costats.App/Resources/tray-icon.ico` · `200.Source/costats.Setup/Resources/setup-icon.ico` |
| 설치 | zip + `install.ps1` | exe 를 실행하면 `%LOCALAPPDATA%\AiUsageMonitor\app` 에 스스로 설치 | `Services/SelfInstaller.cs` · `App.xaml.cs` |
| 자동 업데이트 | 원본 저장소 릴리스 | **이 저장소 릴리스만**(`Costats:Update:Repository`). 비어 있으면 꺼짐. 상태 폴더는 `%LOCALAPPDATA%\AiUsageMonitor\updates`, 설정 창 「일반」에 `업데이트 확인` 버튼 | `UpdateOptions.cs` · `StartupUpdateCoordinator.cs` · `apply-update.ps1` · `appsettings.json` · `Views/SettingsPanel.xaml` |
| 색·글꼴 | 라벤더 | JHJ 팔레트 5종(bull·navy·emerald·violet·slate) × 라이트·다크 · Pretendard. 팔레트는 설정 창 견본으로 고른다 | `Services/ThemeManager.cs` · `App.xaml.cs` · 창 XAML 3개 |
| 모델별 주간 한도 | 없음 | 주간 아래에 `주간 · Fable` 막대(응답 `limits[]` 의 `weekly_scoped`) | `ClaudeOAuthUsageFetcher.cs` · `UsagePulse.cs` · `ProviderPulseViewModel.cs` · `ModelWeekRow.cs` |
| 설정 창 구성 | 구분선 나열 | 위 「앱 설정」(일반·화면·아이콘) / 아래 「계정 · AI 도구」 두 탭. 계정 줄은 「편집」을 눌러야 명칭·유형 칸이 열리고 「저장」해야 반영됩니다 — 「계정」 아래는 Agent 마다(Claude·Codex), 「AI 도구」 아래는 연동 대상마다(Copilot·Gemini) 하위 탭. 설명은 툴팁으로, 탭·콤보는 테마 색 템플릿 | `Views/SettingsPanel.xaml` |
| 로그인 계정 표시 | 없음 | 설정 창 맨 위 「계정」에 Claude·Codex 계정 | `Services/AccountIdentityReader.cs` · `SettingsViewModel.cs` · `Views/SettingsPanel.xaml` |
| 버전 · 제작자 | 1.4.6 · fmdz | 1.0.0 · `제작 HyungJin Ju (메일 주소)` + GitHub 링크 한 줄. 원저작자 고지는 화면 대신 LICENSE·exe 속성(위 「라이선스」) | `200.Source/Directory.Build.props` · `costats.App.csproj` · `Views/SettingsPanel.xaml` |
| 제목 줄 · 테마 버튼 | 없음 | 상단 `AI 통합 사용량 모니터`(영어 `AI Usage Monitor`) 제목, 하단 반달 버튼으로 라이트/다크 전환(설정에 저장) | `GlassWidgetWindow.xaml` · `Services/ThemeManager.cs` · `AppSettings.cs` |
| 기간 선택 | 30일 고정 | Cost 옆 `7일`~`1년` 칩 6개(차트·모델 비중에 적용, 재시작하면 30일). 90·180일은 주 단위, 1년은 월 단위 막대 | `PulseViewModel.cs` · `ProviderPulseViewModel.cs` · `Services/UsageHistoryStore.cs` · `ExpenseAnalyzer.cs` |
| 모델별 사용량 | 없음 | 차트 아래 도넛 + 목록 4줄, 막대 툴팁에 그날의 모델 | `ProviderPulseViewModel.cs` · `ModelUsageRow.cs` |
| 사용량 조회용 계정 | multicc 설정 파일을 손으로 작성 | 설정 창의 계정은 「자리」다 — 「계정 추가 (브라우저 연동)」는 자리를 하나 만들고 바로 브라우저 로그인을 띄운다. 연동이 끝나면 **실제 메일 · 토큰 저장 · 다른 자리와 중복** 세 가지를 확인해 보이고, 틀렸으면 그 자리의 「다시 연동」으로 다른 계정을 고른다(자리는 실패해도 남는다). 로그인 중에는 「브라우저로 열기」·「Chrome 시크릿 창」(이미 로그인된 claude.ai 계정 대신 다른 계정을 고를 때)·「주소 복사」가 보인다 — 창 없이 띄운 CLI 는 브라우저를 스스로 열지 않아 앱이 주소를 받아 연다. 메인 팝업 우측 상단 드롭다운으로 계정 전환(All 포함). 계정이 하나면 그 메일 주소만 표시 | `Services/AccountProfileStore.cs` · `SettingsViewModel.cs` · `PulseViewModel.cs` · `AccountChip.cs` · `ExtraAccountRow.cs` |
| 로그인 | 각 CLI 를 사용자가 따로 실행 | 설정 창 「계정」의 Claude·Codex `로그인` 버튼이 공식 CLI 로그인(`claude auth login` · `codex login`)을 최소화된 터미널로 띄워 브라우저로 넘긴다. CLI 가 끝나면 계정을 다시 읽는다. Codex 는 PATH 에 없으면 VS Code 확장(`openai.chatgpt`)에 든 `codex.exe` 를 쓴다 | `Services/CliLocator.cs` · `SettingsViewModel.cs` · `Views/SettingsPanel.xaml` |
| 토큰 유형 통계 | 없음 | 모델 아래에 `토큰 유형` 구역 — 입력·출력·캐시 읽기·캐시 쓰기의 토큰 수와 비중(모델 줄 툴팁에도 유형별 토큰). 이력 파일에 유형 칸을 더했고, 그 전에 쌓인 날은 `유형 미상` 으로 모인다 | `Services/UsageHistoryStore.cs` · `ProviderPulseViewModel.cs` · `TokenTypeRow.cs` · `GlassWidgetWindow.xaml` |
| 차트 눈금 · 접기 | 시작일과 「오늘」만 | 차트 아래에 구간 시작일(달) 눈금, 최고값에 단위(`일·주·월 최고`). 차트·모델·토큰 유형 머리글을 누르면 접히고 상태는 설정에 저장. 창 높이는 본문에 맞춰 스스로 조정 | `GlassWidgetWindow.xaml` · `GlassWidgetWindow.xaml.cs` · `PulseViewModel.cs` · `AppSettings.cs` |
| 추가 Codex 계정 | 없음 | 설정 창 「계정」의 Codex 구역에서 추가·로그인·삭제. 계정마다 `%LOCALAPPDATA%\AiUsageMonitor\accounts-codex\<이름>\` 을 `CODEX_HOME` 으로 쓰고 `codex:<이름>` 으로 따로 집계한다. Codex 탭에서는 제목 줄 드롭다운으로 계정을 고른다(Claude 와 달리 「All」 쌓아 보기는 없다). 추가·삭제는 앱을 다시 띄워야 반영 | `Services/CodexAccountStore.cs` · `CodexLogSource.cs` · `CodexOAuthUsageFetcher.cs` · `UsageLogScanner.cs` · `LogDigestor.cs` · `PulseViewModel.cs` · `App.xaml.cs` |
| Gemini | 없음 | 4번째 탭(설정 창 「Gemini」에서 켬, 기본 꺼짐). 한도는 Code Assist 비공개 API(`retrieveUserQuota`)에서 Pro·Flash 계열의 일일 사용률, 일별 토큰·비용은 `~/.gemini/tmp/<프로젝트>/chats/session-*.json(l)` 에서 | `GeminiLogSource.cs` · `GeminiQuotaFetcher.cs` · `RateCard.cs` · `PulseViewModel.cs` · `GlassWidgetWindow.xaml` · `Views/SettingsPanel.xaml` |
| 언어 | 영어만 | 한국어 기본, 설정 창에서 한국어/English 전환(즉시 반영, 설정에 저장) | `Localization/Loc.cs` · `Localization/LocStrings.cs` · 창 XAML 3개 · 뷰모델 3개 · `TrayHost.cs` |
| 열 때 새로고침 | 선택 탭만 표시 없이 | 기본 꺼짐(백그라운드 주기 갱신만). 설정 창 「일반」의 `열 때마다 새로고침` 을 켜면 전체를 스피너와 함께 | `Services/TrayHost.cs` · `AppSettings.cs` · `Views/SettingsPanel.xaml` |
| 갱신 시각 | `방금 갱신` 같은 상대 표현만 | 뒤에 시각을 붙임(`방금 갱신 · 14:21`) | `ProviderPulseViewModel.cs` |
| 설정 화면 | 별도 창 | 팝업 창 안에서 화면 전환 — 톱니를 누르면 제목 줄과 하단 줄은 그대로 두고 가운데가 오른쪽에서 밀려 바뀐다. 제목 줄의 계정 자리는 `설정 ✕` 로 바뀌고, 그 칩 · 톱니 · `Esc` 로 돌아온다. 트레이 메뉴의 `설정` 은 팝업을 띄운 뒤 같은 화면으로 넘긴다 | `Views/SettingsPanel.xaml` · `GlassWidgetWindow.xaml` · `GlassWidgetWindow.xaml.cs` · `Services/TrayHost.cs` |
| 갱신 시각 맞춤 | 앱을 켠 때로부터 N분마다 | 시계 눈금에 맞춤(5분이면 :00 · :05 · :10 …). 상태 줄 아래에 `다음 갱신 10:55 · 4:50 후` 를 1초마다 다시 쓴다 | `costats.Application/Pulse/PulseOrchestrator.cs` · `IPulseOrchestrator.cs` · `PulseViewModel.cs` |
| 탭별 창 높이 | 데이터가 없는 탭은 구역이 사라져 높이가 달라짐 | 모든 탭이 같은 구역을 그린다 — 값이 없으면 `--`, 모델별 한도가 없는 도구는 `주간 · 모델별` 빈 줄. 비용에 `일 평균`(최근 30일 ÷ 30) 줄 추가 | `ProviderPulseViewModel.cs` · `GlassWidgetWindow.xaml` |
| 토큰 갱신용 CLI 찾기 | `where claude` 의 첫 줄 | Windows 가 실행할 수 있는 `.exe` · `.cmd` 만 고르고 `.cmd` 는 `cmd.exe` 로 띄운다(npm 설치본은 첫 줄이 확장자 없는 스크립트라 갱신이 조용히 실패해 한도가 0% 로 보였다) | `ClaudeOAuthUsageFetcher.cs` |
| 자동 실행 등록 | 설정에서 켤 때 한 번 | 설정이 켜져 있으면 시작할 때마다 현재 exe 경로로 다시 등록 | `App.xaml.cs` · `SettingsViewModel.cs` |
| 창 닫기 버튼 | 없음(바깥 클릭 · `Esc`) | 제목 줄 우측 끝 `✕` | `GlassWidgetWindow.xaml` |
| 일별 비용 차트 | 없음 | Cost 아래 막대(마우스를 올리면 날짜·비용·토큰) | `ProviderPulseViewModel.cs` · `DailyUsageBar.cs` · `GlassWidgetWindow.xaml` |
| 창 높이 · 닫기 | 580 | 690 · `Esc` 로 닫힘 | `GlassWidgetWindow.xaml` · `GlassWidgetWindow.xaml.cs` |

- 색 정본은 `JHJ_OPS/200.Source/100.Web/src/styles.css` 의 bull 팔레트입니다. 라이트·다크는 **앱을 시작할 때의 Windows 앱 모드**로 정해지고, 바꾸려면 앱을 다시 띄워야 합니다.
- 추가 계정은 `%LOCALAPPDATA%\AiUsageMonitor\accounts\<이름>\` 폴더를 `CLAUDE_CONFIG_DIR` 로 쓰고, 목록은 같은 폴더의 `config.json`(multicc 형식)에 둡니다. **로그인은 앱이 띄운 터미널에서 사용자가 직접** 하고, 추가·삭제는 앱을 다시 띄워야 반영됩니다. 삭제하면 그 폴더(로그인 토큰 포함)도 지웁니다.
- 일별 사용량은 `%LOCALAPPDATA%\AiUsageMonitor\history\<계정>.json` 에 쌓습니다. Claude Code 가 오래된 대화 기록을 지워도 여기 쌓인 날은 남습니다 — **이 폴더를 지우면 로그에 없는 과거는 되살릴 수 없습니다.**
- 계정은 `~/.claude.json` 의 `oauthAccount` 와 `~/.codex/auth.json` 의 `id_token` 에서 이메일만 읽습니다.
- 함정: Claude 는 메일(`.claude.json`)만 남고 토큰(`.credentials.json`)이 없을 수 있습니다 — 그때 설정 줄은 「토큰이 없음 — 다시 로그인」과 `로그인` 버튼을 보이고, 팝업은 한도를 비웁니다. 한도 캐시는 **받아 둔 계정 메일과 지금 로그인 메일이 같을 때만** 씁니다(다른 계정의 한도가 새 로그인 이름 아래 보이던 결함). 명칭·유형도 정할 때의 메일에 묶여, 같은 칸에 다른 계정으로 다시 로그인하면 따라가지 않습니다.
- 함정: 비용·토큰 통계(30일)는 이 PC 의 Claude Code 로그(`~/.claude/projects`)에서 오므로 **계정을 가리지 않습니다** — 한 PC 에서 두 계정을 번갈아 쓰면 합산됩니다. 계정별로 나뉘는 것은 세션·주간 한도뿐입니다.
- 차트의 날짜는 원본 집계를 그대로 써서 **UTC 기준**입니다. 한국 시간 오전 9시 이전 사용분은 전날 막대에 들어갑니다.
- Gemini 함정: 개인 Google 계정의 Gemini CLI 로그인은 2026-06-18 에 막혀 **한도는 Code Assist Standard/Enterprise 좌석에서만** 나옵니다. 그 밖에는 토큰·비용 이력만 보이고, 비용은 API 단가로 환산한 추정입니다. CLI 가 30일 지난 세션을 스스로 지우므로 그 이전은 앱의 `history` 폴더에 쌓인 날만 남습니다. 토큰 갱신에 필요한 OAuth 클라이언트 값은 소스에 넣지 않고 설치된 gemini-cli 번들에서 읽으며, 갱신한 토큰은 파일에 쓰지 않습니다.
- 탭 번호(0 = Codex, 1 = Claude, 2 = Copilot, 3 = Gemini)는 고정입니다. 0·1 은 원본 그대로입니다. 원본을 다시 받아 합칠 때 위 표의 파일만 다시 고치면 됩니다.
