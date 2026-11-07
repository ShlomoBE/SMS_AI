namespace Sms.Core.Psg;

/// <summary>The register selected by bits 6–4 of a latch/data byte.</summary>
public enum PsgRegister : byte
{
    Tone0 = 0,
    Attenuation0 = 1,
    Tone1 = 2,
    Attenuation1 = 3,
    Tone2 = 4,
    Attenuation2 = 5,
    Noise = 6,
    Attenuation3 = 7
}

/// <summary>A detached, immutable inspection value; not a hardware read or save-state format.</summary>
public readonly record struct PsgRegisterSnapshot(
    ushort Tone0,
    ushort Tone1,
    ushort Tone2,
    byte Attenuation0,
    byte Attenuation1,
    byte Attenuation2,
    byte Attenuation3,
    byte Noise,
    PsgRegister LatchedRegister,
    ushort NoiseShiftRegister,
    ulong NoiseReseedCount);

/// <summary>
/// Untimed SMS PSG digital register/latch subset. No counters, clock, or sound generation.
/// See https://www.smspower.org/Development/SN76489 (register writes and LFSR)
/// and IDocs/PSGNotes.txt (SN76489 writes and How the SN76489 makes sound).
/// </summary>
public sealed class PsgRegisters
{
    private readonly ushort[] tones = new ushort[3];
    private readonly byte[] attenuation = [15, 15, 15, 15];
    private byte noise;
    private PsgRegister latchedRegister = PsgRegister.Tone0;
    private ushort noiseShiftRegister = 0x8000;
    private ulong noiseReseedCount;

    private PsgRegisters() { }

    /// <summary>
    /// Explicit software fixture: zero tone/noise, silent attenuation, Tone0 latched,
    /// LFSR seed 0x8000, no writes counted. This is NOT physical power-on or reset.
    /// </summary>
    public static PsgRegisters CreateDeterministicFixture() => new();

    /// <summary>
    /// Applies one write immediately, in caller order. Bus decoding and future access
    /// timestamps belong to the machine owner, not this untimed register subset.
    /// </summary>
    public void Write(byte value)
    {
        bool isLatch = (value & 0x80) != 0;
        if (isLatch)
        {
            latchedRegister = (PsgRegister)((value >> 4) & 7);
        }

        int register = (int)latchedRegister;
        int channel = register >> 1;
        if ((register & 1) != 0)
        {
            attenuation[channel] = (byte)(value & 0x0f);
        }
        else if (latchedRegister == PsgRegister.Noise)
        {
            noise = (byte)(value & 7);
            noiseShiftRegister = 0x8000;
            // Diagnostic write-event count, not a chip counter. Wraps modulo 2^64.
            noiseReseedCount = unchecked(noiseReseedCount + 1);
        }
        else
        {
            tones[channel] = isLatch
                ? (ushort)((tones[channel] & 0x3f0) | (value & 0x0f))
                : (ushort)((tones[channel] & 0x00f) | ((value & 0x3f) << 4));
        }
    }

    /// <summary>Side-effect-free; call only under the machine owner's synchronization.</summary>
    public PsgRegisterSnapshot Inspect() => new(
        tones[0], tones[1], tones[2],
        attenuation[0], attenuation[1], attenuation[2], attenuation[3],
        noise, latchedRegister, noiseShiftRegister, noiseReseedCount);
}
