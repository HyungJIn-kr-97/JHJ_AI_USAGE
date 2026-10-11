using System.Windows.Input;
using costats.Application.Settings;
using Microsoft.Extensions.Logging;
using CoreHotkeys = Jhj.Core.Wpf.Hotkeys.HotkeyService;

namespace costats.App.Services
{
    /// <summary>
    /// 팝업 열고 닫기 단축키. 0번이 「팝업 단축키 1」(AppSettings.Hotkey), 1번부터가 AppSettings.ExtraHotkeys 다.
    /// 계약: 해석·등록·규칙 검사는 JHJ_CS_CORE 가 한다(HotkeyRules·HotkeyService) — 여기는 **이 앱의 설정 두 칸**만 잇는다.
    /// 계약: 자리표는 JHJ_DEV/000.AGENTS_MD/060.단축키/060.단축키-대장.md 다 — AI Usage 는 2번(Ctrl+Alt+2)이다.
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        public const int MaxCount = CoreHotkeys.MaxCount;

        // 왜: Core 가 1번 칸을 되살릴 때 쓰는 값이다 — 안 맞추면 설정이 깨졌을 때 자리표 밖의 키로 산다
        static HotkeyService() => Jhj.Core.Wpf.Hotkeys.HotkeyRules.Default = AppSettings.DefaultHotkey;

        private readonly ILogger<HotkeyService> _logger;
        private readonly CoreHotkeys _inner;

        // 왜: SettingsViewModel → TrayHost → … 순환 때문에 생성자 주입 대신 하나뿐인 인스턴스를 연다
        public static HotkeyService? Current { get; private set; }

        public HotkeyService(TrayHost trayHost, AppSettings settings, ILogger<HotkeyService> logger)
        {
            _logger = logger;
            Current = this;
            _inner = new CoreHotkeys(Slots(settings), onPressed: trayHost.ToggleWidget, warn: Warn);
        }

        /// <summary>설정의 단축키로 다시 맞춘다 — 「기본값으로」·「설정 초기화」가 부른다.</summary>
        public void Reload(AppSettings settings) => _inner.Reload(Slots(settings));

        public IReadOnlyList<string> Texts => _inner.Texts;

        public bool TrySet(int index, Key key, ModifierKeys modifiers) => _inner.TrySet(index, key, modifiers);

        public void RemoveAt(int index) => _inner.RemoveAt(index);

        public void Suspend() => _inner.Suspend();

        public void Resume() => _inner.Resume();

        public void Dispose() => _inner.Dispose();

        private static IEnumerable<string> Slots(AppSettings settings) =>
            new[] { settings.Hotkey }.Concat(settings.ExtraHotkeys);

        private void Warn(string message, Exception? ex) => _logger.LogWarning(ex, "{Message}", message);
    }
}
