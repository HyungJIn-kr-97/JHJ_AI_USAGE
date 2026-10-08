using System.Windows.Input;

namespace costats.App.Services
{
    public enum HotkeyVerdict { Recommended, Allowed, Caution, Blocked }

    /// <summary>
    /// 팝업 단축키의 해석·표기·검사. 막는 것 — 수정키 하나짜리(Ctrl+C·Alt+F4) · Win 조합 · Shift 만 · 탐색·편집 키(Esc·Tab·Delete·방향키 등).
    /// 권장은 Ctrl+Alt+F1~F12 다 — 시스템·브라우저·IDE 가 거의 쓰지 않는다.
    /// </summary>
    public static class HotkeyRules
    {
        // 계약: 기본 단축키는 Ctrl+Alt+A 다 — AppSettings.Hotkey 의 기본값과 같은 값이어야 한다(층이 달라 상수를 공유하지 못한다)
        public const string Default = "Ctrl+Alt+A";

        public static bool TryParse(string? text, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (var token in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (token.ToLowerInvariant())
                {
                    case "ctrl" or "control": modifiers |= ModifierKeys.Control; continue;
                    case "alt": modifiers |= ModifierKeys.Alt; continue;
                    case "shift": modifiers |= ModifierKeys.Shift; continue;
                    case "win" or "windows": modifiers |= ModifierKeys.Windows; continue;
                }

                // 함정: Enum.TryParse 는 "1" 을 숫자 값 1(Key.Cancel)로 읽는다 — 숫자 한 자리는 D0~D9 로 따로 바꾼다
                if (token.Length == 1 && char.IsDigit(token[0]))
                {
                    key = Key.D0 + (token[0] - '0');
                }
                else if (!int.TryParse(token, out _) && Enum.TryParse(token, true, out Key parsed) && Enum.IsDefined(parsed))
                {
                    key = parsed;
                }
                else
                {
                    return false;
                }
            }

            return key != Key.None;
        }

        public static string Format(Key key, ModifierKeys modifiers)
        {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            if (key != Key.None && !IsModifierKey(key))
            {
                parts.Add(key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString() : key.ToString());
            }

            return string.Join("+", parts);
        }

        public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

        /// <summary>계약: Message 는 영어 원문이다 — 화면에 낼 때 Loc.T 로 옮긴다.</summary>
        public static (HotkeyVerdict Verdict, string Message) Check(Key key, ModifierKeys modifiers)
        {
            var isLetter = key is >= Key.A and <= Key.Z;
            var isDigit = key is >= Key.D0 and <= Key.D9;
            var isFunction = key is >= Key.F1 and <= Key.F24;
            var count = new[] { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Shift }.Count(m => modifiers.HasFlag(m));

            if (key == Key.None || IsModifierKey(key))
            {
                return (HotkeyVerdict.Blocked, "Press a letter, number or F1–F12 together with Ctrl and Alt.");
            }

            if (modifiers.HasFlag(ModifierKeys.Windows))
            {
                return (HotkeyVerdict.Blocked, "Win key combinations are reserved by Windows.");
            }

            if (!isLetter && !isDigit && !isFunction)
            {
                return (HotkeyVerdict.Blocked, "Esc, Tab, Space, Enter, Delete, arrows and other editing keys are used by Windows and apps.");
            }

            if (!modifiers.HasFlag(ModifierKeys.Control) && !modifiers.HasFlag(ModifierKeys.Alt))
            {
                return (HotkeyVerdict.Blocked, "Include Ctrl or Alt — Shift alone just types the character.");
            }

            if (count < 2)
            {
                return (HotkeyVerdict.Blocked, "A single modifier (like Ctrl+C or Alt+F4) clashes with app shortcuts. Use two, e.g. Ctrl+Alt.");
            }

            if (isFunction && modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key <= Key.F12)
            {
                return (HotkeyVerdict.Recommended, "Recommended — Windows and common apps rarely use Ctrl+Alt+F1–F12.");
            }

            // 계약: 기본 단축키(Default)도 권장이다 — 기본값이 「적용됨」으로만 뜨면 덜 좋은 선택처럼 보인다
            if (key == Key.A && modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
            {
                return (HotkeyVerdict.Recommended, "Recommended — the default (A for AI).");
            }

            if (count == 2 && modifiers.HasFlag(ModifierKeys.Shift) && !isFunction)
            {
                return (HotkeyVerdict.Caution, "Applied, but browsers and VS Code use many Ctrl+Shift / Alt+Shift letters (e.g. Ctrl+Shift+T, P, N).");
            }

            return (HotkeyVerdict.Allowed, "Applied.");
        }
    }
}
