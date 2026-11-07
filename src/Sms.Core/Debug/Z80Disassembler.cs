using System.Text;
using Sms.Core.Cpu;

namespace Sms.Core.Debug;

/// <summary>
/// Z80 Instruction Disassembler.
/// Converts raw Z80 machine code bytes into human-readable assembly mnemonic strings.
/// </summary>
public static class Z80Disassembler
{
    private static readonly string[] Regs = ["B", "C", "D", "E", "H", "L", "(HL)", "A"];
    private static readonly string[] Conditions = ["NZ", "Z", "NC", "C", "PO", "PE", "P", "M"];
    private static readonly string[] AluOps = ["ADD A,", "ADC A,", "SUB", "SBC A,", "AND", "XOR", "OR", "CP"];
    private static readonly string[] CbOps = ["RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL"];
    private static readonly string[] RegPairs = ["BC", "DE", "HL", "SP"];
    private static readonly string[] RegPairsAF = ["BC", "DE", "HL", "AF"];

    /// <summary>
    /// Disassembles Z80 instruction opcodes at the specified memory address.
    /// </summary>
    /// <param name="code">Byte span containing the instruction sequence.</param>
    /// <param name="pc">Program Counter address where the instruction resides.</param>
    /// <param name="instructionSize">Receives the size of the disassembled instruction in bytes.</param>
    /// <returns>Formatted disassembly string e.g. "0x0000: NOP".</returns>
    public static string DisassembleInstruction(ReadOnlySpan<byte> code, ushort pc, out int instructionSize)
    {
        if (code.IsEmpty)
        {
            instructionSize = 1;
            return $"0x{pc:X4}: NOP";
        }

        byte op0 = code[0];

        if (op0 == 0xCB)
        {
            if (code.Length < 2)
            {
                instructionSize = 1;
                return $"0x{pc:X4}: DB $CB";
            }
            instructionSize = 2;
            string mnemonic = DisassembleCb(code[1]);
            return $"0x{pc:X4}: {mnemonic}";
        }

        if (op0 == 0xED)
        {
            if (code.Length < 2)
            {
                instructionSize = 1;
                return $"0x{pc:X4}: DB $ED";
            }
            instructionSize = 2;
            string mnemonic = DisassembleEd(code, pc, out instructionSize);
            return $"0x{pc:X4}: {mnemonic}";
        }

        if (op0 is 0xDD or 0xFD)
        {
            if (code.Length < 2)
            {
                instructionSize = 1;
                return $"0x{pc:X4}: DB ${op0:X2}";
            }
            string mnemonic = DisassembleIndexed(code, pc, out instructionSize);
            return $"0x{pc:X4}: {mnemonic}";
        }

        string unprefixMnemonic = DisassembleUnprefixed(code, pc, out instructionSize);
        return $"0x{pc:X4}: {unprefixMnemonic}";
    }

    /// <summary>
    /// Disassembles a sequence of Z80 instructions starting at <paramref name="startAddress"/>.
    /// </summary>
    public static List<string> DisassembleRange(IZ80Bus bus, ushort startAddress, int instructionCount)
    {
        ArgumentNullException.ThrowIfNull(bus);
        var result = new List<string>(Math.Max(0, instructionCount));
        ushort currentPc = startAddress;
        byte[] buffer = new byte[4];

        for (int i = 0; i < instructionCount; i++)
        {
            for (int b = 0; b < 4; b++)
            {
                buffer[b] = bus.ReadMemory(unchecked((ushort)(currentPc + b)));
            }

            string line = DisassembleInstruction(buffer, currentPc, out int size);
            result.Add(line);
            currentPc = unchecked((ushort)(currentPc + size));
        }

        return result;
    }

