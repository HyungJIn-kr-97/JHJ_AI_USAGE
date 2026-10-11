using Microsoft.Extensions.Configuration;
using CoreOptions = Jhj.Core.Updates.UpdateOptions;

namespace costats.App.Services.Updates;

/// <summary>
/// 이 앱의 배포 좌표 — 저장소 · 꾸러미 이름 · 설치 관리자 주소 · winget 식별자.
/// 계약: 확인·내려받기·교체는 JHJ_CS_CORE 의 GithubUpdateService 가 한다 — 여기는 **이 앱의 이름들**만 갖는다.
/// 함정: Repository 를 원본(fmdz387/costats)으로 두면 원본 릴리스가 이 포크를 덮어쓴다.
/// </summary>
public static class UpdateOptions
{
    public const string DefaultRepository = "HyungJIn-kr-97/JHJ_AI_USAGE";

    /// <summary>
    /// 업데이트 꾸러미 이름의 접두 — 실제 자산은 &lt;접두&gt;-&lt;rid&gt;-v&lt;버전&gt;.zip 이다.
    /// 계약: 800.Deploy\publish.ps1 이 만드는 zip 이름과 **반드시** 같아야 한다.
    /// 함정: 이미 깔린 1.0.5~1.0.12 가 이 옛 이름으로만 다음 버전을 찾는다 — 바꾸려면 옛 이름을 한 세대 더 함께 올린다.
    ///       자세한 전환 범위는 300.Docs\실행파일-이름-전환.md.
    /// </summary>
    public const string PackagePrefix = "AiUsageMonitor";

    // 계약: 설치 파일 고정 주소 — latest/download 라 언제나 최신 설치 관리자를 받는다
    public const string SetupAssetName = "JHJ_AI-Usage-Monitor_Setup.exe";

    public static string SetupFileUrl => $"https://github.com/{DefaultRepository}/releases/latest/download/{SetupAssetName}";

    // 계약: 800.Deploy\winget-manifest.ps1 의 PackageIdentifier 와 같아야 한다
    public const string WingetId = "HyungJin.JHJ_AI-Usage-Monitor";

    public static string WingetCommand => "winget install " + WingetId;

    // 계약: 구역 이름은 appsettings.json 의 "Costats:Update" 그대로다 — 이미 깔린 PC 의 설정 파일이 그 이름을 쓴다
    public const string SectionName = "Costats:Update";

    public static CoreOptions FromConfiguration(IConfiguration configuration) =>
        CoreOptions.FromConfiguration(configuration, DefaultRepository, PackagePrefix, SectionName);
}
