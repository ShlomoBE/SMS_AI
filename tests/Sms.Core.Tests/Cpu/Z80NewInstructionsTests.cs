using Sms.Core.Cpu;
using Sms.Core.System;
using Xunit;

namespace Sms.Core.Tests.Cpu;

public sealed class Z80NewInstructionsTests
{
    private sealed class TestBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public byte FetchOpcode(ushort address) => Memory[address];
        public byte ReadMemory(ushort address) => Memory[address];
        public void WriteMemory(ushort address, byte value) => Memory[address] = value;
        public byte ReadIo(ushort port) => 0xFF;
        public void WriteIo(ushort port, byte value) { }
        public byte AcknowledgeInterrupt() => 0xFF;
    }

    [Theory]
    [InlineData(0xDD, 0x23, 0xFFFF, 0x0000)]
    [InlineData(0xFD, 0x23, 0x1234, 0x1235)]
    [InlineData(0xDD, 0x2B, 0x1234, 0x1233)]
    [InlineData(0xFD, 0x2B, 0x0000, 0xFFFF)]
    public void IndexedIncDecUpdatesOnlySelectedRegister(byte prefix, byte opcode, ushort initial, ushort expected)
    {
        var bus = new TestBus();
        bus.Memory[0] = prefix;
        bus.Memory[1] = opcode;
        var cpu = new Z80(bus) { IX = 0x1111, IY = 0x2222, H = 0x56, L = 0x78, F = 0xA5 };
        if (prefix == 0xDD) cpu.IX = initial; else cpu.IY = initial;

        Assert.Equal(10, cpu.Step());
        Assert.Equal(expected, prefix == 0xDD ? cpu.IX : cpu.IY);
        Assert.Equal(prefix == 0xDD ? 0x2222 : 0x1111, prefix == 0xDD ? cpu.IY : cpu.IX);
        Assert.Equal(0x56, cpu.H);
        Assert.Equal(0x78, cpu.L);
        Assert.Equal(0xA5, cpu.F);
        Assert.Equal(2, cpu.ProgramCounter);
        Assert.Equal(2, cpu.Refresh);
    }

    [Fact]
    public void IndexedIncDecUsesLastPrefixAndChargesEachPrefix()
    {
        var bus = new TestBus();
        bus.Memory[0] = 0xDD; bus.Memory[1] = 0xFD; bus.Memory[2] = 0x23;
        var cpu = new Z80(bus) { IX = 1, IY = 2 };
        Assert.Equal(14, cpu.Step());
        Assert.Equal(1, cpu.IX);
        Assert.Equal(3, cpu.IY);
        Assert.Equal(3, cpu.Refresh);
    }

    [Fact]
    public void IncDec16_ModifiesRegisterPairsWithoutAffectingFlags()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus);
        cpu.F = 0xFF;

        // INC BC (0x03)
        cpu.B = 0x12; cpu.C = 0x34;
        bus.Memory[0x0000] = 0x03;
        cpu.Step();
        Assert.Equal(0x12, cpu.B); Assert.Equal(0x35, cpu.C);
        Assert.Equal(0xFF, cpu.F);

        // DEC BC (0x0B)
        bus.Memory[0x0001] = 0x0B;
        cpu.Step();
        Assert.Equal(0x12, cpu.B); Assert.Equal(0x34, cpu.C);
        Assert.Equal(0xFF, cpu.F);

        // INC HL (0x23)
        cpu.H = 0xFF; cpu.L = 0xFF;
        bus.Memory[0x0002] = 0x23;
        cpu.Step();
        Assert.Equal(0x00, cpu.H); Assert.Equal(0x00, cpu.L);
        Assert.Equal(0xFF, cpu.F);

        // DEC SP (0x3B)
        cpu.StackPointer = 0x1000;
        bus.Memory[0x0003] = 0x3B;
        cpu.Step();
        Assert.Equal(0x0FFF, cpu.StackPointer);
        Assert.Equal(0xFF, cpu.F);
    }

    [Fact]
    public void PushPop_PushesAndPullsRegisterPairsCorrectly()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus);
        cpu.StackPointer = 0xD000;

        // PUSH BC (0xC5)
        cpu.B = 0x12; cpu.C = 0x34;
        bus.Memory[0x0000] = 0xC5;
        cpu.Step();
        Assert.Equal(0xCFFE, cpu.StackPointer);
        Assert.Equal(0x12, bus.Memory[0xCFFF]);
        Assert.Equal(0x34, bus.Memory[0xCFFE]);

        // POP DE (0xD1)
        bus.Memory[0x0001] = 0xD1;
        cpu.Step();
        Assert.Equal(0xD000, cpu.StackPointer);
        Assert.Equal(0x12, cpu.D); Assert.Equal(0x34, cpu.E);

        // PUSH AF (0xF5) & POP BC (0xC1)
        cpu.A = 0xAB; cpu.F = 0xC5;
        bus.Memory[0x0002] = 0xF5;
        cpu.Step();
        bus.Memory[0x0003] = 0xC1;
        cpu.Step();
        Assert.Equal(0xAB, cpu.B); Assert.Equal(0xC5, cpu.C);
    }

    [Fact]
    public void IndirectALoadsAndStores_PerformCorrectMemoryOps()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus);

        // LD (BC), A (0x02)
        cpu.B = 0xC0; cpu.C = 0x00; cpu.A = 0x77;
        bus.Memory[0x0000] = 0x02;
        cpu.Step();
        Assert.Equal(0x77, bus.Memory[0xC000]);

        // LD A, (DE) (0x1A)
        cpu.D = 0xC0; cpu.E = 0x05; bus.Memory[0xC005] = 0x88;
        bus.Memory[0x0001] = 0x1A;
        cpu.Step();
        Assert.Equal(0x88, cpu.A);

        // LD (nn), A (0x32 0x10 0xC0)
        cpu.A = 0x99;
        bus.Memory[0x0002] = 0x32;
        bus.Memory[0x0003] = 0x10;
        bus.Memory[0x0004] = 0xC0;
        cpu.Step();
        Assert.Equal(0x99, bus.Memory[0xC010]);

        // LD A, (nn) (0x3A 0x20 0xC0)
        bus.Memory[0xC020] = 0xAA;
        bus.Memory[0x0005] = 0x3A;
        bus.Memory[0x0006] = 0x20;
        bus.Memory[0x0007] = 0xC0;
        cpu.Step();
        Assert.Equal(0xAA, cpu.A);
    }

    [Fact]
    public void AccumulatorRotates_UpdatesCarryAndPreservesFlags()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus);

        // RLCA (0x07): 0x80 -> 0x01, Carry=1
        cpu.A = 0x80; cpu.F = 0x40; // Z set
        bus.Memory[0x0000] = 0x07;
        cpu.Step();
        Assert.Equal(0x01, cpu.A);
        Assert.Equal(0x01, cpu.F & 0x01); // C set
        Assert.Equal(0x40, cpu.F & 0x40); // Z preserved

        // RRCA (0x0F): 0x01 -> 0x80, Carry=1
        bus.Memory[0x0001] = 0x0F;
        cpu.Step();
        Assert.Equal(0x80, cpu.A);
        Assert.Equal(0x01, cpu.F & 0x01); // C set

        // RLA (0x17): 0x80 with Carry=1 -> 0x01, Carry=1
        cpu.A = 0x80; cpu.F = 0x01;
        bus.Memory[0x0002] = 0x17;
        cpu.Step();
        Assert.Equal(0x01, cpu.A);
        Assert.Equal(0x01, cpu.F & 0x01);

        // RRA (0x1F): 0x01 with Carry=1 -> 0x80, Carry=1
        cpu.A = 0x01; cpu.F = 0x01;
        bus.Memory[0x0003] = 0x1F;
        cpu.Step();
        Assert.Equal(0x80, cpu.A);
        Assert.Equal(0x01, cpu.F & 0x01);
    }

    [Fact]
    public void Djnz_LoopsUntilCounterReachesZero()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus);

        // DJNZ -2 (0x10 0xFE)
        cpu.B = 2;
        cpu.ProgramCounter = 0x0010;
        bus.Memory[0x0010] = 0x10;
        bus.Memory[0x0011] = 0xFE; // offset -2 -> jump back to 0x0010

        // Step 1: B=2 -> B=1, PC jumps to 0x0010
        int cycles1 = cpu.Step();
        Assert.Equal(13, cycles1);
        Assert.Equal(1, cpu.B);
        Assert.Equal(0x0010, cpu.ProgramCounter);

        // Step 2: B=1 -> B=0, PC falls through to 0x0012
        int cycles2 = cpu.Step();
        Assert.Equal(8, cycles2);
        Assert.Equal(0, cpu.B);
        Assert.Equal(0x0012, cpu.ProgramCounter);
    }

    [Fact]
    public void EdSubcode53And5B_StoreAndLoad16BitRegisters()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus);

        // LD (0xC100), DE (0xED, 0x53, 0x00, 0xC1)
        cpu.D = 0x12; cpu.E = 0x34;
        bus.Memory[0x0000] = 0xED;
        bus.Memory[0x0001] = 0x53;
        bus.Memory[0x0002] = 0x00;
        bus.Memory[0x0003] = 0xC1;
        int cycles1 = cpu.Step();
        Assert.Equal(20, cycles1);
        Assert.Equal(0x34, bus.Memory[0xC100]);
        Assert.Equal(0x12, bus.Memory[0xC101]);

        // LD BC, (0xC100) (0xED, 0x4B, 0x00, 0xC1)
        bus.Memory[0x0004] = 0xED;
        bus.Memory[0x0005] = 0x4B;
        bus.Memory[0x0006] = 0x00;
        bus.Memory[0x0007] = 0xC1;
        cpu.ProgramCounter = 0x0004;
        int cycles2 = cpu.Step();
        Assert.Equal(20, cycles2);
        Assert.Equal(0x12, cpu.B);
        Assert.Equal(0x34, cpu.C);
    }
}
