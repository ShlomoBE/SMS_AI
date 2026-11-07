namespace Sms.Core.Vdp;

/// <summary>
/// Untimed SMS port-state experiment, not a reset model or a complete VDP.
/// Charles MacDonald, IDocs/VDP.txt §§3–5; https://www.smspower.org/Development/ControlPort
/// and https://www.smspower.org/Development/VDPRegisters. Events are explicitly injected;
/// no timing or rendering is modeled. Status bits 4–0 are a fixed fixture approximation.
/// IDocs/VDP.txt §4 assigns collision to bit 5, but §10 also calls it bit 6;
/// the §4 assignment is provisional pending independent hardware verification.
/// </summary>
public sealed class VdpPorts
{
    private readonly byte[] vram;
    private readonly byte[] cram;
    private readonly byte[] registers;
    private int address;
    private int code;
    private bool secondControlByte;
    private byte readBuffer;
    private byte status;
    private bool linePending;

    private VdpPorts(byte[] vram, byte[] cram, byte[] registers, int address, int code,
        bool secondControlByte, byte readBuffer, byte status, bool linePending)
    {
        this.vram = (byte[])vram.Clone();
        this.cram = (byte[])cram.Clone();
        this.registers = (byte[])registers.Clone();
        this.address = address;
        this.code = code;
        this.secondControlByte = secondControlByte;
        this.readBuffer = readBuffer;
        this.status = status;
        this.linePending = linePending;
    }

    /// <summary>All state is explicitly supplied; this does not describe hardware power-on.</summary>
    public static VdpPorts CreateDeterministicFixture(byte[] vram, byte[] cram, byte[] registers,
        int address, int code, bool secondControlByte, byte readBuffer, byte status, bool linePending)
    {
        ArgumentNullException.ThrowIfNull(vram);
        ArgumentNullException.ThrowIfNull(cram);
        ArgumentNullException.ThrowIfNull(registers);
        if (vram.Length != 0x4000 || cram.Length != 32 || registers.Length != 11)
            throw new ArgumentException("Expected 16 KiB VRAM, 32 CRAM bytes, and 11 registers.");
        if (Array.Exists(cram, value => value > 0x3f))
            throw new ArgumentException("CRAM fixture values must contain only six color bits.", nameof(cram));
        if ((uint)address > 0x3fff) throw new ArgumentOutOfRangeException(nameof(address));
        if ((uint)code > 3) throw new ArgumentOutOfRangeException(nameof(code));
        return new(vram, cram, registers, address, code, secondControlByte, readBuffer, status, linePending);
    }

    /// <summary>Current IRQ level, derived from pending sources and enables (IDocs/VDP.txt §12).</summary>
    public bool IrqAsserted => ((status & 0x80) != 0 && (registers[1] & 0x20) != 0)
        || (linePending && (registers[0] & 0x10) != 0);

    // Injection represents an event already determined by a future timing/renderer owner;
    // these latches do not schedule events or imply their physical timing (§§4, 12).
    public void InjectFrameInterrupt() => status |= 0x80;
    public void InjectLineInterrupt() => linePending = true;
    public void InjectSpriteOverflow() => status |= 0x40;
    public void InjectSpriteCollision() => status |= 0x20;

    public void WriteControl(byte value)
    {
        if (!secondControlByte)
        {
            address = (address & 0x3f00) | value;
            secondControlByte = true;
            return;
        }

        secondControlByte = false;
        address = (address & 0xff) | ((value & 0x3f) << 8);
        code = value >> 6;
        if (code == 0)
        {
            readBuffer = vram[address];
            IncrementAddress();
        }
        else if (code == 2 && (value & 15) < registers.Length)
        {
            registers[value & 15] = (byte)address;
        }
    }

    public byte ReadData()
    {
        secondControlByte = false;
        byte result = readBuffer;
        readBuffer = vram[address];
        IncrementAddress();
        return result;
    }

    public void WriteData(byte value)
    {
        secondControlByte = false;
        if (code == 3) cram[address & 31] = (byte)(value & 0x3f);
        else vram[address] = value;
        readBuffer = value;
        IncrementAddress();
    }

    public byte ReadStatus()
    {
        secondControlByte = false;
        byte result = status;
        status &= 0x1f;
        linePending = false;
        return result;
    }

    private void IncrementAddress() => address = (address + 1) & 0x3fff;

    public byte[] InternalVram => vram;
    public byte[] InternalCram => cram;
    public byte[] InternalRegisters => registers;

    public VdpPortSnapshot Inspect() => new(vram, cram, registers, address, code,
        secondControlByte, readBuffer, status, linePending);
}

/// <summary>Detached, read-only inspection of the prototype's state.</summary>
public sealed class VdpPortSnapshot
{
    internal VdpPortSnapshot(byte[] vram, byte[] cram, byte[] registers, int address, int code,
        bool secondControlByte, byte readBuffer, byte status, bool linePending)
    {
        Vram = Array.AsReadOnly((byte[])vram.Clone());
        Cram = Array.AsReadOnly((byte[])cram.Clone());
        Registers = Array.AsReadOnly((byte[])registers.Clone());
        Address = address;
        Code = code;
        SecondControlByte = secondControlByte;
        ReadBuffer = readBuffer;
        Status = status;
        LinePending = linePending;
    }

    public IReadOnlyList<byte> Vram { get; }
    public IReadOnlyList<byte> Cram { get; }
    public IReadOnlyList<byte> Registers { get; }
    public int Address { get; }
    public int Code { get; }
    public bool SecondControlByte { get; }
    public byte ReadBuffer { get; }
    public byte Status { get; }
    public bool LinePending { get; }
}
