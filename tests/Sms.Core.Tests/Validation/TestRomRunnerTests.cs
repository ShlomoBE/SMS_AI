using System;
using System.Security.Cryptography;
using Sms.Core.Cartridge;
using Sms.Core.Cpu;
using Sms.Core.System;
using Xunit;

namespace Sms.Core.Tests.Validation;

public sealed class TestRomRunnerTests
{
    private static byte[] CreateTestProgram(byte[] z80Code)
    {
        byte[] rom = new byte[32768];
        z80Code.CopyTo(rom, 0);
        return rom;
    }

    [Fact]
    public void TestRom_AluAndFlagCompliance()
    {
        // Program:
        // 0x0000: LD A, $15 (3E 15)
        // 0x0002: ADD A, $27 (C6 27) -> A = 0x3C
        // 0x0004: DAA (27)          -> A = 0x42
        // 0x0005: HALT (76)
        byte[] code = [0x3E, 0x15, 0xC6, 0x27, 0x27, 0x76];
        var sys = new SmsSystem(CreateTestProgram(code));

        for (int i = 0; i < 20 && !sys.Cpu.IsHalted; i++)
        {
            sys.StepInstruction();
        }

        Assert.True(sys.Cpu.IsHalted);
        Assert.Equal(0x42, sys.Cpu.A);
    }

    [Fact]
    public void TestRom_LdirBlockTransferCompliance()
    {
        // Program:
        // LD HL, $0010 (21 10 00)
        // LD DE, $C000 (11 00 C0)
        // LD BC, $0004 (01 04 00)
        // LDIR (ED B0)
        // HALT (76)
        byte[] code = new byte[32];
        byte[] prog = [0x21, 0x10, 0x00, 0x11, 0x00, 0xC0, 0x01, 0x04, 0x00, 0xED, 0xB0, 0x76];
        prog.CopyTo(code, 0);
        code[0x10] = 0xDE;
        code[0x11] = 0xAD;
        code[0x12] = 0xBE;
        code[0x13] = 0xEF;

        var sys = new SmsSystem(CreateTestProgram(code));

        for (int i = 0; i < 50 && !sys.Cpu.IsHalted; i++)
        {
            sys.StepInstruction();
        }

        Assert.True(sys.Cpu.IsHalted);
        Assert.Equal(0xDE, sys.Ram[0x0000]);
        Assert.Equal(0xAD, sys.Ram[0x0001]);
        Assert.Equal(0xBE, sys.Ram[0x0002]);
        Assert.Equal(0xEF, sys.Ram[0x0003]);
    }
}
