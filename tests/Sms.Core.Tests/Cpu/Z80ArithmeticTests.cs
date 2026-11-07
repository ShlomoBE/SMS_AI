using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// Documented flags, encodings and T-states: ZiLOG UM0080,
// https://www.zilog.com/docs/z80/um0080.pdf, 8-bit arithmetic group.
// F3/F5: Sean Young, The Undocumented Z80 Documented, section 8.4,
// http://www.z80.info/zip/z80-documented.pdf; corroborating CP description:
// https://worldofspectrum.org/faq/reference/z80reference.htm.
// CP takes operand bits, other arithmetic takes result bits. Variant-dependent.
// MEMPTR: https://gist.github.com/drhelius/8497817 (Boo-boo et al.),
// instructions not listed do not change it; SMS chip identity is unresolved.
public sealed class Z80ArithmeticTests
{
    private static readonly byte[] RegisterBases = [0x80, 0x88, 0x90, 0x98, 0xb8];
    private static readonly byte[] Immediates = [0xc6, 0xce, 0xd6, 0xde, 0xfe];

    [Fact]
    public void RepresentativeRegisterFormsExhaustOperandsAndBothIncomingCarries()
    {
        var bus = new Bus();
        var cpu = new Z80(bus);
        for (int op = 0; op < 5; op++)
        for (int a = 0; a < 256; a++)
        for (int b = 0; b < 256; b++)
        for (int carry = 0; carry < 2; carry++)
        for (int priorFlags = 0; priorFlags <= 0xfe; priorFlags += 0xfe)
        {
            cpu.ProgramCounter = 0x2000;
            cpu.A = (byte)a; cpu.B = (byte)b; cpu.F = (byte)(priorFlags | carry);
            bus.Memory[0x2000] = RegisterBases[op];
            bus.ResetEvents();
            var (result, flags) = Oracle(op, a, b, carry);
            int time = cpu.Step();
            if (time != 4 || cpu.A != result || cpu.F != flags || cpu.B != b ||
                cpu.ProgramCounter != 0x2001 || bus.Count != 1 || bus.Kind1 != 'F' || bus.Address1 != 0x2000)
                throw new Xunit.Sdk.XunitException($"op={op} A={a:X2} operand={b:X2} carry={carry} prior={priorFlags:X2}: expected A={result:X2} F={flags:X2}, got A={cpu.A:X2} F={cpu.F:X2}, T={time}, events={bus.Count}");
        }
    }

