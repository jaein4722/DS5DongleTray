using System.Text;

namespace DS5DongleTray;

internal sealed record DongleSnapshot
{
    public bool DeviceFound { get; init; }
    public string? DevicePath { get; init; }
    public string? FirmwareVersion { get; init; }
    public int? Rssi { get; init; }
    public BatteryStatus? Battery { get; init; }
    public DongleConfig? Config { get; init; }
    public bool BatteryUnsupported { get; init; }
    public bool ConfigUnsupported { get; init; }
    public string? Error { get; init; }

    public string TooltipText
    {
        get
        {
            if (!DeviceFound)
            {
                return "DS5Dongle: not connected";
            }

            if (BatteryUnsupported)
            {
                return "DS5Dongle: battery unsupported by firmware";
            }

            if (Battery is null)
            {
                return "DS5Dongle: battery unknown";
            }

            if (!Battery.IsConnected)
            {
                return "DS5Dongle Controller: disconnected";
            }

            if (!Battery.HasKnownLevel)
            {
                return "DS5Dongle Battery: unknown";
            }

            return $"DS5Dongle Battery: {Battery.Percent}% - {Battery.StateName}";
        }
    }

    public string BatteryMenuText
    {
        get
        {
            if (!DeviceFound)
            {
                return "Battery: not connected";
            }

            if (BatteryUnsupported)
            {
                return "Battery: unsupported";
            }

            return Battery?.DisplayText ?? "Battery: unknown";
        }
    }

    public string ToConsoleText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(DeviceFound ? "DS5Dongle: found" : "DS5Dongle: not connected");

        if (!string.IsNullOrWhiteSpace(DevicePath))
        {
            sb.AppendLine($"Device: {DevicePath}");
        }

        sb.AppendLine($"Firmware: {FirmwareVersion ?? "unsupported or unavailable"}");
        sb.AppendLine($"RSSI: {(Rssi.HasValue ? Rssi.Value.ToString() : "unsupported or unavailable")}");
        sb.AppendLine(BatteryMenuText);
        sb.AppendLine(Config is null ? "Config: unsupported or unavailable" : $"Config: v{Config.ConfigVersion}, haptics {Config.HapticsGain:0.##}x, polling {Config.PollingRateText}");

        if (!string.IsNullOrWhiteSpace(Error))
        {
            sb.AppendLine($"Last error: {Error}");
        }

        return sb.ToString();
    }
}
