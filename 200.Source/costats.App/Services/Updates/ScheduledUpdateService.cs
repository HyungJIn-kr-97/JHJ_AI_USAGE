using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using costats.App.Localization;
using costats.Application.Settings;

namespace costats.App.Services.Updates;

/// <summary>
/// 사용자가 정한 시각(AppSettings.AutoUpdateTime)에 업데이트를 확인해 받아 두고 바로 적용한다 — 설치본에서만 돈다.
/// 왜: 트레이 앱은 며칠씩 떠 있어 「다음 실행 때 교체」만으로는 새 버전이 적용되지 않는다.
/// 계약: UI 스레드에서 만든다 — DispatcherTimer 와 상태 문구 콜백이 UI 스레드로 온다.
/// </summary>
public sealed class ScheduledUpdateService : IDisposable
{
    public const string DefaultTime = "04:00";

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    private readonly StartupUpdateCoordinator _coordinator;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private DateTime _dueAt;
    private bool _running;

    public ScheduledUpdateService(StartupUpdateCoordinator coordinator, AppSettings settings)
    {
        _coordinator = coordinator;
        _settings = settings;
        Reschedule();

        // 왜: 타이머는 절전 동안 멈춘다 — 긴 간격 하나로 기다리지 않고 30초마다 벽시계와 견줘, 깨어난 뒤 지나친 시각을 바로 잡는다
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>적용 전 알림 — (제목, 본문). 트레이가 붙인다.</summary>
    public Action<string, string>? Notify { get; set; }

    /// <summary>진행 문구 — 설정 화면의 업데이트 상태 줄이 받는다.</summary>
    public event Action<string>? StatusChanged;

    public DateTime DueAt => _dueAt;

    /// <summary>"4:5" 같은 입력을 "04:05" 로 맞춘다 — 시각으로 읽히지 않으면 null.</summary>
    public static string? NormalizeTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm", "H:m"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : null;

    /// <summary>설정(시각·켬/끔)이 바뀌면 부른다 — 다음 발생 시각을 다시 잡는다.</summary>
    public void Reschedule()
    {
        var time = TimeOnly.ParseExact(NormalizeTime(_settings.AutoUpdateTime) ?? DefaultTime, "HH:mm", CultureInfo.InvariantCulture);
        var now = DateTime.Now;
        var today = now.Date + time.ToTimeSpan();
        _dueAt = today > now ? today : today.AddDays(1);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_running || DateTime.Now < _dueAt)
        {
            return;
        }

        Reschedule();
        if (_settings.AutoUpdateEnabled && _coordinator.CanInstall)
        {
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        _running = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var result = await Task.Run(() => _coordinator.CheckAndStageUpdateAsync(cts.Token, forceCheck: true), cts.Token);
            if (result is not (UpdateCheckResult.UpdateStaged or UpdateCheckResult.UpdateAlreadyStaged))
            {
                return;
            }

            var version = await Task.Run(() => _coordinator.GetStagedVersionAsync(cts.Token), cts.Token);
            if (version is null)
            {
                return;
            }

            var label = "v" + StartupUpdateCoordinator.Display(version);
            if (_settings.AutoUpdateNotify)
            {
                var notice = Loc.T("Updating to {0} in 1 minute. The app restarts by itself.", label);
                Notify?.Invoke(Loc.T("AI Usage Monitor"), notice);
                StatusChanged?.Invoke(notice);
                await Task.Delay(Grace, cts.Token);
                if (!_settings.AutoUpdateEnabled)
                {
                    StatusChanged?.Invoke(Loc.T("Scheduled update was cancelled."));
                    return;
                }
            }

            StatusChanged?.Invoke(Loc.T("Installing {0}...", label));
            if (await Task.Run(() => _coordinator.TryApplyPendingUpdateAsync(cts.Token, manualTrigger: true), cts.Token))
            {
                // 왜: 교체 스크립트는 이 프로세스가 끝나기를 기다린다 — 재시작 문구를 1.5초 보여 준 뒤 닫아도 늦지 않다
                StatusChanged?.Invoke(Loc.T("Installed {0}. The app restarts in a moment.", label));
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                System.Windows.Application.Current.Shutdown(0);
                return;
            }

            StatusChanged?.Invoke(Loc.T("Could not install {0}.", label));
        }
        catch (OperationCanceledException)
        {
            Trace.WriteLine("[costats-update] scheduled update timed out");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[costats-update] scheduled update failed: {ex}");
        }
        finally
        {
            _running = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
