using System.Text.Json;

namespace DS5DongleTray;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public OverlaySettings Overlay { get; set; } = new();

    public Dictionary<string, DongleConfig> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public UpdateCheckSettings UpdateCheck { get; set; } = new();

    public AppUpdateCheckSettings AppUpdateCheck { get; set; } = new();

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Profiles = new Dictionary<string, DongleConfig>(settings.Profiles, StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DS5DongleTray",
        "settings.json");
}

internal sealed class OverlaySettings
{
    public bool ShowOnLowBattery { get; set; } = true;
    public bool ShowOnPsButton { get; set; } = true;
    public bool ShowOnChargingStateChanged { get; set; } = true;
    public bool ShowOnTrayLeftClick { get; set; } = true;
    public int LowBatteryThresholdPercent { get; set; } = 20;
    public int DisplaySeconds { get; set; } = 3;

    public int ClampedLowBatteryThreshold => Math.Min(Math.Max(LowBatteryThresholdPercent, 10), 50);

    public int ClampedDisplaySeconds => Math.Min(Math.Max(DisplaySeconds, 1), 10);
}

internal sealed class UpdateCheckSettings
{
    public bool Enabled { get; set; } = true;
    public DateTimeOffset LastCheckedAt { get; set; }
    public string? LastFirmwareVersion { get; set; }
    public string? LastLatestVersion { get; set; }
    public string? LastRepository { get; set; }
    public bool LastUpdateAvailable { get; set; }
}

internal sealed class AppUpdateCheckSettings
{
    public bool Enabled { get; set; } = true;
    public DateTimeOffset LastCheckedAt { get; set; }
    public string? LastCurrentVersion { get; set; }
    public string? LastLatestVersion { get; set; }
    public bool LastUpdateAvailable { get; set; }
}
