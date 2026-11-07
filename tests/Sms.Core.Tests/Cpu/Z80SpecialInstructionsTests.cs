using Sms.Core.Cpu;
using Xunit;

namespace Sms.Core.Tests.Cpu;

public sealed class Z80SpecialInstructionsTests
{
    #region Register Exchanges & Stack EX

    [Fact]
    public void ExDeHl_SwapsDeAndHl_PreservesFlags_4TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            D = 0x12, E = 0x34,
            H = 0x56, L = 0x78,
            F = 0x9A
        };
        bus.Memory[0x1000] = 0xEB; // EX DE, HL

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.Equal(0x56, cpu.D); Assert.Equal(0x78, cpu.E);
        Assert.Equal(0x12, cpu.H); Assert.Equal(0x34, cpu.L);
        Assert.Equal(0x9A, cpu.F);
        Assert.Equal(0x1001, cpu.ProgramCounter);
        Assert.Equal(1, bus.Count);
        Assert.Equal('F', bus.Kind1); Assert.Equal(0x1000, bus.Address1);
    }

    [Fact]
    public void ExAfAfPrime_SwapsAfAndAfPrime_4TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            A = 0x12, F = 0x34,
            APrime = 0x56, FPrime = 0x78
        };
        bus.Memory[0x1000] = 0x08; // EX AF, AF'

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.Equal(0x56, cpu.A); Assert.Equal(0x78, cpu.F);
        Assert.Equal(0x12, cpu.APrime); Assert.Equal(0x34, cpu.FPrime);
        Assert.Equal(0x1001, cpu.ProgramCounter);
        Assert.Equal(1, bus.Count);
        Assert.Equal('F', bus.Kind1); Assert.Equal(0x1000, bus.Address1);
    }

    [Fact]
    public void Exx_SwapsBcDeHlWithAlternates_4TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            B = 0x11, C = 0x22, D = 0x33, E = 0x44, H = 0x55, L = 0x66,
            BPrime = 0xAA, CPrime = 0xBB, DPrime = 0xCC, EPrime = 0xDD, HPrime = 0xEE, LPrime = 0xFF,
            F = 0x99
        };
        bus.Memory[0x1000] = 0xD9; // EXX

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.Equal(0xAA, cpu.B); Assert.Equal(0xBB, cpu.C);
        Assert.Equal(0xCC, cpu.D); Assert.Equal(0xDD, cpu.E);
        Assert.Equal(0xEE, cpu.H); Assert.Equal(0xFF, cpu.L);

        Assert.Equal(0x11, cpu.BPrime); Assert.Equal(0x22, cpu.CPrime);
        Assert.Equal(0x33, cpu.DPrime); Assert.Equal(0x44, cpu.EPrime);
        Assert.Equal(0x55, cpu.HPrime); Assert.Equal(0x66, cpu.LPrime);
        Assert.Equal(0x99, cpu.F);
        Assert.Equal(0x1001, cpu.ProgramCounter);
    }

    [Fact]
    public void ExSpHl_SwapsHlWithStackMemory_CorrectBusOrderAnd19TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            StackPointer = 0x2000,
            H = 0x12, L = 0x34
        };
        bus.Memory[0x1000] = 0xE3; // EX (SP), HL
        bus.Memory[0x2000] = 0xAA; // (SP)
        bus.Memory[0x2001] = 0xBB; // (SP+1)

        int tStates = cpu.Step();

        Assert.Equal(19, tStates);
        Assert.Equal(0xBB, cpu.H);
        Assert.Equal(0xAA, cpu.L);
        Assert.Equal(0x34, bus.Memory[0x2000]);
        Assert.Equal(0x12, bus.Memory[0x2001]);
        Assert.Equal(0x1001, cpu.ProgramCounter);

        // Verify exact bus transaction order: Fetch, Read(SP), Read(SP+1), Write(SP+1), Write(SP)
        Assert.Equal(5, bus.Count);
        Assert.Equal('F', bus.Events[0].Kind); Assert.Equal(0x1000, bus.Events[0].Address);
        Assert.Equal('R', bus.Events[1].Kind); Assert.Equal(0x2000, bus.Events[1].Address);
        Assert.Equal('R', bus.Events[2].Kind); Assert.Equal(0x2001, bus.Events[2].Address);
        Assert.Equal('W', bus.Events[3].Kind); Assert.Equal(0x2001, bus.Events[3].Address); Assert.Equal(0x12, bus.Events[3].Value);
        Assert.Equal('W', bus.Events[4].Kind); Assert.Equal(0x2000, bus.Events[4].Address); Assert.Equal(0x34, bus.Events[4].Value);
    }

    #endregion

    #region 16-bit ALU

    [Theory]
    [InlineData(0x09, 0x1234, 0x5678, 0x68AC, false, false, 0x28)] // ADD HL, BC
    [InlineData(0x19, 0x0FFF, 0x0001, 0x1000, true, false, 0x00)]  // ADD HL, DE (H carry from bit 11)
    [InlineData(0x29, 0x8000, 0x8000, 0x0000, false, true, 0x00)]   // ADD HL, HL (C carry from bit 15)
    [InlineData(0x39, 0x0800, 0x0800, 0x1000, true, false, 0x00)]  // ADD HL, SP (H carry from bit 11)
    public void AddHl_AllRegisterPairs_CorrectFlagsAnd11TStates(
        byte opcode, ushort hl, ushort ssVal, ushort expectedHl, bool expectedH, bool expectedC, byte expectedF3F5)
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            H = (byte)(hl >> 8), L = (byte)hl,
            F = 0xC4 // S=1, Z=1, P/V=1 set to ensure they are preserved
        };

        switch (opcode)
        {
            case 0x09: cpu.B = (byte)(ssVal >> 8); cpu.C = (byte)ssVal; break;
            case 0x19: cpu.D = (byte)(ssVal >> 8); cpu.E = (byte)ssVal; break;
            case 0x29: break; // HL self-add
            case 0x39: cpu.StackPointer = ssVal; break;
        }

        bus.Memory[0x1000] = opcode;

        int tStates = cpu.Step();

        Assert.Equal(11, tStates);
        Assert.Equal(expectedHl, (ushort)((cpu.H << 8) | cpu.L));
        Assert.Equal(0xC0, cpu.F & 0xC0); // S and Z preserved
        Assert.Equal(0x04, cpu.F & 0x04); // P/V preserved
        Assert.Equal(0x00, cpu.F & 0x02); // N cleared
        Assert.Equal(expectedH ? 0x10 : 0, cpu.F & 0x10);
        Assert.Equal(expectedC ? 0x01 : 0, cpu.F & 0x01);
        Assert.Equal(expectedF3F5, cpu.F & 0x28);
    }

    [Theory]
    [InlineData(0x4A, 0x1234, 0x5678, 0, 0x68AC, false, false, false, false, false)] // ADC HL, BC carry=0
    [InlineData(0x5A, 0x7FFF, 0x0000, 1, 0x8000, true, false, true, true, false)]   // ADC HL, DE overflow & sign
    [InlineData(0x6A, 0x1234, 0x1234, 0, 0x2468, false, false, false, false, false)] // ADC HL, HL self-add
    [InlineData(0x7A, 0xFFFF, 0x0000, 1, 0x0000, false, true, true, false, true)]   // ADC HL, SP zero & carry
    public void AdcHl_AllRegisterPairs_CorrectFlagsAnd15TStates(
        byte subOpcode, ushort hl, ushort ssVal, int inCarry, ushort expectedHl,
        bool expectedS, bool expectedZ, bool expectedH, bool expectedOverflow, bool expectedC)
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            H = (byte)(hl >> 8), L = (byte)hl,
            F = (byte)inCarry
        };

        switch (subOpcode)
        {
            case 0x4A: cpu.B = (byte)(ssVal >> 8); cpu.C = (byte)ssVal; break;
            case 0x5A: cpu.D = (byte)(ssVal >> 8); cpu.E = (byte)ssVal; break;
            case 0x6A: break;
            case 0x7A: cpu.StackPointer = ssVal; break;
        }

        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = subOpcode;

        int tStates = cpu.Step();

        Assert.Equal(15, tStates);
        Assert.Equal(expectedHl, (ushort)((cpu.H << 8) | cpu.L));
        Assert.Equal(expectedS ? 0x80 : 0, cpu.F & 0x80);
        Assert.Equal(expectedZ ? 0x40 : 0, cpu.F & 0x40);
        Assert.Equal(expectedH ? 0x10 : 0, cpu.F & 0x10);
        Assert.Equal(expectedOverflow ? 0x04 : 0, cpu.F & 0x04);
        Assert.Equal(0x00, cpu.F & 0x02); // N = 0
        Assert.Equal(expectedC ? 0x01 : 0, cpu.F & 0x01);
        Assert.Equal((expectedHl >> 8) & 0x28, cpu.F & 0x28); // F3/F5 from high byte
        Assert.Equal(0x1002, cpu.ProgramCounter);
    }

    [Theory]
    [InlineData(0x42, 0x5678, 0x1234, 0, 0x4444, false, false, false, false, false)] // SBC HL, BC borrow=0
    [InlineData(0x52, 0x8000, 0x0001, 0, 0x7FFF, false, false, true, true, false)]   // SBC HL, DE overflow
    [InlineData(0x62, 0x5678, 0x0000, 0, 0x0000, false, true, false, false, false)]  // SBC HL, HL self-subtract
    [InlineData(0x72, 0x0000, 0x0001, 0, 0xFFFF, true, false, true, false, true)]   // SBC HL, SP borrow out
    public void SbcHl_AllRegisterPairs_CorrectFlagsAnd15TStates(
        byte subOpcode, ushort hl, ushort ssVal, int inCarry, ushort expectedHl,
        bool expectedS, bool expectedZ, bool expectedH, bool expectedOverflow, bool expectedC)
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            H = (byte)(hl >> 8), L = (byte)hl,
            F = (byte)inCarry
        };

        switch (subOpcode)
        {
            case 0x42: cpu.B = (byte)(ssVal >> 8); cpu.C = (byte)ssVal; break;
            case 0x52: cpu.D = (byte)(ssVal >> 8); cpu.E = (byte)ssVal; break;
            case 0x62: break;
            case 0x72: cpu.StackPointer = ssVal; break;
        }

        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = subOpcode;

        int tStates = cpu.Step();

        Assert.Equal(15, tStates);
        Assert.Equal(expectedHl, (ushort)((cpu.H << 8) | cpu.L));
        Assert.Equal(expectedS ? 0x80 : 0, cpu.F & 0x80);
        Assert.Equal(expectedZ ? 0x40 : 0, cpu.F & 0x40);
        Assert.Equal(expectedH ? 0x10 : 0, cpu.F & 0x10);
        Assert.Equal(expectedOverflow ? 0x04 : 0, cpu.F & 0x04);
        Assert.Equal(0x02, cpu.F & 0x02); // N = 1
        Assert.Equal(expectedC ? 0x01 : 0, cpu.F & 0x01);
        Assert.Equal((expectedHl >> 8) & 0x28, cpu.F & 0x28);
        Assert.Equal(0x1002, cpu.ProgramCounter);
    }

    #endregion

    #region Accumulator & Flag Operations

    [Fact]
    public void Daa_FullBcdMatrix_ExhaustiveVerification()
    {
        for (int a = 0; a <= 255; a++)
        for (int c = 0; c <= 1; c++)
        for (int h = 0; h <= 1; h++)
        for (int n = 0; n <= 1; n++)
        {
            var bus = new Bus();
            var cpu = new Z80(bus)
            {
                ProgramCounter = 0x1000,
                A = (byte)a,
                F = (byte)((c != 0 ? 0x01 : 0) | (h != 0 ? 0x10 : 0) | (n != 0 ? 0x02 : 0))
            };
            bus.Memory[0x1000] = 0x27; // DAA

            int tStates = cpu.Step();

            Assert.Equal(4, tStates);

            // Reference BCD calculation:
            byte expectedA = (byte)a;
            byte correction = 0;
            bool outC = c != 0;
            bool outH = false;

            if (h != 0 || (n == 0 && (expectedA & 0x0F) > 9))
            {
                correction |= 0x06;
            }

            if (outC || (n == 0 && expectedA > 0x99))
            {
                correction |= 0x60;
                outC = true;
            }

            if (n != 0)
            {
                outH = h != 0 && (expectedA & 0x0F) < 6;
                expectedA = unchecked((byte)(expectedA - correction));
            }
            else
            {
                outH = (expectedA & 0x0F) > 9;
                expectedA = unchecked((byte)(expectedA + correction));
            }

            int parity = System.Numerics.BitOperations.PopCount((uint)expectedA) % 2 == 0 ? 0x04 : 0;
            byte expectedF = (byte)(
                (expectedA & 0x80) |
                (expectedA == 0 ? 0x40 : 0) |
                (expectedA & 0x28) |
                (outH ? 0x10 : 0) |
                parity |
                (n != 0 ? 0x02 : 0) |
                (outC ? 0x01 : 0)
            );

            Assert.Equal(expectedA, cpu.A);
            Assert.Equal(expectedF, cpu.F);
        }
    }

    [Fact]
    public void Cpl_InvertsA_SetsHAndN_PreservesS_Z_PV_C_4TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            A = 0x55,
            F = 0xC5 // S=1, Z=1, P/V=1, C=1
        };
        bus.Memory[0x1000] = 0x2F; // CPL

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.Equal(0xAA, cpu.A);
        Assert.Equal(0xC5, cpu.F & 0xC5); // S, Z, P/V, C preserved
        Assert.Equal(0x10, cpu.F & 0x10); // H = 1
        Assert.Equal(0x02, cpu.F & 0x02); // N = 1
        Assert.Equal(0x28, cpu.F & 0x28); // F3/F5 from 0xAA (bits 3 & 5 are set)
    }

    [Theory]
    [InlineData(0x44, 0x00, 0x00, true, false, false, false)]  // NEG A=0 -> A=0, Z=1, C=0, H=0, PV=0
    [InlineData(0x4C, 0x01, 0xFF, false, true, true, false)]   // NEG A=1 -> A=FF, C=1, H=1, PV=0
    [InlineData(0x54, 0x80, 0x80, false, true, false, true)]   // NEG A=80 -> A=80, C=1, H=0, PV=1 (overflow)
    [InlineData(0x5C, 0x0F, 0xF1, false, true, true, false)]   // NEG A=0F -> A=F1, C=1, H=1
    [InlineData(0x64, 0x10, 0xF0, false, true, false, false)]  // NEG A=10 -> A=F0
    [InlineData(0x6C, 0x7F, 0x81, false, true, true, false)]   // NEG A=7F -> A=81
    [InlineData(0x74, 0xFE, 0x02, false, true, true, false)]  // NEG A=FE -> A=02
    [InlineData(0x7C, 0xFF, 0x01, false, true, true, false)]   // NEG A=FF -> A=01
    public void Neg_All8Encodings_NegatesA_CorrectFlagsAnd8TStates(
        byte subOpcode, byte inA, byte expectedA, bool expectedZ, bool expectedC, bool expectedH, bool expectedPV)
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            A = inA
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = subOpcode;

        int tStates = cpu.Step();

        Assert.Equal(8, tStates);
        Assert.Equal(expectedA, cpu.A);
        Assert.Equal(expectedA & 0x80, cpu.F & 0x80); // S
        Assert.Equal(expectedZ ? 0x40 : 0, cpu.F & 0x40);
        Assert.Equal(expectedH ? 0x10 : 0, cpu.F & 0x10);
        Assert.Equal(expectedPV ? 0x04 : 0, cpu.F & 0x04);
        Assert.Equal(0x02, cpu.F & 0x02); // N = 1
        Assert.Equal(expectedC ? 0x01 : 0, cpu.F & 0x01);
        Assert.Equal(expectedA & 0x28, cpu.F & 0x28);
    }

    [Theory]
    [InlineData(0, 1, false)] // C=0 -> C=1, H=0 (prev C=0)
    [InlineData(1, 0, true)]  // C=1 -> C=0, H=1 (prev C=1)
    public void Ccf_InvertsCarry_SetsHToPrevCarry_ClearsN_4TStates(int inC, int expectedC, bool expectedH)
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            A = 0x28, // bits 3 and 5 set for F3/F5
            F = (byte)(0xC4 | inC) // S=1, Z=1, P/V=1
        };
        bus.Memory[0x1000] = 0x3F; // CCF

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.Equal(0xC4, cpu.F & 0xC4); // S, Z, P/V preserved
        Assert.Equal(expectedH ? 0x10 : 0, cpu.F & 0x10);
        Assert.Equal(0x00, cpu.F & 0x02); // N = 0
        Assert.Equal(expectedC, cpu.F & 0x01);
        Assert.Equal(0x28, cpu.F & 0x28);
    }

    [Fact]
    public void Scf_SetsCarry_ClearsHAndN_4TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            A = 0x28,
            F = 0x12 // H=1, N=1
        };
        bus.Memory[0x1000] = 0x37; // SCF

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.Equal(0x01, cpu.F & 0x01); // C = 1
        Assert.Equal(0x00, cpu.F & 0x10); // H = 0
        Assert.Equal(0x00, cpu.F & 0x02); // N = 0
        Assert.Equal(0x28, cpu.F & 0x28);
    }

    #endregion

    #region Machine Control & Interrupts

    [Fact]
    public void Halt_SetsIsHaltedTrue_RepeatedStepRefetchesHaltAndIncrementsRefresh_4TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            Refresh = 0x10
        };
        bus.Memory[0x1000] = 0x76; // HALT

        // 1st step executes HALT
        int tStates1 = cpu.Step();
        Assert.Equal(4, tStates1);
        Assert.True(cpu.IsHalted);
        Assert.Equal(0x1001, cpu.ProgramCounter);
        Assert.Equal(0x11, cpu.Refresh);
        Assert.Equal(1, bus.Count);
        Assert.Equal(0x1000, bus.Events[0].Address);

        // 2nd step while halted re-fetches HALT at PC-1 (0x1000)
        bus.ResetEvents();
        int tStates2 = cpu.Step();
        Assert.Equal(4, tStates2);
        Assert.True(cpu.IsHalted);
        Assert.Equal(0x1001, cpu.ProgramCounter); // PC does not advance
        Assert.Equal(0x12, cpu.Refresh);
        Assert.Equal(1, bus.Count);
        Assert.Equal('F', bus.Events[0].Kind);
        Assert.Equal(0x1000, bus.Events[0].Address);

        // Clear halt manually (simulating interrupt response) and execute next instruction
        cpu.IsHalted = false;
        bus.Memory[0x1001] = 0x00; // NOP
        bus.ResetEvents();

        int tStates3 = cpu.Step();
        Assert.Equal(4, tStates3);
        Assert.False(cpu.IsHalted);
        Assert.Equal(0x1002, cpu.ProgramCounter);
        Assert.Equal(1, bus.Count);
        Assert.Equal(0x1001, bus.Events[0].Address);
    }

    [Fact]
    public void Di_ClearsIff1Iff2PendingEi_4TStates()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            IFF1 = true,
            IFF2 = true,
            PendingEi = true
        };
        bus.Memory[0x1000] = 0xF3; // DI

        int tStates = cpu.Step();

        Assert.Equal(4, tStates);
        Assert.False(cpu.IFF1);
        Assert.False(cpu.IFF2);
        Assert.False(cpu.PendingEi);
        Assert.Equal(0x1001, cpu.ProgramCounter);
    }

    [Fact]
    public void Ei_SetsIff1Iff2PendingEi_DelayedByOneInstruction()
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            IFF1 = false,
            IFF2 = false
        };
        bus.Memory[0x1000] = 0xFB; // EI
        bus.Memory[0x1001] = 0x00; // NOP (following instruction)
        bus.Memory[0x1002] = 0x00; // NOP

        // Step 1: EI
        int tStates1 = cpu.Step();
        Assert.Equal(4, tStates1);
        Assert.True(cpu.IFF1);
        Assert.True(cpu.IFF2);
        Assert.True(cpu.PendingEi);

        // Step 2: NOP immediately after EI (PendingEi remains true during step, cleared at end)
        int tStates2 = cpu.Step();
        Assert.Equal(4, tStates2);
        Assert.True(cpu.IFF1);
        Assert.True(cpu.IFF2);
        Assert.False(cpu.PendingEi);

        // Step 3: Next NOP (PendingEi remains false)
        int tStates3 = cpu.Step();
        Assert.Equal(4, tStates3);
        Assert.False(cpu.PendingEi);
    }

    [Theory]
    [InlineData(0x46, 0)] // IM 0
    [InlineData(0x66, 0)] // IM 0
    [InlineData(0x56, 1)] // IM 1
    [InlineData(0x76, 1)] // IM 1
    [InlineData(0x5E, 2)] // IM 2
    [InlineData(0x7E, 2)] // IM 2
    public void Im_AllModeEncodings_SetsInterruptMode_8TStates(byte subOpcode, byte expectedMode)
    {
        var bus = new Bus();
        var cpu = new Z80(bus)
        {
            ProgramCounter = 0x1000,
            InterruptMode = 99
        };
        bus.Memory[0x1000] = 0xED;
        bus.Memory[0x1001] = subOpcode;

        int tStates = cpu.Step();

        Assert.Equal(8, tStates);
        Assert.Equal(expectedMode, cpu.InterruptMode);
        Assert.Equal(0x1002, cpu.ProgramCounter);
        Assert.Equal(2, bus.Count);
        Assert.Equal('F', bus.Events[0].Kind); Assert.Equal(0x1000, bus.Events[0].Address);
        Assert.Equal('F', bus.Events[1].Kind); Assert.Equal(0x1001, bus.Events[1].Address);
    }

    #endregion

    #region Helper Bus Class

    private sealed class Bus : IZ80Bus
    {
        public byte[] Memory { get; } = new byte[65536];

        public readonly record struct Event(char Kind, ushort Address, byte Value);
        public List<Event> Events { get; } = [];

        public int Count => Events.Count;
        public char Kind1 => Events.Count > 0 ? Events[0].Kind : '\0';
        public ushort Address1 => Events.Count > 0 ? Events[0].Address : (ushort)0;

        public void ResetEvents() => Events.Clear();

        public byte FetchOpcode(ushort address)
        {
            Events.Add(new Event('F', address, Memory[address]));
            return Memory[address];
        }

        public byte ReadMemory(ushort address)
        {
            Events.Add(new Event('R', address, Memory[address]));
            return Memory[address];
        }

        public void WriteMemory(ushort address, byte value)
        {
            Events.Add(new Event('W', address, value));
            Memory[address] = value;
        }

        public byte ReadIo(ushort port) => throw new InvalidOperationException("Unexpected I/O read");
        public void WriteIo(ushort port, byte value) => throw new InvalidOperationException("Unexpected I/O write");
        public byte AcknowledgeInterrupt() => throw new InvalidOperationException("Unexpected interrupt acknowledge");
    }

    #endregion
}
