namespace Sms.Core.Vdp;

/// <summary>
/// SMS VDP Sprite Engine &amp; Compositor.
/// Hardware references: IDocs/VDP.txt §§7, 8, 9; https://www.smspower.org/Development/VDPRegisters
/// </summary>
public sealed class VdpSpriteEngine
{
    /// <summary>
    /// Processes sprites for scanline <paramref name="line"/> and composes sprite pixels with <paramref name="bgLine"/> into <paramref name="finalLine"/>.
    /// Outputs <paramref name="overflow"/> (set if more than 8 visible sprites were found on the scanline)
    /// and <paramref name="collision"/> (set if two opaque sprite pixels overlapped on the scanline).
    /// </summary>
    public static void ProcessScanline(
        byte[] vram,
        byte[] cram,
        byte[] registers,
        int line,
        VdpPixel[] bgLine,
        VdpPixel[] finalLine,
        out bool overflow,
        out bool collision)
    {
        ArgumentNullException.ThrowIfNull(vram);
        ArgumentNullException.ThrowIfNull(cram);
        ArgumentNullException.ThrowIfNull(registers);
        ArgumentNullException.ThrowIfNull(bgLine);
        ArgumentNullException.ThrowIfNull(finalLine);

        if (vram.Length < 0x4000)
            throw new ArgumentException("VRAM must be at least 16 KiB (16384 bytes).", nameof(vram));
        if (cram.Length < 32)
            throw new ArgumentException("CRAM must be at least 32 bytes.", nameof(cram));
        if (registers.Length < 10)
            throw new ArgumentException("Registers must contain at least 10 registers (R0-R9).", nameof(registers));
        if (bgLine.Length < 256)
            throw new ArgumentException("Background buffer must have at least 256 pixels.", nameof(bgLine));
        if (finalLine.Length < 256)
            throw new ArgumentException("Final buffer must have at least 256 pixels.", nameof(finalLine));

        overflow = false;
        collision = false;

        byte r0 = registers[0];
        byte r1 = registers[1];
        byte r5 = registers[5];
        byte r6 = registers[6];
        byte r7 = registers[7];

        bool displayBlanked = (r1 & 0x40) == 0;
        bool lineOutOfBounds = line < 0 || line >= 192;

        byte backdropColorIndex = (byte)(r7 & 0x0F);
        byte backdropCramColor = (byte)(cram[16 + backdropColorIndex] & 0x3F);
        VdpPixel maskedPixel = new VdpPixel(backdropColorIndex, 1, backdropCramColor, false, true);

        if (displayBlanked || lineOutOfBounds)
        {
            for (int i = 0; i < 256; i++)
            {
                finalLine[i] = maskedPixel;
            }
            return;
        }

        bool leftColumnMask = (r0 & 0x20) != 0;
        bool shiftLeft32 = (r0 & 0x08) != 0;
        bool is8x16 = (r1 & 0x02) != 0;
        bool isZoomed = (r1 & 0x01) != 0;

        int baseSpriteHeight = is8x16 ? 16 : 8;
        int effectiveHeight = isZoomed ? (baseSpriteHeight * 2) : baseSpriteHeight;

        int satBase = (r5 & 0x7E) << 7;

        // Collect up to 8 visible sprites on this scanline
        Span<int> visibleSprites = stackalloc int[8];
        int visibleCount = 0;

        for (int i = 0; i < 64; i++)
        {
            byte yByte = vram[(satBase + i) & 0x3FFF];
            if (yByte == 208) // 0xD0 in 192-line mode terminates SAT scanning
            {
                break;
            }

            int dy = (line - yByte - 1) & 0xFF;
            if (dy < effectiveHeight)
            {
                if (visibleCount < 8)
                {
                    visibleSprites[visibleCount++] = i;
                }
                else
                {
                    overflow = true;
                }
            }
        }

        // Prepare line buffers for sprite rendering
        Span<byte> spriteColorIndices = stackalloc byte[256];
        Span<bool> spriteOpaqueFlags = stackalloc bool[256];
        spriteColorIndices.Clear();
        spriteOpaqueFlags.Clear();

        // Render visible sprites in order (index 0..63 order -> lower index processed first = highest priority)
        for (int s = 0; s < visibleCount; s++)
        {
            int spriteIndex = visibleSprites[s];

            byte yByte = vram[(satBase + spriteIndex) & 0x3FFF];
            byte xByte = vram[(satBase + 128 + spriteIndex * 2) & 0x3FFF];
            byte tileByte = vram[(satBase + 128 + spriteIndex * 2 + 1) & 0x3FFF];

            int dy = (line - yByte - 1) & 0xFF;
            int patternRow = isZoomed ? (dy / 2) : dy;

            int tileIndex;
            int rowWithinTile;

            if (is8x16)
            {
                int tileBase = tileByte & 0xFE;
                tileIndex = (patternRow < 8) ? tileBase : (tileBase | 1);
                rowWithinTile = patternRow & 7;
            }
            else
            {
                int tileBase = tileByte;
                if ((r6 & 0x04) != 0)
                {
                    tileBase += 256;
                }
                tileIndex = tileBase;
                rowWithinTile = patternRow;
            }

            int rowAddr = ((tileIndex * 32) + (rowWithinTile * 4)) & 0x3FFF;
            byte p0 = vram[rowAddr];
            byte p1 = vram[(rowAddr + 1) & 0x3FFF];
            byte p2 = vram[(rowAddr + 2) & 0x3FFF];
            byte p3 = vram[(rowAddr + 3) & 0x3FFF];

            int screenXBase = xByte;
            if (shiftLeft32)
            {
                screenXBase -= 32;
            }

            int spriteWidth = isZoomed ? 16 : 8;

            for (int col = 0; col < spriteWidth; col++)
            {
                int screenX = screenXBase + col;
                if (screenX < 0 || screenX >= 256)
                {
                    continue;
                }

                int texX = isZoomed ? (col / 2) : col;
                int shift = 7 - texX;

                byte colorIndex = (byte)(
                    (((p3 >> shift) & 1) << 3) |
                    (((p2 >> shift) & 1) << 2) |
                    (((p1 >> shift) & 1) << 1) |
                    ((p0 >> shift) & 1)
                );

                if (colorIndex != 0)
                {
                    if (spriteOpaqueFlags[screenX])
                    {
                        collision = true;
                    }
                    else
                    {
                        spriteOpaqueFlags[screenX] = true;
                        spriteColorIndices[screenX] = colorIndex;
                    }
                }
            }
        }

        // Compose sprites with background line into final line
        for (int x = 0; x < 256; x++)
        {
            if (leftColumnMask && x < 8)
            {
                finalLine[x] = maskedPixel;
                continue;
            }

            byte spriteColorIndex = spriteColorIndices[x];
            VdpPixel bgPixel = bgLine[x];

            if (spriteColorIndex != 0 && (bgPixel.ColorIndex == 0 || !bgPixel.Priority))
            {
                byte cramColor = (byte)(cram[16 + spriteColorIndex] & 0x3F);
                finalLine[x] = new VdpPixel(spriteColorIndex, 1, cramColor, false, false);
            }
            else
            {
                finalLine[x] = bgPixel;
            }
        }
    }
}
