using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// ZiLOG Z80 CPU User Manual UM008001-1000, IDocs/z80cpu_um.pdf,
// printed pp. 81-83, 86, 89 (loads), p. 4 (refresh).
public sealed class Z80LoadTests
{
    public static IEnumerable<object[]> RegisterLoads()
    {
        for (int op = 0x40; op <= 0x7f; op++)
            if (op != 0x76)
                foreach (byte value in new byte[] { 0, 0xff, 0xa5 })
                    yield return [(byte)op, value];
    }

    [Theory]
    [MemberData(nameof(RegisterLoads))]
    public void RegisterAndIndirectLoadsHaveExpectedStateAndBusOrder(byte opcode, byte value)
    {
        const ushort address = 0x3456;
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        int source = opcode & 7;
        int destination = (opcode >> 3) & 7;
        // H and L must stay fixed when used to address memory.
        if (source != 6 && destination != 6) SetRegister(cpu, source, value);
        else if (destination == 6 && source != 4 && source != 5) SetRegister(cpu, source, value);
        bus.Memory[0x2000] = opcode;
        bus.Memory[address] = value;
        byte[] before = Registers(cpu);
        byte expectedValue = source == 6 ? value : before[source];
        byte[] expected = (byte[])before.Clone();
        if (destination != 6) expected[destination] = expectedValue;

        int cycles = cpu.Step();

        Assert.Equal(source == 6 || destination == 6 ? 7 : 4, cycles);
        Assert.Equal(expected, Registers(cpu));
        Assert.Equal(0xa5, cpu.F);
        Assert.Equal(0x2001, cpu.ProgramCounter);
        Assert.Equal(0x80, cpu.Refresh);
        var expectedEvents = new List<string> { $"F:2000:{opcode:X2}" };
        if (source == 6) expectedEvents.Add($"R:3456:{value:X2}");
        if (destination == 6) expectedEvents.Add($"W:3456:{expectedValue:X2}");
        Assert.Equal(expectedEvents, bus.Events);
        Assert.Equal(destination == 6 ? expectedValue : value, bus.Memory[address]);
        Assert.False(cpu.IsFaulted);
    }

    public static IEnumerable<object[]> ImmediateLoads()
    {
        foreach (byte opcode in new byte[] { 0x06, 0x0e, 0x16, 0x1e, 0x26, 0x2e, 0x36, 0x3e })
            foreach (byte value in new byte[] { 0, 0xff, 0xa5 })
                foreach (ushort pc in new ushort[] { 0x2000, 0xfffe, 0xffff })
                    yield return [opcode, value, pc];
    }

    [Theory]
    [MemberData(nameof(ImmediateLoads))]
    public void ImmediateLoadsReadOperandOnceAndWrapPc(byte opcode, byte value, ushort pc)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = pc;
        cpu.Refresh = 0x7f;
        bus.Memory[pc] = opcode;
        ushort operandAddress = unchecked((ushort)(pc + 1));
        bus.Memory[operandAddress] = value;
        int destination = (opcode >> 3) & 7;
        byte[] expected = Registers(cpu);
        if (destination != 6) expected[destination] = value;

        int cycles = cpu.Step();