    private static string DisassembleUnprefixed(ReadOnlySpan<byte> code, ushort pc, out int instructionSize)
    {
        byte op = code[0];

        switch (op)
        {
            case 0x00: instructionSize = 1; return "NOP";
            case 0x07: instructionSize = 1; return "RLCA";
            case 0x08: instructionSize = 1; return "EX AF, AF'";
            case 0x0F: instructionSize = 1; return "RRCA";
            case 0x17: instructionSize = 1; return "RLA";
            case 0x1F: instructionSize = 1; return "RRA";
            case 0x27: instructionSize = 1; return "DAA";
            case 0x2F: instructionSize = 1; return "CPL";
            case 0x37: instructionSize = 1; return "SCF";
            case 0x3F: instructionSize = 1; return "CCF";
            case 0x76: instructionSize = 1; return "HALT";
            case 0xC9: instructionSize = 1; return "RET";
            case 0xD9: instructionSize = 1; return "EXX";
            case 0xE3: instructionSize = 1; return "EX (SP), HL";
            case 0xE9: instructionSize = 1; return "JP (HL)";
            case 0xEB: instructionSize = 1; return "EX DE, HL";
            case 0xF3: instructionSize = 1; return "DI";
            case 0xF9: instructionSize = 1; return "LD SP, HL";
            case 0xFB: instructionSize = 1; return "EI";

            // Relative branches: DJNZ e, JR e, JR cc, e
            case 0x10:
            case 0x18:
            case 0x20: case 0x28: case 0x30: case 0x38:
            {
                if (code.Length < 2) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 2;
                sbyte offset = (sbyte)code[1];
                ushort target = unchecked((ushort)(pc + 2 + offset));
                if (op == 0x10) return $"DJNZ ${target:X4}";
                if (op == 0x18) return $"JR ${target:X4}";
                int cc = (op >> 3) & 3;
                return $"JR {Conditions[cc]}, ${target:X4}";
            }

            // 16-bit Immediate loads: LD rp, nn
            case 0x01: case 0x11: case 0x21: case 0x31:
            {
                if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 3;
                ushort nn = GetU16(code, 1);
                int rp = (op >> 4) & 3;
                return $"LD {RegPairs[rp]}, ${nn:X4}";
            }

            // Indirect A loads/stores
            case 0x02: instructionSize = 1; return "LD (BC), A";
            case 0x12: instructionSize = 1; return "LD (DE), A";
            case 0x0A: instructionSize = 1; return "LD A, (BC)";
            case 0x1A: instructionSize = 1; return "LD A, (DE)";

            // Direct memory 16-bit loads/stores
            case 0x22:
            {
                if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 3;
                return $"LD (${GetU16(code, 1):X4}), HL";
            }
            case 0x2A:
            {
                if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 3;
                return $"LD HL, (${GetU16(code, 1):X4})";
            }
            case 0x32:
            {
                if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 3;
                return $"LD (${GetU16(code, 1):X4}), A";
            }
            case 0x3A:
            {
                if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 3;
                return $"LD A, (${GetU16(code, 1):X4})";
            }

            // 16-bit INC/DEC
            case 0x03: case 0x13: case 0x23: case 0x33:
                instructionSize = 1; return $"INC {RegPairs[(op >> 4) & 3]}";
            case 0x0B: case 0x1B: case 0x2B: case 0x3B:
                instructionSize = 1; return $"DEC {RegPairs[(op >> 4) & 3]}";

            // ADD HL, rp
            case 0x09: case 0x19: case 0x29: case 0x39:
                instructionSize = 1; return $"ADD HL, {RegPairs[(op >> 4) & 3]}";

            // 8-bit INC/DEC
            case 0x04: case 0x0C: case 0x14: case 0x1C: case 0x24: case 0x2C: case 0x34: case 0x3C:
                instructionSize = 1; return $"INC {Regs[(op >> 3) & 7]}";
            case 0x05: case 0x0D: case 0x15: case 0x1D: case 0x25: case 0x2D: case 0x35: case 0x3D:
                instructionSize = 1; return $"DEC {Regs[(op >> 3) & 7]}";

            // 8-bit Immediate load: LD r, n
            case 0x06: case 0x0E: case 0x16: case 0x1E: case 0x26: case 0x2E: case 0x36: case 0x3E:
            {
                if (code.Length < 2) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 2;
                return $"LD {Regs[(op >> 3) & 7]}, ${code[1]:X2}";
            }

            // I/O Immediate
            case 0xD3:
            {
                if (code.Length < 2) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 2;
                return $"OUT (${code[1]:X2}), A";
            }
            case 0xDB:
            {
                if (code.Length < 2) { instructionSize = 1; return $"DB ${op:X2}"; }
                instructionSize = 2;
                return $"IN A, (${code[1]:X2})";
            }
        }

        // 8-bit Register loads: LD r, r' (0x40 - 0x7F)
        if (op is >= 0x40 and <= 0x7F)
        {
            instructionSize = 1;
            int dst = (op >> 3) & 7;
            int src = op & 7;
            return $"LD {Regs[dst]}, {Regs[src]}";
        }

        // 8-bit ALU with register/memory: ADD A, r ... CP r (0x80 - 0xBF)
        if (op is >= 0x80 and <= 0xBF)
        {
            instructionSize = 1;
            int alu = (op >> 3) & 7;
            int src = op & 7;
            return $"{AluOps[alu]} {Regs[src]}";
        }

        // 8-bit ALU with immediate: ADD A, n ... CP n (0xC6, 0xCE, 0xD6, 0xDE, 0xE6, 0xEE, 0xF6, 0xFE)
        if ((op & 0xC7) == 0xC6)
        {
            if (code.Length < 2) { instructionSize = 1; return $"DB ${op:X2}"; }
            instructionSize = 2;
            int alu = (op >> 3) & 7;
            return $"{AluOps[alu]} ${code[1]:X2}";
        }

        // RET cc (0xC0, 0xC8, 0xD0, 0xD8, 0xE0, 0xE8, 0xF0, 0xF8)
        if ((op & 0xC7) == 0xC0)
        {
            instructionSize = 1;
            int cc = (op >> 3) & 7;
            return $"RET {Conditions[cc]}";
        }

        // POP rp (0xC1, 0xD1, 0xE1, 0xF1)
        if ((op & 0xCF) == 0xC1)
        {
            instructionSize = 1;
            int rp = (op >> 4) & 3;
            return $"POP {RegPairsAF[rp]}";
        }

        // PUSH rp (0xC5, 0xD5, 0xE5, 0xF5)
        if ((op & 0xCF) == 0xC5)
        {
            instructionSize = 1;
            int rp = (op >> 4) & 3;
            return $"PUSH {RegPairsAF[rp]}";
        }

        // JP cc, nn (0xC2, 0xCA, 0xD2, 0xDA, 0xE2, 0xEA, 0xF2, 0xFA)
        if ((op & 0xC7) == 0xC2)
        {
            if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
            instructionSize = 3;
            int cc = (op >> 3) & 7;
            return $"JP {Conditions[cc]}, ${GetU16(code, 1):X4}";
        }

        // JP nn (0xC3)
        if (op == 0xC3)
        {
            if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
            instructionSize = 3;
            return $"JP ${GetU16(code, 1):X4}";
        }

        // CALL cc, nn (0xC4, 0xCC, 0xD4, 0xDC, 0xE4, 0xEC, 0xF4, 0xFC)
        if ((op & 0xC7) == 0xC4)
        {
            if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
            instructionSize = 3;
            int cc = (op >> 3) & 7;
            return $"CALL {Conditions[cc]}, ${GetU16(code, 1):X4}";
        }

        // CALL nn (0xCD)
        if (op == 0xCD)
        {
            if (code.Length < 3) { instructionSize = 1; return $"DB ${op:X2}"; }
            instructionSize = 3;
            return $"CALL ${GetU16(code, 1):X4}";
        }

        // RST p (0xC7, 0xCF, 0xD7, 0xDF, 0xE7, 0xEF, 0xF7, 0xFF)
        if ((op & 0xC7) == 0xC7)
        {
            instructionSize = 1;
            int target = op & 0x38;
            return $"RST ${target:X2}";
        }

        instructionSize = 1;
        return $"DB ${op:X2}";
    }

