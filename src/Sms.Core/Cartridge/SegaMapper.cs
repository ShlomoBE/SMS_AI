namespace Sms.Core.Cartridge;

/// <summary>Explicitly selected foundation profile, not automatic cartridge detection.</summary>
public enum SegaMapperProfile
{
    Sega3155235RomOnly,
    Sega3155235Sram8KiB
}

/// <summary>
/// Observation is never exclusive consumption: the bus must also deliver FFFC-FFFF
/// writes to enabled work RAM. Unsupported controls leave the mapper unchanged.
/// </summary>
public enum MapperWriteResult
{
    Ignored,
    RegisterUpdated,
    UnsupportedControlValue,
    CartridgeRamWritten
}

/// <summary>Side-effect-free values of the write-only mapper latches, not bus reads.</summary>
public readonly record struct SegaMapperRegisters(byte Control, byte Slot0, byte Slot1, byte Slot2);

/// <summary>
/// Isolated Sega ROM/SRAM mapper. No bus, BIOS, system RAM or filesystem persistence.
/// See the adjacent README for hardware evidence and deliberately unsupported modes.
/// </summary>
public sealed class SegaMapper
{
    public const int BankSize = 0x4000;
    public const int MaximumRomSize = 32 * BankSize;
    public const int Sram8KiBSize = 0x2000;

    private readonly byte[] rom;
    private readonly int bankMask;
    private readonly byte[] sram;
    private byte control;
    private byte slot0;
    private byte slot1;
    private byte slot2;

    /// <summary>
    /// Copies the supplied raw ROM without stripping a header. Only complete,
    /// power-of-two bank counts within the selected profile's range are accepted.
    /// </summary>
    public SegaMapper(ReadOnlySpan<byte> romBytes, SegaMapperProfile profile)
        : this(romBytes, profile, ReadOnlySpan<byte>.Empty)
    {
    }

    /// <summary>Copies exactly 8 KiB for the SRAM profile; ROM-only accepts only empty SRAM.</summary>
    public SegaMapper(ReadOnlySpan<byte> romBytes, SegaMapperProfile profile, ReadOnlySpan<byte> initialSram)
    {
        if (profile is not (SegaMapperProfile.Sega3155235RomOnly or SegaMapperProfile.Sega3155235Sram8KiB))
            throw new ArgumentOutOfRangeException(nameof(profile));

        int expectedSramSize = profile == SegaMapperProfile.Sega3155235Sram8KiB ? Sram8KiBSize : 0;
        if (initialSram.Length != expectedSramSize)
            throw new ArgumentException("Initial SRAM must match the explicit profile (8 KiB or none).", nameof(initialSram));

        int length = romBytes.Length;
        if (length < BankSize || length > MaximumRomSize ||
            length % BankSize != 0 || (length & (length - 1)) != 0)
        {
            throw new ArgumentException(
                "ROM must contain 1, 2, 4, 8, 16 or 32 complete 16 KiB banks; headers and partial banks are unsupported.",
                nameof(romBytes));
        }

        rom = romBytes.ToArray();
        bankMask = length / BankSize - 1;
        sram = initialSram.ToArray();
        Profile = profile;
        ResetToPowerOnProfile();
    }

    /// <summary>Explicit deterministic SRAM fill, not a claim about physical startup contents.</summary>
    public SegaMapper(ReadOnlySpan<byte> romBytes, SegaMapperProfile profile, byte initialSramValue)
        : this(romBytes, profile, CreateInitialSram(initialSramValue))
    {
    }

    private static byte[] CreateInitialSram(byte value)
    {
        var bytes = new byte[Sram8KiBSize];
        bytes.AsSpan().Fill(value);
        return bytes;
    }

    public int RomSize => rom.Length;
    public SegaMapperProfile Profile { get; }
    public int SramSize => sram.Length;
    public SegaMapperRegisters Registers => new(control, slot0, slot1, slot2);

    /// <summary>Returns an owned copy of physical SRAM (empty for ROM-only), not mirrored windows.</summary>
    public byte[] ExportSram() => sram.ToArray();

    /// <summary>Replaces physical SRAM after length validation; does not change mapping latches.</summary>
    public void ImportSram(ReadOnlySpan<byte> bytes)
    {
        if (sram.Length == 0)
            throw new InvalidOperationException("The selected cartridge profile has no SRAM.");
        if (bytes.Length != sram.Length)
            throw new ArgumentException("SRAM import must contain exactly 8 KiB.", nameof(bytes));
        bytes.CopyTo(sram);
    }

    /// <summary>
    /// Applies the documented 315-5235 power-up latch values (00,00,01,02).
    /// Preserves SRAM. This is not a claim that the console reset button resets a cartridge.
    /// </summary>
    public void ResetToPowerOnProfile()
    {
        control = 0;
        slot0 = 0;
        slot1 = 1;
        slot2 = 2;
    }

    /// <summary>
    /// Returns true only when this cartridge drives a byte. A false result's
    /// value is a placeholder, not an open-bus value. Slot enable/arbitration
    /// belongs to the caller. Reads at mapper-register addresses are undriven.
    /// </summary>
    public bool TryRead(ushort address, out byte value)
    {
        value = default;
        if (address >= 0xc000)
            return false;

        if (IsSramSelected(address))
        {
            value = sram[address & (Sram8KiBSize - 1)];
            return true;
        }

        int bank = address switch
        {
            < 0x0400 => 0,
            < 0x4000 => slot0 & bankMask,
            < 0x8000 => slot1 & bankMask,
            _ => slot2 & bankMask
        };
        value = rom[bank * BankSize + (address & (BankSize - 1))];
        return true;
    }

    /// <summary>
    /// Observes exact FFFC-FFFF writes, including outside the cartridge read
    /// window, and selected slot-2 SRAM writes. DFFC-DFFF RAM mirrors do not
    /// select these latches. ROM writes are ignored. The caller must stop/report UnsupportedControlValue;
    /// it must not present that result as successful hardware emulation.
    /// </summary>
    public MapperWriteResult ObserveWrite(ushort address, byte value)
    {
        if (IsSramSelected(address))
        {
            sram[address & (Sram8KiBSize - 1)] = value;
            return MapperWriteResult.CartridgeRamWritten;
        }

        switch (address)
        {
            case 0xfffc:
                // Bank shift, upper SRAM window and ROM-write mode are deferred.
                // SMS Power Mappers: bit 2 selects 16 KiB SRAM banks; for our
                // explicit 8 KiB physical SRAM both selections and window halves alias.
                // ROM-only continues to reject bit 3. Unused bits 5-6 are retained.
                int unsupportedMask = sram.Length == 0 ? 0x9b : 0x93;
                if ((value & unsupportedMask) != 0)
                    return MapperWriteResult.UnsupportedControlValue;
                control = value;
                break;
            case 0xfffd:
                slot0 = value;
                break;
            case 0xfffe:
                slot1 = value;
                break;
            case 0xffff:
                slot2 = value;
                break;
            default:
                return MapperWriteResult.Ignored;
        }

        return MapperWriteResult.RegisterUpdated;
    }

    private bool IsSramSelected(ushort address) =>
        sram.Length != 0 && (control & 0x08) != 0 && address is >= 0x8000 and < 0xc000;
}
