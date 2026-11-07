using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// ZiLOG Z80 CPU User Manual (IDocs/z80cpu_um.pdf), BIT/RES/SET and timing.
// Sean Young, The Undocumented Z80 Documented (IDocs/z80-documented.pdf):
// BIT S/PV behavior. Register BIT F3/F5 here follow operand bits as in
// https://64nops.wordpress.com/2021/01/13/perfectly-accurate-z80-flags-and-cpc-timing/;
// Young §4.1 differs. BIT (HL) MEMPTR: https://gist.github.com/drhelius/8497817.
// SMS silicon variant is unverified; MEMPTR is deterministically zero here.
public sealed class Z80BitOperationTests
{
    [Fact]
    public void AllBitResSetEncodingsAllBytesAndCarryStates()
    {
        var bus = new Bus();
        for (int op = 0x40; op <= 0xff; op++)
        for (int input = 0; input < 256; input++)
        for (int carry = 0; carry < 2; carry++)
        foreach (int prior in new[] { 0, 0xfe })
        {
            int target = op % 8;
            var cpu = new Z80(bus) { ProgramCounter = 0x2000, Refresh = 0xff,
                A = 0x77, B = 0x11, C = 0x22, D = 0x33, E = 0x44, H = 0x34, L = 0x56,
                F = (byte)(prior | carry) };
            bus.Events.Clear(); bus.Memory[0x2000] = 0xcb; bus.Memory[0x2001] = (byte)op;
            bus.Memory[0x3456] = (byte)input;
            if (target != 6) Set(cpu, target, (byte)input);
            byte[] expectedRegisters = Registers(cpu);
            (byte result, byte flags) = Oracle(op, (byte)input, cpu.F);
            if (op >= 0x80 && target != 6) expectedRegisters[target] = result;

            int cycles = cpu.Step();
            string context = $"CB {op:X2} input={input:X2} carry={carry} prior={prior:X2}";
            Assert.True(cycles == (target != 6 ? 8 : op < 0x80 ? 12 : 15), context);
            Assert.True(expectedRegisters.SequenceEqual(Registers(cpu)), context);
            Assert.True(cpu.F == flags, $"{context} flags {cpu.F:X2} != {flags:X2}");
            Assert.True(cpu.ProgramCounter == 0x2002 && cpu.Refresh == 0x81 && !cpu.IsFaulted, context);
            string[] events = target != 6 ? [$"F2000:CB", $"F2001:{op:X2}"]
                : op < 0x80 ? [$"F2000:CB", $"F2001:{op:X2}", $"R3456:{input:X2}"]
                : [$"F2000:CB", $"F2001:{op:X2}", $"R3456:{input:X2}", $"W3456:{result:X2}"];
            Assert.True(events.SequenceEqual(bus.Events), context + " " + string.Join(",", bus.Events));
            Assert.True(bus.Memory[0x3456] == (target == 6 && op >= 0x80 ? result : input), context);
        }
    }

    // Independent bit-string oracle, including documented flags. For (HL),
    // prototype MEMPTR is deterministically zero; it is not physical reset state.
    private static (byte result, byte flags) Oracle(int op, byte value, byte prior)
    {
        char[] bits = Convert.ToString(value, 2).PadLeft(8, '0').ToCharArray();
        int index = 7 - ((op / 8) % 8);
        if (op < 0x80)
        {
            bool zero = bits[index] == '0';
            int f = (prior % 2) + 0x10 + (zero ? 0x44 : 0)
                + (index == 0 && !zero ? 0x80 : 0);
            if (op % 8 != 6)
                f += (bits[2] == '1' ? 0x20 : 0) + (bits[4] == '1' ? 0x08 : 0);
            return (value, (byte)f);
        }
        bits[index] = op < 0xc0 ? '0' : '1';
        return (Convert.ToByte(new string(bits), 2), prior);
    }

    [Theory]
    [InlineData(0xfffe, 0x7e, 0x00)]
    [InlineData(0xffff, 0xfe, 0x80)]
    public void BitFetchWrapAndRefresh(ushort pc, byte refresh, byte expectedRefresh)
    {
        var bus = new Bus(); var cpu = new Z80(bus) { ProgramCounter = pc, Refresh = refresh, B = 0x80 };
        bus.Memory[pc] = 0xcb; bus.Memory[unchecked((ushort)(pc + 1))] = 0x78;
        Assert.Equal(8, cpu.Step());
        Assert.Equal(unchecked((ushort)(pc + 2)), cpu.ProgramCounter);
        Assert.Equal(expectedRefresh, cpu.Refresh);
        Assert.Equal(new[] { $"F{pc:X4}:CB", $"F{unchecked((ushort)(pc + 1)):X4}:78" }, bus.Events);
    }

    [Fact]
    public void SllStillFaultsAfterBothFetches()
    {
        for (int op = 0x30; op <= 0x37; op++)
        {
            var bus = new Bus(); var cpu = new Z80(bus) { ProgramCounter = 0x2000, Refresh = 0xff };
            bus.Memory[0x2000] = 0xcb; bus.Memory[0x2001] = (byte)op;
            Assert.Throws<NotSupportedException>(() => cpu.Step());
            Assert.True(cpu.IsFaulted); Assert.Equal(0x2002, cpu.ProgramCounter); Assert.Equal(0x81, cpu.Refresh);
            Assert.Equal(new[] { "F2000:CB", $"F2001:{op:X2}" }, bus.Events);
        }
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
        public byte FetchOpcode(ushort a) { byte v = Memory[a]; Events.Add($"F{a:X4}:{v:X2}"); return v; }
        public byte ReadMemory(ushort a) { byte v = Memory[a]; Events.Add($"R{a:X4}:{v:X2}"); return v; }
        public void WriteMemory(ushort a, byte v) { Events.Add($"W{a:X4}:{v:X2}"); Memory[a] = v; }
        public byte ReadIo(ushort p) => throw new InvalidOperationException();
        public void WriteIo(ushort p, byte v) => throw new InvalidOperationException();
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException();
    }
}
