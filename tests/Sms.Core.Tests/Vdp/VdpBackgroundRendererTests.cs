using Sms.Core.Vdp;

namespace Sms.Core.Tests.Vdp;

public sealed class VdpBackgroundRendererTests
{
    private static (byte[] vram, byte[] cram, byte[] registers, VdpPixel[] output) CreateFixture()
    {
        var vram = new byte[0x4000];
        var cram = new byte[32];
        var registers = new byte[11];
        var output = new VdpPixel[256];

        // Display visible (R1 bit 6 = 1), Name Table Base at 0x3800 (R2 = 0x0E)
        registers[1] = 0x40;
        registers[2] = 0x0E;

        for (int i = 0; i < 32; i++)
        {
            cram[i] = (byte)(i & 0x3F);
        }

        return (vram, cram, registers, output);
    }

    private static void SetNameTableEntry(byte[] vram, int nameTableBase, int tileRow, int tileCol, ushort entry)
    {
        int addr = (nameTableBase + (tileRow * 32 + tileCol) * 2) & 0x3FFF;
        vram[addr] = (byte)(entry & 0xFF);
        vram[addr + 1] = (byte)(entry >> 8);
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
    public void BitplaneDecoding_AllSixteenColorsDecodedCorrectly()
    {
        var (vram, cram, registers, output) = CreateFixture();
        int tileIndex = 1;

        // Set up row 0 with pixels 0..7 mapped to colors 0..7
        // px 0 (shift 7): 0000 -> 0
        // px 1 (shift 6): 0001 -> 1 (p0 bit 6)
        // px 2 (shift 5): 0010 -> 2 (p1 bit 5)
        // px 3 (shift 4): 0011 -> 3 (p0, p1 bit 4)
        // px 4 (shift 3): 0100 -> 4 (p2 bit 3)
        // px 5 (shift 2): 0101 -> 5 (p0, p2 bit 2)
        // px 6 (shift 1): 0110 -> 6 (p1, p2 bit 1)
        // px 7 (shift 0): 0111 -> 7 (p0, p1, p2 bit 0)
        byte row0_p0 = 0b01010101; // bits 6, 4, 2, 0 set
        byte row0_p1 = 0b00110011; // bits 5, 4, 1, 0 set
        byte row0_p2 = 0b00001111; // bits 3, 2, 1, 0 set
        byte row0_p3 = 0b00000000;
        SetTileRowPattern(vram, tileIndex, 0, row0_p0, row0_p1, row0_p2, row0_p3);

        // Set up row 1 with pixels 0..7 mapped to colors 8..15 (p3 bit set for all)
        byte row1_p0 = 0b01010101;
        byte row1_p1 = 0b00110011;
        byte row1_p2 = 0b00001111;
        byte row1_p3 = 0b11111111; // all p3 bits set
        SetTileRowPattern(vram, tileIndex, 1, row1_p0, row1_p1, row1_p2, row1_p3);

        SetNameTableEntry(vram, 0x3800, 0, 0, (ushort)tileIndex);

        // Render line 0 (row 0 of tile 1)
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        for (byte x = 0; x < 8; x++)
        {
            Assert.Equal(x, output[x].ColorIndex);
            Assert.Equal(0, output[x].Palette);
            Assert.Equal(x, output[x].CramColor);
            Assert.False(output[x].Masked);
        }

        // Render line 1 (row 1 of tile 1)
        VdpMode4Background.RenderScanline(vram, cram, registers, 1, output);
        for (byte x = 0; x < 8; x++)
        {
            byte expectedColor = (byte)(8 + x);
            Assert.Equal(expectedColor, output[x].ColorIndex);
            Assert.Equal(0, output[x].Palette);
            Assert.Equal(expectedColor, output[x].CramColor);
            Assert.False(output[x].Masked);
        }
    }

    [Fact]
    public void TileFlips_HorizontalVerticalAndCombined()
    {
        var (vram, cram, registers, output) = CreateFixture();
        int tileIndex = 2;

        // In tile 2: Row 0 has px 0 (shift 7) set to color 5 (p0=0x80, p2=0x80)
        SetTileRowPattern(vram, tileIndex, 0, 0x80, 0x00, 0x80, 0x00);

        int nameTableBase = 0x3800;

        // Case 1: No flips
        SetNameTableEntry(vram, nameTableBase, 0, 0, (ushort)tileIndex);
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(5, output[0].ColorIndex);
        Assert.Equal(0, output[7].ColorIndex);

        // Case 2: Horizontal flip (bit 9 = 1)
        ushort hFlipEntry = (ushort)(tileIndex | 0x0200);
        SetNameTableEntry(vram, nameTableBase, 0, 0, hFlipEntry);
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(0, output[0].ColorIndex);
        Assert.Equal(5, output[7].ColorIndex);

        // Case 3: Vertical flip (bit 10 = 1) -> Row 0 appears at line 7
        ushort vFlipEntry = (ushort)(tileIndex | 0x0400);
        SetNameTableEntry(vram, nameTableBase, 0, 0, vFlipEntry);
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(0, output[0].ColorIndex); // line 0 reads row 7 of pattern (color 0)

        VdpMode4Background.RenderScanline(vram, cram, registers, 7, output);
        Assert.Equal(5, output[0].ColorIndex); // line 7 reads row 0 of pattern (color 5)

        // Case 4: Both H and V flip (bits 9 & 10 = 1) -> Row 0 px 0 appears at line 7, px 7
        ushort hvFlipEntry = (ushort)(tileIndex | 0x0600);
        SetNameTableEntry(vram, nameTableBase, 0, 0, hvFlipEntry);
        VdpMode4Background.RenderScanline(vram, cram, registers, 7, output);
        Assert.Equal(0, output[0].ColorIndex);
        Assert.Equal(5, output[7].ColorIndex);
    }

    [Fact]
    public void NameTableAddressCalculation_AndTilePatternAddressing()
    {
        var (vram, cram, registers, output) = CreateFixture();

        // Test 1: R2 = 0x0E -> Name Table Base = 0x3800, Tile Index = 511 (0x1FF) -> Pattern at 0x3FE0
        registers[2] = 0x0E;
        int tile511 = 511;
        SetTileRowPattern(vram, tile511, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1 for all pixels
        SetNameTableEntry(vram, 0x3800, 0, 0, (ushort)tile511);

        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(1, output[0].ColorIndex);

        // Test 2: R2 = 0x00 -> Name Table Base = 0x0000, Tile Index = 256 (0x100) -> Pattern at 0x2000
        registers[2] = 0x00;
        int tile256 = 256;
        SetTileRowPattern(vram, tile256, 0, 0x00, 0xFF, 0x00, 0x00); // Color 2 for all pixels
        SetNameTableEntry(vram, 0x0000, 0, 0, (ushort)tile256);

        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(2, output[0].ColorIndex);

        // Test 3: R2 = 0x04 -> Name Table Base = 0x1000, Tile Index = 0 -> Pattern at 0x0000
        registers[2] = 0x04;
        SetTileRowPattern(vram, 0, 0, 0x00, 0x00, 0xFF, 0x00); // Color 4 for all pixels
        SetNameTableEntry(vram, 0x1000, 0, 0, 0);

        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(4, output[0].ColorIndex);
    }

    [Fact]
    public void FineAndCoarseHorizontalScrolling()
    {
        var (vram, cram, registers, output) = CreateFixture();

        // Tile 1 = Color 1, Tile 2 = Color 2, Tile 31 (Col 31) = Color 7
        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1
        SetTileRowPattern(vram, 2, 0, 0x00, 0xFF, 0x00, 0x00); // Color 2
        SetTileRowPattern(vram, 3, 0, 0xFF, 0xFF, 0xFF, 0x00); // Color 7

        SetNameTableEntry(vram, 0x3800, 0, 0, 1);
        SetNameTableEntry(vram, 0x3800, 0, 1, 2);
        SetNameTableEntry(vram, 0x3800, 0, 31, 3);

        // R8 = 0: col 0 (tile 1) at x=0..7, col 1 (tile 2) at x=8..15
        registers[8] = 0;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(1, output[0].ColorIndex);
        Assert.Equal(1, output[7].ColorIndex);
        Assert.Equal(2, output[8].ColorIndex);

        // R8 = 8 (coarse scroll 1 tile right): col 31 (tile 3) at x=0..7, col 0 (tile 1) at x=8..15
        registers[8] = 8;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(7, output[0].ColorIndex);
        Assert.Equal(7, output[7].ColorIndex);
        Assert.Equal(1, output[8].ColorIndex);

        // R8 = 3 (fine scroll 3 pixels right):
        // x=0..2 comes from col 31 (pixels 5,6,7), x=3..10 comes from col 0 (pixels 0..7)
        registers[8] = 3;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(7, output[0].ColorIndex);
        Assert.Equal(7, output[2].ColorIndex);
        Assert.Equal(1, output[3].ColorIndex);
        Assert.Equal(1, output[10].ColorIndex);

        // R8 = 255 (scroll 255 pixels right):
        // bgX = (x - 255) & 0xFF. For x=0, bgX = 1 -> col 0 pixel 1 (Color 1). For x=255, bgX = 0 -> col 0 pixel 0 (Color 1).
        registers[8] = 255;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(1, output[0].ColorIndex);
        Assert.Equal(1, output[255].ColorIndex);
    }

    [Fact]
    public void FineAndCoarseVerticalScrolling()
    {
        var (vram, cram, registers, output) = CreateFixture();

        // Row 0 of NT points to Tile 1 (Row 0=Color 1, Row 1=Color 2)
        // Row 1 of NT points to Tile 2 (Row 0=Color 3)
        // Row 27 of NT points to Tile 3 (Row 7=Color 6)
        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1
        SetTileRowPattern(vram, 1, 1, 0x00, 0xFF, 0x00, 0x00); // Color 2
        SetTileRowPattern(vram, 2, 0, 0xFF, 0xFF, 0x00, 0x00); // Color 3
        SetTileRowPattern(vram, 3, 7, 0x00, 0xFF, 0xFF, 0x00); // Color 6

        SetNameTableEntry(vram, 0x3800, 0, 0, 1);
        SetNameTableEntry(vram, 0x3800, 1, 0, 2);
        SetNameTableEntry(vram, 0x3800, 27, 0, 3);

        // R9 = 0: line 0 -> NT Row 0, Tile Row 0 (Color 1)
        registers[9] = 0;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(1, output[0].ColorIndex);

        // R9 = 1: line 0 -> NT Row 0, Tile Row 1 (Color 2)
        registers[9] = 1;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(2, output[0].ColorIndex);

        // R9 = 8: line 0 -> NT Row 1, Tile Row 0 (Color 3)
        registers[9] = 8;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(3, output[0].ColorIndex);

        // R9 = 224: modulo 224 -> 0 (line 0 -> Color 1)
        registers[9] = 224;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(1, output[0].ColorIndex);

        // R9 = 225: modulo 224 -> 1 (line 0 -> Color 2)
        registers[9] = 225;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(2, output[0].ColorIndex);
    }

    [Fact]
    public void HScrollLock_Lines0To15IgnoreHScroll()
    {
        var (vram, cram, registers, output) = CreateFixture();

        // Col 0 = Tile 1 (Color 1), Col 30 (for R8=16 scroll) = Tile 2 (Color 2)
        for (int r = 0; r < 8; r++)
        {
            SetTileRowPattern(vram, 1, r, 0xFF, 0x00, 0x00, 0x00); // Color 1
            SetTileRowPattern(vram, 2, r, 0x00, 0xFF, 0x00, 0x00); // Color 2
        }

        for (int tr = 0; tr < 3; tr++)
        {
            SetNameTableEntry(vram, 0x3800, tr, 0, 1);
            SetNameTableEntry(vram, 0x3800, tr, 30, 2);
        }

        registers[8] = 16; // HScroll = 16 (2 tiles)
        registers[0] = 0x40; // R0 bit 6 set (HScroll Lock for lines 0..15)

        // Line 0 (< 16): HScroll is ignored (0) -> Pixel 0 is Col 0 (Tile 1, Color 1)
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(1, output[0].ColorIndex);

        // Line 15 (< 16): HScroll is ignored (0) -> Pixel 0 is Col 0 (Tile 1, Color 1)
        VdpMode4Background.RenderScanline(vram, cram, registers, 15, output);
        Assert.Equal(1, output[0].ColorIndex);

        // Line 16 (>= 16): HScroll is active (16) -> Pixel 0 is Col 30 (Tile 2, Color 2)
        VdpMode4Background.RenderScanline(vram, cram, registers, 16, output);
        Assert.Equal(2, output[0].ColorIndex);
    }

    [Fact]
    public void VScrollLock_Columns24To31IgnoreVScroll()
    {
        var (vram, cram, registers, output) = CreateFixture();

        // Row 0 = Tile 1 (Color 1), Row 2 = Tile 2 (Color 2)
        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1
        SetTileRowPattern(vram, 2, 0, 0x00, 0xFF, 0x00, 0x00); // Color 2

        // Set Col 0..31 for Row 0 to Tile 1, Row 2 to Tile 2
        for (int c = 0; c < 32; c++)
        {
            SetNameTableEntry(vram, 0x3800, 0, c, 1);
            SetNameTableEntry(vram, 0x3800, 2, c, 2);
        }

        registers[9] = 16; // VScroll = 16 (2 tile rows)
        registers[0] = 0x80; // R0 bit 7 set (VScroll Lock for cols 24..31)

        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);

        // Columns 0..23 (x=0..191) apply VScroll (Row 2 -> Tile 2, Color 2)
        Assert.Equal(2, output[0].ColorIndex);
        Assert.Equal(2, output[191].ColorIndex);

        // Columns 24..31 (x=192..255) ignore VScroll (Row 0 -> Tile 1, Color 1)
        Assert.Equal(1, output[192].ColorIndex);
        Assert.Equal(1, output[255].ColorIndex);
    }

    [Fact]
    public void LeftColumnMask_MasksPixels0To7WithBackdropColor()
    {
        var (vram, cram, registers, output) = CreateFixture();

        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1
        SetNameTableEntry(vram, 0x3800, 0, 0, 1);
        SetNameTableEntry(vram, 0x3800, 0, 1, 1);

        registers[7] = 0x09; // Backdrop color index = 9
        cram[16 + 9] = 0x2A; // Palette 1, index 9 CRAM value

        // Test 1: R0 bit 5 set -> Left column mask active
        registers[0] = 0x20;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);

        for (int x = 0; x < 8; x++)
        {
            Assert.True(output[x].Masked);
            Assert.Equal(9, output[x].ColorIndex);
            Assert.Equal(1, output[x].Palette);
            Assert.Equal(0x2A, output[x].CramColor);
            Assert.False(output[x].Priority);
        }

        // Pixels 8..255 are NOT masked
        Assert.False(output[8].Masked);
        Assert.Equal(1, output[8].ColorIndex);

        // Test 2: R0 bit 5 cleared -> Left column mask inactive
        registers[0] = 0x00;
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.False(output[0].Masked);
        Assert.Equal(1, output[0].ColorIndex);
    }

