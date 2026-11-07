using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Sms.Core.Audio;
using Sms.Core.Cartridge;
using Sms.Core.Input;
using Sms.Core.System;

namespace Sms.Desktop;

public partial class MainWindow : Window
{
    private SmsSystem? _sms;
    private WriteableBitmap? _bitmap;
    private AudioPlayer? _audioPlayer;
    private DispatcherTimer? _frameTimer;
    private readonly float[] _audioBuffer = new float[1470]; // ~735-1470 samples per frame at 44.1kHz
    private bool _isPaused;
    private string? _loadedRomPath;

    private readonly Stopwatch _fpsStopwatch = new();
    private int _frameCount;
    private double _fps;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;

        _audioPlayer = new AudioPlayer();
        InitializeBitmap();
        InitializeTimer();
    }

    private void InitializeBitmap()
    {
        _bitmap = new WriteableBitmap(
            new PixelSize(SmsSystem.DisplayWidth, SmsSystem.DisplayHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        DisplayImage.Source = _bitmap;
    }

    private void InitializeTimer()
    {
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16.666) // Default NTSC 60Hz
        };
        _frameTimer.Tick += OnFrameTick;
        _frameTimer.Start();
        _fpsStopwatch.Start();
    }

    private void LoadRomFile(string filePath)
    {
        try
        {
            byte[] romBytes = File.ReadAllBytes(filePath);
            if (romBytes.Length < 16384)
            {
                ShowErrorDialog("Invalid ROM", "ROM file must be at least 16 KiB.");
                return;
            }

            _sms = new SmsSystem(romBytes, SegaMapperProfile.Sega3155235RomOnly);
            _loadedRomPath = filePath;
            _isPaused = false;

            DropPrompt.IsVisible = false;
            StatusText.Text = $"Playing: {Path.GetFileName(filePath)}";
            RomInfoText.Text = $"{romBytes.Length / 1024} KB ROM";

            UpdateTimerInterval();
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Error Loading ROM", ex.Message);
        }
    }

    private void LoadBiosFile(string filePath)
    {
        try
        {
            byte[] biosBytes = File.ReadAllBytes(filePath);
            if (_sms != null)
            {
                _sms = new SmsSystem(
                    mapper: _sms.Mapper,
                    bios: biosBytes,
                    videoStandard: _sms.VideoStandard,
                    audioMode: _sms.AudioMode);
            }
            StatusText.Text = $"Loaded BIOS: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Error Loading BIOS", ex.Message);
        }
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_sms == null || _isPaused || _bitmap == null) return;

        // Step 1 full SMS frame
        _sms.StepFrame(_audioBuffer, 44100);

        // Render frame buffer to WriteableBitmap
        using (var lockHandle = _bitmap.Lock())
        {
            uint[] frameBuffer = _sms.FrameBuffer;
            unsafe
            {
                uint* ptr = (uint*)lockHandle.Address;
                int count = Math.Min(frameBuffer.Length, SmsSystem.DisplayPixelCount);

                for (int i = 0; i < count; i++)
                {
                    uint c = frameBuffer[i];
                    // Convert ARGB 0xFFRRGGBB to BGRA 0xAABBGGRR
                    byte r = (byte)((c >> 16) & 0xFF);
                    byte g = (byte)((c >> 8) & 0xFF);
                    byte b = (byte)(c & 0xFF);
                    ptr[i] = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }

        DisplayImage.InvalidateVisual();

        // Output audio PCM samples
        _audioPlayer?.PlaySamples(_audioBuffer);

        // Update FPS counter
        _frameCount++;
        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            _fps = _frameCount * 1000.0 / _fpsStopwatch.ElapsedMilliseconds;
            FpsText.Text = $"{_fps:0.0} FPS";
            _frameCount = 0;
            _fpsStopwatch.Restart();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_sms == null) return;

        switch (e.Key)
        {
            case Key.Up or Key.W:
                _sms.SetButtonState(1, SmsButtons.Up, true);
                break;
            case Key.Down or Key.S:
                _sms.SetButtonState(1, SmsButtons.Down, true);
                break;
            case Key.Left or Key.A:
                _sms.SetButtonState(1, SmsButtons.Left, true);
                break;
            case Key.Right or Key.D:
                _sms.SetButtonState(1, SmsButtons.Right, true);
                break;
            case Key.Z or Key.J:
                _sms.SetButtonState(1, SmsButtons.Button1, true);
                break;
            case Key.X or Key.K:
                _sms.SetButtonState(1, SmsButtons.Button2, true);
                break;
            case Key.P or Key.Space:
                _sms.SetPauseState(true);
                break;
            case Key.R:
                _sms.Reset();
                break;
            case Key.F5:
                QuickSaveState();
                break;
            case Key.F8:
                QuickLoadState();
                break;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_sms == null) return;

        switch (e.Key)
        {
            case Key.Up or Key.W:
                _sms.SetButtonState(1, SmsButtons.Up, false);
                break;
            case Key.Down or Key.S:
                _sms.SetButtonState(1, SmsButtons.Down, false);
                break;
            case Key.Left or Key.A:
                _sms.SetButtonState(1, SmsButtons.Left, false);
                break;
            case Key.Right or Key.D:
                _sms.SetButtonState(1, SmsButtons.Right, false);
                break;
            case Key.Z or Key.J:
                _sms.SetButtonState(1, SmsButtons.Button1, false);
                break;
            case Key.X or Key.K:
                _sms.SetButtonState(1, SmsButtons.Button2, false);
                break;
            case Key.P or Key.Space:
                _sms.SetPauseState(false);
                break;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    string path = file.Path.LocalPath;
                    if (path.EndsWith(".sms", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".gg", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadRomFile(path);
                        break;
                    }
                }
            }
        }
    }

    private async void OnFileOpenRom_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Sega Master System ROM",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SMS ROMs (*.sms, *.bin)") { Patterns = new[] { "*.sms", "*.bin" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            LoadRomFile(files[0].Path.LocalPath);
        }
    }

    private async void OnFileOpenBios_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Sega Master System BIOS",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("BIOS Images (*.sms, *.bios, *.bin)") { Patterns = new[] { "*.sms", "*.bios", "*.bin" } },
                new FilePickerFileType("All Files (*.*)") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            LoadBiosFile(files[0].Path.LocalPath);
        }
    }

    private void OnFileExit_Click(object? sender, RoutedEventArgs e) => Close();

    private void OnQuickSaveState_Click(object? sender, RoutedEventArgs e) => QuickSaveState();
    private void OnQuickLoadState_Click(object? sender, RoutedEventArgs e) => QuickLoadState();

    private void QuickSaveState()
    {
        if (_sms == null) return;
        try
        {
            string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quicksave.state");
            Sms.Core.State.SmsStateSerializer.SaveStateToFile(_sms, savePath);
            StatusText.Text = "Quick State Saved (F5)";
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Save State Error", ex.Message);
        }
    }

    private void QuickLoadState()
    {
        if (_sms == null) return;
        try
        {
            string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quicksave.state");
            if (!File.Exists(savePath))
            {
                StatusText.Text = "No Quick Save State found.";
                return;
            }
            Sms.Core.State.SmsStateSerializer.LoadStateFromFile(_sms, savePath);
            StatusText.Text = "Quick State Loaded (F8)";
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Load State Error", ex.Message);
        }
    }

    private async void OnSaveStateFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_sms == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Emulator State",
            DefaultExtension = "state",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SMS Save State (*.state)") { Patterns = new[] { "*.state" } }
            }
        });

        if (file != null)
        {
            try
            {
                Sms.Core.State.SmsStateSerializer.SaveStateToFile(_sms, file.Path.LocalPath);
                StatusText.Text = $"State saved to: {file.Name}";
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Save State Error", ex.Message);
            }
        }
    }

    private async void OnLoadStateFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_sms == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Emulator State",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SMS Save State (*.state)") { Patterns = new[] { "*.state" } }
            }
        });

        if (files.Count > 0)
        {
            try
            {
                Sms.Core.State.SmsStateSerializer.LoadStateFromFile(_sms, files[0].Path.LocalPath);
                StatusText.Text = $"State loaded from: {files[0].Name}";
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Load State Error", ex.Message);
            }
        }
    }

    private void OnOpenDebugger_Click(object? sender, RoutedEventArgs e)
    {
        if (_sms == null)
        {
            ShowErrorDialog("Debugger", "Please load a ROM before opening the Debugger.");
            return;
        }

        var dbgWin = new DebuggerWindow(_sms);
        dbgWin.Show(this);
    }

    private void OnEmulationPause_Click(object? sender, RoutedEventArgs e)
    {
        _isPaused = !_isPaused;
        StatusText.Text = _isPaused ? "Paused" : (_loadedRomPath != null ? $"Playing: {Path.GetFileName(_loadedRomPath)}" : "Ready");
    }

    private void OnEmulationReset_Click(object? sender, RoutedEventArgs e)
    {
        _sms?.Reset();
        StatusText.Text = "System Reset";
    }

    private void OnVideoNtsc_Click(object? sender, RoutedEventArgs e)
    {
        MenuVideoNtsc.IsChecked = true;
        MenuVideoPal.IsChecked = false;

        if (_sms != null)
        {
            _sms.VideoStandard = VideoStandard.Ntsc;
            _sms.Reset();
        }
        UpdateTimerInterval();
        StatusText.Text = "Video standard switched to NTSC (60 Hz)";
    }

    private void OnVideoPal_Click(object? sender, RoutedEventArgs e)
    {
        MenuVideoNtsc.IsChecked = false;
        MenuVideoPal.IsChecked = true;

        if (_sms != null)
        {
            _sms.VideoStandard = VideoStandard.Pal;
            _sms.Reset();
        }
        UpdateTimerInterval();
        StatusText.Text = "Video standard switched to PAL (50 Hz)";
    }

    private void UpdateTimerInterval()
    {
        if (_frameTimer != null)
        {
            bool isNtsc = _sms == null || _sms.VideoStandard == VideoStandard.Ntsc;
            _frameTimer.Interval = TimeSpan.FromMilliseconds(isNtsc ? 16.666 : 20.0);
        }
    }

    private void OnAudioAuto_Click(object? sender, RoutedEventArgs e)
    {
        SetAudioMode(SmsAudioMode.Auto);
        MenuAudioAuto.IsChecked = true;
        MenuAudioForcePsg.IsChecked = false;
        MenuAudioForceFm.IsChecked = false;
    }

    private void OnAudioForcePsg_Click(object? sender, RoutedEventArgs e)
    {
        SetAudioMode(SmsAudioMode.ForcePsg);
        MenuAudioAuto.IsChecked = false;
        MenuAudioForcePsg.IsChecked = true;
        MenuAudioForceFm.IsChecked = false;
    }

    private void OnAudioForceFm_Click(object? sender, RoutedEventArgs e)
    {
        SetAudioMode(SmsAudioMode.ForceFm);
        MenuAudioAuto.IsChecked = false;
        MenuAudioForcePsg.IsChecked = false;
        MenuAudioForceFm.IsChecked = true;
    }

    private void SetAudioMode(SmsAudioMode mode)
    {
        if (_sms != null)
        {
            _sms.AudioMode = mode;
        }
        StatusText.Text = $"Audio mode set to: {mode}";
    }

    private void ShowErrorDialog(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brushes.Red },
                    new TextBlock { Text = message, FontSize = 13, TextWrapping = TextWrapping.Wrap }
                }
            }
        };

        dialog.ShowDialog(this);
    }

    private void OnHelpControls_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Controls & Hotkeys",
            Width = 400,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Sega Master System Controls", FontSize = 16, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = "• D-Pad Up / Down / Left / Right: Arrow Keys or W / S / A / D", FontSize = 13 },
                    new TextBlock { Text = "• Button 1 (TL): Z or J", FontSize = 13 },
                    new TextBlock { Text = "• Button 2 (TR): X or K", FontSize = 13 },
                    new TextBlock { Text = "• Console Pause (NMI): Space or P", FontSize = 13 },
                    new TextBlock { Text = "• Reset Machine: R or Ctrl+R", FontSize = 13 },
                    new TextBlock { Text = "• Open ROM: Ctrl+O", FontSize = 13 },
                    new TextBlock { Text = "• Drag & Drop: Drag any .sms ROM onto the main window", FontSize = 13 }
                }
            }
        };

        dialog.ShowDialog(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameTimer?.Stop();
        _audioPlayer?.Dispose();
        base.OnClosed(e);
    }
}
