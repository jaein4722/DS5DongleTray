namespace DS5DongleTray;

internal sealed record UpdateCheckResult(
    FirmwareVersionStatus Status,
    string? CurrentVersion,
    FirmwareRelease? LatestRelease)
{
    public bool IsUpdateAvailable => Status == FirmwareVersionStatus.UpdateAvailable && LatestRelease is not null;
}
