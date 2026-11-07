using Sms.Core.Vdp;

namespace Sms.Core.Tests.Vdp;

public sealed class VdpSpriteEngineTests
{
    private const int SatBase = 0x3F00;

    private static (byte[] vram, byte[] cram, byte[] registers, VdpPixel[] bgLine, VdpPixel[] finalLine) CreateFixture()
    {
        var vram = new byte[0x4000];
        var cram = new byte[32];
        var registers = new byte[11];
        var bgLine = new VdpPixel[256];
        var finalLine = new VdpPixel[256];

        // Display enabled (R1 bit 6 = 1), SAT Base Address = 0x3F00 (R5 = 0x7E)
        registers[1] = 0x40;
        registers[5] = 0x7E;

        for (int i = 0; i < 32; i++)
        {
            cram[i] = (byte)(i & 0x3F);
        }

        // Initialize all 64 sprite Y entries to 0xD0 (208) by default so SAT scanning terminates immediately
        for (int i = 0; i < 64; i++)
        {
            vram[SatBase + i] = 208;
        }

        return (vram, cram, registers, bgLine, finalLine);
    }

    private static void SetSprite(byte[] vram, int satBase, int index, byte yByte, byte xByte, byte tileByte)
    {
        vram[satBase + index] = yByte;
        vram[satBase + 128 + index * 2] = xByte;
        vram[satBase + 128 + index * 2 + 1] = tileByte;
    }

    private static void SetTileRowPattern(byte[] vram, int tileIndex, int row, byte p0, byte p1, byte p2, byte p3)
    {
        int addr = (tileIndex * 32 + row * 4) & 0x3FFF;
        vram[addr] = p0;
        vram[addr + 1] = p1;
        vram[addr + 2] = p2;
        vram[addr + 3] = p3;
    }

    [Fact]
    public void SatYTableTerminationAtD0_StopsProcessingSubsequentSprites()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1 for all pixels

        // Sprite 0: Y = 10 (screen Y = 11), X = 10, Tile = 1
        SetSprite(vram, SatBase, 0, 10, 10, 1);
        // Sprite 1: Y = 10 (screen Y = 11), X = 20, Tile = 1
        SetSprite(vram, SatBase, 1, 10, 20, 1);
        // Sprite 2: Y = 208 (0xD0) -> Termination marker!
        SetSprite(vram, SatBase, 2, 208, 0, 1);
        // Sprite 3: Y = 10 (screen Y = 11), X = 30, Tile = 1 (after 0xD0, should be ignored)
        SetSprite(vram, SatBase, 3, 10, 30, 1);

        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        Assert.False(overflow);
        Assert.False(collision);

        // Sprite 0 rendered at X = 10..17
        Assert.Equal(1, finalLine[10].ColorIndex);
        Assert.Equal(1, finalLine[17].ColorIndex);

        // Sprite 1 rendered at X = 20..27
        Assert.Equal(1, finalLine[20].ColorIndex);
        Assert.Equal(1, finalLine[27].ColorIndex);

