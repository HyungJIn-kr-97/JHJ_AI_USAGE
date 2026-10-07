namespace costats.App.Services.Updates;

public enum UpdateCheckResult
{
    UpToDate,
    UpdateStaged,
    UpdateAlreadyStaged,
    Skipped,
    Disabled,
    AlreadyRunning,
    CheckFailed
}

public enum UpdateStage
{
    Downloading,
    Verifying,
    Extracting
}

/// <summary>계약: Done·Total 은 바이트, Total 이 0 이하면 크기를 모르는 내려받기다.</summary>
public readonly record struct UpdateProgress(UpdateStage Stage, long Done, long Total);