    [Fact]
    public void PaletteSelection_AndPriorityMetadata()
    {
        var (vram, cram, registers, output) = CreateFixture();

        SetTileRowPattern(vram, 1, 0, 0xFF, 0x00, 0x00, 0x00); // Color 1

        cram[1] = 0x12;      // Palette 0, Color 1 -> 0x12
        cram[16 + 1] = 0x34; // Palette 1, Color 1 -> 0x34

        // Entry 1: Palette 0 (bit 11 = 0), Priority 0 (bit 12 = 0)
        SetNameTableEntry(vram, 0x3800, 0, 0, 1);
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(0, output[0].Palette);
        Assert.Equal(0x12, output[0].CramColor);
        Assert.False(output[0].Priority);

        // Entry 2: Palette 1 (bit 11 = 1), Priority 1 (bit 12 = 1)
        ushort entryPal1Pri1 = (ushort)(1 | 0x0800 | 0x1000);
        SetNameTableEntry(vram, 0x3800, 0, 0, entryPal1Pri1);
        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);
        Assert.Equal(1, output[0].Palette);
        Assert.Equal(0x34, output[0].CramColor);
        Assert.True(output[0].Priority);
    }

    [Fact]
    public void DisplayBlanked_FillsScanlineWithMaskedBackdropPixels()
    {
        var (vram, cram, registers, output) = CreateFixture();

        registers[1] = 0x00; // Bit 6 = 0 -> Display blanked
        registers[7] = 0x0B; // Backdrop color 11
        cram[16 + 11] = 0x1E;

        VdpMode4Background.RenderScanline(vram, cram, registers, 0, output);

        for (int x = 0; x < 256; x++)
        {
            Assert.True(output[x].Masked);
            Assert.Equal(11, output[x].ColorIndex);
            Assert.Equal(1, output[x].Palette);
            Assert.Equal(0x1E, output[x].CramColor);
            Assert.False(output[x].Priority);
        }
    }

    [Fact]
    public void LineOutOfBounds_FillsScanlineWithMaskedBackdropPixels()
    {
        var (vram, cram, registers, output) = CreateFixture();

        registers[7] = 0x03; // Backdrop color 3
        cram[16 + 3] = 0x05;

        VdpMode4Background.RenderScanline(vram, cram, registers, -1, output);
        Assert.True(output[0].Masked);
        Assert.Equal(3, output[0].ColorIndex);

        VdpMode4Background.RenderScanline(vram, cram, registers, 192, output);
        Assert.True(output[0].Masked);
        Assert.Equal(3, output[0].ColorIndex);
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        var (vram, cram, registers, output) = CreateFixture();

        Assert.Throws<ArgumentNullException>(() => VdpMode4Background.RenderScanline(null!, cram, registers, 0, output));
        Assert.Throws<ArgumentNullException>(() => VdpMode4Background.RenderScanline(vram, null!, registers, 0, output));
        Assert.Throws<ArgumentNullException>(() => VdpMode4Background.RenderScanline(vram, cram, null!, 0, output));
        Assert.Throws<ArgumentNullException>(() => VdpMode4Background.RenderScanline(vram, cram, registers, 0, null!));
    }

    [Fact]
    public void InvalidBufferSizes_ThrowArgumentException()
    {
        var (vram, cram, registers, output) = CreateFixture();

        Assert.Throws<ArgumentException>(() => VdpMode4Background.RenderScanline(new byte[0x3FFF], cram, registers, 0, output));
        Assert.Throws<ArgumentException>(() => VdpMode4Background.RenderScanline(vram, new byte[31], registers, 0, output));
        Assert.Throws<ArgumentException>(() => VdpMode4Background.RenderScanline(vram, cram, new byte[9], 0, output));
        Assert.Throws<ArgumentException>(() => VdpMode4Background.RenderScanline(vram, cram, registers, 0, new VdpPixel[255]));
    }
}
