using costats.Application.Settings;
using CoreStore = Jhj.Core.Settings.JsonSettingsStore<costats.Application.Settings.AppSettings>;

namespace costats.Infrastructure.Settings;

/// <summary>
/// 설정 한 벌을 %LOCALAPPDATA%\JHJ_AI-Usage-Monitor\settings.json 에 둔다.
/// 계약: 형식·원자적 쓰기·깨짐 복구(settings.bad.json)는 JHJ_CS_CORE 가 한다 — 여기는 인터페이스만 잇는다.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly CoreStore _store = new();

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => _store.LoadAsync(cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => _store.SaveAsync(settings, cancellationToken);
}
