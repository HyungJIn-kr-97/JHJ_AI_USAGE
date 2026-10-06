using System.Windows.Input;
using costats.Application.Settings;
using Microsoft.Extensions.Logging;
using NHotkey;
using NHotkey.Wpf;

namespace costats.App.Services
{
    /// <summary>
    /// 팝업 열고 닫기 단축키. 0번이 「팝업 단축키 1」(AppSettings.Hotkey), 1번부터가 AppSettings.ExtraHotkeys 다.
    /// 규칙(막는 키·권장 키)은 HotkeyRules.
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        public const int MaxCount = 4;
        private readonly ILogger<HotkeyService> _logger;
        private readonly TrayHost _trayHost;
        private readonly List<(Key Key, ModifierKeys Modifiers)> _slots = [];
        private bool _suspended;

        // 왜: SettingsViewModel → TrayHost → … 순환 때문에 생성자 주입 대신 하나뿐인 인스턴스를 연다
        public static HotkeyService? Current { get; private set; }

        public HotkeyService(TrayHost trayHost, AppSettings settings, ILogger<HotkeyService> logger)
        {
            _logger = logger;
            _trayHost = trayHost;
            Current = this;

            foreach (var text in new[] { settings.Hotkey }.Concat(settings.ExtraHotkeys).Take(MaxCount))
            {
                // 왜: 손으로 고친 설정 파일에 Ctrl+C 같은 막힌 키가 있으면 1번은 기본값으로, 나머지는 버린다
                if (HotkeyRules.TryParse(text, out var key, out var modifiers)
                    && HotkeyRules.Check(key, modifiers).Verdict != HotkeyVerdict.Blocked
                    && !_slots.Contains((key, modifiers)))
                {
                    _slots.Add((key, modifiers));
                }
                else if (_slots.Count == 0)
                {
                    _logger.LogWarning("Hotkey '{Hotkey}' is invalid or blocked; using {Default}", text, HotkeyRules.Default);
                    HotkeyRules.TryParse(HotkeyRules.Default, out key, out modifiers);
                    _slots.Add((key, modifiers));
                }
            }

            RegisterAll();
        }

        public IReadOnlyList<string> Texts => _slots.Select(s => HotkeyRules.Format(s.Key, s.Modifiers)).ToList();

        /// <summary>계약: index == Count 면 새로 붙인다. 실패하면 그 자리를 되돌리고 false — 다른 프로그램이 이미 쓰는 키다.</summary>
        public bool TrySet(int index, Key key, ModifierKeys modifiers)
        {
            try
            {
                HotkeyManager.Current.AddOrReplace(NameOf(index), key, modifiers, OnPressed);
            }
            catch (HotkeyAlreadyRegisteredException)
            {
                RegisterAll();
                return false;
            }

            if (index < _slots.Count)
            {
                _slots[index] = (key, modifiers);
            }
            else
            {
                _slots.Add((key, modifiers));
            }

            // 함정: 입력란에 초점이 있는 동안 등록해 두면 같은 키를 다시 누를 때 팝업이 닫힌다 — 확인만 하고 내린다
            if (_suspended)
            {
                HotkeyManager.Current.Remove(NameOf(index));
            }

            return true;
        }

        // 계약: 0번은 지울 수 없다 — 단축키가 하나도 없으면 팝업을 열 길이 트레이 아이콘뿐이다
        public void RemoveAt(int index)
        {
            if (index <= 0 || index >= _slots.Count)
            {
                return;
            }

            UnregisterAll();
            _slots.RemoveAt(index);
            if (!_suspended)
            {
                RegisterAll();
            }
        }

        // 계약: 설정의 단축키 입력란이 키를 받는 동안만 내린다 — Resume 이 다시 건다
        public void Suspend()
        {
            _suspended = true;
            UnregisterAll();
        }

        public void Resume()
        {
            if (_suspended)
            {
                _suspended = false;
                RegisterAll();
            }
        }

        public void Dispose() => UnregisterAll();

        private static string NameOf(int index) => index == 0 ? "ToggleWidget" : $"ToggleWidget{index + 1}";

        private void OnPressed(object? sender, HotkeyEventArgs e) => _trayHost.ToggleWidget();

        private void RegisterAll()
        {
            UnregisterAll();
            for (var i = 0; i < _slots.Count; i++)
            {
                try
                {
                    HotkeyManager.Current.AddOrReplace(NameOf(i), _slots[i].Key, _slots[i].Modifiers, OnPressed);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to register hotkey {Index}", i + 1);
                }
            }
        }

        private void UnregisterAll()
        {
            for (var i = 0; i < MaxCount; i++)
            {
                try
                {
                    HotkeyManager.Current.Remove(NameOf(i));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to unregister hotkey {Index}", i + 1);
                }
            }
        }
    }
}
