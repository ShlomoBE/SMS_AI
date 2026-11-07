using Sms.Core.Cpu;
using Xunit;

namespace Sms.Core.Tests.Cpu;

public sealed class Z80IndexTests
{
    private static Z80 NewCpu(RecordingBus bus) => new(bus)
    {
        ProgramCounter = 0x2000,
        StackPointer = 0xD000,
        Refresh = 0x00,
        A = 0x10,
        B = 0x20,
        C = 0x30,
        D = 0x40,
        E = 0x50,
        H = 0x60,
        L = 0x70,
        F = 0x00,
        IX = 0x1000,
        IY = 0x2000
    };

    [Fact]
    public void RegisterHalves_ReadAndWriteCorrectly()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);

        cpu.IX = 0x1234;
        Assert.Equal(0x12, cpu.IXH);
        Assert.Equal(0x34, cpu.IXL);

        cpu.IXH = 0xAB;
        Assert.Equal(0xAB34, cpu.IX);
        cpu.IXL = 0xCD;
        Assert.Equal(0xABCD, cpu.IX);

        cpu.IY = 0x5678;
        Assert.Equal(0x56, cpu.IYH);
        Assert.Equal(0x78, cpu.IYL);

        cpu.IYH = 0xEF;
        Assert.Equal(0xEF78, cpu.IY);
        cpu.IYL = 0x90;
        Assert.Equal(0xEF90, cpu.IY);
    }

    [Fact]
    public void LdIxImmediate_Loads16BitValueIn14Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = 0xDD; // LD IX, 0x5678
        bus.Memory[0x2001] = 0x21;
        bus.Memory[0x2002] = 0x78;
        bus.Memory[0x2003] = 0x56;

        int cycles = cpu.Step();

        Assert.Equal(14, cycles);
        Assert.Equal(0x5678, cpu.IX);
        Assert.Equal(0x2004, cpu.ProgramCounter);
    }

    [Fact]
    public void LdIyImmediate_Loads16BitValueIn14Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = 0xFD; // LD IY, 0x9ABC
        bus.Memory[0x2001] = 0x21;
        bus.Memory[0x2002] = 0xBC;
        bus.Memory[0x2003] = 0x9A;

        int cycles = cpu.Step();

        Assert.Equal(14, cycles);
        Assert.Equal(0x9ABC, cpu.IY);
        Assert.Equal(0x2004, cpu.ProgramCounter);
    }

    [Fact]
    public void LdDirectMemoryFromIx_StoresValueAndUpdatesMemptrIn20Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x4567;
        bus.Memory[0x2000] = 0xDD; // LD (0x3000), IX
        bus.Memory[0x2001] = 0x22;
        bus.Memory[0x2002] = 0x00;
        bus.Memory[0x2003] = 0x30;

        int cycles = cpu.Step();

        Assert.Equal(20, cycles);
        Assert.Equal(0x67, bus.Memory[0x3000]);
        Assert.Equal(0x45, bus.Memory[0x3001]);
        Assert.Equal(0x2004, cpu.ProgramCounter);

        VerifyMemptrHighByte(cpu, bus, 0x30); // 0x3001 >> 8 = 0x30
    }

    [Fact]
    public void LdIxFromDirectMemory_LoadsValueAndUpdatesMemptrIn20Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        bus.Memory[0x3000] = 0x89;
        bus.Memory[0x3001] = 0xAB;
        bus.Memory[0x2000] = 0xDD; // LD IX, (0x3000)
        bus.Memory[0x2001] = 0x2A;
        bus.Memory[0x2002] = 0x00;
        bus.Memory[0x2003] = 0x30;

        int cycles = cpu.Step();

        Assert.Equal(20, cycles);
        Assert.Equal(0xAB89, cpu.IX);
        Assert.Equal(0x2004, cpu.ProgramCounter);

        VerifyMemptrHighByte(cpu, bus, 0x30); // 0x3001 >> 8 = 0x30
    }

    [Fact]
    public void LdSpIx_UpdatesStackPointerIn10Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0xC000;
        bus.Memory[0x2000] = 0xDD; // LD SP, IX
        bus.Memory[0x2001] = 0xF9;

        int cycles = cpu.Step();

        Assert.Equal(10, cycles);
        Assert.Equal(0xC000, cpu.StackPointer);
        Assert.Equal(0x2002, cpu.ProgramCounter);
    }

    [Theory]
    [InlineData(0x09, 0x1000, 0x0500, 0x1500)] // ADD IX, BC
    [InlineData(0x19, 0x1000, 0x0500, 0x1500)] // ADD IX, DE
    [InlineData(0x29, 0x1000, 0x1000, 0x2000)] // ADD IX, IX
    [InlineData(0x39, 0x1000, 0xD000, 0xE000)] // ADD IX, SP
    public void AddIxRegister_CalculatesSumIn15Cycles(byte subOpcode, ushort initialIx, ushort regVal, ushort expectedResult)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = initialIx;
        cpu.B = (byte)(regVal >> 8); cpu.C = (byte)regVal;
        cpu.D = (byte)(regVal >> 8); cpu.E = (byte)regVal;
        if (subOpcode == 0x39) cpu.StackPointer = regVal;

        bus.Memory[0x2000] = 0xDD;
        bus.Memory[0x2001] = subOpcode;

        int cycles = cpu.Step();

        Assert.Equal(15, cycles);
        Assert.Equal(expectedResult, cpu.IX);
        Assert.Equal(0x2002, cpu.ProgramCounter);
    }

    [Fact]
    public void PushAndPopIx_PushesAndPops16BitRegister()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x55AA;
        bus.Memory[0x2000] = 0xDD; // PUSH IX
        bus.Memory[0x2001] = 0xE5;
        bus.Memory[0x2002] = 0xDD; // POP IX
        bus.Memory[0x2003] = 0xE1;

        int pushCycles = cpu.Step();
        Assert.Equal(15, pushCycles);
        Assert.Equal(0xCFFE, cpu.StackPointer);
        Assert.Equal(0xAA, bus.Memory[0xCFFE]);
        Assert.Equal(0x55, bus.Memory[0xCFFF]);

        cpu.IX = 0x0000;
        int popCycles = cpu.Step();
        Assert.Equal(14, popCycles);
        Assert.Equal(0x55AA, cpu.IX);
        Assert.Equal(0xD000, cpu.StackPointer);
    }

    [Fact]
    public void ExSpIx_ExchangesStackWithIxAndUpdatesMemptrIn23Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x2812;
        cpu.StackPointer = 0xC000;
        bus.Memory[0xC000] = 0x34;
        bus.Memory[0xC001] = 0x12;

        bus.Memory[0x2000] = 0xDD; // EX (SP), IX
        bus.Memory[0x2001] = 0xE3;

        int cycles = cpu.Step();

        Assert.Equal(23, cycles);
        Assert.Equal(0x1234, cpu.IX);
        Assert.Equal(0x12, bus.Memory[0xC000]);
        Assert.Equal(0x28, bus.Memory[0xC001]);
        Assert.Equal(0x2002, cpu.ProgramCounter);

        VerifyMemptrHighByte(cpu, bus, 0x12); // MEMPTR set to new IX value (0x1234)
    }

    [Fact]
    public void JpIx_JumpsToIxAddressIn8Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x3456;
        bus.Memory[0x2000] = 0xDD; // JP (IX)
        bus.Memory[0x2001] = 0xE9;

        int cycles = cpu.Step();

        Assert.Equal(8, cycles);
        Assert.Equal(0x3456, cpu.ProgramCounter);
    }

    [Fact]
    public void DisplacedLd_ReadsAndWritesMemoryAndUpdatesMemptrIn19Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x2800;
        bus.Memory[0x2805] = 0x77;
        bus.Memory[0x2000] = 0xDD; // LD A, (IX+5)
        bus.Memory[0x2001] = 0x7E;
        bus.Memory[0x2002] = 0x05;
        bus.Memory[0x2003] = 0xDD; // LD (IX-2), B
        bus.Memory[0x2004] = 0x70;
        bus.Memory[0x2005] = 0xFE; // -2

        int cyclesRead = cpu.Step();
        Assert.Equal(19, cyclesRead);
        Assert.Equal(0x77, cpu.A);
        VerifyMemptrHighByte(cpu, bus, 0x28); // MEMPTR = 0x2805

        ushort writePc = cpu.ProgramCounter;
        bus.Memory[writePc] = 0xDD; // LD (IX-2), B
        bus.Memory[writePc + 1] = 0x70;
        bus.Memory[writePc + 2] = 0xFE; // -2

        int cyclesWrite = cpu.Step();
        Assert.Equal(19, cyclesWrite);
        Assert.Equal(0x20, bus.Memory[0x27FE]);
        VerifyMemptrHighByte(cpu, bus, 0x27); // MEMPTR = 0x27FE
    }

    [Fact]
    public void DisplacedLdImmediate_WritesMemoryIn19Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x2800;
        bus.Memory[0x2000] = 0xDD; // LD (IX+3), 0x99
        bus.Memory[0x2001] = 0x36;
        bus.Memory[0x2002] = 0x03;
        bus.Memory[0x2003] = 0x99;

        int cycles = cpu.Step();

        Assert.Equal(19, cycles);
        Assert.Equal(0x99, bus.Memory[0x2803]);
        VerifyMemptrHighByte(cpu, bus, 0x28);
    }

    [Fact]
    public void DisplacedAlu_PerformsOperationAndUpdatesMemptrIn19Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x2800;
        cpu.A = 0x20;
        bus.Memory[0x2804] = 0x05;
        bus.Memory[0x2000] = 0xDD; // ADD A, (IX+4)
        bus.Memory[0x2001] = 0x86;
        bus.Memory[0x2002] = 0x04;

        int cycles = cpu.Step();

        Assert.Equal(19, cycles);
        Assert.Equal(0x25, cpu.A);
        VerifyMemptrHighByte(cpu, bus, 0x28);
    }

    [Fact]
    public void IncDecDisplaced_ModifiesMemoryAndUpdatesFlagsIn23Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x2800;
        bus.Memory[0x2802] = 0x0F;
        bus.Memory[0x2000] = 0xDD; // INC (IX+2)
        bus.Memory[0x2001] = 0x34;
        bus.Memory[0x2002] = 0x02;

        int cycles = cpu.Step();

        Assert.Equal(23, cycles);
        Assert.Equal(0x10, bus.Memory[0x2802]);
        Assert.True((cpu.F & 0x10) != 0); // Half-carry
        VerifyMemptrHighByte(cpu, bus, 0x28);
    }

    [Fact]
    public void IncDecIndexHalves_Updates8BitHalfRegisterIn10Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x1234;
        bus.Memory[0x2000] = 0xDD; // INC IXH
        bus.Memory[0x2001] = 0x24;
        bus.Memory[0x2002] = 0xDD; // DEC IXL
        bus.Memory[0x2003] = 0x2D;

        int incCycles = cpu.Step();
        Assert.Equal(10, incCycles);
        Assert.Equal(0x13, cpu.IXH);

        int decCycles = cpu.Step();
        Assert.Equal(10, decCycles);
        Assert.Equal(0x33, cpu.IXL);
    }

    [Fact]
    public void RegisterLoadIndexHalves_TransfersBetweenRegsIn8Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IXH = 0x88;
        cpu.A = 0x44;
        bus.Memory[0x2000] = 0xDD; // LD A, IXH
        bus.Memory[0x2001] = 0x7C;
        bus.Memory[0x2002] = 0xDD; // LD IXL, A
        bus.Memory[0x2003] = 0x6F;

        int cycles1 = cpu.Step();
        Assert.Equal(8, cycles1);
        Assert.Equal(0x88, cpu.A);

        int cycles2 = cpu.Step();
        Assert.Equal(8, cycles2);
        Assert.Equal(0x88, cpu.IXL);
    }

    [Fact]
    public void DdCbBitOperation_TestsBitAndUpdatesMemptrIn20Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x2800;
        bus.Memory[0x2805] = 0x08; // bit 3 is set
        bus.Memory[0x2000] = 0xDD; // BIT 3, (IX+5)
        bus.Memory[0x2001] = 0xCB;
        bus.Memory[0x2002] = 0x05;
        bus.Memory[0x2003] = 0x5E;

        int cycles = cpu.Step();

        Assert.Equal(20, cycles);
        Assert.Equal(0, cpu.F & 0x40); // Zero flag cleared (bit is set)
        Assert.Equal(0x10, cpu.F & 0x10); // Half carry set
        VerifyMemptrHighByte(cpu, bus, 0x28);
    }

    [Fact]
    public void DdCbShiftOperationWithRegCopy_PerformsShiftWriteAndRegCopyIn23Cycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.IX = 0x2800;
        bus.Memory[0x2802] = 0x80;
        bus.Memory[0x2000] = 0xDD; // RLC (IX+2), copy to B (opcode 0x00)
        bus.Memory[0x2001] = 0xCB;
        bus.Memory[0x2002] = 0x02;
        bus.Memory[0x2003] = 0x00;

        int cycles = cpu.Step();

        Assert.Equal(23, cycles);
        Assert.Equal(0x01, bus.Memory[0x2802]);
        Assert.Equal(0x01, cpu.B); // Copied to B
        Assert.Equal(0x01, cpu.F & 0x01); // Carry set from bit 7
        VerifyMemptrHighByte(cpu, bus, 0x28);
    }

    [Fact]
    public void PrefixFallback_ExecutesUnprefixedOpcodeWith4ExtraCycles()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.B = 0x10;
        bus.Memory[0x2000] = 0xDD; // DD prefix followed by INC B (0x04)
        bus.Memory[0x2001] = 0x04;

        int cycles = cpu.Step();

        Assert.Equal(8, cycles); // 4 T prefix + 4 T INC B
        Assert.Equal(0x11, cpu.B);
        Assert.Equal(0x1000, cpu.IX); // IX unchanged
        Assert.Equal(0x2002, cpu.ProgramCounter);
    }

    [Fact]
    public void RepeatedPrefixes_LastPrefixDeterminesIndexRegister()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = 0xDD; // DD prefix
        bus.Memory[0x2001] = 0xFD; // FD prefix
        bus.Memory[0x2002] = 0x21; // LD IY, 0x4321
        bus.Memory[0x2003] = 0x21;
        bus.Memory[0x2004] = 0x43;

        int cycles = cpu.Step();

        Assert.Equal(18, cycles); // 4 + 4 + 10 = 18 T
        Assert.Equal(0x4321, cpu.IY);
        Assert.Equal(0x1000, cpu.IX); // IX untouched
        Assert.Equal(0x2005, cpu.ProgramCounter);
    }

    private static void VerifyMemptrHighByte(Z80 cpu, RecordingBus bus, byte expectedHighByte)
    {
        ushort pc = cpu.ProgramCounter;
        cpu.H = 0x50;
        cpu.L = 0x00;
        bus.Memory[pc] = 0xCB;
        bus.Memory[pc + 1] = 0x5E; // BIT 3, (HL)
        bus.Memory[0x5000] = 0x00;

        cpu.Step();

        byte expectedBits = (byte)(expectedHighByte & 0x28);
        Assert.Equal(expectedBits, (byte)(cpu.F & 0x28));
    }

    private sealed class RecordingBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public List<string> Events { get; } = [];
        public byte FetchOpcode(ushort address) { byte value = Memory[address]; Events.Add($"F:{address:X4}:{value:X2}"); return value; }
        public byte ReadMemory(ushort address)
        {
            byte value = Memory[address]; Events.Add($"R:{address:X4}:{value:X2}");
            return value;
        }
        public void WriteMemory(ushort address, byte value)
        {
            Events.Add($"W:{address:X4}:{value:X2}");
            Memory[address] = value;
        }
        public byte ReadIo(ushort port) => throw new InvalidOperationException();
        public void WriteIo(ushort port, byte value) => throw new InvalidOperationException();
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException();
    }
}