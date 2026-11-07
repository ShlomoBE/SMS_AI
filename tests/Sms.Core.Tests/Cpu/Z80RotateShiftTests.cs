using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// ZiLOG UM008001-1000 (local IDocs/z80cpu_um.pdf), rotate/shift instruction
// descriptions and timing. F3/F5 are variant-sensitive;
// Sean Young, The Undocumented Z80 Documented, flag bits 3 and 5 (IDocs).
public sealed class Z80RotateShiftTests
{
    [Fact]
    public void AllDocumentedEncodingsAllInputsAndCarryStates()
    {
        var bus = new Bus();
        foreach (int group in new[] { 0, 1, 2, 3, 4, 5, 7 })
        for (int target = 0; target < 8; target++)
        for (int input = 0; input < 256; input++)
        for (int carry = 0; carry < 2; carry++)
        foreach (int priorFlags in new[] { 0x00, 0xfe })
        {
            byte op = (byte)(group * 8 + target);
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, Refresh = 0xff,
                A = 0x77, B = 0x11, C = 0x22, D = 0x33, E = 0x44, H = 0x34, L = 0x56,
                F = (byte)(priorFlags | carry) };
            bus.Events.Clear(); bus.Memory[0x2000] = 0xcb; bus.Memory[0x2001] = op;
            bus.Memory[0x3456] = (byte)input;
            if (target != 6) Set(cpu, target, (byte)input);
            byte[] before = Registers(cpu);
            (byte result, byte flags) = Oracle(group, (byte)input, carry);
            byte[] expected = (byte[])before.Clone();
            if (target != 6) expected[target] = result;

            int cycles = cpu.Step();

            string context = $"CB {op:X2} input={input:X2} carry={carry} priorFlags={priorFlags:X2}";
            Assert.True(cycles == (target == 6 ? 15 : 8), context);
            Assert.True(expected.SequenceEqual(Registers(cpu)), context);
            Assert.True(cpu.F == flags, $"{context} flags {cpu.F:X2} != {flags:X2}");
            Assert.True(cpu.ProgramCounter == 0x2002 && cpu.Refresh == 0x81 && !cpu.IsFaulted, context);
            string[] events = target == 6
                ? [$"F2000:CB", $"F2001:{op:X2}", $"R3456:{input:X2}", $"W3456:{result:X2}"]
                : [$"F2000:CB", $"F2001:{op:X2}"];
            Assert.True(events.SequenceEqual(bus.Events), context + " " + string.Join(",", bus.Events));
            Assert.True(bus.Memory[0x3456] == (target == 6 ? result : input), context);
        }
    }

    // Bit-string oracle follows the manual's diagrams rather than the core's
    // integer expressions; parity is counted from the resulting string.
    private static (byte, byte) Oracle(int group, byte input, int carry)
    {
        string bits = Convert.ToString(input, 2).PadLeft(8, '0');
        char shifted = group is 0 or 2 or 4 ? bits[0] : bits[7];
        string result = group switch
        {
            0 => bits[1..] + bits[0], 1 => bits[7] + bits[..7],
            2 => bits[1..] + (carry == 0 ? '0' : '1'),
            3 => (carry == 0 ? '0' : '1') + bits[..7],
            4 => bits[1..] + '0', 5 => bits[0] + bits[..7],
            7 => '0' + bits[..7], _ => throw new ArgumentOutOfRangeException()
        };
        int f = (result[0] == '1' ? 0x80 : 0) | (result.All(c => c == '0') ? 0x40 : 0)
            | (result[2] == '1' ? 0x20 : 0) | (result[4] == '1' ? 0x08 : 0)
            | (result.Count(c => c == '1') % 2 == 0 ? 4 : 0) | (shifted == '1' ? 1 : 0);
        return (Convert.ToByte(result, 2), (byte)f);
    }

    [Theory]
    [InlineData(0xfffe, 0x7e, 0x00)]
    [InlineData(0xffff, 0xfe, 0x80)]
    public void PrefixAndOpcodeWrapAndRefreshRolls(ushort pc, byte refresh, byte expectedRefresh)
    {
        var bus = new Bus(); var cpu = new Z80(bus) { ProgramCounter = pc, Refresh = refresh, B = 0x80 };
        bus.Memory[pc] = 0xcb; bus.Memory[unchecked((ushort)(pc + 1))] = 0x00;
        Assert.Equal(8, cpu.Step());
        Assert.Equal(unchecked((ushort)(pc + 2)), cpu.ProgramCounter);
        Assert.Equal(expectedRefresh, cpu.Refresh);
        Assert.Equal(new[] { $"F{pc:X4}:CB", $"F{unchecked((ushort)(pc + 1)):X4}:00" }, bus.Events);
    }

    [Fact]
    public void UnsupportedSllBytesFaultAfterBothFetchesWithoutDataAccess()
    {
        for (int op = 0; op < 256; op++)
        {
            if (op < 0x30 || op > 0x37) continue;
            var bus = new Bus(); var cpu = new Z80(bus) { ProgramCounter = 0xffff, Refresh = 0xff };
            bus.Memory[0xffff] = 0xcb; bus.Memory[0] = (byte)op;
            Assert.Throws<NotSupportedException>(() => cpu.Step());
            Assert.True(cpu.IsFaulted); Assert.Equal(1, cpu.ProgramCounter); Assert.Equal(0x81, cpu.Refresh);
            Assert.Equal(new[] { "FFFFF:CB", $"F0000:{op:X2}" }, bus.Events);
            Assert.Throws<InvalidOperationException>(() => cpu.Step()); Assert.Equal(2, bus.Events.Count);
        }
    }

    [Theory]
    [InlineData("F2000")]
    [InlineData("F2001")]
    [InlineData("R3456")]
    [InlineData("W3456")]
    public void FetchReadAndWriteFailuresAreTerminal(string failure)
    {
        var bus = new Bus { Failure = failure }; var cpu = new Z80(bus) { ProgramCounter = 0x2000, H = 0x34, L = 0x56 };
        bus.Memory[0x2000] = 0xcb; bus.Memory[0x2001] = 0x06; bus.Memory[0x3456] = 0x80;
        Assert.Throws<InvalidOperationException>(() => cpu.Step()); Assert.True(cpu.IsFaulted);
        string[] all = ["F2000:CB", "F2001:06", "R3456:80", "W3456:01"];
        int length = Array.FindIndex(all, s => s.StartsWith(failure)) + 1;
        Assert.Equal(all.Take(length), bus.Events);
        Assert.Throws<InvalidOperationException>(() => cpu.Step()); Assert.Equal(length, bus.Events.Count);
    }

    [Theory]
    [InlineData(0x2000, 0x97)]
    [InlineData(0x2001, 0x0c)]
    public void MemoryMayAliasEitherOpcodeByte(ushort address, byte result)
    {
        var bus = new Bus(); var cpu = new Z80(bus) { ProgramCounter = 0x2000, H = (byte)(address >> 8), L = (byte)address };
        bus.Memory[0x2000] = 0xcb; bus.Memory[0x2001] = 0x06;
        Assert.Equal(15, cpu.Step()); Assert.Equal(result, bus.Memory[address]);
        Assert.Equal(new[] { "F2000:CB", "F2001:06", $"R{address:X4}:{(address == 0x2000 ? 0xcb : 0x06):X2}", $"W{address:X4}:{result:X2}" }, bus.Events);
    }

    private static byte[] Registers(Z80 c) => [c.B, c.C, c.D, c.E, c.H, c.L, 0, c.A];
    private static void Set(Z80 c, int r, byte v)
    {
        switch (r) { case 0: c.B = v; break; case 1: c.C = v; break; case 2: c.D = v; break;
            case 3: c.E = v; break; case 4: c.H = v; break; case 5: c.L = v; break; case 7: c.A = v; break; }
    }
    private sealed class Bus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public List<string> Events { get; } = [];
        public string? Failure { get; init; }
        private void Log(char kind, ushort address, byte value)
        {
            string name = $"{kind}{address:X4}"; Events.Add($"{name}:{value:X2}");
            if (name == Failure) throw new InvalidOperationException("bus failure");
        }
        public byte FetchOpcode(ushort a) { byte v = Memory[a]; Log('F', a, v); return v; }
        public byte ReadMemory(ushort a) { byte v = Memory[a]; Log('R', a, v); return v; }
        public void WriteMemory(ushort a, byte v) { Log('W', a, v); Memory[a] = v; }
        public byte ReadIo(ushort p) => throw new InvalidOperationException();
        public void WriteIo(ushort p, byte v) => throw new InvalidOperationException();
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException();
    }
}
