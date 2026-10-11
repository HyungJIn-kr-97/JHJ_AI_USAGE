using System.Runtime.InteropServices;
using costats.Application.Settings;

namespace costats.App.Services;

/// <summary>
/// 이 기록을 남긴 장비가 어디인지 — 진단 기록·연동 이력·표시 데이터가 같은 값을 쓴다.
/// 왜: 한 AI 계정을 여러 장비·사용자가 함께 쓴다 — 나중에 기록을 한데 모을 때 줄마다 어느 장비 것인지 가릴 키가 필요하다.
/// 계약: Id 는 설치마다 한 번 만드는 GUID 다 — PC 이름은 바뀌거나 겹칠 수 있어 키로 쓰지 않는다.
/// 계약: 값은 이 PC 의 파일에만 남는다 — 어디로도 보내지 않는다.
/// </summary>
/// <param name="Label">사용자가 붙인 명칭. 안 붙였으면 null.</param>
public sealed record DeviceFacts(string Id, string Name, string? Label, string Os, string Arch);

public static class DeviceInfo
{
    private static AppSettings? _settings;
    private static bool _created;

    /// <summary>설정에서 장비 ID 를 읽고, 없으면 만든다. 여러 번 불러도 된다.</summary>
    public static void Configure(AppSettings settings)
    {
        _settings = settings;
        if (string.IsNullOrWhiteSpace(settings.DeviceId))
        {
            settings.DeviceId = Guid.NewGuid().ToString("N");
            _created = true;
        }
    }

    /// <summary>방금 만든 ID 가 아직 저장되지 않았나 — 한 번 true 를 주고 내린다(저장은 부른 쪽이 한다).</summary>
    public static bool TakeCreated()
    {
        var created = _created;
        _created = false;
        return created;
    }

    public static DeviceFacts Current => new(
        _settings?.DeviceId ?? string.Empty,
        Environment.MachineName,
        string.IsNullOrWhiteSpace(_settings?.DeviceLabel) ? null : _settings!.DeviceLabel.Trim(),
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString());

    /// <summary>화면·이력에 보일 이름 — 사용자가 붙인 명칭, 없으면 PC 이름.</summary>
    public static string DisplayName => Current.Label ?? Current.Name;
}
