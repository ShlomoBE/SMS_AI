namespace Sms.Core.Fm;

/// <summary>
/// Parameters for a single OPLL operator (Modulator or Carrier).
/// </summary>
public readonly struct OperatorParameters
{
    public byte Multiplier { get; init; }
    public bool Ksr { get; init; }
    public bool EgType { get; init; }
    public bool Vibrato { get; init; }
    public bool Tremolo { get; init; }
    public byte Ksl { get; init; }
    public byte TotalLevel { get; init; }
    public byte Waveform { get; init; }
    public byte Feedback { get; init; }
    public byte AttackRate { get; init; }
    public byte DecayRate { get; init; }
    public byte SustainLevel { get; init; }
    public byte ReleaseRate { get; init; }
}

/// <summary>
/// Represents an OPLL instrument patch consisting of Modulator and Carrier parameters.
/// </summary>
public readonly struct OpllInstrumentPatch
{
    public string Name { get; init; }
    public OperatorParameters Modulator { get; init; }
    public OperatorParameters Carrier { get; init; }
}

/// <summary>
/// OPLL built-in ROM patch table and custom patch decoding.
/// </summary>
public static class FmPatchRom
{
    public static readonly string[] PatchNames = new string[]
    {
        "Custom",
        "Violin",
        "Guitar",
        "Piano",
        "Flute",
        "Clarinet",
        "Oboe",
        "Trumpet",
        "Organ",
        "Horn",
        "Synthesizer",
        "Harpsichord",
        "Vibraphone",
        "Synth Bass",
        "Acoustic Bass",
        "Electric Guitar"
    };

    /// <summary>
    /// Raw 8-byte ROM patch table for YM2413 built-in instruments (patches 1..15).
    /// Index 0 is a placeholder for the user Custom Patch.
    /// </summary>
    public static readonly byte[][] RawRomPatches = new byte[16][]
    {
        // 0: Custom (overridden dynamically via registers $00..$07)
        new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
        // 1: Violin
        new byte[] { 0x71, 0x61, 0x1E, 0x17, 0xD0, 0x78, 0x00, 0x17 },
        // 2: Guitar
        new byte[] { 0x13, 0x41, 0x1A, 0x0D, 0xD8, 0xF7, 0x23, 0x13 },
        // 3: Piano
        new byte[] { 0x13, 0x01, 0x99, 0x04, 0xF2, 0xF4, 0x11, 0x23 },
        // 4: Flute
        new byte[] { 0x31, 0x61, 0x0E, 0x07, 0xA8, 0x64, 0x1C, 0x27 },
        // 5: Clarinet
        new byte[] { 0x32, 0x21, 0x1E, 0x06, 0xE1, 0x76, 0x01, 0x28 },
        // 6: Oboe
        new byte[] { 0x31, 0x22, 0x16, 0x05, 0xE0, 0x71, 0x0F, 0x17 },
        // 7: Trumpet
        new byte[] { 0x21, 0x61, 0x1D, 0x07, 0x82, 0x81, 0x16, 0x07 },
        // 8: Organ
        new byte[] { 0x23, 0x21, 0x22, 0x00, 0xA2, 0xA2, 0x27, 0x27 },
        // 9: Horn
        new byte[] { 0x35, 0x11, 0x25, 0x00, 0x40, 0x73, 0x72, 0x01 },
        // 10: Synthesizer
        new byte[] { 0xB5, 0x01, 0x0F, 0x0F, 0xA8, 0xA5, 0x51, 0x02 },
        // 11: Harpsichord
        new byte[] { 0x17, 0xC1, 0x24, 0x07, 0xF8, 0xF8, 0x22, 0x12 },
        // 12: Vibraphone
        new byte[] { 0x71, 0x21, 0x06, 0x00, 0xF0, 0xF0, 0x15, 0x15 },
        // 13: Synth Bass
        new byte[] { 0x01, 0x01, 0x18, 0x0F, 0xDF, 0xF8, 0x6A, 0x6D },
        // 14: Acoustic Bass
        new byte[] { 0x01, 0x01, 0x00, 0x00, 0xC8, 0xD8, 0xA7, 0x68 },
        // 15: Electric Guitar
        new byte[] { 0x61, 0x61, 0x1E, 0x07, 0xF0, 0x78, 0x00, 0x17 }
    };

    /// <summary>
    /// Gets the instrument patch for the specified index (0..15).
    /// Index 0 uses customRegs ($00..$07); indices 1..15 use built-in ROM patches.
    /// </summary>
    public static OpllInstrumentPatch GetPatch(int patchIndex, ReadOnlySpan<byte> customRegs)
    {
        patchIndex &= 0x0F;
        ReadOnlySpan<byte> raw = patchIndex == 0 ? customRegs : RawRomPatches[patchIndex];
        return DecodePatch(PatchNames[patchIndex], raw);
    }

    /// <summary>
    /// Decodes an 8-byte OPLL patch definition into Modulator and Carrier operator parameters.
    /// </summary>
    public static OpllInstrumentPatch DecodePatch(string name, ReadOnlySpan<byte> raw)
    {
        byte b0 = raw.Length > 0 ? raw[0] : (byte)0;
        byte b1 = raw.Length > 1 ? raw[1] : (byte)0;
        byte b2 = raw.Length > 2 ? raw[2] : (byte)0;
        byte b3 = raw.Length > 3 ? raw[3] : (byte)0;
        byte b4 = raw.Length > 4 ? raw[4] : (byte)0;
        byte b5 = raw.Length > 5 ? raw[5] : (byte)0;
        byte b6 = raw.Length > 6 ? raw[6] : (byte)0;
        byte b7 = raw.Length > 7 ? raw[7] : (byte)0;

        var mod = new OperatorParameters
        {
            Multiplier = (byte)(b0 & 0x0F),
            Ksr = (b0 & 0x10) != 0,
            EgType = (b0 & 0x20) != 0,
            Vibrato = (b0 & 0x40) != 0,
            Tremolo = (b0 & 0x80) != 0,
            Ksl = (byte)((b2 >> 6) & 0x03),
            TotalLevel = (byte)(b2 & 0x3F),
            Waveform = (byte)((b3 >> 3) & 0x01),
            Feedback = (byte)(b3 & 0x07),
            AttackRate = (byte)((b4 >> 4) & 0x0F),
            DecayRate = (byte)(b4 & 0x0F),
            SustainLevel = (byte)((b6 >> 4) & 0x0F),
            ReleaseRate = (byte)(b6 & 0x0F)
        };

        var car = new OperatorParameters
        {
            Multiplier = (byte)(b1 & 0x0F),
            Ksr = (b1 & 0x10) != 0,
            EgType = (b1 & 0x20) != 0,
            Vibrato = (b1 & 0x40) != 0,
            Tremolo = (b1 & 0x80) != 0,
            Ksl = (byte)((b3 >> 6) & 0x03),
            TotalLevel = 0, // Carrier total volume controlled via channel volume registers
            Waveform = (byte)((b3 >> 4) & 0x01),
            Feedback = 0,
            AttackRate = (byte)((b5 >> 4) & 0x0F),
            DecayRate = (byte)(b5 & 0x0F),
            SustainLevel = (byte)((b7 >> 4) & 0x0F),
            ReleaseRate = (byte)(b7 & 0x0F)
        };

        return new OpllInstrumentPatch
        {
            Name = name,
            Modulator = mod,
            Carrier = car
        };
    }
}
