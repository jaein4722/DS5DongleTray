using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DS5DongleTray;

internal sealed class AppUpdater
{
    private const string Repository = "jaein4722/DS5DongleTray";
    private readonly HttpClient httpClient = new();

    public AppUpdater()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DS5DongleTray", "1.0"));
    }

    public static string CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version.Split('+')[0];
            }

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        }
    }

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        var status = GetVersionStatus(CurrentVersion, release.Tag);
        return new AppUpdateCheckResult(status, CurrentVersion, release);
    }

    public async Task<AppRelease> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var stream = await httpClient
            .GetStreamAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "latest";
        var htmlUrl = root.GetProperty("html_url").GetString() ?? $"https://github.com/{Repository}/releases";
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new AppAsset(
                asset.GetProperty("name").GetString() ?? "",
                asset.GetProperty("browser_download_url").GetString() ?? ""))
            .ToList();

        var exeAsset = assets.FirstOrDefault(asset =>
            asset.Name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase) ||
            asset.Name.Equals("DS5DongleTray.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Latest release does not contain a Windows x64 .exe asset.");

        var shaAsset = assets.FirstOrDefault(asset =>
            asset.Name.Equals($"{exeAsset.Name}.sha256", StringComparison.OrdinalIgnoreCase));

        return new AppRelease(tag, htmlUrl, exeAsset, shaAsset);
    }

    public async Task<AppDownloadResult> DownloadLatestAsync(AppRelease release, bool useTempDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = useTempDirectory ? Path.GetTempPath() : GetDownloadsDirectory();
        var destination = Path.Combine(directory, release.Asset.Name);
        progress?.Report($"Downloading {release.Asset.Name}...");
        await DownloadFileAsync(release.Asset.DownloadUrl, destination, cancellationToken).ConfigureAwait(false);

        if (release.Sha256Asset is not null)
        {
            progress?.Report("Verifying SHA256...");
            var shaPath = Path.Combine(directory, release.Sha256Asset.Name);
            await DownloadFileAsync(release.Sha256Asset.DownloadUrl, shaPath, cancellationToken).ConfigureAwait(false);
            VerifySha256(destination, shaPath);
        }

        progress?.Report($"Downloaded to {destination}.");
        return new AppDownloadResult(release, destination);
    }

    public static bool CanSelfUpdate(out string reason)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "The app is not running as a standalone exe.";
            return false;
        }

        var fileName = Path.GetFileName(path);
        if (!fileName.Equals("DS5DongleTray.exe", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith("DS5DongleTray-", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Unexpected executable name: {fileName}";
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                reason = "The executable directory could not be determined.";
                return false;
            }

            var probe = Path.Combine(directory, $".ds5dongletray-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"The executable directory is not writable: {ex.Message}";
            return false;
        }
    }

    public static void StartSelfUpdate(string newExePath)
    {
        var target = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path could not be determined.");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"DS5DongleTrayUpdate-{Guid.NewGuid():N}.cmd");
        var pid = Environment.ProcessId;
        File.WriteAllText(scriptPath, BuildUpdateScript(pid, target, newExePath), Encoding.ASCII);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    public static void OpenReleasePage(AppRelease release)
    {
        Process.Start(new ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
    }

    public static FirmwareVersionStatus GetVersionStatus(string currentVersion, string latestTag)
    {
        var current = NormalizeVersion(currentVersion);
        var latest = NormalizeVersion(latestTag);
        if (string.Equals(current, latest, StringComparison.OrdinalIgnoreCase))
        {
            return FirmwareVersionStatus.UpToDate;
        }

        if (Version.TryParse(current, out var currentParsed) &&
            Version.TryParse(latest, out var latestParsed))
        {
            return latestParsed > currentParsed ? FirmwareVersionStatus.UpdateAvailable : FirmwareVersionStatus.UpToDate;
        }

        return FirmwareVersionStatus.UpdateAvailable;
    }

    private async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static void VerifySha256(string filePath, string shaPath)
    {
        var expected = File.ReadAllText(shaPath)
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part.Length == 64 && part.All(Uri.IsHexDigit));
        if (string.IsNullOrWhiteSpace(expected))
        {
            throw new InvalidOperationException("SHA256 checksum file did not contain a valid hash.");
        }

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded app update failed SHA256 verification.");
        }
    }

    private static string GetDownloadsDirectory()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string NormalizeVersion(string version)
    {
        return version.Trim().TrimStart('v', 'V');
    }

    private static string BuildUpdateScript(int pid, string targetPath, string newExePath)
    {
        var backupPath = $"{targetPath}.bak";
        return $"""
@echo off
setlocal
set "PID={pid}"
set "TARGET={targetPath}"
set "NEWEXE={newExePath}"
set "BACKUP={backupPath}"

:wait
tasklist /FI "PID eq %PID%" | find "%PID%" >nul
if not errorlevel 1 (
  timeout /t 1 /nobreak >nul
  goto wait
)

if exist "%BACKUP%" del /f /q "%BACKUP%" >nul 2>nul
move /y "%TARGET%" "%BACKUP%" >nul
copy /y "%NEWEXE%" "%TARGET%" >nul
if errorlevel 1 (
  copy /y "%BACKUP%" "%TARGET%" >nul 2>nul
  start "" "%TARGET%"
  exit /b 1
)

start "" "%TARGET%"
timeout /t 2 /nobreak >nul
del /f /q "%BACKUP%" >nul 2>nul
del /f /q "%NEWEXE%" >nul 2>nul
del /f /q "%~f0" >nul 2>nul
""";
    }
}

internal sealed record AppUpdateCheckResult(
    FirmwareVersionStatus Status,
    string CurrentVersion,
    AppRelease LatestRelease)
{
    public bool IsUpdateAvailable => Status == FirmwareVersionStatus.UpdateAvailable;
}

internal sealed record AppRelease(string Tag, string HtmlUrl, AppAsset Asset, AppAsset? Sha256Asset);

internal sealed record AppAsset(string Name, string DownloadUrl);

internal sealed record AppDownloadResult(AppRelease Release, string DownloadPath);
