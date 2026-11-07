using Sms.Core.Cartridge;
using Sms.Core.Cpu;
using Sms.Core.Debug;
using Sms.Core.System;
using Xunit;

namespace Sms.Core.Tests.Debug;

public class SmsDebuggerTests
{
    private static byte[] CreateTestRom(ReadOnlySpan<byte> initialBytes)
    {
        byte[] rom = new byte[32768];
        initialBytes.CopyTo(rom);
        return rom;
    }

    #region Z80 Disassembler Tests

    [Fact]
    public void Disassembler_StandardInstructions_FormatsCorrectly()
    {
        // NOP
        Assert.Equal("0x0000: NOP", Z80Disassembler.DisassembleInstruction([0x00], 0x0000, out int size));
        Assert.Equal(1, size);

        // LD SP, $DFFF
        Assert.Equal("0x0001: LD SP, $DFFF", Z80Disassembler.DisassembleInstruction([0x31, 0xFF, 0xDF], 0x0001, out size));
        Assert.Equal(3, size);

        // JP $0080
        Assert.Equal("0x0004: JP $0080", Z80Disassembler.DisassembleInstruction([0xC3, 0x80, 0x00], 0x0004, out size));
        Assert.Equal(3, size);

        // JR $0010 (offset +14 at pc 0x0000 -> target 0x0010)
        Assert.Equal("0x0000: JR $0010", Z80Disassembler.DisassembleInstruction([0x18, 0x0E], 0x0000, out size));
        Assert.Equal(2, size);

        // JR NZ, $0002 (offset -2 at pc 0x0002 -> target 0x0002)
        Assert.Equal("0x0002: JR NZ, $0002", Z80Disassembler.DisassembleInstruction([0x20, 0xFE], 0x0002, out size));
        Assert.Equal(2, size);

        // LD A, $42
        Assert.Equal("0x0000: LD A, $42", Z80Disassembler.DisassembleInstruction([0x3E, 0x42], 0x0000, out size));
        Assert.Equal(2, size);

        // LD (HL), A
        Assert.Equal("0x0000: LD (HL), A", Z80Disassembler.DisassembleInstruction([0x77], 0x0000, out size));
        Assert.Equal(1, size);
    }

    [Fact]
    public void Disassembler_CbPrefixInstructions_FormatsCorrectly()
    {
        // RLC A
        Assert.Equal("0x0100: RLC A", Z80Disassembler.DisassembleInstruction([0xCB, 0x07], 0x0100, out int size));
        Assert.Equal(2, size);

        // BIT 0, A
        Assert.Equal("0x0100: BIT 0, A", Z80Disassembler.DisassembleInstruction([0xCB, 0x47], 0x0100, out size));
        Assert.Equal(2, size);

        // SET 0, (HL)
        Assert.Equal("0x0100: SET 0, (HL)", Z80Disassembler.DisassembleInstruction([0xCB, 0xC6], 0x0100, out size));
        Assert.Equal(2, size);

        // RES 0, C
        Assert.Equal("0x0100: RES 0, C", Z80Disassembler.DisassembleInstruction([0xCB, 0x81], 0x0100, out size));
        Assert.Equal(2, size);
    }

    [Fact]
    public void Disassembler_EdPrefixInstructions_FormatsCorrectly()
    {
        // RETI
        Assert.Equal("0x0038: RETI", Z80Disassembler.DisassembleInstruction([0xED, 0x4D], 0x0038, out int size));
        Assert.Equal(2, size);

        // RETN
        Assert.Equal("0x0066: RETN", Z80Disassembler.DisassembleInstruction([0xED, 0x45], 0x0066, out size));
        Assert.Equal(2, size);

        // LDIR
        Assert.Equal("0x0000: LDIR", Z80Disassembler.DisassembleInstruction([0xED, 0xB0], 0x0000, out size));
        Assert.Equal(2, size);

        // LD I, A
        Assert.Equal("0x0000: LD I, A", Z80Disassembler.DisassembleInstruction([0xED, 0x47], 0x0000, out size));
        Assert.Equal(2, size);

        // IM 1
        Assert.Equal("0x0000: IM 1", Z80Disassembler.DisassembleInstruction([0xED, 0x56], 0x0000, out size));
        Assert.Equal(2, size);

        // LD ($C000), BC
        Assert.Equal("0x0000: LD ($C000), BC", Z80Disassembler.DisassembleInstruction([0xED, 0x43, 0x00, 0xC0], 0x0000, out size));
        Assert.Equal(4, size);
    }