    private static string DisassembleCb(byte op)
    {
        int reg = op & 7;
        string rName = Regs[reg];

        if (op < 0x40)
        {
            int cb = (op >> 3) & 7;
            return $"{CbOps[cb]} {rName}";
        }
        if (op < 0x80)
        {
            int bit = (op >> 3) & 7;
            return $"BIT {bit}, {rName}";
        }
        if (op < 0xC0)
        {
            int bit = (op >> 3) & 7;
            return $"RES {bit}, {rName}";
        }
        {
            int bit = (op >> 3) & 7;
            return $"SET {bit}, {rName}";
        }
    }

    private static string DisassembleEd(ReadOnlySpan<byte> code, ushort pc, out int instructionSize)
    {
        instructionSize = 2;
        byte op = code[1];

        switch (op)
        {
            case 0x40: return "IN B, (C)";
            case 0x41: return "OUT (C), B";
            case 0x42: return "SBC HL, BC";
            case 0x44: case 0x4C: case 0x54: case 0x5C: case 0x64: case 0x6C: case 0x74: case 0x7C: return "NEG";
            case 0x45: case 0x55: case 0x65: case 0x75: case 0x5D: case 0x6D: case 0x7D: return "RETN";
            case 0x46: case 0x4E: case 0x66: case 0x6E: return "IM 0";
            case 0x47: return "LD I, A";
            case 0x48: return "IN C, (C)";
            case 0x49: return "OUT (C), C";
            case 0x4A: return "ADC HL, BC";
            case 0x4D: return "RETI";
            case 0x4F: return "LD R, A";
            case 0x50: return "IN D, (C)";
            case 0x51: return "OUT (C), D";
            case 0x52: return "SBC HL, DE";
            case 0x56: case 0x76: return "IM 1";
            case 0x57: return "LD A, I";
            case 0x58: return "IN E, (C)";
            case 0x59: return "OUT (C), E";
            case 0x5A: return "ADC HL, DE";
            case 0x5E: case 0x7E: return "IM 2";
            case 0x5F: return "LD A, R";
            case 0x60: return "IN H, (C)";
            case 0x61: return "OUT (C), H";
            case 0x62: return "SBC HL, HL";
            case 0x67: return "RRD";
            case 0x68: return "IN L, (C)";
            case 0x69: return "OUT (C), L";
            case 0x6A: return "ADC HL, HL";
            case 0x6F: return "RLD";
            case 0x70: return "IN (C)";
            case 0x71: return "OUT (C), 0";
            case 0x72: return "SBC HL, SP";
            case 0x78: return "IN A, (C)";
            case 0x79: return "OUT (C), A";
            case 0x7A: return "ADC HL, SP";

            case 0xA0: return "LDI";
            case 0xA1: return "CPI";
            case 0xA2: return "INI";
            case 0xA3: return "OUTI";
            case 0xA8: return "LDD";
            case 0xA9: return "CPD";
            case 0xAA: return "IND";
            case 0xAB: return "OUTD";
            case 0xB0: return "LDIR";
            case 0xB1: return "CPIR";
            case 0xB2: return "INIR";
            case 0xB3: return "OTIR";
            case 0xB8: return "LDDR";
            case 0xB9: return "CPDR";
            case 0xBA: return "INDR";
            case 0xBB: return "OTDR";
        }

        // LD ($XXXX), rp (0x43, 0x53, 0x63, 0x73)
        if ((op & 0xC7) == 0x43)
        {
            if (code.Length < 4) { instructionSize = 2; return $"DB $ED, ${op:X2}"; }
            instructionSize = 4;
            ushort addr = GetU16(code, 2);
            int rp = (op >> 4) & 3;
            return $"LD (${addr:X4}), {RegPairs[rp]}";
        }

        // LD rp, ($XXXX) (0x4B, 0x5B, 0x6B, 0x7B)
        if ((op & 0xC7) == 0x4B)
        {
            if (code.Length < 4) { instructionSize = 2; return $"DB $ED, ${op:X2}"; }
            instructionSize = 4;
            ushort addr = GetU16(code, 2);
            int rp = (op >> 4) & 3;
            return $"LD {RegPairs[rp]}, (${addr:X4})";
        }

        return $"DB $ED, ${op:X2}";
    }

