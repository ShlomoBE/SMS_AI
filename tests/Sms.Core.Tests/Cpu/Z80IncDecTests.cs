using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// ZiLOG UM0080 INC/DEC 8-bit instruction group and timing:
// https://www.zilog.com/docs/z80/um0080.pdf
// Sean Young, The Undocumented Z80 Documented, IDocs/z80-documented.pdf,
// flags 3 and 5: result bits. Undocumented behavior is variant-dependent;
// neither source establishes the exact SMS CPU revision.
public sealed class Z80IncDecTests
{
    [Theory]
    [InlineData(0x04, 0, false)] [InlineData(0x05, 0, true)]
    [InlineData(0x0c, 1, false)] [InlineData(0x0d, 1, true)]
    [InlineData(0x14, 2, false)] [InlineData(0x15, 2, true)]
    [InlineData(0x1c, 3, false)] [InlineData(0x1d, 3, true)]
    [InlineData(0x24, 4, false)] [InlineData(0x25, 4, true)]
    [InlineData(0x2c, 5, false)] [InlineData(0x2d, 5, true)]
    [InlineData(0x34, 6, false)] [InlineData(0x35, 6, true)]
    [InlineData(0x3c, 7, false)] [InlineData(0x3d, 7, true)]
    public void AllInputsAndCarryValues(byte opcode, int target, bool decrement)
    {
        var bus = new RecordingBus();
        var cpu = new Z80(bus);
        for (int input = 0; input < 256; input++)
        for (int carry = 0; carry < 2; carry++)
        foreach (int prior in new[] { 0, 0xfe })
        {
            cpu.ProgramCounter = 0x2000; cpu.StackPointer = 0x1234; cpu.Refresh = 0xff;
            cpu.B = 0x11; cpu.C = 0x22; cpu.D = 0x33; cpu.E = 0x44;
            cpu.H = 0x55; cpu.L = 0x66; cpu.A = 0x77;
            cpu.F = (byte)(prior | carry);
            if (target != 6) SetRegister(cpu, target, (byte)input);
            bus.Memory[0x2000] = opcode;
            bus.Memory[0x5566] = (byte)input;
            bus.Events.Clear();
            byte[] expectedRegisters = Registers(cpu);
            int mathematicalResult = input + (decrement ? -1 : 1);
            byte result = unchecked((byte)mathematicalResult);
            if (target != 6) expectedRegisters[target] = result;
            // Independent conditions, expressed using arithmetic ranges rather
            // than the implementation's masked-result formula.
            int expectedFlags = carry;
            if (result >= 128) expectedFlags |= 0x80;
            if (result == 0) expectedFlags |= 0x40;
            if ((result / 32) % 2 == 1) expectedFlags |= 0x20;
            if (decrement ? input % 16 == 0 : input % 16 == 15) expectedFlags |= 0x10;
            if ((result / 8) % 2 == 1) expectedFlags |= 0x08;
            if (decrement ? input == 128 : input == 127) expectedFlags |= 0x04;
            if (decrement) expectedFlags |= 0x02;

            Assert.Equal(target == 6 ? 11 : 4, cpu.Step());
            Assert.Equal(expectedRegisters, Registers(cpu));
            Assert.Equal((byte)expectedFlags, cpu.F);
            Assert.Equal(0x2001, cpu.ProgramCounter);
            Assert.Equal(0x1234, cpu.StackPointer);
            Assert.Equal(0x80, cpu.Refresh);
            Assert.False(cpu.IsFaulted);
            Assert.Equal(target == 6
                ? new[] { $"F:2000:{opcode:X2}", $"R:5566:{input:X2}", $"W:5566:{result:X2}" }
                : new[] { $"F:2000:{opcode:X2}" }, bus.Events);
            Assert.Equal(target == 6 ? result : (byte)input, bus.Memory[0x5566]);
        }
    }

