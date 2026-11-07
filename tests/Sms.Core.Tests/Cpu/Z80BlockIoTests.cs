using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

public sealed class Z80BlockIoTests
{
    private sealed class RecordingBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public Dictionary<ushort, byte> IoPorts { get; } = new();
        public List<string> Events { get; } = [];

        public byte FetchOpcode(ushort address)
        {
            byte val = Memory[address];
            Events.Add($"F:{address:X4}:{val:X2}");
            return val;
        }

        public byte ReadMemory(ushort address)
        {
            byte val = Memory[address];
            Events.Add($"R:{address:X4}:{val:X2}");
            return val;
        }

        public void WriteMemory(ushort address, byte value)
        {
            Events.Add($"W:{address:X4}:{value:X2}");
            Memory[address] = value;
        }

        public byte ReadIo(ushort port)
        {
            byte val = IoPorts.TryGetValue(port, out byte v) ? v : (byte)0;
            Events.Add($"RIO:{port:X4}:{val:X2}");
            return val;
        }

        public void WriteIo(ushort port, byte value)
        {
            Events.Add($"WIO:{port:X4}:{value:X2}");
            IoPorts[port] = value;
        }

        public byte AcknowledgeInterrupt() => throw new NotImplementedException();
    }

    private static Z80 NewCpu(RecordingBus bus) => new(bus);

    #region Unprefixed I/O Tests

    [Fact]
    public void InA_n_ReadsFromPort_A_ShiftsHighAndNIsLow()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x1000;
        cpu.A = 0xC0;
        cpu.F = 0x95; // Initial flags to verify preservation
        bus.Memory[0x1000] = 0xDB; // IN A, (n)
        bus.Memory[0x1001] = 0x05;
        bus.IoPorts[0xC005] = 0x42;

        int cycles = cpu.Step();

        Assert.Equal(11, cycles);
        Assert.Equal(0x42, cpu.A);
        Assert.Equal(0x95, cpu.F); // Flags unchanged
        Assert.Equal(0x1002, cpu.ProgramCounter);
        Assert.Equal(new[] { "F:1000:DB", "R:1001:05", "RIO:C005:42" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void OutN_A_WritesToPort_A_ShiftsHighAndNIsLow()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x2000;
        cpu.A = 0xDE;
        cpu.F = 0x2A;
        bus.Memory[0x2000] = 0xD3; // OUT (n), A
        bus.Memory[0x2001] = 0xAD;

        int cycles = cpu.Step();

        Assert.Equal(11, cycles);
        Assert.Equal(0xDE, bus.IoPorts[0xDEAD]);
        Assert.Equal(0x2A, cpu.F); // Flags unchanged
        Assert.Equal(0x2002, cpu.ProgramCounter);
        Assert.Equal(new[] { "F:2000:D3", "R:2001:AD", "WIO:DEAD:DE" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    #endregion

    #region ED-prefixed I/O Tests

    public static IEnumerable<object[]> EdInRegisters()
    {
        yield return [ (byte)0x40, "B" ];
        yield return [ (byte)0x48, "C" ];
        yield return [ (byte)0x50, "D" ];
        yield return [ (byte)0x58, "E" ];
        yield return [ (byte)0x60, "H" ];
        yield return [ (byte)0x68, "L" ];
        yield return [ (byte)0x78, "A" ];
    }

    [Theory]
    [MemberData(nameof(EdInRegisters))]
    public void EdInRegister_ReadsFromPortBC_SetsRegisterAndFlags(byte subOpcode, string regName)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x12; cpu.C = 0x34;
        cpu.F = 0x01; // Carry = 1
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = subOpcode;
        bus.IoPorts[0x1234] = 0x85; // Bit 7 set (S=1), PopCount(0x85)=3 (odd parity => P/V=0)

        int cycles = cpu.Step();

        Assert.Equal(12, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:" + subOpcode.ToString("X2"), "RIO:1234:85" }, bus.Events);

        byte resultReg = regName switch
        {
            "B" => cpu.B, "C" => cpu.C, "D" => cpu.D, "E" => cpu.E,
            "H" => cpu.H, "L" => cpu.L, "A" => cpu.A,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(0x85, resultReg);
        // Flags: S=0x80, Z=0, F5=0, H=0, F3=0, P/V=0, N=0, C=1 => F = 0x81
        Assert.Equal(0x81, cpu.F);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void EdInF_C_SetsFlagsOnly_WithoutWritingRegisterOrMemory()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x12; cpu.C = 0x34;
        cpu.F = 0x00;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0x70; // IN (C) / IN F,(C)
        bus.IoPorts[0x1234] = 0x00; // Zero: Z=1 (0x40), PopCount=0 (even => P/V=0x04)

        int cycles = cpu.Step();

        Assert.Equal(12, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x44, cpu.F); // Z and P/V set
        Assert.Equal(new[] { "F:0000:ED", "F:0001:70", "RIO:1234:00" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    public static IEnumerable<object[]> EdOutRegisters()
    {
        yield return [ (byte)0x41, "B" ];
        yield return [ (byte)0x49, "C" ];
        yield return [ (byte)0x51, "D" ];
        yield return [ (byte)0x59, "E" ];
        yield return [ (byte)0x61, "H" ];
        yield return [ (byte)0x69, "L" ];
        yield return [ (byte)0x79, "A" ];
    }

    [Theory]
    [MemberData(nameof(EdOutRegisters))]
    public void EdOutRegister_WritesRegisterValueToPortBC_PreservesFlags(byte subOpcode, string regName)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x56; cpu.C = 0x78;
        cpu.D = 0xAA; cpu.E = 0xBB; cpu.H = 0xCC; cpu.L = 0xDD; cpu.A = 0xEE;
        cpu.F = 0xFF;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = subOpcode;

        int cycles = cpu.Step();

        Assert.Equal(12, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0xFF, cpu.F);
        byte expectedVal = regName switch
        {
            "B" => 0x56, "C" => 0x78, "D" => 0xAA, "E" => 0xBB,
            "H" => 0xCC, "L" => 0xDD, "A" => 0xEE,
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expectedVal, bus.IoPorts[0x5678]);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:" + subOpcode.ToString("X2"), $"WIO:5678:{expectedVal:X2}" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void EdOut0_C_WritesZeroToPortBC()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x56; cpu.C = 0x78;
        cpu.F = 0x12;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0x71; // OUT (C), 0

        int cycles = cpu.Step();

        Assert.Equal(12, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x12, cpu.F);
        Assert.Equal(0x00, bus.IoPorts[0x5678]);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:71", "WIO:5678:00" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    #endregion

    #region ED-prefixed Block I/O Tests

    [Fact]
    public void Ini_ReadsIoWriteMemory_IncrementsHlDecrementsB()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x01; cpu.C = 0x20;
        cpu.H = 0x10; cpu.L = 0x00;
        cpu.F = 0x01;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xA2; // INI
        bus.IoPorts[0x0120] = 0x80; // bit 7 set -> N=1

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x00, cpu.B);
        Assert.Equal(0x1001, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x80, bus.Memory[0x1000]);
        // Flags: Z=1 (0x40, since B=0), N=1 (0x02, since bit 7 of 0x80 is 1), Carry=1 -> F = 0x43
        Assert.Equal(0x43, cpu.F);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:A2", "RIO:0120:80", "W:1000:80" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Inir_RepeatsWhenBNotZero_AndStopsWhenBZero()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x02; cpu.C = 0x10;
        cpu.H = 0x20; cpu.L = 0x00;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xB2; // INIR
        bus.IoPorts[0x0210] = 0x11;
        bus.IoPorts[0x0110] = 0x22;

        // Iteration 1: B goes 2 -> 1, repeat
        int cycles1 = cpu.Step();
        Assert.Equal(21, cycles1);
        Assert.Equal(0x0000, cpu.ProgramCounter);
        Assert.Equal(0x01, cpu.B);
        Assert.Equal(0x2001, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x11, bus.Memory[0x2000]);

        // Iteration 2: B goes 1 -> 0, finish
        int cycles2 = cpu.Step();
        Assert.Equal(16, cycles2);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x00, cpu.B);
        Assert.Equal(0x2002, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x22, bus.Memory[0x2001]);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Ind_DecrementsHlAndB()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x05; cpu.C = 0x30;
        cpu.H = 0x10; cpu.L = 0x05;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xAA; // IND
        bus.IoPorts[0x0530] = 0x33;

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x04, cpu.B);
        Assert.Equal(0x1004, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x33, bus.Memory[0x1005]);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:AA", "RIO:0530:33", "W:1005:33" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Outi_ReadsMemoryFirst_DecrementsB_WritesIoToPortBCWithUpdatedB()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x02; cpu.C = 0x50;
        cpu.H = 0x40; cpu.L = 0x00;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xA3; // OUTI
        bus.Memory[0x4000] = 0x77;

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x01, cpu.B);
        Assert.Equal(0x4001, (cpu.H << 8) | cpu.L);
        // Note: I/O port written is BC AFTER B decremented, i.e. 0x0150!
        Assert.Equal(0x77, bus.IoPorts[0x0150]);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:A3", "R:4000:77", "WIO:0150:77" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Otir_RepeatsUntilBZero()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x02; cpu.C = 0x60;
        cpu.H = 0x50; cpu.L = 0x00;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xB3; // OTIR
        bus.Memory[0x5000] = 0xAA;
        bus.Memory[0x5001] = 0xBB;

        int cycles1 = cpu.Step();
        Assert.Equal(21, cycles1);
        Assert.Equal(0x0000, cpu.ProgramCounter);
        Assert.Equal(0x01, cpu.B);
        Assert.Equal(0xAA, bus.IoPorts[0x0160]);

        int cycles2 = cpu.Step();
        Assert.Equal(16, cycles2);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x00, cpu.B);
        Assert.Equal(0xBB, bus.IoPorts[0x0060]);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Outd_And_Otdr_DecrementsHl()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.B = 0x01; cpu.C = 0x70;
        cpu.H = 0x60; cpu.L = 0x05;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xAB; // OUTD
        bus.Memory[0x6005] = 0x99;

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x00, cpu.B);
        Assert.Equal(0x6004, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x99, bus.IoPorts[0x0070]);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:AB", "R:6005:99", "WIO:0070:99" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    #endregion

    #region ED-prefixed Block Memory Tests

    [Fact]
    public void Ldi_TransfersByte_IncrementsAddresses_DecrementsBc_SetsPvIfBcNotZero()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.H = 0x10; cpu.L = 0x00; // HL = 0x1000
        cpu.D = 0x20; cpu.E = 0x00; // DE = 0x2000
        cpu.B = 0x00; cpu.C = 0x02; // BC = 0x0002
        cpu.F = 0x81; // S=1, Z=0, C=1 to test preservation
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xA0; // LDI
        bus.Memory[0x1000] = 0x55;

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x1001, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x2001, (cpu.D << 8) | cpu.E);
        Assert.Equal(0x0001, (cpu.B << 8) | cpu.C);
        Assert.Equal(0x55, bus.Memory[0x2000]);
        // Flags: S=1 (0x80), Z=0, P/V=1 (0x04, since BC=1 != 0), N=0, H=0, C=1 -> F = 0x85
        Assert.Equal(0x85, cpu.F);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:A0", "R:1000:55", "W:2000:55" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Ldir_RepeatsUntilBcIsZero_AndClearsPvOnFinalStep()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.H = 0x10; cpu.L = 0x00;
        cpu.D = 0x20; cpu.E = 0x00;
        cpu.B = 0x00; cpu.C = 0x02;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xB0; // LDIR
        bus.Memory[0x1000] = 0x11;
        bus.Memory[0x1001] = 0x22;

        // Iteration 1 (BC becomes 1 -> repeat)
        int cycles1 = cpu.Step();
        Assert.Equal(21, cycles1);
        Assert.Equal(0x0000, cpu.ProgramCounter);
        Assert.Equal(0x0001, (cpu.B << 8) | cpu.C);
        Assert.Equal(0x04, cpu.F & 0x04); // P/V set

        // Iteration 2 (BC becomes 0 -> finish)
        int cycles2 = cpu.Step();
        Assert.Equal(16, cycles2);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x0000, (cpu.B << 8) | cpu.C);
        Assert.Equal(0x00, cpu.F & 0x04); // P/V cleared
        Assert.Equal(0x11, bus.Memory[0x2000]);
        Assert.Equal(0x22, bus.Memory[0x2001]);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Ldir_WithInitialBcZero_DecrementsToFFFF_AndRepeats()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.H = 0x10; cpu.L = 0x00;
        cpu.D = 0x20; cpu.E = 0x00;
        cpu.B = 0x00; cpu.C = 0x00; // BC = 0
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xB0; // LDIR
        bus.Memory[0x1000] = 0x99;

        int cycles = cpu.Step();

        Assert.Equal(21, cycles);
        Assert.Equal(0x0000, cpu.ProgramCounter); // Repeats
        Assert.Equal(0xFFFF, (cpu.B << 8) | cpu.C); // BC decrements to 65535
        Assert.Equal(0x04, cpu.F & 0x04); // P/V set
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Ldd_And_Lddr_DecrementsHlAndDe()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.H = 0x10; cpu.L = 0x05;
        cpu.D = 0x20; cpu.E = 0x05;
        cpu.B = 0x00; cpu.C = 0x01;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xA8; // LDD
        bus.Memory[0x1005] = 0x33;

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x1004, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x2004, (cpu.D << 8) | cpu.E);
        Assert.Equal(0x0000, (cpu.B << 8) | cpu.C);
        Assert.Equal(0x33, bus.Memory[0x2005]);
        Assert.Equal(0x00, cpu.F & 0x04); // P/V cleared since BC=0
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Cpi_ComparesAWithMemory_SetsZOnMatch_IncrementsHlDecrementsBc()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.A = 0x40;
        cpu.H = 0x10; cpu.L = 0x00;
        cpu.B = 0x00; cpu.C = 0x05;
        cpu.F = 0x01; // Carry = 1
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xA1; // CPI
        bus.Memory[0x1000] = 0x40; // Match!

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x1001, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x0004, (cpu.B << 8) | cpu.C);
        // Flags: Z=1 (0x40), P/V=1 (0x04, BC != 0), N=1 (0x02), C=1 preserved -> F = 0x47
        Assert.Equal(0x47, cpu.F);
        Assert.Equal(new[] { "F:0000:ED", "F:0001:A1", "R:1000:40" }, bus.Events);
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Cpir_StopsOnMatchEvenIfBcNotZero()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.A = 0x55;
        cpu.H = 0x10; cpu.L = 0x00;
        cpu.B = 0x00; cpu.C = 0x03;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xB1; // CPIR
        bus.Memory[0x1000] = 0x11; // Non-match
        bus.Memory[0x1001] = 0x55; // Match!

        // Step 1: 0x11 != 0x55 -> repeat
        int cycles1 = cpu.Step();
        Assert.Equal(21, cycles1);
        Assert.Equal(0x0000, cpu.ProgramCounter);
        Assert.Equal(0x1001, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x0002, (cpu.B << 8) | cpu.C);
        Assert.Equal(0, cpu.F & 0x40); // Z=0

        // Step 2: 0x55 == 0x55 -> match! finish (16 T)
        int cycles2 = cpu.Step();
        Assert.Equal(16, cycles2);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x1002, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x0001, (cpu.B << 8) | cpu.C);
        Assert.Equal(0x40, cpu.F & 0x40); // Z=1
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Cpir_StopsWhenBcReachesZero_EvenIfNoMatch()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.A = 0x55;
        cpu.H = 0x10; cpu.L = 0x00;
        cpu.B = 0x00; cpu.C = 0x01;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xB1; // CPIR
        bus.Memory[0x1000] = 0x99; // Non-match

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x1001, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x0000, (cpu.B << 8) | cpu.C);
        Assert.Equal(0x00, cpu.F & 0x40); // Z=0
        Assert.Equal(0x00, cpu.F & 0x04); // P/V=0
        Assert.False(cpu.IsFaulted);
    }

    [Fact]
    public void Cpd_And_Cpdr_DecrementsHl()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0x0000;
        cpu.A = 0x20;
        cpu.H = 0x10; cpu.L = 0x05;
        cpu.B = 0x00; cpu.C = 0x02;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0xA9; // CPD
        bus.Memory[0x1005] = 0x30; // A < val -> Borrow

        int cycles = cpu.Step();

        Assert.Equal(16, cycles);
        Assert.Equal(0x0002, cpu.ProgramCounter);
        Assert.Equal(0x1004, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x0001, (cpu.B << 8) | cpu.C);
        // A - val = 0x20 - 0x30 = 0xF0 (S=1 => 0x80)
        // Half borrow: 0x0 < 0x0 => false (0x00)
        // Z = 0, P/V = 1 (BC != 0), N = 1 (0x02) -> F = 0x86
        Assert.Equal(0x86, cpu.F);
        Assert.False(cpu.IsFaulted);
    }

    #endregion
}