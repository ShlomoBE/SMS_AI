using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Sms.Core.Debug;
using Sms.Core.System;
using Sms.Core.Vdp;

namespace Sms.Desktop;

public partial class DebuggerWindow : Window
{
    private readonly SmsDebugger _debugger;
    private DispatcherTimer? _refreshTimer;
    private WriteableBitmap? _tileBitmap;

    public DebuggerWindow()
        : this(new SmsSystem(new byte[16384]))
    {
    }

    public DebuggerWindow(SmsSystem system)
    {
        InitializeComponent();
        _debugger = new SmsDebugger(system);

        InitializeTileBitmap();
        RefreshAllViews();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _refreshTimer.Tick += (s, e) => RefreshAllViews();
        _refreshTimer.Start();
    }

    private void InitializeTileBitmap()
    {
        // 512 tiles arranged in a grid of 32 columns x 16 rows of 8x8 tiles = 256 x 128 pixels
        _tileBitmap = new WriteableBitmap(
            new PixelSize(256, 128),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        ImgTileAtlas.Source = _tileBitmap;
    }

    public void RefreshAllViews()
    {
        RefreshCpuView();
        RefreshDisassembly();
        RefreshMemoryView();
        RefreshVdpView();
        RefreshTileAtlas();
    }

    private void RefreshCpuView()
    {
        var snap = _debugger.GetCpuSnapshot();

        TxtPc.Text = $"0x{snap.PC:X4}";
        TxtSp.Text = $"0x{snap.SP:X4}";
        TxtAf.Text = $"0x{snap.A:X2}{snap.F:X2}";
        TxtBc.Text = $"0x{snap.B:X2}{snap.C:X2}";
        TxtDe.Text = $"0x{snap.D:X2}{snap.E:X2}";
        TxtHl.Text = $"0x{snap.H:X2}{snap.L:X2}";
        TxtIx.Text = $"0x{snap.IX:X4}";
        TxtIy.Text = $"0x{snap.IY:X4}";
        TxtAfAlt.Text = $"0x{snap.APrime:X2}{snap.FPrime:X2}";
        TxtBcAlt.Text = $"0x{snap.BPrime:X2}{snap.CPrime:X2}";
        TxtDeAlt.Text = $"0x{snap.DPrime:X2}{snap.EPrime:X2}";
        TxtHlAlt.Text = $"0x{snap.HPrime:X2}{snap.LPrime:X2}";

        // Flags [S Z 5 H 3 P N C]
        byte f = snap.F;
        string flagsStr = $"Flags: [{(f & 0x80) >> 7} {(f & 0x40) >> 6} {(f & 0x20) >> 5} {(f & 0x10) >> 4} {(f & 0x08) >> 3} {(f & 0x04) >> 2} {(f & 0x02) >> 1} {f & 0x01}]";
        TxtFlags.Text = flagsStr;

        TxtInterrupts.Text = $"IFF1: {(snap.IFF1 ? 1 : 0)}  IFF2: {(snap.IFF2 ? 1 : 0)}  IM: {snap.InterruptMode}  HALT: {(snap.IsHalted ? "Yes" : "No")}";
        BtnPauseResume.Content = _debugger.IsPaused ? "Resume" : "Pause";
    }

    private void RefreshDisassembly()
    {
        ushort pc = _debugger.GetCpuSnapshot().PC;
        List<string> lines = Z80Disassembler.DisassembleRange(_debugger.System.Bus, pc, 20);

        LstDisasm.ItemsSource = lines;
    }

    private void RefreshMemoryView()
    {
        ushort baseAddr = 0x0000;
        if (ushort.TryParse(TxtMemAddr.Text, System.Globalization.NumberStyles.HexNumber, null, out ushort parsed))
        {
            baseAddr = (ushort)(parsed & 0xFFF0);
        }

        List<string> rows = new();
        for (int row = 0; row < 16; row++)
        {
            ushort addr = (ushort)(baseAddr + row * 16);
            var sb = new StringBuilder();
            sb.Append($"{addr:X4}: ");

            var asciiSb = new StringBuilder();
            for (int col = 0; col < 16; col++)
            {
                byte b = _debugger.ReadMemoryByteSideEffectFree((ushort)(addr + col));
                sb.Append($"{b:X2} ");
                char c = (b >= 32 && b <= 126) ? (char)b : '.';
                asciiSb.Append(c);
            }

            sb.Append($"  |{asciiSb}|");
            rows.Add(sb.ToString());
        }

        LstMemoryHex.ItemsSource = rows;
    }

    private void RefreshVdpView()
    {
        var vdpSnap = _debugger.GetVdpSnapshot();
        List<string> regList = new();
        for (int i = 0; i < vdpSnap.Registers.Count; i++)
        {
            regList.Add($"R{i:D2}: 0x{vdpSnap.Registers[i]:X2}");
        }
        LstVdpRegisters.ItemsSource = regList;

        // Refresh Palette Swatches
        UpdatePaletteContainer(Palette0Container, vdpSnap.Cram, 0);
        UpdatePaletteContainer(Palette1Container, vdpSnap.Cram, 16);
    }

    private static void UpdatePaletteContainer(ItemsControl container, IReadOnlyList<byte> cram, int offset)
    {
        List<Rectangle> rects = new();
        for (int i = 0; i < 16; i++)
        {
            byte cramByte = cram[offset + i];
            byte r2 = (byte)(cramByte & 3);
            byte g2 = (byte)((cramByte >> 2) & 3);
            byte b2 = (byte)((cramByte >> 4) & 3);

            Color color = Color.FromRgb((byte)(r2 * 85), (byte)(g2 * 85), (byte)(b2 * 85));

            var rect = new Rectangle
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(2),
                Fill = new SolidColorBrush(color),
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };
            ToolTip.SetTip(rect, $"Index {i}: 0x{cramByte:X2}");
            rects.Add(rect);
        }

        container.ItemsSource = rects;
    }