    [Fact]
    public void EveryEncodingHasExpectedStateTimingAndOrderedBusOperations()
    {
        for (int op = 0; op < 5; op++)
        for (int form = 0; form < 9; form++)
        foreach (int carry in new[] { 0, 1 })
        {
            var bus = new Bus();
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, StackPointer = 0x1234, Refresh = 0xff,
                A = 0x80, B = 0x01, C = 0x02, D = 0x7f, E = 0xff, H = 0x34, L = 0x56, F = (byte)(0xfe | carry) };
            byte opcode = form == 8 ? Immediates[op] : (byte)(RegisterBases[op] + form);
            bus.Memory[0x2000] = opcode; bus.Memory[0x2001] = 0x81; bus.Memory[0x3456] = 0x81;
            byte[] original = [cpu.B, cpu.C, cpu.D, cpu.E, cpu.H, cpu.L, 0x81, cpu.A, 0x81];
            int operand = original[form];
            var (result, flags) = Oracle(op, cpu.A, operand, carry);

            Assert.Equal(form is 6 or 8 ? 7 : 4, cpu.Step());
            Assert.Equal(result, cpu.A); Assert.Equal(flags, cpu.F);
            Assert.Equal(new byte[] { 1, 2, 0x7f, 0xff, 0x34, 0x56 }, new byte[] { cpu.B, cpu.C, cpu.D, cpu.E, cpu.H, cpu.L });
            Assert.Equal(0x1234, cpu.StackPointer); Assert.Equal(0x80, cpu.Refresh);
            Assert.Equal(form == 8 ? 0x2002 : 0x2001, cpu.ProgramCounter);
            Assert.False(cpu.IsFaulted);
            Assert.Equal(form is 6 or 8 ? 2 : 1, bus.Count);
            Assert.Equal('F', bus.Kind1); Assert.Equal(0x2000, bus.Address1);
            if (form is 6 or 8) { Assert.Equal('R', bus.Kind2); Assert.Equal(form == 6 ? 0x3456 : 0x2001, bus.Address2); }
            Assert.Equal(0x81, bus.Memory[0x3456]);
        }
    }

    [Theory]
    [InlineData(0x80, 0xc6)] [InlineData(0x88, 0xce)] [InlineData(0x90, 0xd6)]
    [InlineData(0x98, 0xde)] [InlineData(0xb8, 0xfe)]
    public void PcRefreshWrapAndAliasedIndirectRead(byte baseOpcode, byte immediateOpcode)
    {
        var bus = new Bus();
        var cpu = new Z80(bus) { ProgramCounter = 0xffff, Refresh = 0x7f, H = 0xff, L = 0xff, A = 0x42 };
        bus.Memory[0xffff] = (byte)(baseOpcode + 6);
        Assert.Equal(7, cpu.Step());
        Assert.Equal(0, cpu.ProgramCounter); Assert.Equal(0, cpu.Refresh);
        Assert.Equal(2, bus.Count); Assert.Equal('F', bus.Kind1); Assert.Equal('R', bus.Kind2);
        Assert.Equal(0xffff, bus.Address1); Assert.Equal(0xffff, bus.Address2);

        cpu.ProgramCounter = 0xffff; cpu.Refresh = 0xff; cpu.A = 0x42;
        bus.Memory[0xffff] = immediateOpcode; bus.Memory[0] = 0x81; bus.ResetEvents();
        Assert.Equal(7, cpu.Step());
        Assert.Equal(1, cpu.ProgramCounter); Assert.Equal(0x80, cpu.Refresh);
        Assert.Equal(2, bus.Count); Assert.Equal('F', bus.Kind1); Assert.Equal('R', bus.Kind2);
        Assert.Equal(0xffff, bus.Address1); Assert.Equal(0, bus.Address2);
    }

    [Theory]
    [InlineData(0x86)] [InlineData(0x8e)] [InlineData(0x96)] [InlineData(0x9e)] [InlineData(0xbe)]
    [InlineData(0xc6)] [InlineData(0xce)] [InlineData(0xd6)] [InlineData(0xde)] [InlineData(0xfe)]
    public void FetchAndOperandReadFailuresAreTerminal(byte opcode)
    {
        foreach (char failure in new[] { 'F', 'R' })
        {
            var bus = new Bus { Failure = failure };
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, H = 0x34, L = 0x56, A = 0x80, F = 0xa5 };
            bus.Memory[0x2000] = opcode; bus.Memory[0x2001] = 1; bus.Memory[0x3456] = 1;
            Assert.Throws<InvalidOperationException>(() => cpu.Step());
            Assert.True(cpu.IsFaulted); Assert.Equal(0x80, cpu.A); Assert.Equal(0xa5, cpu.F);
            Assert.Equal(failure == 'F' ? 1 : 2, bus.Count);
            Assert.Equal('F', bus.Kind1); Assert.Equal(0x2000, bus.Address1);
            if (failure == 'R') { Assert.Equal('R', bus.Kind2); Assert.Equal(opcode >= 0xc0 ? 0x2001 : 0x3456, bus.Address2); }
            Assert.Throws<InvalidOperationException>(() => cpu.Step());
            Assert.Equal(failure == 'F' ? 1 : 2, bus.Count);
        }
    }

    [Fact]
    public void ArithmeticDoesNotChangeObservableInitialMemptrBits()
    {
        // No interface permits injecting nonzero MEMPTR, so this checks only
        // preservation of its prototype initial value via BIT 0,(HL).
        for (int op = 0; op < 5; op++)
        for (int form = 0; form < 9; form++)
        {
            var bus = new Bus();
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, H = 0x28, L = 0x00, A = 0x28 };
            bus.Memory[0x2000] = form == 8 ? Immediates[op] : (byte)(RegisterBases[op] + form);
            bus.Memory[0x2001] = form == 8 ? (byte)0x28 : (byte)0xcb;
            bus.Memory[0x2002] = form == 8 ? (byte)0xcb : (byte)0x46;
            bus.Memory[0x2003] = 0x46; bus.Memory[0x2800] = 0x28;
            cpu.Step(); cpu.Step();
            Assert.Equal(0, cpu.F & 0x28);
        }
    }

    // Mathematical oracle uses signed ranges and base-16 arithmetic, rather
    // than the implementation's bitwise overflow and half-carry formulae.
    private static (byte Result, byte Flags) Oracle(int op, int a, int b, int incomingCarry)
    {
        bool sub = op >= 2;
        int carry = op is 1 or 3 ? incomingCarry : 0;
        int wide = sub ? a - b - carry : a + b + carry;
        int result = (wide + 256) % 256;
        int signedA = a < 128 ? a : a - 256;
        int signedB = b < 128 ? b : b - 256;
        int signed = sub ? signedA - signedB - carry : signedA + signedB + carry;
        int nibble = sub ? a % 16 - b % 16 - carry : a % 16 + b % 16 + carry;
        int f = result >= 128 ? 0x80 : 0;
        if (result == 0) f |= 0x40;
        int undocumented = op == 4 ? b : result;
        if (undocumented / 32 % 2 != 0) f |= 0x20;
        if (nibble < 0 || nibble > 15) f |= 0x10;
        if (undocumented / 8 % 2 != 0) f |= 8;
        if (signed < -128 || signed > 127) f |= 4;
        if (sub) f |= 2;
        if (wide < 0 || wide > 255) f |= 1;
        return ((byte)(op == 4 ? a : result), (byte)f);
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
        public void ResetEvents() => Count = 0;
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
