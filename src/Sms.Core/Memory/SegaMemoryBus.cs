using Sms.Core.Cartridge;

namespace Sms.Core.Memory;

/// <summary>
/// Minimal, untimed cartridge-enabled memory profile, not an IZ80Bus implementation.
/// Charles MacDonald, IDocs/General.txt section 2 (Z80 memory map / Register
/// mirroring): 8 KiB work RAM mirrors at E000-FFFF; exact FFFC-FFFF writes
/// reach both RAM and the Sega mapper, while reads there return RAM.
/// </summary>
/// <remarks>
/// No BIOS, port 3E, slot arbitration, disabled RAM, open-bus resolution, I/O,
/// upper-window cartridge RAM, peripherals or timing is modeled. The caller explicitly
/// selects this restricted profile and owns the injected mapper exclusively.
/// RAM initialization is deterministic configuration, not physical power-on behavior.
/// </remarks>
public sealed class SegaMemoryBus
{
    public const int RamSize = 0x2000;

    private readonly SegaMapper mapper;
    private readonly byte[] ram;

    /// <summary>Initializes every work RAM byte to the explicitly supplied value.</summary>
    public SegaMemoryBus(SegaMapper mapper, byte initialRamValue)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        this.mapper = mapper;
        ram = new byte[RamSize];
        ram.AsSpan().Fill(initialRamValue);
    }

    /// <summary>Copies exactly 8 KiB of explicitly supplied work RAM contents.</summary>
    public SegaMemoryBus(SegaMapper mapper, ReadOnlySpan<byte> initialRam)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        if (initialRam.Length != RamSize)
            throw new ArgumentException("Initial work RAM must contain exactly 8 KiB.", nameof(initialRam));

        this.mapper = mapper;
        ram = initialRam.ToArray();
    }

    /// <summary>
    /// Reads selected cartridge ROM/SRAM or work RAM. An unexpectedly undriven
    /// cartridge read is an invalid profile state, never a fabricated bus byte.
    /// </summary>
    public byte ReadMemory(ushort address)
    {
        if (address >= 0xc000)
            return ram[address & (RamSize - 1)];

        if (!mapper.TryRead(address, out byte value))
            throw new InvalidOperationException("The selected cartridge did not drive the memory read.");

        return value;
    }

    /// <summary>
    /// Writes work RAM, delivers 8000-BFFF cartridge writes and fans out exact
    /// FFFC-FFFF writes to the mapper. CartridgeRamWritten denotes selected SRAM;
    /// Ignored can still mean work RAM changed, not exclusive write consumption.
    /// UnsupportedControlValue MUST be handled as unsupported emulation by the
    /// caller: RAM has already received the byte, but the mapper is unchanged.
    /// There is no transaction rollback or implied support for that control mode.
    /// </summary>
    public MapperWriteResult WriteMemory(ushort address, byte value)
    {
        if (address >= 0xc000)
            ram[address & (RamSize - 1)] = value;

        return address is >= 0x8000 and < 0xc000 or >= 0xfffc
            ? mapper.ObserveWrite(address, value)
            : MapperWriteResult.Ignored;
    }
}
