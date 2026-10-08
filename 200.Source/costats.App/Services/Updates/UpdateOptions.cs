using Microsoft.Extensions.Configuration;

namespace costats.App.Services.Updates;

public sealed class UpdateOptions
{
    // 함정: Repository 를 원본(fmdz387/costats)으로 두면 원본 릴리스가 이 포크를 덮어쓴다 — 포크 릴리스 저장소만 적는다
    // 계약: Repository 가 비어 있으면 Enabled 값과 무관하게 업데이트는 꺼진다
    public const string DefaultRepository = "HyungJIn-kr-97/JHJ_AI_USAGE";

    // 계약: 설치 파일 고정 주소 — latest/download 라 언제나 최신 설치 관리자를 받는다. 파일명은 publish.ps1 이 올리는 자산 이름과 같아야 한다
    public static string SetupFileUrl => "https://github.com/" + DefaultRepository + "/releases/latest/download/AI-Usage-Monitor_JHJ_Setup.exe";

    // 계약: winget 패키지 식별자 — 800.Deploy\winget-manifest.ps1 의 PackageIdentifier 와 같아야 한다
    public const string WingetId = "HyungJin.AI-Usage-Monitor_JHJ";

    public static string WingetCommand => "winget install " + WingetId;
    public bool Enabled { get; init; } = false;
    public string Repository { get; init; } = DefaultRepository;
    public int CheckIntervalHours { get; init; } = 6;
    public bool AllowPrerelease { get; init; } = false;
    public bool ApplyStagedUpdateOnStartup { get; init; } = true;

    public static UpdateOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Costats:Update");

        var repository = string.IsNullOrWhiteSpace(section["Repository"]) ? DefaultRepository : section["Repository"]!.Trim();

        return new UpdateOptions
        {
            Enabled = GetBool(section["Enabled"], defaultValue: true) && repository.Length > 0,
            Repository = repository,
            CheckIntervalHours = Math.Clamp(GetInt(section["CheckIntervalHours"], defaultValue: 6), 1, 168),
            AllowPrerelease = GetBool(section["AllowPrerelease"], defaultValue: false),
            ApplyStagedUpdateOnStartup = GetBool(section["ApplyStagedUpdateOnStartup"], defaultValue: true)
        };
    }

    private static bool GetBool(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static int GetInt(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
