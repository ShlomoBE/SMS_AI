namespace Sms.Core.Cpu;

/// <summary>An intentionally incomplete Z80 foundation; unsupported instructions fail explicitly.</summary>
public sealed class Z80(IZ80Bus bus)
{
    private readonly IZ80Bus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    // Deterministic prototype initialization, not a claim about physical reset.
    // Future instructions that change MEMPTR must update this hidden state.
    private ushort _memoryPointer = 0;

    public ushort ProgramCounter { get; set; }
    public ushort StackPointer { get; set; }
    public byte Refresh { get; set; }
    public byte A { get; set; }
    public byte B { get; set; }
    public byte C { get; set; }
    public byte D { get; set; }
    public byte E { get; set; }
    public byte H { get; set; }
    public byte L { get; set; }
    public byte F { get; set; }
    public bool IsFaulted { get; private set; }

    /// <summary>Executes one supported instruction and returns CPU T-states.</summary>
    public int Step()
    {
        if (IsFaulted)
        {
            throw new InvalidOperationException("The CPU is faulted and cannot continue; create a new machine.");
        }

        try
        {
            byte opcode = _bus.FetchOpcode(ProgramCounter);
            bool registerLoad = opcode >= 0x40 && opcode <= 0x7F && opcode != 0x76;
            bool immediateLoad = (opcode & 0xC7) == 0x06;
            bool wordImmediateLoad = (opcode & 0xCF) == 0x01;
            bool incDec8 = (opcode & 0xC6) == 0x04;
            bool arithmetic = opcode is >= 0x80 and <= 0x9F or >= 0xB8 and <= 0xBF
                or 0xC6 or 0xCE or 0xD6 or 0xDE or 0xFE;
            bool logical = opcode is >= 0xA0 and <= 0xB7 or 0xE6 or 0xEE or 0xF6;
            if (opcode != 0x00 && opcode != 0xCB && !registerLoad && !immediateLoad && !wordImmediateLoad && !incDec8 && !arithmetic && !logical)
            {
                throw new NotSupportedException($"Z80 opcode 0x{opcode:X2} at 0x{ProgramCounter:X4} is not implemented.");
            }

            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            if (opcode == 0xCB) return ExecuteCb();
            if (incDec8) return ExecuteIncDec8(opcode);
            if (arithmetic) return ExecuteArithmetic(opcode);
            if (logical) return ExecuteLogical(opcode);
            if (wordImmediateLoad)
            {
                byte low = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                byte high = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                switch (opcode)
                {
                    case 0x01: B = high; C = low; break;
                    case 0x11: D = high; E = low; break;
                    case 0x21: H = high; L = low; break;
                    case 0x31: StackPointer = (ushort)((high << 8) | low); break;
                }
                // Boo-boo et al., Z80 MEMPTR: instructions not listed do not
                // affect MEMPTR; immediate register-pair loads are not listed.
                return 10;
            }
            if (registerLoad)
            {
                int destination = (opcode >> 3) & 7;
                int source = opcode & 7;
                byte value = ReadRegister(source);
                WriteRegister(destination, value);
                return source == 6 || destination == 6 ? 7 : 4;
            }

            if (immediateLoad)
            {
                int destination = (opcode >> 3) & 7;
                byte value = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                WriteRegister(destination, value);
                return destination == 6 ? 10 : 7;
            }

            return 4;
        }
        catch
        {
            IsFaulted = true;
            throw;
        }
    }

    private ushort HL => (ushort)((H << 8) | L);

    // ZiLOG UM0080 logical instructions and timing. Young's undocumented
    // result-derived F3/F5 and unchanged MEMPTR are not verified on SMS silicon.
    private int ExecuteLogical(byte opcode)
    {
        bool immediate = opcode >= 0xC0;
        int operation = immediate ? (opcode - 0xE6) >> 3 : (opcode - 0xA0) >> 3;
        byte operand;
        if (immediate)
        {
            operand = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
        }
        else operand = ReadRegister(opcode & 7);

        byte result = operation switch
        {
            0 => (byte)(A & operand),
            1 => (byte)(A ^ operand),
            2 => (byte)(A | operand),
            _ => throw new InvalidOperationException()
        };
        int parity = System.Numerics.BitOperations.PopCount((uint)result) % 2 == 0 ? 4 : 0;
        A = result;
        F = (byte)((result & 0xA8) | (result == 0 ? 0x40 : 0)
            | (operation == 0 ? 0x10 : 0) | parity);
        return immediate || (opcode & 7) == 6 ? 7 : 4;
    }

