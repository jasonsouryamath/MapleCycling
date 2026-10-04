/// <summary>
/// Decodes a Bluetooth SIG "Heart Rate Measurement" (characteristic 0x2A37) payload.
///
/// Layout (Heart Rate Service, 0x180D): byte 0 is a flags field; bit 0 selects the HR value
/// width - 0 = the next single byte (uint8), 1 = the next two bytes little-endian (uint16). The
/// remaining optional fields (energy expended, RR intervals) are not needed for the HUD readout,
/// so only the bpm is extracted here.
///
/// Kept transport-agnostic and side-effect-free on purpose: it is the one piece of the BLE path
/// that compiles and can be reasoned about WITHOUT a Windows runtime, and it is shared by every
/// future transport (WinRT today, a plugin or ANT+ bridge later).
/// </summary>
public static class HeartRateMeasurementParser
{
    /// <summary>Beats per minute from a raw notification payload, or -1 if it is malformed.</summary>
    public static int Parse(byte[] data)
    {
        if (data == null || data.Length < 2) return -1;

        byte flags = data[0];
        bool sixteenBit = (flags & 0x01) != 0;

        if (sixteenBit)
        {
            if (data.Length < 3) return -1;
            return data[1] | (data[2] << 8);
        }
        return data[1];
    }
}