    [Fact]
    public void Disassembler_DdAndFdPrefixInstructions_FormatsCorrectly()
    {
        // LD IX, $C000
        Assert.Equal("0x0000: LD IX, $C000", Z80Disassembler.DisassembleInstruction([0xDD, 0x21, 0x00, 0xC0], 0x0000, out int size));
        Assert.Equal(4, size);

        // LD (IX+$05), A
        Assert.Equal("0x0000: LD (IX+$05), A", Z80Disassembler.DisassembleInstruction([0xDD, 0x77, 0x05], 0x0000, out size));
        Assert.Equal(3, size);

        // LD (IX-$05), A (0xFB is -5 as sbyte)
        Assert.Equal("0x0000: LD (IX-$05), A", Z80Disassembler.DisassembleInstruction([0xDD, 0x77, 0xFB], 0x0000, out size));
        Assert.Equal(3, size);

        // ADD IX, BC
        Assert.Equal("0x0000: ADD IX, BC", Z80Disassembler.DisassembleInstruction([0xDD, 0x09], 0x0000, out size));
        Assert.Equal(2, size);

        // LD IY, $1050
        Assert.Equal("0x0000: LD IY, $1050", Z80Disassembler.DisassembleInstruction([0xFD, 0x21, 0x50, 0x10], 0x0000, out size));
        Assert.Equal(4, size);

        // ADD A, (IY+$02)
        Assert.Equal("0x0000: ADD A, (IY+$02)", Z80Disassembler.DisassembleInstruction([0xFD, 0x86, 0x02], 0x0000, out size));
        Assert.Equal(3, size);

        // RLC (IX+$05)
        Assert.Equal("0x0000: RLC (IX+$05)", Z80Disassembler.DisassembleInstruction([0xDD, 0xCB, 0x05, 0x06], 0x0000, out size));
        Assert.Equal(4, size);

        // BIT 0, (IY+$02)
        Assert.Equal("0x0000: BIT 0, (IY+$02)", Z80Disassembler.DisassembleInstruction([0xFD, 0xCB, 0x02, 0x46], 0x0000, out size));
        Assert.Equal(4, size);
    }

    [Fact]
    public void Disassembler_DisassembleRange_DisassemblesMultipleInstructions()
    {
        byte[] code = [0x00, 0x31, 0xFF, 0xDF, 0xC3, 0x80, 0x00];
        byte[] rom = CreateTestRom(code);
        var sys = new SmsSystem(rom);
        var debugger = new SmsDebugger(sys);

        List<string> lines = Z80Disassembler.DisassembleRange(sys.Bus, 0x0000, 3);

        Assert.Equal(3, lines.Count);
        Assert.Equal("0x0000: NOP", lines[0]);
        Assert.Equal("0x0001: LD SP, $DFFF", lines[1]);
        Assert.Equal("0x0004: JP $0080", lines[2]);
    }

    #endregion

    #region Side-Effect-Free Memory & State Inspection

    [Fact]
    public void Debugger_ReadMemoryByteSideEffectFree_PreservesState()
    {
        byte[] rom = CreateTestRom([0x12, 0x34, 0x56, 0x78]);
        var sys = new SmsSystem(rom);
        var debugger = new SmsDebugger(sys);

        sys.Bus.MemoryControl = 0x04; // Bios disabled
        byte val0 = debugger.ReadMemoryByteSideEffectFree(0x0000);
        byte val1 = debugger.ReadMemoryByteSideEffectFree(0x0001);

        Assert.Equal(0x12, val0);
        Assert.Equal(0x34, val1);
        Assert.Equal(0x04, sys.Bus.MemoryControl);

        // Verify VDP control latch status remains unchanged
        var vdpSnapBefore = debugger.GetVdpSnapshot();
        debugger.ReadMemoryByteSideEffectFree(0x0002);
        var vdpSnapAfter = debugger.GetVdpSnapshot();

        Assert.Equal(vdpSnapBefore.SecondControlByte, vdpSnapAfter.SecondControlByte);
        Assert.Equal(vdpSnapBefore.Address, vdpSnapAfter.Address);
    }

    #endregion

    #region VDP Tile Pattern Decoding

    [Fact]
    public void Debugger_DecodeTilePatterns_Decodes512TilesCorrectly()
    {
        var sys = new SmsSystem(CreateTestRom([0x00]));
        var debugger = new SmsDebugger(sys);

        // Setup tile 0 row 0 in VRAM:
        // Byte 0 = 0x80 (b0 = 1000 0000)
        // Byte 1 = 0x00 (b1 = 0000 0000)
        // Byte 2 = 0x00 (b2 = 0000 0000)
        // Byte 3 = 0x00 (b3 = 0000 0000)
        // This gives pixel 0 palette index = 1, pixels 1..7 palette index = 0.
        sys.Vdp.WriteControl(0x00);
        sys.Vdp.WriteControl(0x40); // Set VRAM address 0x0000 for write
        sys.Vdp.WriteData(0x80);
        sys.Vdp.WriteData(0x00);
        sys.Vdp.WriteData(0x00);
        sys.Vdp.WriteData(0x00);

        // Setup CRAM color 0 = 0x00 (Black), color 1 = 0x03 (Red)
        sys.Vdp.WriteControl(0x00);
        sys.Vdp.WriteControl(0xC0); // Set CRAM address 0 for write
        sys.Vdp.WriteData(0x00); // Color 0
        sys.Vdp.WriteData(0x03); // Color 1 (Red: R=3*85=255, G=0, B=0 -> 0xFFFF0000)

        uint[][] tiles = debugger.DecodeTilePatterns();

        Assert.Equal(512, tiles.Length);
        Assert.Equal(64, tiles[0].Length);

        uint expectedRedArgb = SmsSystem.ConvertCramToArgb(0x03);
        uint expectedBlackArgb = SmsSystem.ConvertCramToArgb(0x00);

        // Pixel (0,0) -> tile[0][0] should be Red
        Assert.Equal(expectedRedArgb, tiles[0][0]);
        // Pixel (1,0) -> tile[0][1] should be Black
        Assert.Equal(expectedBlackArgb, tiles[0][1]);
    }

