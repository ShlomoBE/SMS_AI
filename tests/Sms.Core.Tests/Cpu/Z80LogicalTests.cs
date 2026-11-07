using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// ZiLOG UM0080, logical instruction group and instruction summary:
// https://www.zilog.com/docs/z80/um0080.pdf. F3/F5 result bits: Sean Young,
// The Undocumented Z80 Documented, http://www.z80.info/zip/z80-documented.pdf.
// MEMPTR: https://gist.github.com/drhelius/8497817 (Boo-boo et al.).
// Undocumented behavior is variant-qualified; SMS silicon is not established.
public sealed class Z80LogicalTests
{
    private static readonly byte[] Bases = [0xa0, 0xa8, 0xb0];
    private static readonly byte[] Immediates = [0xe6, 0xee, 0xf6];
    private static readonly byte[] FlagPolarities = [0, 0xff];
    private static readonly int[][] TruthTables = [[0, 0, 0, 1], [0, 1, 1, 0], [0, 1, 1, 1]];

    [Fact]
    public void RepresentativeRegisterFormsExhaustOperandsAndBothFlagPolarities()
    {
        var bus = new Bus();
        var cpu = new Z80(bus);
        for (int op = 0; op < 3; op++)
        for (int a = 0; a < 256; a++)
        for (int b = 0; b < 256; b++)
        foreach (byte prior in FlagPolarities)
        {
            cpu.ProgramCounter = 0x2000; cpu.A = (byte)a; cpu.B = (byte)b; cpu.F = prior;
            bus.Memory[0x2000] = Bases[op]; bus.Reset();
            var (result, flags) = Oracle(op, a, b);
            int time = cpu.Step();
            if (time != 4 || cpu.A != result || cpu.F != flags || cpu.B != b ||
                cpu.ProgramCounter != 0x2001 || bus.Count != 1 || bus.Kind1 != 'F' || bus.Address1 != 0x2000)
                throw new Xunit.Sdk.XunitException($"op={op} A={a:X2} B={b:X2} prior={prior:X2}: expected A={result:X2} F={flags:X2}, got A={cpu.A:X2} F={cpu.F:X2} T={time} events={bus.Count}");
        }
    }

