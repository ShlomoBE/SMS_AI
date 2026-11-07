using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// ZiLOG UM0080, 16-bit load group LD dd,nn and instruction timing:
// https://www.zilog.com/docs/z80/um0080.pdf
public sealed class Z80WordImmediateTests
{
    [Theory]
    [InlineData(0x01)]
    [InlineData(0x11)]
    [InlineData(0x21)]
    [InlineData(0x31)]
    public void EveryImmediateWordHasExpectedDestinationAndPreservesState(byte opcode)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        for (int word = 0; word <= 0xffff; word++)
        {
            Initialize(cpu);
            bus.Events.Clear();
            bus.Memory[0x2000] = opcode;
            bus.Memory[0x2001] = (byte)word;
            bus.Memory[0x2002] = (byte)(word >> 8);
            // Explicit opcode cases are independent of the decoder's bit mask.
            int bc = opcode == 0x01 ? word : 0x1122;
            int de = opcode == 0x11 ? word : 0x3344;
            int hl = opcode == 0x21 ? word : 0x5566;
            int sp = opcode == 0x31 ? word : 0x7788;
            Assert.Equal(10, cpu.Step());
            Assert.Equal(bc, (cpu.B << 8) | cpu.C);
            Assert.Equal(de, (cpu.D << 8) | cpu.E);
            Assert.Equal(hl, (cpu.H << 8) | cpu.L);
            Assert.Equal(sp, cpu.StackPointer);
            Assert.Equal(0x99, cpu.A);
            Assert.Equal(0xa5, cpu.F);
            Assert.Equal(0x2003, cpu.ProgramCounter);
            Assert.Equal(0x80, cpu.Refresh);
            Assert.Equal(new[] { "F:2000", "R:2001", "R:2002" }, bus.Events);
            Assert.False(cpu.IsFaulted);
        }
    }

    [Theory]
    [InlineData(0x01, 0xfffd)]
    [InlineData(0x11, 0xfffd)]
    [InlineData(0x21, 0xfffd)]
    [InlineData(0x31, 0xfffd)]
    [InlineData(0x01, 0xfffe)]
    [InlineData(0x11, 0xfffe)]
    [InlineData(0x21, 0xfffe)]
    [InlineData(0x31, 0xfffe)]
    [InlineData(0x01, 0xffff)]
    [InlineData(0x11, 0xffff)]
    [InlineData(0x21, 0xffff)]
    [InlineData(0x31, 0xffff)]
    public void WrapsOperandReadsAndPc(byte opcode, ushort pc)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.ProgramCounter = pc;
        cpu.Refresh = 0x7f;
        ushort lowAddress = unchecked((ushort)(pc + 1));
        ushort highAddress = unchecked((ushort)(pc + 2));
        bus.Memory[pc] = opcode;
        bus.Memory[lowAddress] = 0x5a;
        bus.Memory[highAddress] = 0xc3;
        Assert.Equal(10, cpu.Step());
        Assert.Equal(0xc35a, opcode switch {
            0x01 => (cpu.B << 8) | cpu.C, 0x11 => (cpu.D << 8) | cpu.E,
            0x21 => (cpu.H << 8) | cpu.L, _ => cpu.StackPointer });
        Assert.Equal(unchecked((ushort)(pc + 3)), cpu.ProgramCounter);
        Assert.Equal(0, cpu.Refresh);
        Assert.Equal(new[] { $"F:{pc:X4}", $"R:{lowAddress:X4}", $"R:{highAddress:X4}" }, bus.Events);
    }

    [Theory]
    [InlineData(0x01)] [InlineData(0x11)] [InlineData(0x21)] [InlineData(0x31)]
    public void PreservesAllFlagValues(byte opcode)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = opcode;
        for (int flags = 0; flags < 256; flags++)
        {
            cpu.ProgramCounter = 0x2000;
            cpu.F = (byte)flags;
            cpu.Step();
            Assert.Equal((byte)flags, cpu.F);
            bus.Events.Clear();
        }
    }

    [Theory]
    [InlineData(0x01, 0x00, 0x01)]
    [InlineData(0x11, 0x01, 0x02)]
    [InlineData(0x21, 0x80, 0x81)]
    [InlineData(0x31, 0x2a, 0x2b)]
    public void RefreshAdvancesNormally(byte opcode, byte initial, byte expected)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.Refresh = initial;
        bus.Memory[0x2000] = opcode;
        Assert.Equal(10, cpu.Step());
        Assert.Equal(expected, cpu.Refresh);
    }

    [Theory]
    [InlineData(0x01)] [InlineData(0x11)] [InlineData(0x21)] [InlineData(0x31)]
    public void FetchFailureIsTerminalAndDoesNotRepeatSideEffect(byte opcode)
    {
        var bus = new RecordingBus { FailFetch = true };
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = opcode;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        Assert.Equal(new[] { "F:2000" }, bus.Events);
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Single(bus.Events);
    }

    [Theory]
    [InlineData(0x01, 1)] [InlineData(0x01, 2)]
    [InlineData(0x11, 1)] [InlineData(0x11, 2)]
    [InlineData(0x21, 1)] [InlineData(0x21, 2)]
    [InlineData(0x31, 1)] [InlineData(0x31, 2)]
    public void OperandFailuresAreTerminalWithoutFurtherBusAccess(byte opcode, int failRead)
    {
        var bus = new RecordingBus { FailRead = failRead };
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = opcode;
        bus.Memory[0x2001] = 0xab;
        bus.Memory[0x2002] = 0xcd;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        Assert.Equal(failRead == 1 ? new[] { "F:2000", "R:2001" } :
            new[] { "F:2000", "R:2001", "R:2002" }, bus.Events);
        Assert.Equal(0x1122, (cpu.B << 8) | cpu.C);
        Assert.Equal(0x3344, (cpu.D << 8) | cpu.E);
        Assert.Equal(0x5566, (cpu.H << 8) | cpu.L);
        Assert.Equal(0x7788, cpu.StackPointer);
        Assert.Equal(0xa5, cpu.F);
        int count = bus.Events.Count;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(count, bus.Events.Count);
    }

    private static Z80 NewCpu(RecordingBus bus) { var cpu = new Z80(bus); Initialize(cpu); return cpu; }
    private static void Initialize(Z80 cpu)
    {
        cpu.ProgramCounter = 0x2000; cpu.StackPointer = 0x7788; cpu.Refresh = 0xff;
        cpu.B = 0x11; cpu.C = 0x22; cpu.D = 0x33; cpu.E = 0x44;
        cpu.H = 0x55; cpu.L = 0x66; cpu.A = 0x99; cpu.F = 0xa5;
    }

    private sealed class RecordingBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public List<string> Events { get; } = [];
        public int FailRead { get; init; }
        public bool FailFetch { get; init; }
        private int _readCount;
        public byte FetchOpcode(ushort address)
        {
            Events.Add($"F:{address:X4}");
            if (FailFetch) throw new InvalidOperationException("fetch failed");
            return Memory[address];
        }
        public byte ReadMemory(ushort address)
        {
            Events.Add($"R:{address:X4}");
            if (++_readCount == FailRead) throw new InvalidOperationException("read failed");
            return Memory[address];
        }
        public void WriteMemory(ushort address, byte value) => throw new InvalidOperationException("unexpected write");
        public byte ReadIo(ushort port) => throw new InvalidOperationException("unexpected I/O");
        public void WriteIo(ushort port, byte value) => throw new InvalidOperationException("unexpected I/O");
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException("unexpected acknowledge");
    }
}