    // ZiLOG UM0080 8-bit arithmetic; Young, The Undocumented Z80 Documented,
    // F3/F5 follow the result except CP, where they follow the operand.
    // Boo-boo et al. MEMPTR research does not list these instructions as
    // modifying MEMPTR. Undocumented behavior is not verified on SMS silicon.
    private int ExecuteArithmetic(byte opcode)
    {
        bool immediate = opcode >= 0xC0;
        int operation = immediate ? (opcode == 0xFE ? 7 : (opcode - 0xC6) >> 3) : (opcode - 0x80) >> 3;
        byte operand;
        if (immediate)
        {
            operand = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
        }
        else operand = ReadRegister(opcode & 7);

        int accumulator = A;
        bool subtract = operation is 2 or 3 or 7;
        int carry = operation is 1 or 3 ? F & 1 : 0;
        int wide = subtract ? accumulator - operand - carry : accumulator + operand + carry;
        byte result = unchecked((byte)wide);
        int half = ((accumulator ^ operand ^ result) & 0x10) != 0 ? 0x10 : 0;
        int overflow = (subtract
            ? ((accumulator ^ operand) & (accumulator ^ result) & 0x80)
            : (~(accumulator ^ operand) & (accumulator ^ result) & 0x80)) != 0 ? 4 : 0;
        F = (byte)((result & 0x80) | (result == 0 ? 0x40 : 0)
            | ((operation == 7 ? operand : result) & 0x28) | half | overflow
            | (subtract ? 2 : 0) | (wide < 0 || wide > 255 ? 1 : 0));
        if (operation != 7) A = result;
        return immediate || (opcode & 7) == 6 ? 7 : 4;
    }

    // ZiLOG UM0080 INC/DEC 8-bit group; Young, The Undocumented Z80
    // Documented, flags 3/5 follow the result. SMS silicon is not verified.
    private int ExecuteIncDec8(byte opcode)
    {
        int target = (opcode >> 3) & 7;
        bool decrement = (opcode & 1) != 0;
        byte value = ReadRegister(target);
        byte result = unchecked((byte)(value + (decrement ? -1 : 1)));
        WriteRegister(target, result);
        int halfCarry = decrement ? (value & 15) == 0 ? 0x10 : 0 : (value & 15) == 15 ? 0x10 : 0;
        int overflow = value == (decrement ? 0x80 : 0x7f) ? 4 : 0;
        F = (byte)((F & 1) | (result & 0xa8) | (result == 0 ? 0x40 : 0)
            | halfCarry | overflow | (decrement ? 2 : 0));
        return target == 6 ? 11 : 4;
    }

    // ZiLOG UM0080, rotate/shift instructions; undocumented F3/F5 follow the
    // result per Sean Young, The Undocumented Z80 Documented, flag bits 3/5.
    // The exact SMS CPU variant remains unverified.
    private int ExecuteCb()
    {
        ushort address = ProgramCounter;
        byte opcode = _bus.FetchOpcode(address);
        ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
        Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
        if (opcode >= 0x40) return ExecuteBitOperation(opcode);
        int operation = opcode >> 3;
        if (operation >= 8 || operation == 6)
            throw new NotSupportedException($"Z80 CB opcode 0x{opcode:X2} at 0x{address:X4} is not implemented.");

        int target = opcode & 7;
        byte value = ReadRegister(target);
        int oldCarry = F & 1;
        int carry = operation is 0 or 2 or 4 ? value >> 7 : value & 1;
        byte result = operation switch
        {
            0 => (byte)((value << 1) | carry),
            1 => (byte)((value >> 1) | (carry << 7)),
            2 => (byte)((value << 1) | oldCarry),
            3 => (byte)((value >> 1) | (oldCarry << 7)),
            4 => (byte)(value << 1),
            5 => (byte)((value >> 1) | (value & 0x80)),
            7 => (byte)(value >> 1),
            _ => throw new InvalidOperationException()
        };
        WriteRegister(target, result);
        int parity = System.Numerics.BitOperations.PopCount((uint)result) % 2 == 0 ? 4 : 0;
        F = (byte)((result & 0xA8) | (result == 0 ? 0x40 : 0) | parity | carry);
        return target == 6 ? 15 : 8;
    }

    // ZiLOG UM0080 BIT/RES/SET; Sean Young describes BIT S/PV behavior.
    // Register BIT F3/F5 here follow operand bits, supported by independent
    // CPC tests but described differently in Young §4.1; SMS silicon is unverified.
    // BIT (HL) uses prior MEMPTR high byte (Boo-boo et al., Z80 MEMPTR).
    // MEMPTR initialization and undocumented flags remain variant-dependent.
    private int ExecuteBitOperation(byte opcode)
    {
        int target = opcode & 7;
        int bit = (opcode >> 3) & 7;
        byte value = ReadRegister(target);
        int mask = 1 << bit;
        if (opcode < 0x80)
        {
            bool set = (value & mask) != 0;
            int undocumented = target == 6 ? (_memoryPointer >> 8) & 0x28 : value & 0x28;
            F = (byte)((F & 1) | 0x10 | undocumented | (set ? 0 : 0x44)
                | (bit == 7 && set ? 0x80 : 0));
            return target == 6 ? 12 : 8;
        }

        byte result = opcode < 0xC0 ? (byte)(value & ~mask) : (byte)(value | mask);
        WriteRegister(target, result);
        return target == 6 ? 15 : 8;
    }

    private byte ReadRegister(int register) => register switch
    {
        0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L,
        6 => _bus.ReadMemory(HL), 7 => A,
        _ => throw new ArgumentOutOfRangeException(nameof(register))
    };

    private void WriteRegister(int register, byte value)
    {
        switch (register)
        {
            case 0: B = value; break;
            case 1: C = value; break;
            case 2: D = value; break;
            case 3: E = value; break;
            case 4: H = value; break;
            case 5: L = value; break;
            case 6: _bus.WriteMemory(HL, value); break;
            case 7: A = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(register));
        }
    }
}
