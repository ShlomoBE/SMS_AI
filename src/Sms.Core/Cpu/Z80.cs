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
    public ushort IX { get; set; }
    public ushort IY { get; set; }

    public byte IXH
    {
        get => (byte)(IX >> 8);
        set => IX = (ushort)((value << 8) | (IX & 0xFF));
    }

    public byte IXL
    {
        get => (byte)(IX & 0xFF);
        set => IX = (ushort)((IX & 0xFF00) | value);
    }

    public byte IYH
    {
        get => (byte)(IY >> 8);
        set => IY = (ushort)((value << 8) | (IY & 0xFF));
    }

    public byte IYL
    {
        get => (byte)(IY & 0xFF);
        set => IY = (ushort)((IY & 0xFF00) | value);
    }

    public bool IsFaulted { get; private set; }

    public byte APrime { get; set; }
    public byte BPrime { get; set; }
    public byte CPrime { get; set; }
    public byte DPrime { get; set; }
    public byte EPrime { get; set; }
    public byte HPrime { get; set; }
    public byte LPrime { get; set; }
    public byte FPrime { get; set; }

    public bool IsHalted { get; set; }
    public bool IFF1 { get; set; }
    public bool IFF2 { get; set; }
    public bool PendingEi { get; set; }
    public byte InterruptMode { get; set; }
    public byte I { get; set; }
    public byte R
    {
        get => Refresh;
        set => Refresh = value;
    }

    /// <summary>Executes one supported instruction and returns CPU T-states.</summary>
    public int Step()
    {
        if (IsFaulted)
        {
            throw new InvalidOperationException("The CPU is faulted and cannot continue; create a new machine.");
        }

        bool hadPendingEi = PendingEi;

        try
        {
            if (IsHalted)
            {
                _bus.FetchOpcode(unchecked((ushort)(ProgramCounter - 1)));
                Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
                if (hadPendingEi) PendingEi = false;
                return 4;
            }

            byte opcode = _bus.FetchOpcode(ProgramCounter);
            int prefixCycles = 0;
            bool useIY = false;
            bool hasPrefix = false;

            while (opcode is 0xDD or 0xFD)
            {
                hasPrefix = true;
                useIY = opcode == 0xFD;
                prefixCycles += 4;
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
                opcode = _bus.FetchOpcode(ProgramCounter);
            }

            if (hasPrefix && ExecuteIndexOpcode(opcode, useIY, prefixCycles, out int indexCycles))
            {
                if (hadPendingEi && opcode != 0xFB)
                {
                    PendingEi = false;
                }
                return indexCycles;
            }

            bool registerLoad = opcode >= 0x40 && opcode <= 0x7F && opcode != 0x76;
            bool immediateLoad = (opcode & 0xC7) == 0x06;
            bool wordImmediateLoad = (opcode & 0xCF) == 0x01;
            bool incDec8 = (opcode & 0xC6) == 0x04;
            bool incDec16 = (opcode & 0xC7) is 0x03 or 0x0B;
            bool pushPop = (opcode & 0xCF) is 0xC5 or 0xC1;
            bool indirectALoadStore = opcode is 0x02 or 0x12 or 0x0A or 0x1A or 0x32 or 0x3A;
            bool accRotates = opcode is 0x07 or 0x0F or 0x17 or 0x1F;
            bool djnz = opcode == 0x10;
            bool arithmetic = opcode is >= 0x80 and <= 0x9F or >= 0xB8 and <= 0xBF
                or 0xC6 or 0xCE or 0xD6 or 0xDE or 0xFE;
            bool logical = opcode is >= 0xA0 and <= 0xB7 or 0xE6 or 0xEE or 0xF6;
            bool controlFlow = opcode is 0x18 or 0xC3 or 0xC9 or 0xCD or 0xE9
                || (opcode & 0xE7) == 0x20
                || (opcode & 0xC7) is 0xC0 or 0xC2 or 0xC4 or 0xC7;
            bool ioUnprefixed = opcode is 0xDB or 0xD3;
            bool addHl = (opcode & 0xCF) == 0x09;
            bool specialOps = opcode is 0x08 or 0x27 or 0x2F or 0x37 or 0x3F or 0x76 or 0xD9 or 0xE3 or 0xEB or 0xF3 or 0xFB;
            bool direct16BitLoads = opcode is 0x22 or 0x2A or 0xF9;

            if (opcode != 0x00 && opcode != 0xCB && opcode != 0xED && !ioUnprefixed && !registerLoad && !immediateLoad && !wordImmediateLoad && !incDec8 && !incDec16 && !pushPop && !indirectALoadStore && !accRotates && !djnz && !arithmetic && !logical && !controlFlow && !addHl && !specialOps && !direct16BitLoads)
            {
                throw new NotSupportedException($"Z80 opcode 0x{opcode:X2} at 0x{ProgramCounter:X4} is not implemented.");
            }

            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));

            int cycles;
            if (opcode == 0xCB) cycles = ExecuteCb();
            else if (opcode == 0xED) cycles = ExecuteEd();
            else if (ioUnprefixed) cycles = ExecuteUnprefixedIo(opcode);
            else if (controlFlow) cycles = ExecuteControlFlow(opcode);
            else if (incDec8) cycles = ExecuteIncDec8(opcode);
            else if (incDec16) cycles = ExecuteIncDec16(opcode);
            else if (pushPop) cycles = ExecutePushPop(opcode);
            else if (indirectALoadStore) cycles = ExecuteIndirectALoadStore(opcode);
            else if (accRotates) cycles = ExecuteAccRotates(opcode);
            else if (djnz) cycles = ExecuteDjnz();
            else if (arithmetic) cycles = ExecuteArithmetic(opcode);
            else if (logical) cycles = ExecuteLogical(opcode);
            else if (addHl) cycles = ExecuteAddHl(opcode);
            else if (specialOps) cycles = ExecuteSpecial(opcode);
            else if (opcode == 0x22)
            {
                byte low = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                byte high = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                ushort address = (ushort)((high << 8) | low);
                _bus.WriteMemory(address, L);
                _bus.WriteMemory(unchecked((ushort)(address + 1)), H);
                _memoryPointer = unchecked((ushort)(address + 1));
                cycles = 16;
            }
            else if (opcode == 0x2A)
            {
                byte low = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                byte high = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                ushort address = (ushort)((high << 8) | low);
                L = _bus.ReadMemory(address);
                H = _bus.ReadMemory(unchecked((ushort)(address + 1)));
                _memoryPointer = unchecked((ushort)(address + 1));
                cycles = 16;
            }
            else if (opcode == 0xF9)
            {
                StackPointer = HL;
                cycles = 6;
            }
            else if (wordImmediateLoad)
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
                cycles = 10;
            }
            else if (registerLoad)
            {
                int destination = (opcode >> 3) & 7;
                int source = opcode & 7;
                byte value = ReadRegister(source);
                WriteRegister(destination, value);
                cycles = source == 6 || destination == 6 ? 7 : 4;
            }
            else if (immediateLoad)
            {
                int destination = (opcode >> 3) & 7;
                byte value = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                WriteRegister(destination, value);
                cycles = destination == 6 ? 10 : 7;
            }
            else
            {
                cycles = 4;
            }

            if (hadPendingEi && opcode != 0xFB)
            {
                PendingEi = false;
            }

            return cycles + prefixCycles;
        }
        catch
        {
            IsFaulted = true;
            throw;
        }
    }

    /// <summary>Processes a Non-Maskable Interrupt (NMI) and returns T-states consumed (11 T-states).</summary>
    public int AcceptNmi()
    {
        if (IsHalted)
        {
            IsHalted = false;
        }

        IFF2 = IFF1;
        IFF1 = false;

        StackPointer = unchecked((ushort)(StackPointer - 1));
        _bus.WriteMemory(StackPointer, (byte)(ProgramCounter >> 8));
        StackPointer = unchecked((ushort)(StackPointer - 1));
        _bus.WriteMemory(StackPointer, (byte)ProgramCounter);

        ProgramCounter = 0x0066;
        return 11;
    }

    /// <summary>
    /// Processes a maskable interrupt (IRQ) if enabled and not inhibited by pending EI.
    /// Returns T-states consumed (0 if inhibited, 13 for IM 0/1, 19 for IM 2).
    /// </summary>
    public int AcceptIrq()
    {
        if (!IFF1 || PendingEi)
        {
            return 0;
        }

        if (IsHalted)
        {
            IsHalted = false;
        }

        IFF1 = false;
        IFF2 = false;

        byte vector = _bus.AcknowledgeInterrupt();

        switch (InterruptMode)
        {
            case 0:
            {
                if ((vector & 0xC7) == 0xC7) // RST p
                {
                    ushort target = (ushort)(vector & 0x38);
                    StackPointer = unchecked((ushort)(StackPointer - 1));
                    _bus.WriteMemory(StackPointer, (byte)(ProgramCounter >> 8));
                    StackPointer = unchecked((ushort)(StackPointer - 1));
                    _bus.WriteMemory(StackPointer, (byte)ProgramCounter);
                    ProgramCounter = target;
                }
                return 13;
            }

            case 1:
            {
                StackPointer = unchecked((ushort)(StackPointer - 1));
                _bus.WriteMemory(StackPointer, (byte)(ProgramCounter >> 8));
                StackPointer = unchecked((ushort)(StackPointer - 1));
                _bus.WriteMemory(StackPointer, (byte)ProgramCounter);
                ProgramCounter = 0x0038;
                return 13;
            }

            case 2:
            {
                ushort tableAddress = (ushort)((I << 8) | (vector & 0xFE));
                byte targetLow = _bus.ReadMemory(tableAddress);
                byte targetHigh = _bus.ReadMemory(unchecked((ushort)(tableAddress + 1)));
                ushort targetAddress = (ushort)((targetHigh << 8) | targetLow);

                StackPointer = unchecked((ushort)(StackPointer - 1));
                _bus.WriteMemory(StackPointer, (byte)(ProgramCounter >> 8));
                StackPointer = unchecked((ushort)(StackPointer - 1));
                _bus.WriteMemory(StackPointer, (byte)ProgramCounter);

                ProgramCounter = targetAddress;
                return 19;
            }

            default:
                throw new InvalidOperationException($"Invalid interrupt mode: {InterruptMode}");
        }
    }

    private ushort BC
    {
        get => (ushort)((B << 8) | C);
        set { B = (byte)(value >> 8); C = (byte)value; }
    }

    private ushort DE
    {
        get => (ushort)((D << 8) | E);
        set { D = (byte)(value >> 8); E = (byte)value; }
    }

    private ushort HL
    {
        get => (ushort)((H << 8) | L);
        set { H = (byte)(value >> 8); L = (byte)value; }
    }

    private ushort BCPrime
    {
        get => (ushort)((BPrime << 8) | CPrime);
        set { BPrime = (byte)(value >> 8); CPrime = (byte)value; }
    }

    private ushort DEPrime
    {
        get => (ushort)((DPrime << 8) | EPrime);
        set { DPrime = (byte)(value >> 8); EPrime = (byte)value; }
    }

    private ushort HLPrime
    {
        get => (ushort)((HPrime << 8) | LPrime);
        set { HPrime = (byte)(value >> 8); LPrime = (byte)value; }
    }

    // ZiLOG UM0080 control flow group: JP, JR, CALL, RET, RST and condition codes.
    // Boo-boo et al., Z80 MEMPTR research:
    // JP nn, CALL nn, JP cc,nn (taken), CALL cc,nn (taken) set MEMPTR = nn.
    // JR e, JR cc,e (taken), RST p set MEMPTR = target.
    // RET, RET cc, JP (HL) leave MEMPTR unchanged.
    private int ExecuteControlFlow(byte opcode)
    {
        if (opcode == 0xE9) // JP (HL)
        {
            ProgramCounter = HL;
            return 4;
        }

        if (opcode == 0x18) // JR e
        {
            sbyte offset = (sbyte)_bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort target = unchecked((ushort)(ProgramCounter + offset));
            ProgramCounter = target;
            _memoryPointer = target;
            return 12;
        }

        if ((opcode & 0xE7) == 0x20) // JR cc, e
        {
            sbyte offset = (sbyte)_bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            int cc = (opcode >> 3) & 3;
            if (CheckCondition(cc))
            {
                ushort target = unchecked((ushort)(ProgramCounter + offset));
                ProgramCounter = target;
                _memoryPointer = target;
                return 12;
            }
            return 7;
        }

        if (opcode == 0xC3) // JP nn
        {
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort target = (ushort)((high << 8) | low);
            ProgramCounter = target;
            _memoryPointer = target;
            return 10;
        }

        if ((opcode & 0xC7) == 0xC2) // JP cc, nn
        {
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort target = (ushort)((high << 8) | low);
            int cc = (opcode >> 3) & 7;
            if (CheckCondition(cc))
            {
                ProgramCounter = target;
                _memoryPointer = target;
            }
            return 10;
        }

        if (opcode == 0xCD) // CALL nn
        {
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort target = (ushort)((high << 8) | low);
            ushort returnAddress = ProgramCounter;
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, (byte)(returnAddress >> 8));
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, (byte)returnAddress);
            ProgramCounter = target;
            _memoryPointer = target;
            return 17;
        }

        if ((opcode & 0xC7) == 0xC4) // CALL cc, nn
        {
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort target = (ushort)((high << 8) | low);
            int cc = (opcode >> 3) & 7;
            if (CheckCondition(cc))
            {
                ushort returnAddress = ProgramCounter;
                StackPointer = unchecked((ushort)(StackPointer - 1));
                _bus.WriteMemory(StackPointer, (byte)(returnAddress >> 8));
                StackPointer = unchecked((ushort)(StackPointer - 1));
                _bus.WriteMemory(StackPointer, (byte)returnAddress);
                ProgramCounter = target;
                _memoryPointer = target;
                return 17;
            }
            return 10;
        }

        if (opcode == 0xC9) // RET
        {
            byte low = _bus.ReadMemory(StackPointer);
            StackPointer = unchecked((ushort)(StackPointer + 1));
            byte high = _bus.ReadMemory(StackPointer);
            StackPointer = unchecked((ushort)(StackPointer + 1));
            ProgramCounter = (ushort)((high << 8) | low);
            return 10;
        }

        if ((opcode & 0xC7) == 0xC0) // RET cc
        {
            int cc = (opcode >> 3) & 7;
            if (CheckCondition(cc))
            {
                byte low = _bus.ReadMemory(StackPointer);
                StackPointer = unchecked((ushort)(StackPointer + 1));
                byte high = _bus.ReadMemory(StackPointer);
                StackPointer = unchecked((ushort)(StackPointer + 1));
                ProgramCounter = (ushort)((high << 8) | low);
                return 11;
            }
            return 5;
        }

        if ((opcode & 0xC7) == 0xC7) // RST p
        {
            ushort target = (ushort)(opcode & 0x38);
            ushort returnAddress = ProgramCounter;
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, (byte)(returnAddress >> 8));
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, (byte)returnAddress);
            ProgramCounter = target;
            _memoryPointer = target;
            return 11;
        }

        throw new InvalidOperationException($"Unhandled control flow opcode: 0x{opcode:X2}");
    }

    private bool CheckCondition(int cc) => cc switch
    {
        0 => (F & 0x40) == 0, // NZ
        1 => (F & 0x40) != 0, // Z
        2 => (F & 0x01) == 0, // NC
        3 => (F & 0x01) != 0, // C
        4 => (F & 0x04) == 0, // PO
        5 => (F & 0x04) != 0, // PE
        6 => (F & 0x80) == 0, // P
        7 => (F & 0x80) != 0, // M
        _ => throw new ArgumentOutOfRangeException(nameof(cc))
    };

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
        int parity = global::System.Numerics.BitOperations.PopCount((uint)result) % 2 == 0 ? 4 : 0;
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
        int parity = global::System.Numerics.BitOperations.PopCount((uint)result) % 2 == 0 ? 4 : 0;
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

    private int ExecuteIncDec16(byte opcode)
    {
        bool decrement = (opcode & 0x08) != 0;
        int regPair = (opcode >> 4) & 3;
        ushort val = regPair switch
        {
            0 => BC,
            1 => DE,
            2 => HL,
            3 => StackPointer,
            _ => 0
        };
        val = unchecked((ushort)(val + (decrement ? -1 : 1)));
        switch (regPair)
        {
            case 0: BC = val; break;
            case 1: DE = val; break;
            case 2: HL = val; break;
            case 3: StackPointer = val; break;
        }
        return 6;
    }

    private int ExecutePushPop(byte opcode)
    {
        bool isPush = (opcode & 0x0F) == 0x05;
        int regPair = (opcode >> 4) & 3;

        if (isPush)
        {
            byte high = regPair switch
            {
                0 => B,
                1 => D,
                2 => H,
                3 => A,
                _ => 0
            };
            byte low = regPair switch
            {
                0 => C,
                1 => E,
                2 => L,
                3 => F,
                _ => 0
            };
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, high);
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, low);
            return 11;
        }
        else // POP
        {
            byte low = _bus.ReadMemory(StackPointer);
            StackPointer = unchecked((ushort)(StackPointer + 1));
            byte high = _bus.ReadMemory(StackPointer);
            StackPointer = unchecked((ushort)(StackPointer + 1));
            switch (regPair)
            {
                case 0: B = high; C = low; break;
                case 1: D = high; E = low; break;
                case 2: H = high; L = low; break;
                case 3: A = high; F = low; break;
            }
            return 10;
        }
    }

    private int ExecuteIndirectALoadStore(byte opcode)
    {
        switch (opcode)
        {
            case 0x02: // LD (BC), A
                _bus.WriteMemory(BC, A);
                _memoryPointer = (ushort)((A << 8) | ((BC + 1) & 0xFF));
                return 7;
            case 0x12: // LD (DE), A
                _bus.WriteMemory(DE, A);
                _memoryPointer = (ushort)((A << 8) | ((DE + 1) & 0xFF));
                return 7;
            case 0x0A: // LD A, (BC)
                A = _bus.ReadMemory(BC);
                _memoryPointer = unchecked((ushort)(BC + 1));
                return 7;
            case 0x1A: // LD A, (DE)
                A = _bus.ReadMemory(DE);
                _memoryPointer = unchecked((ushort)(DE + 1));
                return 7;
            case 0x32: // LD (nn), A
            {
                byte low = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                byte high = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                ushort address = (ushort)((high << 8) | low);
                _bus.WriteMemory(address, A);
                _memoryPointer = (ushort)((A << 8) | ((address + 1) & 0xFF));
                return 13;
            }
            case 0x3A: // LD A, (nn)
            {
                byte low = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                byte high = _bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                ushort address = (ushort)((high << 8) | low);
                A = _bus.ReadMemory(address);
                _memoryPointer = unchecked((ushort)(address + 1));
                return 13;
            }
            default:
                throw new InvalidOperationException($"Invalid indirect A load/store opcode 0x{opcode:X2}");
        }
    }

    private int ExecuteAccRotates(byte opcode)
    {
        bool oldCarry = (F & 0x01) != 0;
        bool newCarry;
        byte result;

        switch (opcode)
        {
            case 0x07: // RLCA
                newCarry = (A & 0x80) != 0;
                result = (byte)((A << 1) | (newCarry ? 1 : 0));
                break;
            case 0x0F: // RRCA
                newCarry = (A & 0x01) != 0;
                result = (byte)((A >> 1) | (newCarry ? 0x80 : 0));
                break;
            case 0x17: // RLA
                newCarry = (A & 0x80) != 0;
                result = (byte)((A << 1) | (oldCarry ? 1 : 0));
                break;
            case 0x1F: // RRA
                newCarry = (A & 0x01) != 0;
                result = (byte)((A >> 1) | (oldCarry ? 0x80 : 0));
                break;
            default:
                throw new InvalidOperationException($"Invalid accumulator rotate opcode 0x{opcode:X2}");
        }

        A = result;
        F = (byte)((F & 0xC4) | (result & 0x28) | (newCarry ? 0x01 : 0));
        return 4;
    }

    private int ExecuteDjnz()
    {
        sbyte offset = (sbyte)_bus.ReadMemory(ProgramCounter);
        ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
        B = unchecked((byte)(B - 1));

        if (B != 0)
        {
            ushort target = unchecked((ushort)(ProgramCounter + offset));
            ProgramCounter = target;
            _memoryPointer = target;
            return 13;
        }

        return 8;
    }

    private int ExecuteAddHl(byte opcode)
    {
        ushort ss = ((opcode >> 4) & 3) switch
        {
            0 => BC,
            1 => DE,
            2 => HL,
            3 => StackPointer,
            _ => throw new InvalidOperationException()
        };
        int val1 = HL;
        int val2 = ss;
        int sum = val1 + val2;
        ushort result = (ushort)sum;
        HL = result;
        int halfCarry = ((val1 ^ val2 ^ sum) & 0x1000) != 0 ? 0x10 : 0;
        int carry = sum > 0xFFFF ? 0x01 : 0;
        byte resultHigh = (byte)(result >> 8);
        int undocumented = resultHigh & 0x28;
        F = (byte)((F & 0xC4) | undocumented | halfCarry | carry);
        return 11;
    }

    private int ExecuteSpecial(byte opcode)
    {
        switch (opcode)
        {
            case 0x08: // EX AF, AF'
            {
                byte tempA = A; A = APrime; APrime = tempA;
                byte tempF = F; F = FPrime; FPrime = tempF;
                return 4;
            }
            case 0x27: // DAA
            {
                byte a = A;
                byte correction = 0;
                bool carry = (F & 0x01) != 0;
                bool halfCarry = (F & 0x10) != 0;
                bool subtract = (F & 0x02) != 0;

                if (halfCarry || (!subtract && (a & 0x0F) > 9))
                {
                    correction |= 0x06;
                }

                if (carry || (!subtract && a > 0x99))
                {
                    correction |= 0x60;
                    carry = true;
                }

                if (subtract)
                {
                    halfCarry = (F & 0x10) != 0 && (a & 0x0F) < 6;
                    a = unchecked((byte)(a - correction));
                }
                else
                {
                    halfCarry = (a & 0x0F) > 9;
                    a = unchecked((byte)(a + correction));
                }

                A = a;
                int parity = global::System.Numerics.BitOperations.PopCount((uint)a) % 2 == 0 ? 0x04 : 0;
                int sign = (a & 0x80) != 0 ? 0x80 : 0;
                int zero = a == 0 ? 0x40 : 0;
                int undocumented = a & 0x28;
                F = (byte)(sign | zero | undocumented | (halfCarry ? 0x10 : 0) | parity | (subtract ? 0x02 : 0) | (carry ? 0x01 : 0));
                return 4;
            }
            case 0x2F: // CPL
            {
                A = (byte)~A;
                int undocumented = A & 0x28;
                F = (byte)((F & 0xC5) | undocumented | 0x10 | 0x02);
                return 4;
            }
            case 0x37: // SCF
            {
                int undocumented = A & 0x28;
                F = (byte)((F & 0xC4) | undocumented | 0x01);
                return 4;
            }
            case 0x3F: // CCF
            {
                bool oldC = (F & 0x01) != 0;
                int undocumented = A & 0x28;
                F = (byte)((F & 0xC4) | undocumented | (oldC ? 0x10 : 0) | (oldC ? 0 : 0x01));
                return 4;
            }
            case 0x76: // HALT
            {
                IsHalted = true;
                return 4;
            }
            case 0xD9: // EXX
            {
                ushort tempBC = BC; BC = BCPrime; BCPrime = tempBC;
                ushort tempDE = DE; DE = DEPrime; DEPrime = tempDE;
                ushort tempHL = HL; HL = HLPrime; HLPrime = tempHL;
                return 4;
            }
            case 0xE3: // EX (SP), HL
            {
                byte low = _bus.ReadMemory(StackPointer);
                byte high = _bus.ReadMemory(unchecked((ushort)(StackPointer + 1)));
                _bus.WriteMemory(unchecked((ushort)(StackPointer + 1)), H);
                _bus.WriteMemory(StackPointer, L);
                _memoryPointer = (ushort)((high << 8) | low);
                H = high;
                L = low;
                return 19;
            }
            case 0xEB: // EX DE, HL
            {
                ushort temp = DE;
                DE = HL;
                HL = temp;
                return 4;
            }
            case 0xF3: // DI
            {
                IFF1 = false;
                IFF2 = false;
                PendingEi = false;
                return 4;
            }
            case 0xFB: // EI
            {
                IFF1 = true;
                IFF2 = true;
                PendingEi = true;
                return 4;
            }
            default:
                throw new InvalidOperationException($"Unhandled special opcode: 0x{opcode:X2}");
        }
    }

    private int ExecuteUnprefixedIo(byte opcode)
    {
        byte portLow = _bus.ReadMemory(ProgramCounter);
        ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
        ushort port = (ushort)((A << 8) | portLow);
        if (opcode == 0xDB)
        {
            A = _bus.ReadIo(port);
        }
        else
        {
            _bus.WriteIo(port, A);
        }
        return 11;
    }

    private int ExecuteEd()
    {
        ushort address = ProgramCounter;
        byte subOpcode = _bus.FetchOpcode(address);
        ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
        Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));

        if ((subOpcode & 0xCF) == 0x43)
        {
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort addr = (ushort)((high << 8) | low);
            _memoryPointer = addr;

            ushort val = ((subOpcode >> 4) & 3) switch
            {
                0 => BC,
                1 => DE,
                2 => HL,
                3 => StackPointer,
                _ => throw new InvalidOperationException()
            };

            _bus.WriteMemory(addr, (byte)(val & 0xFF));
            _bus.WriteMemory(unchecked((ushort)(addr + 1)), (byte)(val >> 8));
            return 20;
        }

        if ((subOpcode & 0xCF) == 0x4B)
        {
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort addr = (ushort)((high << 8) | low);
            _memoryPointer = addr;

            byte rLow = _bus.ReadMemory(addr);
            byte rHigh = _bus.ReadMemory(unchecked((ushort)(addr + 1)));
            ushort val = (ushort)((rHigh << 8) | rLow);

            switch ((subOpcode >> 4) & 3)
            {
                case 0: BC = val; break;
                case 1: DE = val; break;
                case 2: HL = val; break;
                case 3: StackPointer = val; break;
            }
            return 20;
        }

        if ((subOpcode & 0xCF) == 0x4A)
        {
            ushort ss = ((subOpcode >> 4) & 3) switch
            {
                0 => BC,
                1 => DE,
                2 => HL,
                3 => StackPointer,
                _ => throw new InvalidOperationException()
            };
            int val1 = HL;
            int val2 = ss;
            int c = F & 1;
            int sum = val1 + val2 + c;
            ushort result = (ushort)sum;
            HL = result;

            int sign = (result & 0x8000) != 0 ? 0x80 : 0;
            int zero = result == 0 ? 0x40 : 0;
            int undocumented = (result >> 8) & 0x28;
            int halfCarry = ((val1 ^ val2 ^ sum) & 0x1000) != 0 ? 0x10 : 0;
            int overflow = (~(val1 ^ val2) & (val1 ^ sum) & 0x8000) != 0 ? 0x04 : 0;
            int carry = sum > 0xFFFF ? 0x01 : 0;

            F = (byte)(sign | zero | undocumented | halfCarry | overflow | carry);
            return 15;
        }

        if ((subOpcode & 0xCF) == 0x42)
        {
            ushort ss = ((subOpcode >> 4) & 3) switch
            {
                0 => BC,
                1 => DE,
                2 => HL,
                3 => StackPointer,
                _ => throw new InvalidOperationException()
            };
            int val1 = HL;
            int val2 = ss;
            int c = F & 1;
            int diff = val1 - val2 - c;
            ushort result = (ushort)diff;
            HL = result;

            int sign = (result & 0x8000) != 0 ? 0x80 : 0;
            int zero = result == 0 ? 0x40 : 0;
            int undocumented = (result >> 8) & 0x28;
            int halfCarry = ((val1 ^ val2 ^ diff) & 0x1000) != 0 ? 0x10 : 0;
            int overflow = ((val1 ^ val2) & (val1 ^ diff) & 0x8000) != 0 ? 0x04 : 0;
            int nFlag = 0x02;
            int carry = diff < 0 ? 0x01 : 0;

            F = (byte)(sign | zero | undocumented | halfCarry | overflow | nFlag | carry);
            return 15;
        }

        if ((subOpcode & 0xC7) == 0x44)
        {
            byte original = A;
            byte result = unchecked((byte)(0 - original));
            A = result;

            int sign = (result & 0x80) != 0 ? 0x80 : 0;
            int zero = result == 0 ? 0x40 : 0;
            int undocumented = result & 0x28;
            int halfCarry = (original & 0x0F) != 0 ? 0x10 : 0;
            int overflow = original == 0x80 ? 0x04 : 0;
            int nFlag = 0x02;
            int carry = original != 0 ? 0x01 : 0;

            F = (byte)(sign | zero | undocumented | halfCarry | overflow | nFlag | carry);
            return 8;
        }

        if ((subOpcode & 0xC7) == 0x46)
        {
            int mode = ((subOpcode >> 3) & 7) switch
            {
                0 or 1 or 4 or 5 => 0,
                2 or 6 => 1,
                3 or 7 => 2,
                _ => throw new InvalidOperationException()
            };
            InterruptMode = (byte)mode;
            return 8;
        }

        if ((subOpcode & 0xC7) == 0x40)
        {
            int reg = (subOpcode >> 3) & 7;
            byte val = _bus.ReadIo(BC);
            if (reg != 6)
            {
                WriteRegisterNoMem(reg, val);
            }
            int parity = global::System.Numerics.BitOperations.PopCount((uint)val) % 2 == 0 ? 4 : 0;
            F = (byte)((F & 1) | (val & 0xA8) | (val == 0 ? 0x40 : 0) | parity);
            return 12;
        }

        if ((subOpcode & 0xC7) == 0x41)
        {
            int reg = (subOpcode >> 3) & 7;
            byte val = reg == 6 ? (byte)0 : ReadRegisterNoMem(reg);
            _bus.WriteIo(BC, val);
            return 12;
        }

        switch (subOpcode)
        {
            case 0x47: // LD I, A
            {
                I = A;
                return 9;
            }
            case 0x4F: // LD R, A
            {
                Refresh = (byte)((Refresh & 0x80) | (A & 0x7F));
                return 9;
            }
            case 0x57: // LD A, I
            {
                A = I;
                F = (byte)((A & 0x80) | (A == 0 ? 0x40 : 0) | (A & 0x28) | (IFF2 ? 0x04 : 0) | (F & 0x01));
                return 9;
            }
            case 0x5F: // LD A, R
            {
                A = Refresh;
                F = (byte)((A & 0x80) | (A == 0 ? 0x40 : 0) | (A & 0x28) | (IFF2 ? 0x04 : 0) | (F & 0x01));
                return 9;
            }
            case 0x45: // RETN
            case 0x55:
            case 0x65:
            case 0x75:
            {
                IFF1 = IFF2;
                byte low = _bus.ReadMemory(StackPointer);
                StackPointer = unchecked((ushort)(StackPointer + 1));
                byte high = _bus.ReadMemory(StackPointer);
                StackPointer = unchecked((ushort)(StackPointer + 1));
                ProgramCounter = (ushort)((high << 8) | low);
                return 14;
            }
            case 0x4D: // RETI
            case 0x5D:
            case 0x6D:
            case 0x7D:
            {
                byte low = _bus.ReadMemory(StackPointer);
                StackPointer = unchecked((ushort)(StackPointer + 1));
                byte high = _bus.ReadMemory(StackPointer);
                StackPointer = unchecked((ushort)(StackPointer + 1));
                ProgramCounter = (ushort)((high << 8) | low);
                return 14;
            }

            case 0xA0:
            case 0xB0:
            case 0xA8:
            case 0xB8:
            {
                byte val = _bus.ReadMemory(HL);
                _bus.WriteMemory(DE, val);
                bool decrement = subOpcode is 0xA8 or 0xB8;
                HL = unchecked((ushort)(HL + (decrement ? -1 : 1)));
                DE = unchecked((ushort)(DE + (decrement ? -1 : 1)));
                BC = unchecked((ushort)(BC - 1));

                F = (byte)((F & 0xC1) | (BC != 0 ? 0x04 : 0));

                bool repeat = subOpcode is 0xB0 or 0xB8;
                if (repeat && BC != 0)
                {
                    ProgramCounter = unchecked((ushort)(ProgramCounter - 2));
                    return 21;
                }
                return 16;
            }

            case 0xA1:
            case 0xB1:
            case 0xA9:
            case 0xB9:
            {
                byte val = _bus.ReadMemory(HL);
                bool decrement = subOpcode is 0xA9 or 0xB9;
                HL = unchecked((ushort)(HL + (decrement ? -1 : 1)));
                BC = unchecked((ushort)(BC - 1));

                byte res = unchecked((byte)(A - val));
                int halfBorrow = (A & 0x0F) < (val & 0x0F) ? 0x10 : 0;
                int sign = res & 0x80;
                int zero = A == val ? 0x40 : 0;
                int pv = BC != 0 ? 0x04 : 0;
                int n = 0x02;
                int carry = F & 0x01;
                F = (byte)(sign | zero | halfBorrow | pv | n | carry);

                bool repeat = subOpcode is 0xB1 or 0xB9;
                if (repeat && BC != 0 && (F & 0x40) == 0)
                {
                    ProgramCounter = unchecked((ushort)(ProgramCounter - 2));
                    return 21;
                }
                return 16;
            }

            case 0xA2:
            case 0xB2:
            case 0xAA:
            case 0xBA:
            {
                byte val = _bus.ReadIo(BC);
                _bus.WriteMemory(HL, val);
                bool decrement = subOpcode is 0xAA or 0xBA;
                HL = unchecked((ushort)(HL + (decrement ? -1 : 1)));
                B = unchecked((byte)(B - 1));

                F = (byte)((F & 0x01) | (B == 0 ? 0x40 : 0) | ((val & 0x80) >> 6));

                bool repeat = subOpcode is 0xB2 or 0xBA;
                if (repeat && B != 0)
                {
                    ProgramCounter = unchecked((ushort)(ProgramCounter - 2));
                    return 21;
                }
                return 16;
            }

            case 0xA3:
            case 0xB3:
            case 0xAB:
            case 0xBB:
            {
                byte val = _bus.ReadMemory(HL);
                B = unchecked((byte)(B - 1));
                _bus.WriteIo(BC, val);
                bool decrement = subOpcode is 0xAB or 0xBB;
                HL = unchecked((ushort)(HL + (decrement ? -1 : 1)));

                F = (byte)((F & 0x01) | (B == 0 ? 0x40 : 0) | ((val & 0x80) >> 6));

                bool repeat = subOpcode is 0xB3 or 0xBB;
                if (repeat && B != 0)
                {
                    ProgramCounter = unchecked((ushort)(ProgramCounter - 2));
                    return 21;
                }
                return 16;
            }

            case 0x67: // RRD
            {
                byte mem = _bus.ReadMemory(HL);
                byte oldA = A;
                A = (byte)((A & 0xF0) | (mem & 0x0F));
                byte newMem = (byte)(((oldA & 0x0F) << 4) | (mem >> 4));
                _bus.WriteMemory(HL, newMem);

                int sign = (A & 0x80) != 0 ? 0x80 : 0;
                int zero = A == 0 ? 0x40 : 0;
                int parity = global::System.Numerics.BitOperations.PopCount((uint)A) % 2 == 0 ? 0x04 : 0;
                int undocumented = A & 0x28;
                F = (byte)(sign | zero | undocumented | parity | (F & 0x01));
                return 18;
            }
            case 0x6F: // RLD
            {
                byte mem = _bus.ReadMemory(HL);
                byte oldA = A;
                A = (byte)((A & 0xF0) | (mem >> 4));
                byte newMem = (byte)(((mem & 0x0F) << 4) | (oldA & 0x0F));
                _bus.WriteMemory(HL, newMem);

                int sign = (A & 0x80) != 0 ? 0x80 : 0;
                int zero = A == 0 ? 0x40 : 0;
                int parity = global::System.Numerics.BitOperations.PopCount((uint)A) % 2 == 0 ? 0x04 : 0;
                int undocumented = A & 0x28;
                F = (byte)(sign | zero | undocumented | parity | (F & 0x01));
                return 18;
            }

            default:
                throw new NotSupportedException($"Z80 ED opcode 0x{subOpcode:X2} at 0x{address:X4} is not implemented.");
        }
    }

    private byte ReadRegisterNoMem(int register) => register switch
    {
        0 => B, 1 => C, 2 => D, 3 => E, 4 => H, 5 => L, 7 => A,
        _ => throw new ArgumentOutOfRangeException(nameof(register))
    };

    private void WriteRegisterNoMem(int register, byte value)
    {
        switch (register)
        {
            case 0: B = value; break;
            case 1: C = value; break;
            case 2: D = value; break;
            case 3: E = value; break;
            case 4: H = value; break;
            case 5: L = value; break;
            case 7: A = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(register));
        }
    }

    private byte ReadRegister(int register) => register == 6 ? _bus.ReadMemory(HL) : ReadRegisterNoMem(register);

    private void WriteRegister(int register, byte value)
    {
        if (register == 6)
        {
            _bus.WriteMemory(HL, value);
        }
        else
        {
            WriteRegisterNoMem(register, value);
        }
    }

    private bool ExecuteIndexOpcode(byte opcode, bool useIY, int prefixCycles, out int cycles)
    {
        ushort indexVal = GetIndexRegister(useIY);

        // ZiLOG UM0080: INC/DEC IX or IY leave all flags unchanged.
        if (opcode is 0x23 or 0x2B)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            SetIndexRegister(useIY, unchecked((ushort)(indexVal + (opcode == 0x23 ? 1 : -1))));
            cycles = prefixCycles + 6;
            return true;
        }

        if (opcode == 0xCB)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            sbyte d = (sbyte)_bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte cbOp = _bus.FetchOpcode(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));

            ushort addr = unchecked((ushort)(indexVal + d));
            _memoryPointer = addr;
            byte val = _bus.ReadMemory(addr);

            if (cbOp is >= 0x40 and <= 0x7F)
            {
                int bit = (cbOp >> 3) & 7;
                int mask = 1 << bit;
                bool set = (val & mask) != 0;
                int undocumented = (_memoryPointer >> 8) & 0x28;
                F = (byte)((F & 1) | 0x10 | undocumented | (set ? 0 : 0x44) | (bit == 7 && set ? 0x80 : 0));
                cycles = prefixCycles + 16;
            }
            else
            {
                byte res;
                if (cbOp < 0x40)
                {
                    int opGroup = cbOp >> 3;
                    int oldCarry = F & 1;
                    int carry = opGroup is 0 or 2 or 4 ? val >> 7 : val & 1;
                    res = opGroup switch
                    {
                        0 => (byte)((val << 1) | carry),
                        1 => (byte)((val >> 1) | (carry << 7)),
                        2 => (byte)((val << 1) | oldCarry),
                        3 => (byte)((val >> 1) | (oldCarry << 7)),
                        4 => (byte)(val << 1),
                        5 => (byte)((val >> 1) | (val & 0x80)),
                        7 => (byte)(val >> 1),
                        _ => throw new InvalidOperationException()
                    };
                    int parity = global::System.Numerics.BitOperations.PopCount((uint)res) % 2 == 0 ? 4 : 0;
                    F = (byte)((res & 0xA8) | (res == 0 ? 0x40 : 0) | parity | carry);
                }
                else
                {
                    int bit = (cbOp >> 3) & 7;
                    int mask = 1 << bit;
                    res = cbOp < 0xC0 ? (byte)(val & ~mask) : (byte)(val | mask);
                }

                _bus.WriteMemory(addr, res);
                int targetReg = cbOp & 7;
                if (targetReg != 6)
                {
                    WriteRegisterNoMem(targetReg, res);
                }
                cycles = prefixCycles + 19;
            }
            return true;
        }

        if (opcode == 0x21)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            SetIndexRegister(useIY, (ushort)((high << 8) | low));
            cycles = prefixCycles + 10;
            return true;
        }

        if (opcode == 0x22)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort addr = (ushort)((high << 8) | low);
            ushort val = GetIndexRegister(useIY);
            _bus.WriteMemory(addr, (byte)val);
            _bus.WriteMemory(unchecked((ushort)(addr + 1)), (byte)(val >> 8));
            _memoryPointer = unchecked((ushort)(addr + 1));
            cycles = prefixCycles + 16;
            return true;
        }

        if (opcode == 0x2A)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            byte low = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte high = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort addr = (ushort)((high << 8) | low);
            byte valLow = _bus.ReadMemory(addr);
            byte valHigh = _bus.ReadMemory(unchecked((ushort)(addr + 1)));
            SetIndexRegister(useIY, (ushort)((valHigh << 8) | valLow));
            _memoryPointer = unchecked((ushort)(addr + 1));
            cycles = prefixCycles + 16;
            return true;
        }

        if (opcode == 0xF9)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            StackPointer = indexVal;
            cycles = prefixCycles + 6;
            return true;
        }

        if ((opcode & 0xCF) == 0x09)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            ushort ssVal = ((opcode >> 4) & 3) switch
            {
                0 => BC,
                1 => DE,
                2 => indexVal,
                3 => StackPointer,
                _ => throw new InvalidOperationException()
            };
            int val1 = indexVal;
            int val2 = ssVal;
            int sum = val1 + val2;
            ushort result = (ushort)sum;
            SetIndexRegister(useIY, result);
            int halfCarry = ((val1 ^ val2 ^ sum) & 0x1000) != 0 ? 0x10 : 0;
            int carry = sum > 0xFFFF ? 0x01 : 0;
            byte resultHigh = (byte)(result >> 8);
            int undocumented = resultHigh & 0x28;
            F = (byte)((F & 0xC4) | undocumented | halfCarry | carry);
            cycles = prefixCycles + 11;
            return true;
        }

        if (opcode == 0xE1)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            byte low = _bus.ReadMemory(StackPointer);
            StackPointer = unchecked((ushort)(StackPointer + 1));
            byte high = _bus.ReadMemory(StackPointer);
            StackPointer = unchecked((ushort)(StackPointer + 1));
            SetIndexRegister(useIY, (ushort)((high << 8) | low));
            cycles = prefixCycles + 10;
            return true;
        }

        if (opcode == 0xE5)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            ushort val = GetIndexRegister(useIY);
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, (byte)(val >> 8));
            StackPointer = unchecked((ushort)(StackPointer - 1));
            _bus.WriteMemory(StackPointer, (byte)val);
            cycles = prefixCycles + 11;
            return true;
        }

        if (opcode == 0xE3)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            ushort val = GetIndexRegister(useIY);
            byte low = _bus.ReadMemory(StackPointer);
            byte high = _bus.ReadMemory(unchecked((ushort)(StackPointer + 1)));
            _bus.WriteMemory(unchecked((ushort)(StackPointer + 1)), (byte)(val >> 8));
            _bus.WriteMemory(StackPointer, (byte)val);
            ushort newVal = (ushort)((high << 8) | low);
            SetIndexRegister(useIY, newVal);
            _memoryPointer = newVal;
            cycles = prefixCycles + 19;
            return true;
        }

        if (opcode == 0xE9)
        {
            ProgramCounter = indexVal;
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            cycles = prefixCycles + 4;
            return true;
        }

        if (opcode == 0x34)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            sbyte d = (sbyte)_bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort addr = unchecked((ushort)(indexVal + d));
            _memoryPointer = addr;
            byte val = _bus.ReadMemory(addr);
            byte res = unchecked((byte)(val + 1));
            _bus.WriteMemory(addr, res);
            int halfCarry = (val & 15) == 15 ? 0x10 : 0;
            int overflow = val == 0x7F ? 4 : 0;
            F = (byte)((F & 1) | (res & 0xA8) | (res == 0 ? 0x40 : 0) | halfCarry | overflow);
            cycles = prefixCycles + 19;
            return true;
        }

        if (opcode == 0x35)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            sbyte d = (sbyte)_bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort addr = unchecked((ushort)(indexVal + d));
            _memoryPointer = addr;
            byte val = _bus.ReadMemory(addr);
            byte res = unchecked((byte)(val - 1));
            _bus.WriteMemory(addr, res);
            int halfCarry = (val & 15) == 0 ? 0x10 : 0;
            int overflow = val == 0x80 ? 4 : 0;
            F = (byte)((F & 1) | (res & 0xA8) | (res == 0 ? 0x40 : 0) | halfCarry | overflow | 2);
            cycles = prefixCycles + 19;
            return true;
        }

        if (opcode == 0x36)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            sbyte d = (sbyte)_bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            byte n = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            ushort addr = unchecked((ushort)(indexVal + d));
            _memoryPointer = addr;
            _bus.WriteMemory(addr, n);
            cycles = prefixCycles + 15;
            return true;
        }

        if (opcode is 0x24 or 0x25 or 0x2C or 0x2D)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            bool dec = (opcode & 1) != 0;
            bool isLow = opcode >= 0x2C;
            byte val = isLow ? GetIndexLow(useIY) : GetIndexHigh(useIY);
            byte res = unchecked((byte)(val + (dec ? -1 : 1)));
            if (isLow) SetIndexLow(useIY, res); else SetIndexHigh(useIY, res);
            int halfCarry = dec ? (val & 15) == 0 ? 0x10 : 0 : (val & 15) == 15 ? 0x10 : 0;
            int overflow = val == (dec ? 0x80 : 0x7F) ? 4 : 0;
            F = (byte)((F & 1) | (res & 0xA8) | (res == 0 ? 0x40 : 0) | halfCarry | overflow | (dec ? 2 : 0));
            cycles = prefixCycles + 6;
            return true;
        }

        if (opcode is 0x26 or 0x2E)
        {
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
            byte n = _bus.ReadMemory(ProgramCounter);
            ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
            bool isLow = opcode == 0x2E;
            if (isLow) SetIndexLow(useIY, n); else SetIndexHigh(useIY, n);
            cycles = prefixCycles + 7;
            return true;
        }

        if (opcode is >= 0x80 and <= 0xBF)
        {
            int src = opcode & 7;
            int operation = (opcode - 0x80) >> 3;
            if (src == 6)
            {
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
                sbyte d = (sbyte)_bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                ushort addr = unchecked((ushort)(indexVal + d));
                _memoryPointer = addr;
                byte operand = _bus.ReadMemory(addr);
                PerformAluOperation(operation, operand);
                cycles = prefixCycles + 15;
                return true;
            }
            else if (src == 4 || src == 5)
            {
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
                byte operand = src == 4 ? GetIndexHigh(useIY) : GetIndexLow(useIY);
                PerformAluOperation(operation, operand);
                cycles = prefixCycles + 4;
                return true;
            }
        }

        if (opcode is >= 0x40 and <= 0x7F && opcode != 0x76)
        {
            int dest = (opcode >> 3) & 7;
            int src = opcode & 7;
            if (src == 6)
            {
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
                sbyte d = (sbyte)_bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                ushort addr = unchecked((ushort)(indexVal + d));
                _memoryPointer = addr;
                byte val = _bus.ReadMemory(addr);
                WriteRegisterNoMem(dest, val);
                cycles = prefixCycles + 15;
                return true;
            }
            else if (dest == 6)
            {
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
                sbyte d = (sbyte)_bus.ReadMemory(ProgramCounter);
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                ushort addr = unchecked((ushort)(indexVal + d));
                _memoryPointer = addr;
                byte val = ReadRegisterNoMem(src);
                _bus.WriteMemory(addr, val);
                cycles = prefixCycles + 15;
                return true;
            }
            else if (dest == 4 || dest == 5 || src == 4 || src == 5)
            {
                ProgramCounter = unchecked((ushort)(ProgramCounter + 1));
                Refresh = (byte)((Refresh & 0x80) | ((Refresh + 1) & 0x7F));
                byte val = ReadIndexRegister8(useIY, src);
                WriteIndexRegister8(useIY, dest, val);
                cycles = prefixCycles + 4;
                return true;
            }
        }

        cycles = 0;
        return false;
    }

    private void PerformAluOperation(int operation, byte operand)
    {
        if (operation is 4 or 5 or 6)
        {
            int logicalOp = operation - 4;
            byte result = logicalOp switch
            {
                0 => (byte)(A & operand),
                1 => (byte)(A ^ operand),
                2 => (byte)(A | operand),
                _ => throw new InvalidOperationException()
            };
            int parity = global::System.Numerics.BitOperations.PopCount((uint)result) % 2 == 0 ? 4 : 0;
            A = result;
            F = (byte)((result & 0xA8) | (result == 0 ? 0x40 : 0)
                | (logicalOp == 0 ? 0x10 : 0) | parity);
        }
        else
        {
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
        }
    }

    private byte ReadIndexRegister8(bool useIY, int reg) => reg switch
    {
        4 => GetIndexHigh(useIY),
        5 => GetIndexLow(useIY),
        _ => ReadRegisterNoMem(reg)
    };

    private void WriteIndexRegister8(bool useIY, int reg, byte value)
    {
        switch (reg)
        {
            case 4: SetIndexHigh(useIY, value); break;
            case 5: SetIndexLow(useIY, value); break;
            default: WriteRegisterNoMem(reg, value); break;
        }
    }

    private ushort GetIndexRegister(bool useIY) => useIY ? IY : IX;
    private void SetIndexRegister(bool useIY, ushort val) { if (useIY) IY = val; else IX = val; }
    private byte GetIndexHigh(bool useIY) => useIY ? IYH : IXH;
    private void SetIndexHigh(bool useIY, byte val) { if (useIY) IYH = val; else IXH = val; }
    private byte GetIndexLow(bool useIY) => useIY ? IYL : IXL;
    private void SetIndexLow(bool useIY, byte val) { if (useIY) IYL = val; else IXL = val; }
}
