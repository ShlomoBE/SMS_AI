using Sms.Core.Cpu;
using Xunit;

namespace Sms.Core.Tests.Cpu;

public sealed class Z80InterruptTests
{
    #region Register I & R Operations

    [Fact]
    public void LdI_A_LoadsAccumulatorIntoI_9TStates_FlagsUnchanged()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            A = 0x42,
            I = 0x00,
            F = 0x95
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = 0x47; // LD I, A

        int tStates = cpu.Step();

        Assert.Equal(9, tStates);
        Assert.Equal(0x42, cpu.I);
        Assert.Equal(0x95, cpu.F);
        Assert.Equal(0x1002, cpu.ProgramCounter);
    }

    [Theory]
    [InlineData(0x80, 0x35, 0xB5)] // Bit 7 (0x80) kept, low 7 bits loaded from A (0x35)
    [InlineData(0x00, 0x35, 0x35)] // Bit 7 (0x00) kept, low 7 bits loaded from A (0x35)
    [InlineData(0x80, 0xFF, 0xFF)] // Bit 7 (0x80) kept, low 7 bits loaded from A (0x7F) -> 0xFF
    [InlineData(0x00, 0xFF, 0x7F)] // Bit 7 (0x00) kept, low 7 bits loaded from A (0x7F) -> 0x7F
    public void LdR_A_LoadsLow7BitsOfAccumulatorIntoR_PreservesBit7_9TStates_FlagsUnchanged(
        byte initialR, byte inA, byte expectedR)
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            Refresh = initialR,
            A = inA,
            F = 0x81
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = 0x4F; // LD R, A

        int tStates = cpu.Step();

        Assert.Equal(9, tStates);
        Assert.Equal(expectedR, cpu.R);
        Assert.Equal(expectedR, cpu.Refresh);
        Assert.Equal(0x81, cpu.F);
        Assert.Equal(0x1002, cpu.ProgramCounter);
    }

    [Theory]
    [InlineData(0x85, true, 0x01, 0x85)]  // S=1, Z=0, F5=0, F3=0, P/V(IFF2)=1, N=0, C=1 -> 0x85
    [InlineData(0x00, false, 0x00, 0x40)] // S=0, Z=1, F5=0, F3=0, P/V(IFF2)=0, N=0, C=0 -> 0x40
    [InlineData(0x28, true, 0x01, 0x2D)]  // S=0, Z=0, F5=1, F3=1, P/V(IFF2)=1, N=0, C=1 -> 0x2D
    public void LdA_I_LoadsIIntoA_SetsFlagsCorrectly_9TStates(
        byte initialI, bool iff2, byte inCarry, byte expectedF)
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            I = initialI,
            IFF2 = iff2,
            F = inCarry
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = 0x57; // LD A, I

        int tStates = cpu.Step();

        Assert.Equal(9, tStates);
        Assert.Equal(initialI, cpu.A);
        Assert.Equal(expectedF, cpu.F);
        Assert.Equal(0x1002, cpu.ProgramCounter);
    }

    [Theory]
    [InlineData(0x80, true, 0x01)]
    [InlineData(0x00, false, 0x00)]
    [InlineData(0x28, true, 0x00)]
    public void LdA_R_LoadsRIntoA_SetsFlagsCorrectly_9TStates(
        byte initialR, bool iff2, byte inCarry)
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            R = initialR,
            IFF2 = iff2,
            F = inCarry
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = 0x5F; // LD A, R

        // Step() increments Refresh twice (once for 0xED, once for 0x5F)
        byte rOnExecution = (byte)((initialR & 0x80) | ((initialR + 2) & 0x7F));

        int tStates = cpu.Step();

        Assert.Equal(9, tStates);
        Assert.Equal(rOnExecution, cpu.A);
        Assert.Equal(0x1002, cpu.ProgramCounter);

        byte expectedSign = (byte)(rOnExecution & 0x80);
        byte expectedZero = (byte)(rOnExecution == 0 ? 0x40 : 0);
        byte expectedF3F5 = (byte)(rOnExecution & 0x28);
        byte expectedPv = (byte)(iff2 ? 0x04 : 0);
        byte expectedFlags = (byte)(expectedSign | expectedZero | expectedF3F5 | expectedPv | (inCarry & 1));
        Assert.Equal(expectedFlags, cpu.F);
    }

    #endregion

    #region Return from Interrupt

    [Theory]
    [InlineData(0x45)] // RETN encodings
    [InlineData(0x55)]
    [InlineData(0x65)]
    [InlineData(0x75)]
    public void Retn_Encodings_RestoresIff1FromIff2_PopsPcFromStack_14TStates(byte subOpcode)
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            StackPointer = 0x2000,
            IFF1 = false,
            IFF2 = true
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = subOpcode;
        bus.Memory[0x2000] = 0x34; // Low byte of target PC
        bus.Memory[0x2001] = 0x12; // High byte of target PC

        int tStates = cpu.Step();

        Assert.Equal(14, tStates);
        Assert.True(cpu.IFF1);
        Assert.True(cpu.IFF2);
        Assert.Equal(0x1234, cpu.ProgramCounter);
        Assert.Equal(0x2002, cpu.StackPointer);
    }

    [Theory]
    [InlineData(0x4D)] // RETI encodings
    [InlineData(0x5D)]
    [InlineData(0x6D)]
    [InlineData(0x7D)]
    public void Reti_Encodings_PopsPcFromStack_PreservesIffState_14TStates(byte subOpcode)
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            StackPointer = 0x2000,
            IFF1 = false,
            IFF2 = false
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = subOpcode;
        bus.Memory[0x2000] = 0x78;
        bus.Memory[0x2001] = 0x56;

        int tStates = cpu.Step();

        Assert.Equal(14, tStates);
        Assert.False(cpu.IFF1);
        Assert.False(cpu.IFF2);
        Assert.Equal(0x5678, cpu.ProgramCounter);
        Assert.Equal(0x2002, cpu.StackPointer);
    }

    #endregion

    #region AcceptNmi

    [Fact]
    public void AcceptNmi_ExecutesSequence_PushesPc_SetsPc0x0066_CopiesIff1ToIff2_ClearsIff1_11TStates()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1234,
            StackPointer = 0x2000,
            IFF1 = true,
            IFF2 = false
        };

        int tStates = cpu.AcceptNmi();

        Assert.Equal(11, tStates);
        Assert.Equal(0x0066, cpu.ProgramCounter);
        Assert.Equal(0x1FFE, cpu.StackPointer);
        Assert.Equal(0x12, bus.Memory[0x1FFF]); // PC high byte
        Assert.Equal(0x34, bus.Memory[0x1FFE]); // PC low byte
        Assert.True(cpu.IFF2);  // Copies IFF1 (true) to IFF2
        Assert.False(cpu.IFF1); // Clears IFF1
    }

    [Fact]
    public void AcceptNmi_WhenHalted_ExitsHalt_AdvancesPcPastHalt_PushesPcSavesIff_11TStates()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            StackPointer = 0x2000,
            IsHalted = true,
            IFF1 = true,
            IFF2 = true
        };

        int tStates = cpu.AcceptNmi();

        Assert.Equal(11, tStates);
        Assert.False(cpu.IsHalted);
        Assert.Equal(0x0066, cpu.ProgramCounter);
        Assert.Equal(0x1FFE, cpu.StackPointer);
        Assert.Equal(0x10, bus.Memory[0x1FFF]); // PC high byte (0x1001 >> 8)
        Assert.Equal(0x01, bus.Memory[0x1FFE]); // PC low byte (0x1001 & 0xFF)
        Assert.True(cpu.IFF2);
        Assert.False(cpu.IFF1);
    }

    #endregion

    #region AcceptIrq & Interrupt Modes

    [Fact]
    public void AcceptIrq_WhenIff1False_Returns0_DoesNotAcknowledge()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            IFF1 = false,
            IFF2 = true,
            PendingEi = false
        };

        int tStates = cpu.AcceptIrq();

        Assert.Equal(0, tStates);
        Assert.Equal(0x1000, cpu.ProgramCounter);
        Assert.False(bus.AcknowledgeCalled);
    }

    [Fact]
    public void AcceptIrq_WhenPendingEiTrue_Returns0_InhibitsIrq()
    {
        var bus = new TestBus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            IFF1 = true,
            IFF2 = true,
            PendingEi = true
        };

        int tStates = cpu.AcceptIrq();

        Assert.Equal(0, tStates);
        Assert.Equal(0x1000, cpu.ProgramCounter);
        Assert.False(bus.AcknowledgeCalled);
    }

    [Fact]
    public void AcceptIrq_Mode0_RstOpcode_PushesPc_JumpsToRstTarget_13TStates()
    {
        var bus = new TestBus { InterruptVector = 0xFF }; // RST 38h
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1234,
            StackPointer = 0x2000,
            InterruptMode = 0,
            IFF1 = true,
            IFF2 = true
        };

        int tStates = cpu.AcceptIrq();

        Assert.Equal(13, tStates);
        Assert.True(bus.AcknowledgeCalled);
        Assert.False(cpu.IFF1);
        Assert.False(cpu.IFF2);
        Assert.Equal(0x0038, cpu.ProgramCounter);
        Assert.Equal(0x1FFE, cpu.StackPointer);
        Assert.Equal(0x12, bus.Memory[0x1FFF]);
        Assert.Equal(0x34, bus.Memory[0x1FFE]);
    }

    [Fact]
    public void AcceptIrq_Mode1_PushesPc_JumpsTo0x0038_13TStates()
    {
        var bus = new TestBus { InterruptVector = 0x00 };
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1234,
            StackPointer = 0x2000,
            InterruptMode = 1,
            IFF1 = true,
            IFF2 = true
        };

        int tStates = cpu.AcceptIrq();

        Assert.Equal(13, tStates);
        Assert.True(bus.AcknowledgeCalled);
        Assert.False(cpu.IFF1);
        Assert.False(cpu.IFF2);
        Assert.Equal(0x0038, cpu.ProgramCounter);
        Assert.Equal(0x1FFE, cpu.StackPointer);
        Assert.Equal(0x12, bus.Memory[0x1FFF]);
        Assert.Equal(0x34, bus.Memory[0x1FFE]);
    }

    [Fact]
    public void AcceptIrq_Mode2_ReadsVectorTable_PushesPc_JumpsToTarget_19TStates()
    {
        var bus = new TestBus { InterruptVector = 0x0E }; // Low bit 0 -> 0x0E
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1234,
            StackPointer = 0x2000,
            I = 0x80,
            InterruptMode = 2,
            IFF1 = true,
            IFF2 = true
        };

        // Table address = (0x80 << 8) | (0x0E & 0xFE) = 0x800E
        bus.Memory[0x800E] = 0x78; // Low byte of target
        bus.Memory[0x800F] = 0x56; // High byte of target

        int tStates = cpu.AcceptIrq();

        Assert.Equal(19, tStates);
        Assert.True(bus.AcknowledgeCalled);
        Assert.False(cpu.IFF1);
        Assert.False(cpu.IFF2);
        Assert.Equal(0x5678, cpu.ProgramCounter);
        Assert.Equal(0x1FFE, cpu.StackPointer);
        Assert.Equal(0x12, bus.Memory[0x1FFF]);
        Assert.Equal(0x34, bus.Memory[0x1FFE]);
    }

    [Fact]
    public void AcceptIrq_WhenHalted_ExitsHalt_AdvancesPcPastHalt_PushesPc_JumpsToVector_13TStates()
    {
        var bus = new TestBus { InterruptVector = 0x00 };
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            StackPointer = 0x2000,
            IsHalted = true,
            InterruptMode = 1,
            IFF1 = true,
            IFF2 = true
        };

        int tStates = cpu.AcceptIrq();

        Assert.Equal(13, tStates);
        Assert.False(cpu.IsHalted);
        Assert.False(cpu.IFF1);
        Assert.False(cpu.IFF2);
        Assert.Equal(0x0038, cpu.ProgramCounter);
        Assert.Equal(0x1FFE, cpu.StackPointer);
        Assert.Equal(0x10, bus.Memory[0x1FFF]); // 0x1001 high
        Assert.Equal(0x01, bus.Memory[0x1FFE]); // 0x1001 low
    }

    #endregion

    #region Helper Bus Class

    private sealed class TestBus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];
        public byte InterruptVector { get; set; } = 0x00;
        public bool AcknowledgeCalled { get; private set; }

        public byte FetchOpcode(ushort address) => Memory[address];
        public byte ReadMemory(ushort address) => Memory[address];
        public void WriteMemory(ushort address, byte value) => Memory[address] = value;
        public byte ReadIo(ushort port) => 0;
        public void WriteIo(ushort port, byte value) { }

        public byte AcknowledgeInterrupt()
        {
            AcknowledgeCalled = true;
            return InterruptVector;
        }
    }

    #endregion
}