    private static string DisassembleIndexed(ReadOnlySpan<byte> code, ushort pc, out int instructionSize)
    {
        byte prefix = code[0];
        string regName = prefix == 0xDD ? "IX" : "IY";
        byte op = code[1];

        if (op == 0xCB)
        {
            if (code.Length < 4)
            {
                instructionSize = 2;
                return $"DB ${prefix:X2}, $CB";
            }
            instructionSize = 4;
            sbyte d = (sbyte)code[2];
            byte cbOp = code[3];
            string offsetStr = FormatOffset(d);
            string memRef = $"({regName}{offsetStr})";

            if (cbOp < 0x40)
            {
                int cb = (cbOp >> 3) & 7;
                return $"{CbOps[cb]} {memRef}";
            }
            if (cbOp < 0x80)
            {
                int bit = (cbOp >> 3) & 7;
                return $"BIT {bit}, {memRef}";
            }
            if (cbOp < 0xC0)
            {
                int bit = (cbOp >> 3) & 7;
                return $"RES {bit}, {memRef}";
            }
            {
                int bit = (cbOp >> 3) & 7;
                return $"SET {bit}, {memRef}";
            }
        }

        switch (op)
        {
            case 0x09: instructionSize = 2; return $"ADD {regName}, BC";
            case 0x19: instructionSize = 2; return $"ADD {regName}, DE";
            case 0x29: instructionSize = 2; return $"ADD {regName}, {regName}";
            case 0x39: instructionSize = 2; return $"ADD {regName}, SP";
            case 0x23: instructionSize = 2; return $"INC {regName}";
            case 0x2B: instructionSize = 2; return $"DEC {regName}";
            case 0xE1: instructionSize = 2; return $"POP {regName}";
            case 0xE5: instructionSize = 2; return $"PUSH {regName}";
            case 0xE3: instructionSize = 2; return $"EX (SP), {regName}";
            case 0xE9: instructionSize = 2; return $"JP ({regName})";
            case 0xF9: instructionSize = 2; return $"LD SP, {regName}";

            case 0x21:
            {
                if (code.Length < 4) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
                instructionSize = 4;
                return $"LD {regName}, ${GetU16(code, 2):X4}";
            }
            case 0x22:
            {
                if (code.Length < 4) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
                instructionSize = 4;
                return $"LD (${GetU16(code, 2):X4}), {regName}";
            }
            case 0x2A:
            {
                if (code.Length < 4) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
                instructionSize = 4;
                return $"LD {regName}, (${GetU16(code, 2):X4})";
            }
            case 0x34:
            {
                if (code.Length < 3) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
                instructionSize = 3;
                return $"INC ({regName}{FormatOffset((sbyte)code[2])})";
            }
            case 0x35:
            {
                if (code.Length < 3) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
                instructionSize = 3;
                return $"DEC ({regName}{FormatOffset((sbyte)code[2])})";
            }
            case 0x36:
            {
                if (code.Length < 4) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
                instructionSize = 4;
                return $"LD ({regName}{FormatOffset((sbyte)code[2])}), ${code[3]:X2}";
            }
        }

        // Indexed instructions with (HL) substituted (r = 6)
        if ((op & 0xC0) == 0x40 && op != 0x76)
        {
            int dst = (op >> 3) & 7;
            int src = op & 7;
            if (dst == 6 || src == 6)
            {
                if (code.Length < 3) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
                instructionSize = 3;
                string offsetStr = FormatOffset((sbyte)code[2]);
                string memRef = $"({regName}{offsetStr})";
                string dstName = dst == 6 ? memRef : Regs[dst];
                string srcName = src == 6 ? memRef : Regs[src];
                return $"LD {dstName}, {srcName}";
            }
        }

        // Indexed ALU operations (ADD A, (IX+d), etc.)
        if (op is >= 0x80 and <= 0xBF && (op & 7) == 6)
        {
            if (code.Length < 3) { instructionSize = 2; return $"DB ${prefix:X2}, ${op:X2}"; }
            instructionSize = 3;
            int alu = (op >> 3) & 7;
            string offsetStr = FormatOffset((sbyte)code[2]);
            return $"{AluOps[alu]} ({regName}{offsetStr})";
        }

        // Fallback to disassembling the opcode without index prefix
        string sub = DisassembleUnprefixed(code.Slice(1), unchecked((ushort)(pc + 1)), out int subSize);
        instructionSize = 1 + subSize;
        return sub;
    }

    private static ushort GetU16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

    private static string FormatOffset(sbyte d)
    {
        if (d >= 0)
            return $"+${d:X2}";
        else
            return $"-${-d:X2}";
    }
}
