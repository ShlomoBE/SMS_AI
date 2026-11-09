using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
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
    public const string AppVersion = "v2.1.0";

    [DllImport("winmm.dll")]
    private static extern int timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern int timeEndPeriod(uint uPeriod);

    private readonly object _emuLock = new();
    private SmsSystem? _sms;
    private WriteableBitmap? _bitmap;
    private AudioPlayer? _audioPlayer;
    private float[] _audioBuffer = new float[735];
    private bool _isPaused;
    private string? _loadedRomPath;
    private byte[]? _biosBytes;
    private DesktopSettings _settings = new();
    private int? _previewPsgVolume;
    private int? _previewFmVolume;
    private VideoStandard _currentVideo = VideoStandard.Ntsc;
    private SmsAudioMode _currentAudio = SmsAudioMode.Auto;

    private CancellationTokenSource? _emuCts;
    private Task? _emuTask;
    private CancellationTokenSource? _toastCts;

    private readonly Stopwatch _fpsStopwatch = new();
    private int _frameCount;
    private double _fps;
    private readonly DispatcherTimer _soundRoutingTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public MainWindow()
    {
        InitializeComponent();
        _soundRoutingTimer.Tick += (_, _) => UpdateSoundRouting();
        _soundRoutingTimer.Start();
        _settings = DesktopSettings.Load(out var settingsError);
        _currentVideo = _settings.VideoStandard;
        _currentAudio = _settings.AudioMode;
        UpdateModeMenus();
        if (settingsError != null) Opened += (_, _) => ShowErrorDialog("Settings", settingsError);
        Title = $"Sega Master System Emulator {AppVersion}";

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Deactivated += (_, _) =>
        {
            lock (_emuLock)
            {
                _sms?.SetButtonState(1, SmsButtons.Up | SmsButtons.Down | SmsButtons.Left | SmsButtons.Right | SmsButtons.Button1 | SmsButtons.Button2, false);
                _sms?.SetPauseState(false);
            }
        };

        _audioPlayer = new AudioPlayer();
        InitializeBitmap();
        _fpsStopwatch.Start();

        StartEmulationThread();
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

    // Poll on the UI thread rather than queueing another update for every frame.
    private void UpdateSoundRouting()
    {
        string routing;
        lock (_emuLock)
        {
            if (_sms == null)
                routing = "—";
            else
            {
                var mixer = _sms.AudioMixer;
                routing = (mixer.IsPsgEnabled, mixer.IsFmEnabled) switch
                {
                    (true, true) => "PSG + FM",
                    (true, false) => "PSG",
                    (false, true) => "FM",
                    _ => "muted"
                };
            }
        }
        SoundRoutingText.Text = $"Mixer: {routing}";
    }

    private void ShowToastNotification(string message, string icon = "ℹ️")
    {
        Dispatcher.UIThread.Post(() =>
        {
            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var token = _toastCts.Token;

            NotificationIcon.Text = icon;
            NotificationText.Text = message;
            NotificationToast.IsVisible = true;

            Task.Delay(2000, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        NotificationToast.IsVisible = false;
                    });
                }
            }, TaskScheduler.Default);
        });
    }

    private void StartEmulationThread()
    {
        _emuCts?.Cancel();
        _emuCts = new CancellationTokenSource();
        var token = _emuCts.Token;
        _emuTask = Task.Run(() => RunEmulationLoop(token), token);
    }

    private void RunEmulationLoop(CancellationToken token)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            timeBeginPeriod(1);
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            double frameTimeMs = 1000.0 / 60.0;
            double nextFrameTarget = stopwatch.Elapsed.TotalMilliseconds;

            while (!token.IsCancellationRequested)
            {
                uint[]? fbCopy = null;

                lock (_emuLock)
                {
                    var sms = _sms;
                    if (sms == null || _isPaused)
                    {
                        nextFrameTarget = stopwatch.Elapsed.TotalMilliseconds;
                    }
                    else
                    {
                        try
                        {
                            bool isNtsc = sms.VideoStandard == VideoStandard.Ntsc;
                            frameTimeMs = isNtsc ? (1000.0 / 60.0) : (1000.0 / 50.0);
                            int requiredSamples = isNtsc ? 735 : 882;

                            if (_audioBuffer.Length != requiredSamples)
                            {
                                _audioBuffer = new float[requiredSamples];
                            }

                            // Host gains are reapplied after reset or state load replaces the mixer.
                            sms.AudioMixer.PsgGain = (_previewPsgVolume ?? _settings.PsgVolumePercent) / 100f;
                            sms.AudioMixer.FmGain = (_previewFmVolume ?? _settings.FmVolumePercent) / 100f;

                            // Step 1 SMS frame
                            sms.StepFrame(_audioBuffer, 44100);

                            // Output audio PCM samples on background thread
                            _audioPlayer?.PlaySamples(_audioBuffer);

                            // Copy frame buffer for UI rendering
                            fbCopy = (uint[])sms.FrameBuffer.Clone();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Emulation step error: {ex.Message}");
                        }
                    }
                }

                if (fbCopy != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        UpdateFrameBitmap(fbCopy);
                    }, DispatcherPriority.Render);
                }
                else
                {
                    Thread.Sleep(10);
                }

                // High-precision pacing
                nextFrameTarget += frameTimeMs;
                double now = stopwatch.Elapsed.TotalMilliseconds;
                double sleepTimeMs = nextFrameTarget - now;

                if (sleepTimeMs > 1.0)
                {
                    Thread.Sleep((int)sleepTimeMs);
                }
                else if (sleepTimeMs < 0.0)
                {
                    nextFrameTarget = stopwatch.Elapsed.TotalMilliseconds;
                }
            }
        }
        finally
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                timeEndPeriod(1);
            }
        }
    }

    private void UpdateFrameBitmap(uint[] frameBuffer)
    {
        if (_bitmap == null) return;

        using (var lockHandle = _bitmap.Lock())
        {
            unsafe
            {
                uint* ptr = (uint*)lockHandle.Address;
                int count = Math.Min(frameBuffer.Length, SmsSystem.DisplayPixelCount);

                for (int i = 0; i < count; i++)
                {
                    uint c = frameBuffer[i];
                    byte r = (byte)((c >> 16) & 0xFF);
                    byte g = (byte)((c >> 8) & 0xFF);
                    byte b = (byte)(c & 0xFF);
                    ptr[i] = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
                }
            }
        }

        DisplayImage.InvalidateVisual();

        _frameCount++;
        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
        {
            _fps = _frameCount * 1000.0 / _fpsStopwatch.ElapsedMilliseconds;
            FpsText.Text = $"{_fps:0.0} FPS";
            _frameCount = 0;
            _fpsStopwatch.Restart();
        }
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

            lock (_emuLock)
            {
                var machine = new SmsSystem(romBytes, SegaMapperProfile.Sega3155235Sram8KiB,
                    bios: _biosBytes, videoStandard: _settings.VideoStandard, audioMode: _settings.AudioMode);
                // SmsSystem.Reset currently recreates the mixer, including during construction.
                machine.AudioMode = _settings.AudioMode;
                _sms = machine;
                _currentVideo = _settings.VideoStandard;
                _currentAudio = _settings.AudioMode;
                _loadedRomPath = filePath;
                _isPaused = false;
            }

            UpdateModeMenus();
            DropPrompt.IsVisible = false;
            StatusText.Text = $"Playing: {Path.GetFileName(filePath)}";
            RomInfoText.Text = $"{romBytes.Length / 1024} KB ROM";

            ShowToastNotification($"Loaded {Path.GetFileName(filePath)}", "🎮");
            Focus();
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
            lock (_emuLock)
            {
                if (_sms != null)
                {
                    _sms = new SmsSystem(
                        mapper: _sms.Mapper,
                        bios: biosBytes,
                        videoStandard: _sms.VideoStandard,
                        audioMode: _sms.AudioMode);
                    _sms.AudioMode = _currentAudio;
                }
                _biosBytes = biosBytes;
            }
            StatusText.Text = $"Loaded BIOS: {Path.GetFileName(filePath)}";
            ShowToastNotification($"Loaded BIOS {Path.GetFileName(filePath)}", "💾");
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Error Loading BIOS", ex.Message);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        lock (_emuLock)
        {
            if (_sms == null) return;

            bool handled = true;
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
                case Key.Space:
                    _sms.SetPauseState(true);
                    break;
                case Key.P:
                    TogglePause();
                    break;
                case Key.R:
                    ResetEmulation();
                    break;
                case Key.F5:
                    QuickSaveState();
                    break;
                case Key.F8:
                    QuickLoadState();
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
            }
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        lock (_emuLock)
        {
            if (_sms == null) return;

            bool handled = true;
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
                case Key.Space:
                    _sms.SetPauseState(false);
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                e.Handled = true;
            }
        }
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;
        StatusText.Text = _isPaused ? "Paused" : (_loadedRomPath != null ? $"Playing: {Path.GetFileName(_loadedRomPath)}" : "Ready");
        ShowToastNotification(_isPaused ? "Emulation Paused" : "Emulation Resumed", _isPaused ? "⏸️" : "▶️");
    }

    private void ResetEmulation()
    {
        lock (_emuLock)
        {
            _sms?.Reset();
            if (_sms != null) _sms.AudioMode = _currentAudio;
        }
        StatusText.Text = "System Reset";
        ShowToastNotification("System Reset", "🔄");
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

        try
        {
        var folderPath = _settings.ResolvedRomFolder;
        var folder = Directory.Exists(folderPath)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(folderPath))
            : null;
        if (folder == null && _settings.RomFolder != "Roms")
            ShowToastNotification("ROM folder unavailable; choose another folder", "⚠️");
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Sega Master System ROM",
            SuggestedStartLocation = folder,
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
        catch (Exception ex) { ShowErrorDialog("Open ROM", ex.Message); }
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
        lock (_emuLock)
        {
            if (_sms == null) return;
            try
            {
                string savesDir = _settings.ResolvedSavesFolder;
                Directory.CreateDirectory(savesDir);
                string savePath = Path.Combine(savesDir, "quicksave.state");
                Sms.Core.State.SmsStateSerializer.SaveStateToFile(_sms, savePath);
                StatusText.Text = "Quick State Saved (F5)";
                ShowToastNotification("Quick State Saved (F5)", "💾");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Save State Error", ex.Message);
            }
        }
    }

    private void QuickLoadState()
    {
        lock (_emuLock)
        {
            if (_sms == null) return;
            try
            {
                string savesDir = _settings.ResolvedSavesFolder;
                string savePath = Path.Combine(savesDir, "quicksave.state");
                if (!File.Exists(savePath))
                {
                    StatusText.Text = "No Quick Save State found.";
                    ShowToastNotification("No Save State found", "⚠️");
                    return;
                }
                Sms.Core.State.SmsStateSerializer.LoadStateFromFile(_sms, savePath);
                _currentVideo = _sms.VideoStandard;
                UpdateModeMenus();
                StatusText.Text = "Quick State Loaded (F8)";
                ShowToastNotification("Quick State Loaded (F8)", "📂");
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Load State Error", ex.Message);
            }
        }
    }

    private async void OnSaveStateFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_sms == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            string savesDir = _settings.ResolvedSavesFolder;
            Directory.CreateDirectory(savesDir);
            var folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(savesDir));

            string gameName = _loadedRomPath != null ? Path.GetFileNameWithoutExtension(_loadedRomPath) : "SMS";
            string defaultName = $"{gameName}_{DateTime.Now:yyyyMMdd_HHmm}.state";

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Emulator State",
                SuggestedStartLocation = folder,
                SuggestedFileName = defaultName,
                DefaultExtension = "state",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("SMS Save State (*.state)") { Patterns = new[] { "*.state" } }
                }
            });

            if (file != null)
            {
                lock (_emuLock)
                {
                    Sms.Core.State.SmsStateSerializer.SaveStateToFile(_sms, file.Path.LocalPath);
                }
                StatusText.Text = $"State saved to: {file.Name}";
                ShowToastNotification($"Saved to {file.Name}", "💾");
            }
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Save State Error", ex.Message);
        }
    }

    private async void OnLoadStateFile_Click(object? sender, RoutedEventArgs e)
    {
        if (_sms == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            string savesDir = _settings.ResolvedSavesFolder;
            var folder = Directory.Exists(savesDir)
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(savesDir))
                : null;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Emulator State",
                SuggestedStartLocation = folder,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("SMS Save State (*.state)") { Patterns = new[] { "*.state" } }
                }
            });

            if (files.Count > 0)
            {
                lock (_emuLock)
                {
                    Sms.Core.State.SmsStateSerializer.LoadStateFromFile(_sms, files[0].Path.LocalPath);
                    _currentVideo = _sms.VideoStandard;
                }
                UpdateModeMenus();
                StatusText.Text = $"State loaded from: {files[0].Name}";
                ShowToastNotification($"Loaded {files[0].Name}", "📂");
            }
        }
        catch (Exception ex)
        {
            ShowErrorDialog("Load State Error", ex.Message);
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

    private void OnEmulationPause_Click(object? sender, RoutedEventArgs e) => TogglePause();

    private void OnEmulationReset_Click(object? sender, RoutedEventArgs e) => ResetEmulation();

    private void ShowErrorDialog(string title, string message, Window? owner = null)
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

        dialog.ShowDialog(owner ?? this);
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
                    new TextBlock { Text = "• Console Pause (NMI): Space", FontSize = 13 },
                    new TextBlock { Text = "• Toggle Emulation Pause: P", FontSize = 13 },
                    new TextBlock { Text = "• Reset Machine: R", FontSize = 13 },
                    new TextBlock { Text = "• Quick Save / Load: F5 / F8", FontSize = 13 },
                    new TextBlock { Text = "• Open ROM: Ctrl+O", FontSize = 13 },
                    new TextBlock { Text = "• Drag & Drop: Drag any .sms ROM onto the main window", FontSize = 13 }
                }
            }
        };

        dialog.ShowDialog(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _soundRoutingTimer.Stop();
        _emuCts?.Cancel();
        _toastCts?.Cancel();
        _audioPlayer?.Dispose();
        base.OnClosed(e);
    }
}