    private void RefreshTileAtlas()
    {
        if (_tileBitmap == null) return;

        uint[][] tiles = _debugger.DecodeTilePatterns();

        using (var lockHandle = _tileBitmap.Lock())
        {
            unsafe
            {
                uint* ptr = (uint*)lockHandle.Address;

                for (int tileIdx = 0; tileIdx < 512; tileIdx++)
                {
                    int tileCol = tileIdx % 32;
                    int tileRow = tileIdx / 32;
                    uint[] pixels = tiles[tileIdx];

                    for (int r = 0; r < 8; r++)
                    {
                        for (int c = 0; c < 8; c++)
                        {
                            int px = (tileRow * 8 + r) * 256 + (tileCol * 8 + c);
                            uint argb = pixels[r * 8 + c];

                            // Convert ARGB 0xFFRRGGBB to BGRA 0xAABBGGRR
                            byte red = (byte)((argb >> 16) & 0xFF);
                            byte green = (byte)((argb >> 8) & 0xFF);
                            byte blue = (byte)(argb & 0xFF);
                            ptr[px] = 0xFF000000 | ((uint)red << 16) | ((uint)green << 8) | blue;
                        }
                    }
                }
            }
        }

        ImgTileAtlas.InvalidateVisual();
    }

    private void OnPauseResume_Click(object? sender, RoutedEventArgs e)
    {
        _debugger.IsPaused = !_debugger.IsPaused;
        RefreshAllViews();
    }

    private void OnStepInstruction_Click(object? sender, RoutedEventArgs e)
    {
        _debugger.StepInstruction();
        RefreshAllViews();
    }

    private void OnStepScanline_Click(object? sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 228 / 4; i++) _debugger.StepInstruction();
        RefreshAllViews();
    }

    private void OnAddBreakpoint_Click(object? sender, RoutedEventArgs e)
    {
        if (ushort.TryParse(TxtBreakpoint.Text, System.Globalization.NumberStyles.HexNumber, null, out ushort pc))
        {
            _debugger.AddPcBreakpoint(pc);
            RefreshAllViews();
        }
    }

    private void OnClearBreakpoints_Click(object? sender, RoutedEventArgs e)
    {
        _debugger.ClearBreakpoints();
        RefreshAllViews();
    }

    private void OnGoToMemAddr_Click(object? sender, RoutedEventArgs e) => RefreshMemoryView();
    private void OnRefreshTiles_Click(object? sender, RoutedEventArgs e) => RefreshTileAtlas();

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer?.Stop();
        base.OnClosed(e);
    }
}