    #endregion

    #region Breakpoint & Watchpoint Manager

    [Fact]
    public void Debugger_PcBreakpoint_PausesExecution()
    {
        // 0x0000: NOP
        // 0x0001: NOP
        // 0x0002: NOP
        byte[] rom = CreateTestRom([0x00, 0x00, 0x00]);
        var sys = new SmsSystem(rom);
        var debugger = new SmsDebugger(sys);

        debugger.AddPcBreakpoint(0x0002);

        debugger.StepFrameWithBreakpoints(out bool hit);

        Assert.True(hit);
        Assert.True(debugger.BreakpointHit);
        Assert.Equal((ushort)0x0002, debugger.HitAddress);
        Assert.Equal(0x0002, sys.Cpu.ProgramCounter);
    }

    [Fact]
    public void Debugger_MemoryWatchpoint_DetectsWritesAndReads()
    {
        // 0x0000: LD HL, $C000 (21 00 C0)
        // 0x0003: LD A, $42 (3E 42)
        // 0x0005: LD (HL), A (77)
        byte[] rom = CreateTestRom([0x21, 0x00, 0xC0, 0x3E, 0x42, 0x77]);
        var sys = new SmsSystem(rom);
        var debugger = new SmsDebugger(sys);

        debugger.AddMemoryWatchpoint(0xC000, write: true);

        debugger.StepInstruction(); // LD HL, $C000
        debugger.StepInstruction(); // LD A, $42
        Assert.False(debugger.BreakpointHit);

        debugger.StepInstruction(); // LD (HL), A
        Assert.True(debugger.BreakpointHit);
        Assert.Equal((ushort)0xC000, debugger.HitAddress);
        Assert.Equal(0x42, sys.Ram[0x0000]);
    }

    #endregion

    #region Snapshot Immutability & Accuracy

    [Fact]
    public void Debugger_GetCpuSnapshot_AccurateAndImmutable()
    {
        var sys = new SmsSystem(CreateTestRom([0x00]));
        var debugger = new SmsDebugger(sys);

        sys.Cpu.ProgramCounter = 0x1234;
        sys.Cpu.StackPointer = 0xDFFF;
        sys.Cpu.A = 0xAA;
        sys.Cpu.F = 0x55;
        sys.Cpu.B = 0x11;
        sys.Cpu.C = 0x22;
        sys.Cpu.D = 0x33;
        sys.Cpu.E = 0x44;
        sys.Cpu.H = 0x55;
        sys.Cpu.L = 0x66;
        sys.Cpu.IX = 0x7788;
        sys.Cpu.IY = 0x99AA;
        sys.Cpu.APrime = 0xBB;
        sys.Cpu.FPrime = 0xCC;

        CpuSnapshot snapshot = debugger.GetCpuSnapshot();

        Assert.Equal(0x1234, snapshot.PC);
        Assert.Equal(0xDFFF, snapshot.SP);
        Assert.Equal(0xAA, snapshot.A);
        Assert.Equal(0x55, snapshot.F);
        Assert.Equal(0x11, snapshot.B);
        Assert.Equal(0x22, snapshot.C);
        Assert.Equal(0x33, snapshot.D);
        Assert.Equal(0x44, snapshot.E);
        Assert.Equal(0x55, snapshot.H);
        Assert.Equal(0x66, snapshot.L);
        Assert.Equal(0x7788, snapshot.IX);
        Assert.Equal(0x99AA, snapshot.IY);
        Assert.Equal(0xBB, snapshot.APrime);
        Assert.Equal(0xCC, snapshot.FPrime);
        Assert.Equal(0xBBCC, snapshot.AFPrime);

        // Mutate CPU
        sys.Cpu.ProgramCounter = 0x9999;
        sys.Cpu.A = 0x00;

        // Snapshot remains unchanged (immutable)
        Assert.Equal(0x1234, snapshot.PC);
        Assert.Equal(0xAA, snapshot.A);
    }

    [Fact]
    public void Debugger_GetVdpSnapshot_AccurateAndImmutable()
    {
        var sys = new SmsSystem(CreateTestRom([0x00]));
        var debugger = new SmsDebugger(sys);

        VdpSnapshot snapshot = debugger.GetVdpSnapshot();

        Assert.Equal(0x4000, snapshot.Vram.Count);
        Assert.Equal(32, snapshot.Cram.Count);
        Assert.Equal(11, snapshot.Registers.Count);

        // Modify VRAM
        sys.Vdp.WriteControl(0x00);
        sys.Vdp.WriteControl(0x40);
        sys.Vdp.WriteData(0xFF);

        // Snapshot remains unchanged (immutable copy)
        Assert.Equal(0x00, snapshot.Vram[0]);
    }

    #endregion
}
