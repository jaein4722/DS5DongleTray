using System.Text;
using HidSharp;

namespace DS5DongleTray;

internal sealed class DongleHidClient
{
    private const int SonyVid = 0x054C;
    private const int DualSensePid = 0x0CE6;
    private const int DualSenseEdgePid = 0x0DF2;
    private const byte ReportFirmwareVersion = 0xF8;
    private const byte ReportRssi = 0xF9;
    private const byte ReportBatteryStatus = 0xFA;
    private const int FeatureReportLength = 64;

    private static readonly (int Vid, int Pid)[] CandidateIds =
    [
        (SonyVid, DualSensePid),
        (SonyVid, DualSenseEdgePid)
    ];

    public Task<DongleSnapshot> ReadSnapshotAsync()
    {
        return Task.Run(ReadSnapshot);
    }

    private DongleSnapshot ReadSnapshot()
    {
        Log("Starting DS5Dongle poll.");

        foreach (var hidDevice in EnumerateCandidates())
        {
            try
            {
                if (!hidDevice.TryOpen(out var stream))
                {
                    continue;
                }

                using (stream)
                {
                    stream.ReadTimeout = 1000;
                    stream.WriteTimeout = 1000;

                    var firmware = TryReadFirmware(stream);
                    if (string.IsNullOrWhiteSpace(firmware))
                    {
                        continue;
                    }

                    var rssi = TryReadRssi(stream);
                    var battery = TryReadBattery(stream, out var batteryUnsupported);

                    return new DongleSnapshot
                    {
                        DeviceFound = true,
                        DevicePath = hidDevice.DevicePath,
                        FirmwareVersion = firmware,
                        Rssi = rssi,
                        Battery = battery,
                        BatteryUnsupported = batteryUnsupported
                    };
                }
            }
            catch (Exception ex)
            {
                Log($"Device poll failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return new DongleSnapshot { DeviceFound = false };
    }

    private static IEnumerable<HidDevice> EnumerateCandidates()
    {
        var list = DeviceList.Local;
        foreach (var (vid, pid) in CandidateIds)
        {
            foreach (var device in list.GetHidDevices(vid, pid))
            {
                yield return device;
            }
        }
    }

    private static string? TryReadFirmware(HidStream stream)
    {
        try
        {
            var report = GetFeature(stream, ReportFirmwareVersion);
            var end = Array.IndexOf(report, (byte)0, 1);
            if (end < 0)
            {
                end = report.Length;
            }

            return Encoding.ASCII.GetString(report, 1, end - 1).Trim();
        }
        catch (Exception ex)
        {
            Log($"Firmware read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static int? TryReadRssi(HidStream stream)
    {
        try
        {
            var report = GetFeature(stream, ReportRssi);
            return unchecked((sbyte)report[1]);
        }
        catch (Exception ex)
        {
            Log($"RSSI read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static BatteryStatus? TryReadBattery(HidStream stream, out bool unsupported)
    {
        unsupported = false;

        try
        {
            var report = GetFeature(stream, ReportBatteryStatus);
            return BatteryStatus.FromFeaturePayload(report);
        }
        catch (Exception ex)
        {
            unsupported = true;
            Log($"Battery read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static byte[] GetFeature(HidStream stream, byte reportId)
    {
        var report = new byte[FeatureReportLength];
        report[0] = reportId;
        stream.GetFeature(report);
        return report;
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DS5DongleTray");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "DS5DongleTray.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break tray polling.
        }
    }
}