    [Theory]
    [InlineData(0x34, 0x35)] [InlineData(0x35, 0x34)]
    public void IndirectCanReadAndWriteItsOwnOpcodeAtWrappedPc(byte opcode, byte result)
    {
        var bus = new RecordingBus();
        var cpu = new Z80(bus) { ProgramCounter = 0xffff, H = 0xff, L = 0xff, Refresh = 0x7f };
        bus.Memory[0xffff] = opcode;
        Assert.Equal(11, cpu.Step());
        Assert.Equal(0, cpu.ProgramCounter);
        Assert.Equal(0, cpu.Refresh);
        Assert.Equal(result, bus.Memory[0xffff]);
        Assert.Equal(new[] { $"F:FFFF:{opcode:X2}", $"R:FFFF:{opcode:X2}", $"W:FFFF:{result:X2}" }, bus.Events);
    }

    [Theory]
    [InlineData(0x34, "fetch")] [InlineData(0x34, "read")] [InlineData(0x34, "write")]
    [InlineData(0x35, "fetch")] [InlineData(0x35, "read")] [InlineData(0x35, "write")]
    public void BusFailureIsTerminalAndNeverRetried(byte opcode, string failure)
    {
        var bus = new RecordingBus { Failure = failure };
        var cpu = new Z80(bus) { ProgramCounter = 0x2000, H = 0x55, L = 0x66, F = 0xa5 };
        bus.Memory[0x2000] = opcode; bus.Memory[0x5566] = 0x80;
        byte result = opcode == 0x34 ? (byte)0x81 : (byte)0x7f;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        Assert.Equal(failure switch {
            "fetch" => new[] { $"F:2000:{opcode:X2}" },
            "read" => new[] { $"F:2000:{opcode:X2}", "R:5566:80" },
            _ => new[] { $"F:2000:{opcode:X2}", "R:5566:80", $"W:5566:{result:X2}" }
        }, bus.Events);
        Assert.Equal(0x80, bus.Memory[0x5566]);
        Assert.Equal(0xa5, cpu.F);
        int count = bus.Events.Count;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(count, bus.Events.Count);
    }

    [Theory]
    [InlineData(0x34, 0x81)]
    [InlineData(0x35, 0x7f)]
    public void WriteSideEffectBeforeFailureIsNotRetriedOrRolledBack(byte opcode, byte result)
    {
        var bus = new RecordingBus { Failure = "write", CommitBeforeFailure = true };
        var cpu = new Z80(bus) { ProgramCounter = 0x2000, H = 0x55, L = 0x66 };
        bus.Memory[0x2000] = opcode;
        bus.Memory[0x5566] = 0x80;

        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);
        Assert.Equal(result, bus.Memory[0x5566]);
        Assert.Equal(3, bus.Events.Count);
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(3, bus.Events.Count);
        Assert.Equal(result, bus.Memory[0x5566]);
    }

    private static byte[] Registers(Z80 cpu) => [cpu.B, cpu.C, cpu.D, cpu.E, cpu.H, cpu.L, 0, cpu.A];
    private static void SetRegister(Z80 cpu, int target, byte value)
    {
        switch (target)
        {
            case 0: cpu.B = value; break; case 1: cpu.C = value; break;
            case 2: cpu.D = value; break; case 3: cpu.E = value; break;
            case 4: cpu.H = value; break; case 5: cpu.L = value; break;
            case 7: cpu.A = value; break; default: throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    private sealed class RecordingBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public List<string> Events { get; } = [];
        public string? Failure { get; init; }
        public bool CommitBeforeFailure { get; init; }
        public byte FetchOpcode(ushort address)
        {
            byte value = Memory[address]; Events.Add($"F:{address:X4}:{value:X2}");
            if (Failure == "fetch") throw new InvalidOperationException();
            return value;
        }
        public byte ReadMemory(ushort address)
        {
            byte value = Memory[address]; Events.Add($"R:{address:X4}:{value:X2}");
            if (Failure == "read") throw new InvalidOperationException();
            return value;
        }
        public void WriteMemory(ushort address, byte value)
        {
            Events.Add($"W:{address:X4}:{value:X2}");
            if (Failure == "write" && CommitBeforeFailure) Memory[address] = value;
            if (Failure == "write") throw new InvalidOperationException();
            Memory[address] = value;
        }
        public byte ReadIo(ushort port) => throw new InvalidOperationException();
        public void WriteIo(ushort port, byte value) => throw new InvalidOperationException();
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException();
    }
}
