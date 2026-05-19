using System.Text;
using HidSharp;

namespace DS5DongleTray;

internal sealed class DongleHidClient
{
    private const int SonyVid = 0x054C;
    private const int DualSensePid = 0x0CE6;
    private const int DualSenseEdgePid = 0x0DF2;
    private const byte ReportFirmwareVersion = 0xF8;
    private const byte ReportConfig = 0xF7;
    private const byte ReportCommand = 0xF6;
    private const byte ReportRssi = 0xF9;
    private const int FeatureReportLength = 64;
    private const int InputReportReadTimeoutMs = 300;
    private const int InputReportReadAttempts = 3;
    private const byte CommandApplyConfig = 0x01;
    private const byte CommandSaveConfig = 0x02;
    private const byte CommandReconnectUsb = 0x03;
    private const byte CommandEnterBootloader = 0x04;

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
                    var battery = TryReadBatteryFromInput(hidDevice, stream);
                    var config = TryReadConfig(stream, out var configUnsupported);

                    return new DongleSnapshot
                    {
                        DeviceFound = true,
                        DevicePath = hidDevice.DevicePath,
                        FirmwareVersion = firmware,
                        Rssi = rssi,
                        Battery = battery,
                        Config = config,
                        BatteryUnsupported = false,
                        ConfigUnsupported = configUnsupported
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

    public Task<DongleConfig?> ReadConfigAsync()
    {
        return Task.Run(() =>
        {
            using var stream = OpenDongleStream();
            return TryReadConfig(stream, out _);
        });
    }

    public Task ApplyConfigAsync(DongleConfig config)
    {
        return Task.Run(() =>
        {
            using var stream = OpenDongleStream();
            var payload = config.ToPayload();
            var report = NewCommandReport(CommandApplyConfig);
            Buffer.BlockCopy(payload, 0, report, 2, payload.Length);
            stream.SetFeature(report);
        });
    }

    public Task SaveConfigAsync()
    {
        return SendCommandAsync(CommandSaveConfig);
    }

    public Task ReconnectUsbAsync()
    {
        return SendCommandAsync(CommandReconnectUsb);
    }

    public Task EnterBootloaderAsync()
    {
        return Task.Run(() =>
        {
            var snapshot = ReadSnapshot();
            if (!snapshot.SupportsTrayFirmwareUpdate)
            {
                throw new InvalidOperationException(
                    $"Firmware update requires custom firmware with '-custom' in the version string. Current firmware: {snapshot.FirmwareVersion ?? "unknown"}.");
            }

            using var stream = OpenDongleStream();
            stream.SetFeature(NewCommandReport(CommandEnterBootloader));
        });
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

    private static HidStream OpenDongleStream()
    {
        foreach (var hidDevice in EnumerateCandidates())
        {
            try
            {
                if (!hidDevice.TryOpen(out var stream))
                {
                    continue;
                }

                stream.ReadTimeout = 1000;
                stream.WriteTimeout = 1000;

                var firmware = TryReadFirmware(stream);
                if (!string.IsNullOrWhiteSpace(firmware))
                {
                    return stream;
                }

                stream.Dispose();
            }
            catch (Exception ex)
            {
                Log($"Open dongle failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException("DS5Dongle was not found.");
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

    private static BatteryStatus? TryReadBatteryFromInput(HidDevice hidDevice, HidStream stream)
    {
        var oldReadTimeout = stream.ReadTimeout;
        try
        {
            stream.ReadTimeout = InputReportReadTimeoutMs;
            var reportLength = Math.Max(hidDevice.GetMaxInputReportLength(), FeatureReportLength);

            for (var attempt = 0; attempt < InputReportReadAttempts; attempt++)
            {
                var report = new byte[reportLength];
                var bytesRead = stream.Read(report);
                if (bytesRead == 0)
                {
                    continue;
                }

                if (report[0] != 0x01)
                {
                    Log($"Skipped input report 0x{report[0]:X2} while looking for battery state.");
                    continue;
                }

                var battery = BatteryStatus.FromInputReport(report, bytesRead);
                Log($"Input battery read succeeded: bytes={bytesRead}, level={battery.LevelRaw}, percent={battery.Percent}, state=0x{battery.PowerState:X2}, usbPower={battery.IsUsbPowered}.");
                return battery;
            }

            Log("No 0x01 input report was observed while looking for battery state.");
        }
        catch (Exception ex)
        {
            Log($"Input battery read failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            stream.ReadTimeout = oldReadTimeout;
        }

        return null;
    }

    private static DongleConfig? TryReadConfig(HidStream stream, out bool unsupported)
    {
        unsupported = false;

        try
        {
            var report = GetFeature(stream, ReportConfig);
            var config = DongleConfig.FromFeaturePayload(report);
            return config.IsSupported ? config : null;
        }
        catch (Exception ex)
        {
            unsupported = true;
            Log($"Config read failed: {ex.GetType().Name}: {ex.Message}");
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

    private Task SendCommandAsync(byte command)
    {
        return Task.Run(() =>
        {
            using var stream = OpenDongleStream();
            stream.SetFeature(NewCommandReport(command));
        });
    }

    private static byte[] NewCommandReport(byte command)
    {
        var report = new byte[FeatureReportLength];
        report[0] = ReportCommand;
        report[1] = command;
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