        Assert.Equal(destination == 6 ? 10 : 7, cycles);
        Assert.Equal(expected, Registers(cpu));
        Assert.Equal(0xa5, cpu.F);
        Assert.Equal(unchecked((ushort)(pc + 2)), cpu.ProgramCounter);
        Assert.Equal(0, cpu.Refresh);
        var expectedEvents = new List<string> { $"F:{pc:X4}:{opcode:X2}", $"R:{operandAddress:X4}:{value:X2}" };
        if (destination == 6) expectedEvents.Add($"W:3456:{value:X2}");
        Assert.Equal(expectedEvents, bus.Events);
        if (destination == 6) Assert.Equal(value, bus.Memory[0x3456]);
    }

    [Fact]
    public void RegisterLoadWrapsPcAndIndirectAccessCanUseLastAddress()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = 0xffff;
        cpu.H = 0xff; cpu.L = 0xff;
        bus.Memory[0xffff] = 0x7e; // LD A,(HL), reads its own opcode
        Assert.Equal(7, cpu.Step());
        Assert.Equal(0, cpu.ProgramCounter);
        Assert.Equal(0x7e, cpu.A);
        Assert.Equal(new[] { "F:FFFF:7E", "R:FFFF:7E" }, bus.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataBusFailuresAreTerminal(bool failWrite)
    {
        var bus = new RecordingBus { FailRead = !failWrite, FailWrite = failWrite };
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = failWrite ? (byte)0x70 : (byte)0x7e;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        int eventCount = bus.Events.Count;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(eventCount, bus.Events.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndirectImmediateLoadFaultsWithoutRetryOnOperandReadOrDestinationWrite(bool failWrite)
    {
        var bus = new RecordingBus { FailRead = !failWrite, FailWrite = failWrite };
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = 0x36;
        bus.Memory[0x2001] = 0xA5;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        Assert.Equal(failWrite
            ? new[] { "F:2000:36", "R:2001:A5", "W:3456:A5" }
            : new[] { "F:2000:36", "R:2001:A5" }, bus.Events);
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(failWrite ? 3 : 2, bus.Events.Count);
    }

    [Theory]
    [InlineData(0x2000)]
    [InlineData(0x2001)]
    public void IndirectImmediateLoadReadsOperandBeforeWritingAliasedCode(ushort address)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.H = (byte)(address >> 8);
        cpu.L = (byte)address;
        bus.Memory[0x2000] = 0x36;
        bus.Memory[0x2001] = 0xA5;

        Assert.Equal(10, cpu.Step());
        Assert.Equal(0xA5, bus.Memory[address]);
        Assert.Equal(new[] { "F:2000:36", "R:2001:A5", $"W:{address:X4}:A5" }, bus.Events);
    }

    [Theory]
    [InlineData(0x74, 0x00, 0x20)]
    [InlineData(0x74, 0xFF, 0x20)]
    [InlineData(0x75, 0x20, 0x00)]
    [InlineData(0x75, 0x20, 0xFF)]
    public void StoreHOrLUsesTheOriginalAddressAndRegisterValue(byte opcode, byte h, byte l)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.H = h; cpu.L = l;
        bus.Memory[0x2000] = opcode;
        ushort address = (ushort)((h << 8) | l);
        byte value = opcode == 0x74 ? h : l;

        Assert.Equal(7, cpu.Step());
        Assert.Equal(value, bus.Memory[address]);
        Assert.Equal(new[] { $"F:2000:{opcode:X2}", $"W:{address:X4}:{value:X2}" }, bus.Events);
    }

    [Theory]
    [InlineData(0x41)] // LD B,C
    [InlineData(0x7e)] // LD A,(HL)
    [InlineData(0x70)] // LD (HL),B
    [InlineData(0x06)] // LD B,n
    [InlineData(0x36)] // LD (HL),n
    public void LoadFamiliesPreserveEveryPossibleFlagValue(byte opcode)
    {
        for (int flags = 0; flags <= byte.MaxValue; flags++)
        {
            var bus = new RecordingBus();
            var cpu = NewCpu(bus);
            cpu.F = (byte)flags;
            bus.Memory[0x2000] = opcode;
            bus.Memory[0x2001] = 0x5a;
            bus.Memory[0x3456] = 0xa5;

            cpu.Step();

            Assert.Equal((byte)flags, cpu.F);
        }
    }

    private static Z80 NewCpu(RecordingBus bus) => new(bus)
    {
        ProgramCounter = 0x2000, Refresh = 0xff,
        B = 0x11, C = 0x22, D = 0x33, E = 0x44, H = 0x34, L = 0x56, A = 0x77, F = 0xa5
    };

    private static byte[] Registers(Z80 cpu) => [cpu.B, cpu.C, cpu.D, cpu.E, cpu.H, cpu.L, 0, cpu.A];

    private static void SetRegister(Z80 cpu, int index, byte value)
    {
        switch (index)
        {
            case 0: cpu.B = value; break; case 1: cpu.C = value; break;
            case 2: cpu.D = value; break; case 3: cpu.E = value; break;
            case 4: cpu.H = value; break; case 5: cpu.L = value; break;
            case 7: cpu.A = value; break; default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private sealed class RecordingBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public List<string> Events { get; } = [];
        public bool FailRead { get; init; }
        public bool FailWrite { get; init; }
        public byte FetchOpcode(ushort address) { byte value = Memory[address]; Events.Add($"F:{address:X4}:{value:X2}"); return value; }
        public byte ReadMemory(ushort address)
        {
            byte value = Memory[address]; Events.Add($"R:{address:X4}:{value:X2}");
            if (FailRead) throw new InvalidOperationException("read failed");
            return value;
        }
        public void WriteMemory(ushort address, byte value)
        {
            Events.Add($"W:{address:X4}:{value:X2}");
            if (FailWrite) throw new InvalidOperationException("write failed");
            Memory[address] = value;
        }
        public byte ReadIo(ushort port) => throw new InvalidOperationException();
        public void WriteIo(ushort port, byte value) => throw new InvalidOperationException();
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException();
    }
}
