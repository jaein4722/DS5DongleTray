#if CUSTOM_FIRMWARE
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DS5DongleTray;

internal sealed class FirmwareUpdater
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/jaein4722/DS5Dongle/releases/latest";
    private readonly DongleHidClient client;
    private readonly HttpClient httpClient = new();

    public FirmwareUpdater(DongleHidClient client)
    {
        this.client = client;
        httpClient.Timeout = TimeSpan.FromSeconds(15);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DS5DongleTray", "1.0"));
    }

    public async Task<FirmwareRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var stream = await httpClient.GetStreamAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "latest";
        var htmlUrl = root.GetProperty("html_url").GetString() ?? "https://github.com/jaein4722/DS5Dongle/releases";
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new FirmwareAsset(
                asset.GetProperty("name").GetString() ?? "",
                asset.GetProperty("browser_download_url").GetString() ?? ""))
            .Where(asset => asset.Name.EndsWith(".uf2", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var selectedAsset = SelectStandardFirmwareAsset(assets)
            ?? throw new InvalidOperationException("Latest release does not contain a standard .uf2 firmware asset.");

        return new FirmwareRelease(tag, htmlUrl, selectedAsset);
    }

    public async Task<FirmwareUpdateResult> DownloadAndInstallLatestAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking latest release...");
        var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report($"Downloading {release.Asset.Name}...");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"DS5Dongle-{release.Tag}-{release.Asset.Name}");
        await DownloadFileAsync(release.Asset.DownloadUrl, downloadPath, cancellationToken).ConfigureAwait(false);

        var destination = await InstallFirmwareFileAsync(downloadPath, release.Asset.Name, progress, cancellationToken).ConfigureAwait(false);
        return new FirmwareUpdateResult(release, destination);
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

internal sealed record FirmwareRelease(string Tag, string HtmlUrl, FirmwareAsset Asset);

internal sealed record FirmwareAsset(string Name, string DownloadUrl);

internal sealed record FirmwareUpdateResult(FirmwareRelease Release, string DestinationPath);
#endif
