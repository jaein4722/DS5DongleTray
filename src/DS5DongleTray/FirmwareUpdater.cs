using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace DS5DongleTray;

internal sealed class FirmwareUpdater
{
    private const string OfficialRepository = "awalol/DS5Dongle";
    private const string CustomRepository = "jaein4722/DS5Dongle";
    private readonly DongleHidClient client;
    private readonly HttpClient httpClient = new();

    public FirmwareUpdater(DongleHidClient client)
    {
        this.client = client;
        httpClient.Timeout = TimeSpan.FromSeconds(15);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DS5DongleTray", "1.0"));
    }

    public async Task<FirmwareRelease> GetLatestReleaseAsync(FirmwareReleaseChannel channel, CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(channel);
        using var stream = await httpClient.GetStreamAsync($"https://api.github.com/repos/{repository}/releases/latest", cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "latest";
        var htmlUrl = root.GetProperty("html_url").GetString() ?? $"https://github.com/{repository}/releases";
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new FirmwareAsset(
                asset.GetProperty("name").GetString() ?? "",
                asset.GetProperty("browser_download_url").GetString() ?? ""))
            .Where(asset => asset.Name.EndsWith(".uf2", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var selectedAsset = SelectStandardFirmwareAsset(assets)
            ?? throw new InvalidOperationException("Latest release does not contain a standard .uf2 firmware asset.");

        return new FirmwareRelease(tag, htmlUrl, selectedAsset, channel, repository);
    }

    public async Task<string?> GetCurrentFirmwareVersionAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await client.ReadSnapshotAsync().ConfigureAwait(false);
        return snapshot.DeviceFound ? snapshot.FirmwareVersion : null;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var currentFirmware = await GetCurrentFirmwareVersionAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(currentFirmware))
        {
            return new UpdateCheckResult(FirmwareVersionStatus.Unknown, currentFirmware, null);
        }

        var channel = GetChannelForFirmware(currentFirmware);
        var release = await GetLatestReleaseAsync(channel, cancellationToken).ConfigureAwait(false);
        return new UpdateCheckResult(GetVersionStatus(currentFirmware, release), currentFirmware, release);
    }

