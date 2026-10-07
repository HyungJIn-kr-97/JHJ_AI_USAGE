using System.Windows.Media;

namespace costats.App.ViewModels;

/// <summary>
/// 프로그램(데스크톱 앱 · VS Code · 터미널 CLI · 에이전트 모드) 한 줄.
/// 계약: Share 는 0~1 — 선택한 기간의 전체 비용 중 이 프로그램의 몫이다. 토큰은 비중을 매기지 않고 수만 보인다.
/// </summary>
public sealed record ProgramUsageRow(string Name, string CostText, string TokensText, string ShareText, double Share, Brush Color, string Tooltip);
