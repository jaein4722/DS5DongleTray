namespace DS5DongleTray;

internal sealed record BatteryStatus(
    byte LevelRaw,
    byte Percent,
    byte PowerState,
    bool IsCharging,
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

    public static BatteryStatus FromFeaturePayload(byte[] report)
    {
        const int payloadOffset = 1;
        if (report.Length < payloadOffset + 10)
        {
            throw new InvalidDataException("Battery report is too short.");
        }

        return new BatteryStatus(
            report[payloadOffset + 0],
            report[payloadOffset + 1],
            report[payloadOffset + 2],
            report[payloadOffset + 3] != 0,
            report[payloadOffset + 4] != 0,
            report[payloadOffset + 5] != 0,
            BitConverter.ToUInt32(report, payloadOffset + 6));
    }
}