        // Sprite 3 at X = 30..37 was AFTER 0xD0 marker, so it must be IGNORED (remains background color 0)
        Assert.Equal(0, finalLine[30].ColorIndex);
        Assert.Equal(0, finalLine[37].ColorIndex);
    }

    [Fact]
    public void Sprite8x8_RenderingAndTilePatternIndexing()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        // Case 1: R6 bit 2 = 0 -> Tile 5 at VRAM 5 * 32
        SetTileRowPattern(vram, 5, 0, 0x00, 0xFF, 0x00, 0x00); // Color 2 for pixels
        SetSprite(vram, SatBase, 0, 10, 16, 5);

        registers[6] = 0x00; // R6 bit 2 = 0
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        Assert.False(overflow);
        Assert.False(collision);
        Assert.Equal(2, finalLine[16].ColorIndex);

        // Case 2: R6 bit 2 = 1 (0x04) -> Tile 5 + 256 = 261 at VRAM 261 * 32
        SetTileRowPattern(vram, 261, 0, 0xFF, 0xFF, 0x00, 0x00); // Color 3 for pixels
        registers[6] = 0x04; // R6 bit 2 set
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out overflow, out collision);

        Assert.Equal(3, finalLine[16].ColorIndex);
    }

    [Fact]
    public void Sprite8x16_RenderingAndTilePatternIndexing()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        registers[1] = 0x42; // Display enabled (0x40) + 8x16 mode (0x02)

        // SAT specifies Tile Index = 5 (odd index)
        // For 8x16 sprites:
        // Top half (pattern rows 0..7) forces tile bit 0 to 0 -> Tile Index 4 (5 & 0xFE)
        // Bottom half (pattern rows 8..15) forces tile bit 0 to 1 -> Tile Index 5 ((5 & 0xFE) | 1)
        SetTileRowPattern(vram, 4, 0, 0xFF, 0x00, 0x00, 0x00); // Top half row 0 -> Color 1
        SetTileRowPattern(vram, 5, 0, 0x00, 0xFF, 0x00, 0x00); // Bottom half row 0 (scanline row 8) -> Color 2

        // Sprite at Y = 10 (screen Y = 11..26)
        SetSprite(vram, SatBase, 0, 10, 40, 5);

        // Line 11 (scanline 11 - top half row 0)
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);
        Assert.Equal(1, finalLine[40].ColorIndex);

        // Line 19 (scanline 19 - bottom half row 0, pattern row 8)
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 19, bgLine, finalLine, out overflow, out collision);
        Assert.Equal(2, finalLine[40].ColorIndex);
    }

    [Fact]
    public void SpriteZoom_2xRendering()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        registers[1] = 0x41; // Display enabled (0x40) + 2x Zoom (0x01)

        // Tile 1 row 0: pixel 0 (bit 7) = color 4 (p2=0x80), pixel 1 (bit 6) = color 6 (p1=0x40, p2=0x40)
        SetTileRowPattern(vram, 1, 0, 0x00, 0x40, 0xC0, 0x00);
        SetSprite(vram, SatBase, 0, 10, 0, 1);

        // Line 11 (screen Y = 11, 1st screen line of pattern row 0)
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        // Zoom 2x: tex pixel 0 spreads across screen X = 0..1; tex pixel 1 spreads across screen X = 2..3
        Assert.Equal(4, finalLine[0].ColorIndex);
        Assert.Equal(4, finalLine[1].ColorIndex);
        Assert.Equal(6, finalLine[2].ColorIndex);
        Assert.Equal(6, finalLine[3].ColorIndex);

        // Line 12 (screen Y = 12, 2nd screen line of pattern row 0)
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 12, bgLine, finalLine, out overflow, out collision);
        Assert.Equal(4, finalLine[0].ColorIndex);
        Assert.Equal(4, finalLine[1].ColorIndex);
        Assert.Equal(6, finalLine[2].ColorIndex);
        Assert.Equal(6, finalLine[3].ColorIndex);
    }

    [Fact]
    public void EightSpritesPerLineLimit_TriggersOverflowOnNinthSprite()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Tile 1 -> Color 1
        SetTileRowPattern(vram, 2, 0, 0x00, 0xFF, 0x00, 0x00); // Tile 2 -> Color 2

        // Place 8 sprites on scanline 11 (Y = 10) at X = 0, 8, 16, 24, 32, 40, 48, 56
        for (int i = 0; i < 8; i++)
        {
            SetSprite(vram, SatBase, i, 10, (byte)(i * 8), 1);
        }

        // Case 1: Exactly 8 sprites on scanline -> overflow is FALSE
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);
        Assert.False(overflow);
        Assert.Equal(1, finalLine[56].ColorIndex);

        // Case 2: Add 9th sprite on scanline 11 at X = 64 with Tile 2
        SetSprite(vram, SatBase, 8, 10, 64, 2);

        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out overflow, out collision);

        Assert.True(overflow); // Overflow flag MUST be set!
        // 9th sprite at X = 64 MUST be ignored for rendering (remains background color 0)
        Assert.Equal(0, finalLine[64].ColorIndex);
    }

    [Fact]
    public void OpaqueSpriteToSpritePixelCollision_TriggersCollisionFlag()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        // Tile 1 has opaque color 4 at px 0
        SetTileRowPattern(vram, 1, 0, 0x00, 0x00, 0x80, 0x00);
        // Tile 2 has opaque color 7 at px 0
        SetTileRowPattern(vram, 2, 0, 0x80, 0x80, 0x80, 0x00);

        // Sprite 0 at X = 20, Tile 1
        SetSprite(vram, SatBase, 0, 10, 20, 1);

        // Case 1: Single sprite -> collision is FALSE
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);
        Assert.False(collision);

        // Case 2: Sprite 1 at X = 20 (overlapping X = 20..27), Tile 2
        SetSprite(vram, SatBase, 1, 10, 20, 2);
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out overflow, out collision);
        Assert.True(collision); // Opaque overlap -> collision is TRUE!

        // Case 3: Transparent pixel overlap does NOT trigger collision
        // Tile 3 has color 0 (transparent) at px 0, color 1 at px 1
        SetTileRowPattern(vram, 3, 0, 0x40, 0x00, 0x00, 0x00);
        // Sprite 0 at X = 50 (Tile 3 - px 0 transparent, px 1 opaque)
        SetSprite(vram, SatBase, 0, 10, 50, 3);
        // Sprite 1 at X = 50 (Tile 1 - px 0 opaque)
        SetSprite(vram, SatBase, 1, 10, 50, 1);
        SetSprite(vram, SatBase, 2, 208, 0, 0); // Terminate rest

        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out overflow, out collision);
        // Pixel 0 at X = 50: Sprite 0 is transparent (color 0), Sprite 1 is color 4 -> ONLY ONE opaque pixel!
        // Pixel 1 at X = 51: Sprite 0 is color 1, Sprite 1 is transparent -> ONLY ONE opaque pixel!
        Assert.False(collision);
    }

    [Fact]
    public void SpriteToBackgroundCompositionPriority()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        // Tile 1: pixel 0 = color 5, pixel 1 = color 0 (transparent)
        SetTileRowPattern(vram, 1, 0, 0x80, 0x00, 0x80, 0x00);
        SetSprite(vram, SatBase, 0, 10, 0, 1);

        cram[16 + 5] = 0x35; // Sprite palette CRAM value

        // Background setup:
        // bgLine[0]: ColorIndex = 0 (transparent BG), Priority = false
        bgLine[0] = new VdpPixel(0, 0, 0x00, priority: false, masked: false);
        // bgLine[1]: ColorIndex = 2, Priority = false
        bgLine[1] = new VdpPixel(2, 0, 0x12, priority: false, masked: false);
        // bgLine[2]: ColorIndex = 3, Priority = true
        bgLine[2] = new VdpPixel(3, 0, 0x13, priority: true, masked: false);

        // Put sprite at X = 0 (px 0 = color 5, px 1 = color 0)
        // Sprite covers X = 0..7. Pixel 0 at X=0, Pixel 1 at X=1, Pixel 2 at X=2 (transparent).
        SetTileRowPattern(vram, 1, 0, 0xC0, 0x00, 0xC0, 0x00); // px 0 & 1 = color 5, px 2 = color 0

        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        // X = 0: Sprite (color 5) over BG (color 0, priority false) -> Sprite wins!
        Assert.Equal(5, finalLine[0].ColorIndex);
        Assert.Equal(1, finalLine[0].Palette);
        Assert.Equal(0x35, finalLine[0].CramColor);

        // X = 1: Sprite (color 5) over BG (color 2, priority false) -> Sprite wins!
        Assert.Equal(5, finalLine[1].ColorIndex);
        Assert.Equal(1, finalLine[1].Palette);

        // X = 2: Sprite (color 5) over BG (color 3, priority TRUE) -> BG wins!
        bgLine[1] = new VdpPixel(3, 0, 0x13, priority: true, masked: false);
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out overflow, out collision);
        Assert.Equal(3, finalLine[1].ColorIndex);
        Assert.Equal(0, finalLine[1].Palette);
        Assert.True(finalLine[1].Priority);

        // Transparent sprite pixel over BG (color 3, priority true) -> BG wins!
        Assert.Equal(3, finalLine[2].ColorIndex);
    }

    [Fact]
    public void SpriteIndexPriorityOrdering_LowerIndexWins()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        // Tile 1 (used by Sprite 1) -> Color 3
        SetTileRowPattern(vram, 1, 0, 0x80, 0x80, 0x00, 0x00);
        // Tile 2 (used by Sprite 4) -> Color 7
        SetTileRowPattern(vram, 2, 0, 0x80, 0x80, 0x80, 0x00);

        // Sprites 0, 2, 3 off-screen (Y = 254) so SAT processing does not terminate at index 0 or 2
        vram[SatBase + 0] = 254;
        vram[SatBase + 2] = 254;
        vram[SatBase + 3] = 254;
        // Sprite 1 (lower index) at X = 30
        SetSprite(vram, SatBase, 1, 10, 30, 1);
        // Sprite 4 (higher index) at X = 30
        SetSprite(vram, SatBase, 4, 10, 30, 2);

        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        // Lower sprite index (Sprite 1) wins priority at X = 30!
        Assert.Equal(3, finalLine[30].ColorIndex);
        // Overlap occurred -> collision flag set
        Assert.True(collision);
    }

    [Fact]
    public void LeftColumnMask_MasksPixels0To7WithBackdropColor()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        // Tile 1 -> Color 5
        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0xFF, 0x00);

        // Sprite 0 at X = 4 (covers X = 4..11)
        SetSprite(vram, SatBase, 0, 10, 4, 1);

        registers[7] = 0x0B; // Backdrop color index 11
        cram[16 + 11] = 0x2E; // CRAM palette 1 color 11

        // Case 1: R0 bit 5 = 1 -> Left column mask ACTIVE
        registers[0] = 0x20;
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        // Pixels X = 0..7 MUST be masked with backdrop color
        for (int x = 0; x < 8; x++)
        {
            Assert.True(finalLine[x].Masked);
            Assert.Equal(11, finalLine[x].ColorIndex);
            Assert.Equal(1, finalLine[x].Palette);
            Assert.Equal(0x2E, finalLine[x].CramColor);
        }

        // Pixels X = 8..11 are NOT masked and render Sprite 0 (Color 5)
        Assert.False(finalLine[8].Masked);
        Assert.Equal(5, finalLine[8].ColorIndex);

        // Case 2: R0 bit 5 = 0 -> Left column mask INACTIVE
        registers[0] = 0x00;
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out overflow, out collision);
        Assert.False(finalLine[4].Masked);
        Assert.Equal(5, finalLine[4].ColorIndex);
    }

    [Fact]
    public void ShiftSpritesLeft32Pixels_WhenR0Bit3Set()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1

        // Sprite at SAT X = 40. When R0 bit 3 is set, screen X = 40 - 32 = 8.
        SetSprite(vram, SatBase, 0, 10, 40, 1);

        registers[0] = 0x08; // R0 bit 3 set
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        Assert.Equal(1, finalLine[8].ColorIndex);
        Assert.Equal(1, finalLine[15].ColorIndex);
        Assert.Equal(0, finalLine[40].ColorIndex);
    }

    [Fact]
    public void DisplayBlankedOrOutOfBounds_FillsLineWithMaskedBackdrop()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        registers[7] = 0x07; // Backdrop color 7
        cram[16 + 7] = 0x1F;

        // Display blanked (R1 bit 6 = 0)
        registers[1] = 0x00;
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, 11, bgLine, finalLine, out bool overflow, out bool collision);

        Assert.False(overflow);
        Assert.False(collision);
        for (int x = 0; x < 256; x++)
        {
            Assert.True(finalLine[x].Masked);
            Assert.Equal(7, finalLine[x].ColorIndex);
            Assert.Equal(0x1F, finalLine[x].CramColor);
        }

        // Out of bounds line (-1)
        registers[1] = 0x40; // Display enabled
        VdpSpriteEngine.ProcessScanline(vram, cram, registers, -1, bgLine, finalLine, out overflow, out collision);
        Assert.False(overflow);
        Assert.False(collision);
        Assert.True(finalLine[0].Masked);
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        Assert.Throws<ArgumentNullException>(() => VdpSpriteEngine.ProcessScanline(null!, cram, registers, 0, bgLine, finalLine, out _, out _));
        Assert.Throws<ArgumentNullException>(() => VdpSpriteEngine.ProcessScanline(vram, null!, registers, 0, bgLine, finalLine, out _, out _));
        Assert.Throws<ArgumentNullException>(() => VdpSpriteEngine.ProcessScanline(vram, cram, null!, 0, bgLine, finalLine, out _, out _));
        Assert.Throws<ArgumentNullException>(() => VdpSpriteEngine.ProcessScanline(vram, cram, registers, 0, null!, finalLine, out _, out _));
        Assert.Throws<ArgumentNullException>(() => VdpSpriteEngine.ProcessScanline(vram, cram, registers, 0, bgLine, null!, out _, out _));
    }

    [Fact]
    public void InvalidBufferSizes_ThrowArgumentException()
    {
        var (vram, cram, registers, bgLine, finalLine) = CreateFixture();

        Assert.Throws<ArgumentException>(() => VdpSpriteEngine.ProcessScanline(new byte[0x3FFF], cram, registers, 0, bgLine, finalLine, out _, out _));
        Assert.Throws<ArgumentException>(() => VdpSpriteEngine.ProcessScanline(vram, new byte[31], registers, 0, bgLine, finalLine, out _, out _));
        Assert.Throws<ArgumentException>(() => VdpSpriteEngine.ProcessScanline(vram, cram, new byte[9], 0, bgLine, finalLine, out _, out _));
        Assert.Throws<ArgumentException>(() => VdpSpriteEngine.ProcessScanline(vram, cram, registers, 0, new VdpPixel[255], finalLine, out _, out _));
        Assert.Throws<ArgumentException>(() => VdpSpriteEngine.ProcessScanline(vram, cram, registers, 0, bgLine, new VdpPixel[255], out _, out _));
    }
}
