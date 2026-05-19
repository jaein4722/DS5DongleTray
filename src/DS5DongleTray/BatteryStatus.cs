namespace DS5DongleTray;

internal sealed record BatteryStatus(
    byte LevelRaw,
    byte Percent,
    byte PowerState,
    bool IsCharging,
    bool IsUsbPowered,
    bool IsPsButtonPressed,
    bool IsConnected,
    bool IsFresh,
    uint LastReportAgeMs)
{
    public bool HasKnownLevel => IsConnected && IsFresh && LevelRaw <= 10 && Percent <= 100;

    public string StateName => PowerStateNames.GetName(PowerState, IsConnected, IsFresh);

    public string DisplayText
    {
        get
        {
            if (!IsConnected)
            {
                return "Controller: disconnected";
            }

            if (!IsFresh || !HasKnownLevel)
            {
                return "Battery: unknown";
            }

            return $"Battery: {Percent}% - {StateName}";
        }
    }

    public static BatteryStatus FromInputReport(byte[] report, int bytesRead)
    {
        const byte inputReportId = 0x01;
        const int payloadOffset = 1;
        const int batteryByteIndex = payloadOffset + 52;
        const int flagsByteIndex = payloadOffset + 53;
        const byte powerStateCharging = 0x01;
        const byte powerStateComplete = 0x02;
        const byte pluggedUsbPowerMask = 1 << 4;
        const byte usbPowerOnBluetoothMask = 1 << 5;

        if (bytesRead <= flagsByteIndex)
        {
            throw new InvalidDataException("Input report is too short.");
        }

        if (report[0] != inputReportId)
        {
            throw new InvalidDataException($"Unexpected input report ID 0x{report[0]:X2}.");
        }

        var battery = report[batteryByteIndex];
        var levelRaw = (byte)(battery & 0x0F);
        var powerState = (byte)((battery >> 4) & 0x0F);
        var percent = powerState == powerStateComplete
            ? (byte)100
            : levelRaw <= 10 ? (byte)(levelRaw * 10) : (byte)0xFF;
        var flags = report[flagsByteIndex];
        var usbPowered = (flags & (pluggedUsbPowerMask | usbPowerOnBluetoothMask)) != 0;
        var psButtonPressed = IsPsButtonPressedFromInputReport(report, bytesRead);

        return new BatteryStatus(
            levelRaw,
            percent,
            powerState,
            powerState == powerStateCharging,
            usbPowered,
            psButtonPressed,
            true,
            true,
            0);
    }

    private static bool IsPsButtonPressedFromInputReport(byte[] report, int bytesRead)
    {
        var index = bytesRead is 64 or 79 ? 10 : 11;
        return bytesRead > index && report[index] == 0x01;
    }
}