    public static FirmwareVersionStatus GetVersionStatus(string? currentVersion, FirmwareRelease latestRelease)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return FirmwareVersionStatus.Unknown;
        }

        var current = NormalizeVersion(currentVersion);
        var latest = NormalizeVersion(latestRelease.Tag);

        if (string.Equals(current, latest, StringComparison.OrdinalIgnoreCase))
        {
            return FirmwareVersionStatus.UpToDate;
        }

        var latestBase = NormalizeVersion(StripCustomSuffix(latestRelease.Tag));
        if (!string.IsNullOrWhiteSpace(latestBase) &&
            string.Equals(current, latestBase, StringComparison.OrdinalIgnoreCase))
        {
            return FirmwareVersionStatus.UpToDate;
        }

        return FirmwareVersionStatus.UpdateAvailable;
    }

    public async Task<FirmwareDownloadResult> DownloadLatestAsync(FirmwareReleaseChannel channel, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking latest release...");
        var release = await GetLatestReleaseAsync(channel, cancellationToken).ConfigureAwait(false);

        progress?.Report($"Downloading {release.Asset.Name}...");
        var downloadPath = GetDownloadPath(release);
        await DownloadFileAsync(release.Asset.DownloadUrl, downloadPath, cancellationToken).ConfigureAwait(false);
        progress?.Report($"Downloaded to {downloadPath}.");

        return new FirmwareDownloadResult(release, downloadPath);
    }

    public async Task<FirmwareUpdateResult> DownloadAndInstallLatestAsync(FirmwareReleaseChannel channel, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var download = await DownloadLatestAsync(channel, progress, cancellationToken).ConfigureAwait(false);

        var destination = await InstallFirmwareFileAsync(download.DownloadPath, download.Release.Asset.Name, progress, cancellationToken).ConfigureAwait(false);
        return new FirmwareUpdateResult(download.Release, destination);
    }

    public async Task<string> InstallLocalFirmwareAsync(string firmwarePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(firmwarePath))
        {
            throw new ArgumentException("Firmware path is empty.", nameof(firmwarePath));
        }

        if (!File.Exists(firmwarePath))
        {
            throw new FileNotFoundException("Firmware file was not found.", firmwarePath);
        }

        if (!firmwarePath.EndsWith(".uf2", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Please select a .uf2 firmware file.");
        }

        return await InstallFirmwareFileAsync(firmwarePath, Path.GetFileName(firmwarePath), progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnterBootloaderOnlyAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Requesting UF2 bootloader...");
        await client.EnterBootloaderAsync().ConfigureAwait(false);

        progress?.Report("Waiting for UF2 drive...");
        _ = await WaitForUf2DriveAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("UF2 bootloader drive was not detected. This firmware may not support bootloader entry yet.");

        progress?.Report("UF2 bootloader drive detected.");
    }

    private async Task<string> InstallFirmwareFileAsync(string firmwarePath, string fileName, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Requesting UF2 bootloader...");
        await client.EnterBootloaderAsync().ConfigureAwait(false);

        progress?.Report("Waiting for UF2 drive...");
        var drive = await WaitForUf2DriveAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("UF2 bootloader drive was not detected. This firmware may not support bootloader entry yet.");

        progress?.Report($"Copying firmware to {drive.Name}...");
        var destination = Path.Combine(drive.RootDirectory.FullName, fileName);
        await Task.Run(() => File.Copy(firmwarePath, destination, overwrite: true), cancellationToken).ConfigureAwait(false);

        progress?.Report("Firmware copied. Waiting for device reboot...");
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);

        return destination;
    }

    public static void OpenReleasePage(FirmwareRelease release)
    {
        Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
    }

    public static string GetRepository(FirmwareReleaseChannel channel)
    {
        return channel == FirmwareReleaseChannel.Custom ? CustomRepository : OfficialRepository;
    }

    public static FirmwareReleaseChannel GetChannelForFirmware(string? firmwareVersion)
    {
        return firmwareVersion?.Contains("-custom", StringComparison.OrdinalIgnoreCase) == true
            ? FirmwareReleaseChannel.Custom
            : FirmwareReleaseChannel.Official;
    }

    private static FirmwareAsset? SelectStandardFirmwareAsset(IEnumerable<FirmwareAsset> assets)
    {
        var candidates = assets
            .Where(asset => !asset.Name.Contains("debug", StringComparison.OrdinalIgnoreCase))
            .Where(asset => !asset.Name.Contains("picow", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return candidates.FirstOrDefault(asset => asset.Name.Equals("ds5-bridge.uf2", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(asset => asset.Name.StartsWith("ds5-bridge", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    private static string NormalizeVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    private static string StripCustomSuffix(string version)
    {
        return Regex.Replace(version.Trim(), "-custom\\.\\d+$", "", RegexOptions.IgnoreCase);
    }

    private static string GetDownloadPath(FirmwareRelease release)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        var directory = Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(directory, $"DS5Dongle-{release.Tag}-{release.Asset.Name}");
    }

    private async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DriveInfo?> WaitForUf2DriveAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var drive = await Task.Run(FindUf2Drive, cancellationToken).ConfigureAwait(false);
            if (drive is not null)
            {
                return drive;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static DriveInfo? FindUf2Drive()
    {
        return DriveInfo.GetDrives().FirstOrDefault(IsUf2Drive);
    }

    private static bool IsUf2Drive(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Removable)
            {
                return false;
            }

            var infoPath = Path.Combine(drive.RootDirectory.FullName, "INFO_UF2.TXT");
            if (!File.Exists(infoPath))
            {
                return false;
            }

            var info = File.ReadAllText(infoPath);
            return info.Contains("UF2", StringComparison.OrdinalIgnoreCase)
                && (info.Contains("RP2350", StringComparison.OrdinalIgnoreCase)
                    || info.Contains("RPI-RP2", StringComparison.OrdinalIgnoreCase)
                    || info.Contains("Raspberry Pi", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}

internal sealed record FirmwareRelease(string Tag, string HtmlUrl, FirmwareAsset Asset, FirmwareReleaseChannel Channel, string Repository);

internal sealed record FirmwareAsset(string Name, string DownloadUrl);

internal sealed record FirmwareDownloadResult(FirmwareRelease Release, string DownloadPath);

internal sealed record FirmwareUpdateResult(FirmwareRelease Release, string DestinationPath);

internal enum FirmwareReleaseChannel
{
    Official,
    Custom
}

internal enum FirmwareVersionStatus
{
    Unknown,
    UpToDate,
    UpdateAvailable
}
