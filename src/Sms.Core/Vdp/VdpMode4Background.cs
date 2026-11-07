namespace Sms.Core.Vdp;

/// <summary>
/// Pixel output metadata produced by the VDP background tile renderer.
/// </summary>
public readonly struct VdpPixel : IEquatable<VdpPixel>
{
    public byte ColorIndex { get; }
    public byte Palette { get; }
    public byte CramColor { get; }
    public bool Priority { get; }
    public bool Masked { get; }

    public VdpPixel(byte colorIndex, byte palette, byte cramColor, bool priority, bool masked)
    {
        ColorIndex = colorIndex;
        Palette = palette;
        CramColor = cramColor;
        Priority = priority;
        Masked = masked;
    }

    public bool Equals(VdpPixel other) =>
        ColorIndex == other.ColorIndex &&
        Palette == other.Palette &&
        CramColor == other.CramColor &&
        Priority == other.Priority &&
        Masked == other.Masked;

    public override bool Equals(object? obj) => obj is VdpPixel other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ColorIndex, Palette, CramColor, Priority, Masked);

    public static bool operator ==(VdpPixel left, VdpPixel right) => left.Equals(right);
    public static bool operator !=(VdpPixel left, VdpPixel right) => !left.Equals(right);
}

/// <summary>
/// SMS VDP Mode 4 background tile scanline renderer.
/// Hardware references: IDocs/VDP.txt §§7, 9; https://www.smspower.org/Development/VDPRegisters
/// </summary>
public sealed class VdpMode4Background
{
    /// <summary>
    /// Renders a single scanline (256 pixels) of the background layer into <paramref name="outputLine"/>.
    /// </summary>
    public static void RenderScanline(byte[] vram, byte[] cram, byte[] registers, int line, VdpPixel[] outputLine)
    {
        ArgumentNullException.ThrowIfNull(vram);
        ArgumentNullException.ThrowIfNull(cram);
        ArgumentNullException.ThrowIfNull(registers);
        ArgumentNullException.ThrowIfNull(outputLine);

        if (vram.Length < 0x4000)
            throw new ArgumentException("VRAM must be at least 16 KiB (16384 bytes).", nameof(vram));
        if (cram.Length < 32)
            throw new ArgumentException("CRAM must be at least 32 bytes.", nameof(cram));
        if (registers.Length < 10)
            throw new ArgumentException("Registers must contain at least 10 registers (R0-R9).", nameof(registers));
        if (outputLine.Length < 256)
            throw new ArgumentException("Output buffer must have at least 256 pixels.", nameof(outputLine));

        byte r0 = registers[0];
        byte r1 = registers[1];
        byte r2 = registers[2];
        byte r7 = registers[7];
        byte r8 = registers[8];
        byte r9 = registers[9];

        bool displayBlanked = (r1 & 0x40) == 0;
        bool lineOutOfBounds = line < 0 || line >= 192;

        byte backdropColorIndex = (byte)(r7 & 0x0F);
        byte backdropCramColor = (byte)(cram[16 + backdropColorIndex] & 0x3F);
        VdpPixel maskedPixel = new VdpPixel(backdropColorIndex, 1, backdropCramColor, false, true);

        if (displayBlanked || lineOutOfBounds)
        {
            for (int i = 0; i < 256; i++)
            {
                outputLine[i] = maskedPixel;
            }
            return;
        }

        bool leftColumnMask = (r0 & 0x20) != 0;
        bool hScrollLock = (r0 & 0x40) != 0 && line < 16;
        bool vScrollLock = (r0 & 0x80) != 0;

        int baseHScroll = hScrollLock ? 0 : r8;
        int baseVScroll = r9 % 224;

        int nameTableBase = (r2 & 0x0E) << 10;

        for (int x = 0; x < 256; x++)
        {
            if (leftColumnMask && x < 8)
            {
                outputLine[x] = maskedPixel;
                continue;
            }

            int effectiveVScroll = (vScrollLock && x >= 192) ? 0 : baseVScroll;
            int bgY = (line + effectiveVScroll) % 224;
            int tileRow = bgY / 8;
            int pixelY = bgY % 8;

            int bgX = (x - baseHScroll) & 0xFF;
            int tileCol = bgX / 8;
            int pixelX = bgX % 8;

            int entryAddr = (nameTableBase + (tileRow * 32 + tileCol) * 2) & 0x3FFF;
            ushort entry = (ushort)(vram[entryAddr] | (vram[(entryAddr + 1) & 0x3FFF] << 8));

            int tileIndex = entry & 0x01FF;
            bool hFlip = (entry & 0x0200) != 0;
            bool vFlip = (entry & 0x0400) != 0;
            byte palette = (byte)((entry >> 11) & 1);
            bool priority = (entry & 0x1000) != 0;

            int effY = vFlip ? (7 - pixelY) : pixelY;
            int effX = hFlip ? (7 - pixelX) : pixelX;

            int rowAddr = ((tileIndex * 32) + (effY * 4)) & 0x3FFF;

            byte p0 = vram[rowAddr];
            byte p1 = vram[(rowAddr + 1) & 0x3FFF];
            byte p2 = vram[(rowAddr + 2) & 0x3FFF];
            byte p3 = vram[(rowAddr + 3) & 0x3FFF];

            int shift = 7 - effX;
            byte colorIndex = (byte)(
                (((p3 >> shift) & 1) << 3) |
                (((p2 >> shift) & 1) << 2) |
                (((p1 >> shift) & 1) << 1) |
                ((p0 >> shift) & 1)
            );

            byte cramColor = (byte)(cram[palette * 16 + colorIndex] & 0x3F);

            outputLine[x] = new VdpPixel(colorIndex, palette, cramColor, priority, false);
        }
    }
}
