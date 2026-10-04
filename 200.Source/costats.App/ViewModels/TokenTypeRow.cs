using System.Windows.Media;

namespace costats.App.ViewModels;

/// <summary>
/// 토큰 유형(입력 · 출력 · 캐시 읽기 · 캐시 쓰기) 한 줄.
/// 계약: Share 는 0~1 — 선택한 기간의 전체 토큰 중 이 유형의 몫이다.
/// </summary>
public sealed record TokenTypeRow(string Name, string TokensText, string ShareText, double Share, Brush Color, string Tooltip);
