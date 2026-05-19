namespace DS5DongleTray;

internal static class PowerStateNames
{
    public static string GetName(byte state, bool isConnected, bool isFresh)
    {
        if (!isConnected)
        {
            return "Disconnected";
        }

        if (!isFresh)
        {
            return "Unknown";
        }

        return state switch
        {
            0x00 => "Discharging",
            0x01 => "Charging",
            0x02 => "Complete",
            0x0A => "Abnormal Voltage",
            0x0B => "Abnormal Temperature",
            0x0F => "Charging Error",
            _ => "Unknown"
        };
    }
}
