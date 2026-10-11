using System.Windows;
using Jhj.Core.Wpf.Theme;

namespace costats.App.Services;

/// <summary>
/// 이 앱만 쓰는 테마 브러시 — 공급자 색과 막대 색. 공통 브러시는 JHJ_CS_CORE 의 JhjTheme 이 넣는다.
/// 계약: 키 이름은 XAML 이 DynamicResource 로 물고 있다 — 바꾸면 그 자리가 기본색으로 돌아간다.
/// </summary>
internal static class ThemeExtras
{
    public static void Add(ResourceDictionary dict, JhjTone tone, bool dark)
    {
        dict["SessionAccent"] = JhjTheme.Frozen(tone.Accent);
        dict["WeeklyAccent"] = JhjTheme.Frozen(tone.Brand);
        dict["CostTodayAccent"] = JhjTheme.Frozen(tone.Accent);
        dict["CostMonthAccent"] = JhjTheme.Frozen(tone.Brand);
        dict["SessionBarBrush"] = JhjTheme.Frozen(tone.Accent);
        dict["SessionTextBrush"] = JhjTheme.Frozen(tone.Accent);
        dict["WeeklyBarBrush"] = JhjTheme.Frozen(tone.Brand);
        dict["WeeklyTextBrush"] = JhjTheme.Frozen(tone.Brand);
        dict["SuccessBrush"] = dict["OkBrush"];

        // 계약: 요금제 글자색은 도구마다 다르다 — 팝업에서 어느 도구인지 색으로 가린다
        dict["PlanTextCodexBrush"] = JhjTheme.Frozen(dark ? "#34D399" : "#047857");
        dict["PlanTextClaudeBrush"] = JhjTheme.Frozen(dark ? "#FDBA74" : "#C2410C");
        dict["PlanTextCopilotBrush"] = JhjTheme.Frozen(dark ? "#C4B5FD" : "#4C1D95");
        dict["PlanTextGeminiBrush"] = JhjTheme.Frozen(dark ? "#93C5FD" : "#1E40AF");
    }
}