    [Fact]
    public void AllTwentySevenEncodingsPreserveStateAndHaveOrderedBusAndTiming()
    {
        for (int op = 0; op < 3; op++)
        for (int form = 0; form < 9; form++)
        foreach (byte prior in FlagPolarities)
        {
            var bus = new Bus();
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, StackPointer = 0x1234, Refresh = 0xff,
                A = 0x80, B = 1, C = 2, D = 0x7f, E = 0xff, H = 0x34, L = 0x56, F = prior };
            byte opcode = form == 8 ? Immediates[op] : (byte)(Bases[op] + form);
            bus.Memory[0x2000] = opcode; bus.Memory[0x2001] = 0x81; bus.Memory[0x3456] = 0x81;
            byte[] operands = [cpu.B, cpu.C, cpu.D, cpu.E, cpu.H, cpu.L, 0x81, cpu.A, 0x81];
            var (result, flags) = Oracle(op, cpu.A, operands[form]);
            Assert.Equal(form is 6 or 8 ? 7 : 4, cpu.Step());
            Assert.Equal(result, cpu.A); Assert.Equal(flags, cpu.F);
            Assert.Equal(new byte[] { 1, 2, 0x7f, 0xff, 0x34, 0x56 }, new byte[] { cpu.B, cpu.C, cpu.D, cpu.E, cpu.H, cpu.L });
            Assert.Equal(0x1234, cpu.StackPointer); Assert.Equal(0x80, cpu.Refresh);
            Assert.Equal(form == 8 ? 0x2002 : 0x2001, cpu.ProgramCounter); Assert.False(cpu.IsFaulted);
            Assert.Equal(form is 6 or 8 ? 2 : 1, bus.Count);
            Assert.Equal('F', bus.Kind1); Assert.Equal(0x2000, bus.Address1);
            if (form is 6 or 8) { Assert.Equal('R', bus.Kind2); Assert.Equal(form == 6 ? 0x3456 : 0x2001, bus.Address2); }
            Assert.Equal(0x81, bus.Memory[0x3456]);
        }
    }

    [Theory]
    [InlineData(0xa0, 0xe6)] [InlineData(0xa8, 0xee)] [InlineData(0xb0, 0xf6)]
    public void PcAndRefreshWrapAndIndirectFetchAlias(byte baseOpcode, byte immediateOpcode)
    {
        var bus = new Bus();
        var cpu = new Z80(bus) { ProgramCounter = 0xffff, Refresh = 0x7f, H = 0xff, L = 0xff, A = 0x42 };
        bus.Memory[0xffff] = (byte)(baseOpcode + 6);
        var (result, flags) = Oracle((baseOpcode - 0xa0) / 8, 0x42, baseOpcode + 6);
        Assert.Equal(7, cpu.Step()); Assert.Equal(result, cpu.A); Assert.Equal(flags, cpu.F);
        Assert.Equal(0, cpu.ProgramCounter); Assert.Equal(0, cpu.Refresh);
        Assert.Equal(2, bus.Count); Assert.Equal('F', bus.Kind1); Assert.Equal('R', bus.Kind2);
        Assert.Equal(0xffff, bus.Address1); Assert.Equal(0xffff, bus.Address2);
        cpu.ProgramCounter = 0xffff; cpu.Refresh = 0xff; cpu.A = 0x42;
        bus.Memory[0xffff] = immediateOpcode; bus.Memory[0] = 0x81; bus.Reset();
        (result, flags) = Oracle((baseOpcode - 0xa0) / 8, 0x42, 0x81);
        Assert.Equal(7, cpu.Step()); Assert.Equal(result, cpu.A); Assert.Equal(flags, cpu.F);
        Assert.Equal(1, cpu.ProgramCounter); Assert.Equal(0x80, cpu.Refresh);
        Assert.Equal(2, bus.Count); Assert.Equal('F', bus.Kind1); Assert.Equal('R', bus.Kind2);
        Assert.Equal(0xffff, bus.Address1); Assert.Equal(0, bus.Address2);
    }

    [Theory]
    [InlineData(0xa6)] [InlineData(0xae)] [InlineData(0xb6)]
    [InlineData(0xe6)] [InlineData(0xee)] [InlineData(0xf6)]
    public void BusFailuresAreTerminalAndDoNotCommitAlu(byte opcode)
    {
        foreach (char failure in new[] { 'F', 'R' })
        {
            var bus = new Bus { Failure = failure };
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, Refresh = 0xff, H = 0x34, L = 0x56, A = 0x80, F = 0xa5 };
            bus.Memory[0x2000] = opcode; bus.Memory[0x2001] = 1; bus.Memory[0x3456] = 1;
            Assert.Throws<InvalidOperationException>(() => cpu.Step());
            Assert.True(cpu.IsFaulted); Assert.Equal(0x80, cpu.A); Assert.Equal(0xa5, cpu.F);
            Assert.Equal(failure == 'F' ? 0x2000 : 0x2001, cpu.ProgramCounter);
            Assert.Equal(failure == 'F' ? 0xff : 0x80, cpu.Refresh);
            Assert.Equal(failure == 'F' ? 1 : 2, bus.Count);
            Assert.Equal('F', bus.Kind1); Assert.Equal(0x2000, bus.Address1);
            if (failure == 'R') { Assert.Equal('R', bus.Kind2); Assert.Equal(opcode >= 0xc0 ? 0x2001 : 0x3456, bus.Address2); }
            Assert.Throws<InvalidOperationException>(() => cpu.Step());
            Assert.Equal(failure == 'F' ? 1 : 2, bus.Count);
        }
    }

    [Fact]
    public void LogicalOperationsPreserveObservableInitialMemptrBits()
    {
        // Only the deterministic zero initial MEMPTR is injectable via this API.
        for (int op = 0; op < 3; op++)
        for (int form = 0; form < 9; form++)
        {
            var bus = new Bus();
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, H = 0x28, L = 0, A = 0x28 };
            bus.Memory[0x2000] = form == 8 ? Immediates[op] : (byte)(Bases[op] + form);
            bus.Memory[0x2001] = form == 8 ? (byte)0x28 : (byte)0xcb;
            bus.Memory[0x2002] = form == 8 ? (byte)0xcb : (byte)0x46;
            bus.Memory[0x2003] = 0x46; bus.Memory[0x2800] = 0x28;
            cpu.Step(); cpu.Step(); Assert.Equal(0, cpu.F & 0x28);
        }
    }

    // Per-bit truth tables and bit counts, independent of the CPU's bitwise
    // operations and popcount flag calculation. Table index is 2*A_bit+B_bit.
    private static (byte Result, byte Flags) Oracle(int op, int a, int b)
    {
        int result = 0, ones = 0;
        for (int place = 1; place <= 128; place *= 2)
        {
            int bit = TruthTables[op][2 * (a / place % 2) + b / place % 2];
            result += bit * place; ones += bit;
        }
        int flags = (result >= 128 ? 0x80 : 0) + (result == 0 ? 0x40 : 0)
            + (result / 32 % 2) * 0x20 + (op == 0 ? 0x10 : 0)
            + (result / 8 % 2) * 8 + (ones % 2 == 0 ? 4 : 0);
        return ((byte)result, (byte)flags);
    }

    private sealed class Bus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public char Failure { get; init; }
        public int Count { get; private set; }
        public char Kind1 { get; private set; }
        public char Kind2 { get; private set; }
        public ushort Address1 { get; private set; }
        public ushort Address2 { get; private set; }
        public void Reset() => Count = 0;
        private void Record(char kind, ushort address)
        {
            Count++;
            if (Count == 1) { Kind1 = kind; Address1 = address; }
            if (Count == 2) { Kind2 = kind; Address2 = address; }
            if (kind == Failure) throw new InvalidOperationException("Bus failure after access");
        }
        public byte FetchOpcode(ushort address) { Record('F', address); return Memory[address]; }
        public byte ReadMemory(ushort address) { Record('R', address); return Memory[address]; }
        public void WriteMemory(ushort address, byte value) { Record('W', address); Memory[address] = value; }
        public byte ReadIo(ushort port) => throw new InvalidOperationException();
        public void WriteIo(ushort port, byte value) => throw new InvalidOperationException();
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException();
    }
}
