using System.Buffers.Binary;

namespace DS5DongleTray;

internal sealed record DongleConfig(
    byte ConfigVersion,
    float HapticsGain,
    float SpeakerVolume,
    byte InactiveTimeMinutes,
    bool DisableInactiveDisconnect,
    bool DisablePicoLed,
    byte PollingRateMode,
    byte AudioBufferLength,
    byte ControllerMode)
{
    public const int PayloadLength = 15;
    public const byte SupportedConfigVersion = 1;

    public bool IsSupported => ConfigVersion == SupportedConfigVersion;

    public static DongleConfig WebConfigDefault => new(
        SupportedConfigVersion,
        1.0f,
        0.0f,
        10,
        false,
        false,
        0,
        64,
        2);

    public static DongleConfig FromFeaturePayload(byte[] report)
    {
        const int payloadOffset = 1;
        if (report.Length < payloadOffset + PayloadLength)
        {
            throw new InvalidDataException("Config report is too short.");
        }

        return new DongleConfig(
            report[payloadOffset],
            ReadSingle(report, payloadOffset + 1),
            ReadSingle(report, payloadOffset + 5),
            report[payloadOffset + 9],
            report[payloadOffset + 10] != 0,
            report[payloadOffset + 11] != 0,
            report[payloadOffset + 12],
            report[payloadOffset + 13],
            report[payloadOffset + 14]);
    }

    public byte[] ToPayload()
    {
        var payload = new byte[PayloadLength];
        payload[0] = ConfigVersion;
        WriteSingle(payload, 1, HapticsGain);
        WriteSingle(payload, 5, SpeakerVolume);
        payload[9] = InactiveTimeMinutes;
        payload[10] = DisableInactiveDisconnect ? (byte)1 : (byte)0;
        payload[11] = DisablePicoLed ? (byte)1 : (byte)0;
        payload[12] = PollingRateMode;
        payload[13] = AudioBufferLength;
        payload[14] = ControllerMode;
        return payload;
    }

    public string PollingRateText => PollingRateMode switch
    {
        0 => "250 Hz",
        1 => "500 Hz",
        2 => "Real-time",
        _ => "Unknown"
    };

    public string ControllerModeText => ControllerMode switch
    {
        0 => "DualSense",
        1 => "DualSense Edge",
        2 => "Auto",
        _ => "Unknown"
    };

    private static float ReadSingle(byte[] data, int offset)
    {
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)));
    }

    private static void WriteSingle(byte[] data, int offset, float value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
    }
}
