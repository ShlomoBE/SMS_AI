using Sms.Core.Cpu;

namespace Sms.Core.Tests.Cpu;

// ZiLOG Z80 CPU User Manual UM008001-1000, Control Flow Group
// (JP, JR, CALL, RET, RST and Condition Codes)
public sealed class Z80ControlFlowTests
{
    [Fact]
    public void JpUnconditional_SetsPcAndMemptrAndReturns10T()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = 0xC3; // JP 0x1234
        bus.Memory[0x2001] = 0x34;
        bus.Memory[0x2002] = 0x12;

        int tStates = cpu.Step();

        Assert.Equal(10, tStates);
        Assert.Equal(0x1234, cpu.ProgramCounter);
        Assert.Equal(0x00, cpu.Refresh);
        Assert.Equal(new[] { "F:2000:C3", "R:2001:34", "R:2002:12" }, bus.Events);

        // Verify MEMPTR was set to 0x1234 via BIT 3, (HL)
        VerifyMemptrHighByte(cpu, bus, 0x12);
    }

    [Theory]
    [InlineData(0xC2, 0x40, false, 0x1234, 10)] // JP NZ, Z=1 (not taken)
    [InlineData(0xC2, 0x00, true,  0x1234, 10)] // JP NZ, Z=0 (taken)
    [InlineData(0xCA, 0x00, false, 0x1234, 10)] // JP Z,  Z=0 (not taken)
    [InlineData(0xCA, 0x40, true,  0x1234, 10)] // JP Z,  Z=1 (taken)
    [InlineData(0xD2, 0x01, false, 0x1234, 10)] // JP NC, C=1 (not taken)
    [InlineData(0xD2, 0x00, true,  0x1234, 10)] // JP NC, C=0 (taken)
    [InlineData(0xDA, 0x00, false, 0x1234, 10)] // JP C,  C=0 (not taken)
    [InlineData(0xDA, 0x01, true,  0x1234, 10)] // JP C,  C=1 (taken)
    [InlineData(0xE2, 0x04, false, 0x1234, 10)] // JP PO, P/V=1 (not taken)
    [InlineData(0xE2, 0x00, true,  0x1234, 10)] // JP PO, P/V=0 (taken)
    [InlineData(0xEA, 0x00, false, 0x1234, 10)] // JP PE, P/V=0 (not taken)
    [InlineData(0xEA, 0x04, true,  0x1234, 10)] // JP PE, P/V=1 (taken)
    [InlineData(0xF2, 0x80, false, 0x1234, 10)] // JP P,  S=1 (not taken)
    [InlineData(0xF2, 0x00, true,  0x1234, 10)] // JP P,  S=0 (taken)
    [InlineData(0xFA, 0x00, false, 0x1234, 10)] // JP M,  S=0 (not taken)
    [InlineData(0xFA, 0x80, true,  0x1234, 10)] // JP M,  S=1 (taken)
    public void JpConditional_ExecutesCorrectly(byte opcode, byte flagState, bool shouldJump, ushort target, int expectedT)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.F = flagState;
        bus.Memory[0x2000] = opcode;
        bus.Memory[0x2001] = (byte)target;
        bus.Memory[0x2002] = (byte)(target >> 8);

        int tStates = cpu.Step();

        Assert.Equal(expectedT, tStates);
        Assert.Equal(shouldJump ? target : (ushort)0x2003, cpu.ProgramCounter);
        Assert.Equal(new[] { $"F:2000:{opcode:X2}", $"R:2001:{(byte)target:X2}", $"R:2002:{(byte)(target >> 8):X2}" }, bus.Events);

        if (shouldJump)
        {
            VerifyMemptrHighByte(cpu, bus, (byte)(target >> 8));
        }
    }

    [Fact]
    public void JpHl_LoadsPcFromHlAndReturns4T()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.H = 0x80;
        cpu.L = 0x40;
        bus.Memory[0x2000] = 0xE9; // JP (HL)

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.Equal(0x8040, cpu.ProgramCounter);
        Assert.Equal(new[] { "F:2000:E9" }, bus.Events);
    }

    [Theory]
    [InlineData(0x00, 0x2002)] // offset 0 -> 0x2002
    [InlineData(0x05, 0x2007)] // offset +5 -> 0x2007
    [InlineData(0x7F, 0x2081)] // offset +127 -> 0x2081
    [InlineData(0xFE, 0x2000)] // offset -2 -> 0x2000 (infinite loop JR $)
    [InlineData(0x80, 0x1F82)] // offset -128 -> 0x1F82
    public void JrUnconditional_DisplacesPcSignedAndReturns12T(byte offset, ushort expectedPc)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        bus.Memory[0x2000] = 0x18; // JR offset
        bus.Memory[0x2001] = offset;

        int tStates = cpu.Step();

        Assert.Equal(12, tStates);
        Assert.Equal(expectedPc, cpu.ProgramCounter);
        Assert.Equal(new[] { "F:2000:18", $"R:2001:{offset:X2}" }, bus.Events);

        VerifyMemptrHighByte(cpu, bus, (byte)(expectedPc >> 8));
    }

    [Theory]
    [InlineData(0x20, 0x40, false, 0x05, 0x2002, 7)]  // JR NZ, Z=1 (not taken)
    [InlineData(0x20, 0x00, true,  0x05, 0x2007, 12)] // JR NZ, Z=0 (taken)
    [InlineData(0x28, 0x00, false, 0x05, 0x2002, 7)]  // JR Z,  Z=0 (not taken)
    [InlineData(0x28, 0x40, true,  0x05, 0x2007, 12)] // JR Z,  Z=1 (taken)
    [InlineData(0x30, 0x01, false, 0x05, 0x2002, 7)]  // JR NC, C=1 (not taken)
    [InlineData(0x30, 0x00, true,  0x05, 0x2007, 12)] // JR NC, C=0 (taken)
    [InlineData(0x38, 0x00, false, 0x05, 0x2002, 7)]  // JR C,  C=0 (not taken)
    [InlineData(0x38, 0x01, true,  0x05, 0x2007, 12)] // JR C,  C=1 (taken)
    [InlineData(0x20, 0x00, true,  0xFE, 0x2000, 12)] // JR NZ, backward jump (taken)
    public void JrConditional_ExecutesCorrectly(byte opcode, byte flagState, bool shouldBranch, byte offset, ushort expectedPc, int expectedT)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.F = flagState;
        bus.Memory[0x2000] = opcode;
        bus.Memory[0x2001] = offset;

        int tStates = cpu.Step();

        Assert.Equal(expectedT, tStates);
        Assert.Equal(expectedPc, cpu.ProgramCounter);
        Assert.Equal(new[] { $"F:2000:{opcode:X2}", $"R:2001:{offset:X2}" }, bus.Events);

        if (shouldBranch)
        {
            VerifyMemptrHighByte(cpu, bus, (byte)(expectedPc >> 8));
        }
    }

    [Fact]
    public void CallUnconditional_PushesReturnAddressAndSetsPcAndReturns17T()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.StackPointer = 0xD000;
        bus.Memory[0x2000] = 0xCD; // CALL 0x4000
        bus.Memory[0x2001] = 0x00;
        bus.Memory[0x2002] = 0x40;

        int tStates = cpu.Step();

        Assert.Equal(17, tStates);
        Assert.Equal(0x4000, cpu.ProgramCounter);
        Assert.Equal(0xCFFE, cpu.StackPointer);
        Assert.Equal(0x20, bus.Memory[0xCFFF]); // high byte of return address 0x2003
        Assert.Equal(0x03, bus.Memory[0xCFFE]); // low byte of return address 0x2003
        Assert.Equal(new[] { "F:2000:CD", "R:2001:00", "R:2002:40", "W:CFFF:20", "W:CFFE:03" }, bus.Events);

        VerifyMemptrHighByte(cpu, bus, 0x40);
    }

    [Theory]
    [InlineData(0xC4, 0x40, false, 0x4000, 10)] // CALL NZ, Z=1 (not taken)
    [InlineData(0xC4, 0x00, true,  0x4000, 17)] // CALL NZ, Z=0 (taken)
    [InlineData(0xCC, 0x00, false, 0x4000, 10)] // CALL Z,  Z=0 (not taken)
    [InlineData(0xCC, 0x40, true,  0x4000, 17)] // CALL Z,  Z=1 (taken)
    [InlineData(0xD4, 0x01, false, 0x4000, 10)] // CALL NC, C=1 (not taken)
    [InlineData(0xD4, 0x00, true,  0x4000, 17)] // CALL NC, C=0 (taken)
    [InlineData(0xDC, 0x00, false, 0x4000, 10)] // CALL C,  C=0 (not taken)
    [InlineData(0xDC, 0x01, true,  0x4000, 17)] // CALL C,  C=1 (taken)
    [InlineData(0xE4, 0x04, false, 0x4000, 10)] // CALL PO, P/V=1 (not taken)
    [InlineData(0xE4, 0x00, true,  0x4000, 17)] // CALL PO, P/V=0 (taken)
    [InlineData(0xEC, 0x00, false, 0x4000, 10)] // CALL PE, P/V=0 (not taken)
    [InlineData(0xEC, 0x04, true,  0x4000, 17)] // CALL PE, P/V=1 (taken)
    [InlineData(0xF4, 0x80, false, 0x4000, 10)] // CALL P,  S=1 (not taken)
    [InlineData(0xF4, 0x00, true,  0x4000, 17)] // CALL P,  S=0 (taken)
    [InlineData(0xFC, 0x00, false, 0x4000, 10)] // CALL M,  S=0 (not taken)
    [InlineData(0xFC, 0x80, true,  0x4000, 17)] // CALL M,  S=1 (taken)
    public void CallConditional_ExecutesCorrectly(byte opcode, byte flagState, bool shouldCall, ushort target, int expectedT)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.F = flagState;
        cpu.StackPointer = 0xD000;
        bus.Memory[0x2000] = opcode;
        bus.Memory[0x2001] = (byte)target;
        bus.Memory[0x2002] = (byte)(target >> 8);

        int tStates = cpu.Step();

        Assert.Equal(expectedT, tStates);
        Assert.Equal(shouldCall ? target : (ushort)0x2003, cpu.ProgramCounter);
        Assert.Equal(shouldCall ? (ushort)0xCFFE : (ushort)0xD000, cpu.StackPointer);

        if (shouldCall)
        {
            Assert.Equal(0x20, bus.Memory[0xCFFF]);
            Assert.Equal(0x03, bus.Memory[0xCFFE]);
            Assert.Equal(new[] { $"F:2000:{opcode:X2}", $"R:2001:{(byte)target:X2}", $"R:2002:{(byte)(target >> 8):X2}", "W:CFFF:20", "W:CFFE:03" }, bus.Events);
            VerifyMemptrHighByte(cpu, bus, (byte)(target >> 8));
        }
        else
        {
            Assert.Equal(new[] { $"F:2000:{opcode:X2}", $"R:2001:{(byte)target:X2}", $"R:2002:{(byte)(target >> 8):X2}" }, bus.Events);
        }
    }

    [Fact]
    public void RetUnconditional_PopsPcFromStackAndReturns10T()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.StackPointer = 0xCFFE;
        bus.Memory[0xCFFE] = 0x34; // low byte
        bus.Memory[0xCFFF] = 0x12; // high byte
        bus.Memory[0x2000] = 0xC9; // RET

        int tStates = cpu.Step();

        Assert.Equal(10, tStates);
        Assert.Equal(0x1234, cpu.ProgramCounter);
        Assert.Equal(0xD000, cpu.StackPointer);
        Assert.Equal(new[] { "F:2000:C9", "R:CFFE:34", "R:CFFF:12" }, bus.Events);
    }

    [Theory]
    [InlineData(0xC0, 0x40, false, 5)]  // RET NZ, Z=1 (not taken)
    [InlineData(0xC0, 0x00, true,  11)] // RET NZ, Z=0 (taken)
    [InlineData(0xC8, 0x00, false, 5)]  // RET Z,  Z=0 (not taken)
    [InlineData(0xC8, 0x40, true,  11)] // RET Z,  Z=1 (taken)
    [InlineData(0xD0, 0x01, false, 5)]  // RET NC, C=1 (not taken)
    [InlineData(0xD0, 0x00, true,  11)] // RET NC, C=0 (taken)
    [InlineData(0xD8, 0x00, false, 5)]  // RET C,  C=0 (not taken)
    [InlineData(0xD8, 0x01, true,  11)] // RET C,  C=1 (taken)
    [InlineData(0xE0, 0x04, false, 5)]  // RET PO, P/V=1 (not taken)
    [InlineData(0xE0, 0x00, true,  11)] // RET PO, P/V=0 (taken)
    [InlineData(0xE8, 0x00, false, 5)]  // RET PE, P/V=0 (not taken)
    [InlineData(0xE8, 0x04, true,  11)] // RET PE, P/V=1 (taken)
    [InlineData(0xF0, 0x80, false, 5)]  // RET P,  S=1 (not taken)
    [InlineData(0xF0, 0x00, true,  11)] // RET P,  S=0 (taken)
    [InlineData(0xF8, 0x00, false, 5)]  // RET M,  S=0 (not taken)
    [InlineData(0xF8, 0x80, true,  11)] // RET M,  S=1 (taken)
    public void RetConditional_ExecutesCorrectly(byte opcode, byte flagState, bool shouldReturn, int expectedT)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.F = flagState;
        cpu.StackPointer = 0xCFFE;
        bus.Memory[0xCFFE] = 0x56;
        bus.Memory[0xCFFF] = 0x34;
        bus.Memory[0x2000] = opcode;

        int tStates = cpu.Step();

        Assert.Equal(expectedT, tStates);
        Assert.Equal(shouldReturn ? (ushort)0x3456 : (ushort)0x2001, cpu.ProgramCounter);
        Assert.Equal(shouldReturn ? (ushort)0xD000 : (ushort)0xCFFE, cpu.StackPointer);

        if (shouldReturn)
        {
            Assert.Equal(new[] { $"F:2000:{opcode:X2}", "R:CFFE:56", "R:CFFF:34" }, bus.Events);
        }
        else
        {
            Assert.Equal(new[] { $"F:2000:{opcode:X2}" }, bus.Events);
        }
    }

    [Theory]
    [InlineData(0xC7, 0x0000)] // RST 00h
    [InlineData(0xCF, 0x0008)] // RST 08h
    [InlineData(0xD7, 0x0010)] // RST 10h
    [InlineData(0xDF, 0x0018)] // RST 18h
    [InlineData(0xE7, 0x0020)] // RST 20h
    [InlineData(0xEF, 0x0028)] // RST 28h
    [InlineData(0xF7, 0x0030)] // RST 30h
    [InlineData(0xFF, 0x0038)] // RST 38h
    public void Rst_PushesReturnAddressAndJumpsToVectorAndReturns11T(byte opcode, ushort expectedTarget)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.StackPointer = 0xD000;
        bus.Memory[0x2000] = opcode;

        int tStates = cpu.Step();

        Assert.Equal(11, tStates);
        Assert.Equal(expectedTarget, cpu.ProgramCounter);
        Assert.Equal(0xCFFE, cpu.StackPointer);
        Assert.Equal(0x20, bus.Memory[0xCFFF]); // high byte of return address 0x2001
        Assert.Equal(0x01, bus.Memory[0xCFFE]); // low byte of return address 0x2001
        Assert.Equal(new[] { $"F:2000:{opcode:X2}", "W:CFFF:20", "W:CFFE:01" }, bus.Events);

        VerifyMemptrHighByte(cpu, bus, (byte)(expectedTarget >> 8));
    }

    [Fact]
    public void PcAndStackPointer_WrapAround65536Boundaries()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);

        // Test SP wrap on CALL: SP = 0x0000 -> SP - 1 = 0xFFFF, SP - 2 = 0xFFFE
        cpu.ProgramCounter = 0x1000;
        cpu.StackPointer = 0x0000;
        bus.Memory[0x1000] = 0xCD; // CALL 0x5000
        bus.Memory[0x1001] = 0x00;
        bus.Memory[0x1002] = 0x50;

        cpu.Step();
        Assert.Equal(0x5000, cpu.ProgramCounter);
        Assert.Equal(0xFFFE, cpu.StackPointer);
        Assert.Equal(0x10, bus.Memory[0xFFFF]); // high byte of 0x1003
        Assert.Equal(0x03, bus.Memory[0xFFFE]); // low byte of 0x1003

        // Test SP wrap on RET: SP = 0xFFFF -> pop low at 0xFFFF, SP becomes 0x0000, pop high at 0x0000, SP becomes 0x0001
        cpu.ProgramCounter = 0x5000;
        cpu.StackPointer = 0xFFFF;
        bus.Memory[0xFFFF] = 0x78;
        bus.Memory[0x0000] = 0x56;
        bus.Memory[0x5000] = 0xC9; // RET

        cpu.Step();
        Assert.Equal(0x5678, cpu.ProgramCounter);
        Assert.Equal(0x0001, cpu.StackPointer);

        // Test PC wrap on JR: startPC = 0xFFFF, JR +5 -> startPC+2=0x0001 + 5 = 0x0006
        cpu.ProgramCounter = 0xFFFF;
        bus.Memory[0xFFFF] = 0x18; // JR +5
        bus.Memory[0x0000] = 0x05;

        cpu.Step();
        Assert.Equal(0x0006, cpu.ProgramCounter);
    }

    [Theory]
    [InlineData("fetch",  false, false, false)]
    [InlineData("read",   true,  false, false)]
    [InlineData("write1", false, true,  false)]
    [InlineData("write2", false, false, true)]
    public void TerminalBusFailures_SetFaultedAndBlockSubsequentSteps(string scenario, bool failRead, bool failWrite1, bool failWrite2)
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);
        cpu.StackPointer = 0xD000;
        bus.Memory[0x2000] = 0xCD; // CALL 0x3000
        bus.Memory[0x2001] = 0x00;
        bus.Memory[0x2002] = 0x30;

        if (scenario == "fetch") bus.FailFetch = true;
        if (failRead) bus.FailReadAtAddress = 0x2001;
        if (failWrite1) bus.FailWriteAtAddress = 0xCFFF;
        if (failWrite2) bus.FailWriteAtAddress = 0xCFFE;

        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.True(cpu.IsFaulted);

        int eventCount = bus.Events.Count;
        Assert.Throws<InvalidOperationException>(() => cpu.Step());
        Assert.Equal(eventCount, bus.Events.Count);
    }

    [Fact]
    public void RetAndJpHl_LeaveMemptrUnchanged()
    {
        var bus = new RecordingBus();
        var cpu = NewCpu(bus);

        // First set MEMPTR to 0x2800 via JP 0x2800
        bus.Memory[0x2000] = 0xC3; // JP 0x2800
        bus.Memory[0x2001] = 0x00;
        bus.Memory[0x2002] = 0x28;
        cpu.Step();
        VerifyMemptrHighByte(cpu, bus, 0x28);

        // Now execute RET (C9); MEMPTR must remain 0x2800
        cpu.StackPointer = 0xCFFE;
        bus.Memory[0xCFFE] = 0x00;
        bus.Memory[0xCFFF] = 0x30;
        bus.Memory[cpu.ProgramCounter] = 0xC9; // RET
        cpu.Step();
        Assert.Equal(0x3000, cpu.ProgramCounter);
        VerifyMemptrHighByte(cpu, bus, 0x28);

        // Now execute JP (HL) (E9); MEMPTR must remain 0x2800
        cpu.H = 0x40; cpu.L = 0x00;
        bus.Memory[cpu.ProgramCounter] = 0xE9; // JP (HL)
        cpu.Step();
        Assert.Equal(0x4000, cpu.ProgramCounter);
        VerifyMemptrHighByte(cpu, bus, 0x28);
    }

    private static void VerifyMemptrHighByte(Z80 cpu, RecordingBus bus, byte expectedHighByte)
    {
        // Execute BIT 3, (HL) (opcode 0xCB 0x5E).
        // For BIT 3, (HL), bits 3 and 5 of F are populated from (MEMPTR >> 8) & 0x28.
        ushort pc = cpu.ProgramCounter;
        cpu.H = 0x50;
        cpu.L = 0x00;
        bus.Memory[pc] = 0xCB;
        bus.Memory[pc + 1] = 0x5E;
        bus.Memory[0x5000] = 0x00;

        cpu.Step();

        byte expectedBits = (byte)(expectedHighByte & 0x28);
        Assert.Equal(expectedBits, (byte)(cpu.F & 0x28));
    }

    private static Z80 NewCpu(RecordingBus bus) => new(bus)
    {
        ProgramCounter = 0x2000,
        StackPointer = 0xD000,
        Refresh = 0x7F,
        A = 0x00,
        B = 0x11, C = 0x22, D = 0x33, E = 0x44, H = 0x55, L = 0x66,
        F = 0x00
    };

    private sealed class RecordingBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public List<string> Events { get; } = new();
        public bool FailFetch { get; set; }
        public ushort? FailReadAtAddress { get; set; }
        public ushort? FailWriteAtAddress { get; set; }

        public byte FetchOpcode(ushort address)
        {
            if (FailFetch) throw new InvalidOperationException("Bus fetch failed.");
            byte value = Memory[address];
            Events.Add($"F:{address:X4}:{value:X2}");
            return value;
        }

        public byte ReadMemory(ushort address)
        {
            if (FailReadAtAddress.HasValue && FailReadAtAddress.Value == address)
                throw new InvalidOperationException("Bus read failed.");
            byte value = Memory[address];
            Events.Add($"R:{address:X4}:{value:X2}");
            return value;
        }

        public void WriteMemory(ushort address, byte value)
        {
            if (FailWriteAtAddress.HasValue && FailWriteAtAddress.Value == address)
                throw new InvalidOperationException("Bus write failed.");
            Memory[address] = value;
            Events.Add($"W:{address:X4}:{value:X2}");
        }

        public byte ReadIo(ushort port) => 0;
        public void WriteIo(ushort port, byte value) { }
        public byte AcknowledgeInterrupt() => 0;
    }
}