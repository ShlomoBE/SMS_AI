using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Sms.Core.Audio;
using Sms.Core.System;

namespace Sms.Desktop;

public partial class MainWindow
{
    private void UpdateModeMenus()
    {
        MenuVideoNtsc.IsChecked = _currentVideo == VideoStandard.Ntsc;
        MenuVideoPal.IsChecked = _currentVideo == VideoStandard.Pal;
        MenuAudioAuto.IsChecked = _currentAudio == SmsAudioMode.Auto;
        MenuAudioForcePsg.IsChecked = _currentAudio == SmsAudioMode.ForcePsg;
        MenuAudioForceFm.IsChecked = _currentAudio == SmsAudioMode.ForceFm;
    }

    private void OnVideoNtsc_Click(object? s, RoutedEventArgs e) => SetCurrentModes(VideoStandard.Ntsc, _currentAudio);
    private void OnVideoPal_Click(object? s, RoutedEventArgs e) => SetCurrentModes(VideoStandard.Pal, _currentAudio);
    private void OnAudioAuto_Click(object? s, RoutedEventArgs e) => SetCurrentModes(_currentVideo, SmsAudioMode.Auto);
    private void OnAudioForcePsg_Click(object? s, RoutedEventArgs e) => SetCurrentModes(_currentVideo, SmsAudioMode.ForcePsg);
    private void OnAudioForceFm_Click(object? s, RoutedEventArgs e) => SetCurrentModes(_currentVideo, SmsAudioMode.ForceFm);

    private void SetCurrentModes(VideoStandard video, SmsAudioMode audio)
    {
        try
        {
            bool audioChanged = audio != _currentAudio;
            lock (_emuLock)
            {
                if (_sms != null && video != _currentVideo)
                {
                    _sms.VideoStandard = video;
                    _sms.AudioMode = audio;
                }
                else if (_sms != null)
                {
                    _sms.AudioMode = audio;
                }
                _currentVideo = video;
                _currentAudio = audio;
            }
            UpdateModeMenus();
            StatusText.Text = $"Current session: {video}, {audio}";
            if (audioChanged && _sms != null)
                ShowToastNotification("Sound driver may require Reset to change", "🔊");
        }
        catch (Exception ex) { UpdateModeMenus(); ShowErrorDialog("Cannot change mode", ex.Message); }
    }

    private async void OnSettings_Click(object? sender, RoutedEventArgs e)
    {
        var video = new ComboBox { ItemsSource = new[] { "NTSC", "PAL" }, SelectedIndex = _settings.VideoStandard == VideoStandard.Pal ? 1 : 0 };
        var audio = new ComboBox { ItemsSource = new[] { "Automatic", "Force PSG", "Force FM" }, SelectedIndex = _settings.AudioMode switch { SmsAudioMode.ForcePsg => 1, SmsAudioMode.ForceFm => 2, _ => 0 } };
        var psgVolume = new Slider { Minimum = 0, Maximum = 200, Value = _settings.PsgVolumePercent, TickFrequency = 1, IsSnapToTickEnabled = true };
        var fmVolume = new Slider { Minimum = 0, Maximum = 200, Value = _settings.FmVolumePercent, TickFrequency = 1, IsSnapToTickEnabled = true };
        var psgLabel = new TextBlock { Text = $"PSG volume: {_settings.PsgVolumePercent}%" };
        var fmLabel = new TextBlock { Text = $"FM volume: {_settings.FmVolumePercent}%" };
        psgVolume.ValueChanged += (_, _) =>
        {
            int value = (int)Math.Round(psgVolume.Value);
            psgLabel.Text = $"PSG volume: {value}%";
            lock (_emuLock) { _previewPsgVolume = value; }
        };
        fmVolume.ValueChanged += (_, _) =>
        {
            int value = (int)Math.Round(fmVolume.Value);
            fmLabel.Text = $"FM volume: {value}%";
            lock (_emuLock) { _previewFmVolume = value; }
        };
        var folder = new TextBox { Text = _settings.RomFolder };
        var browse = new Button { Content = "Browse..." };
        var savesFolder = new TextBox { Text = _settings.SavesFolder };
        var savesBrowse = new Button { Content = "Browse..." };
        var save = new Button { Content = "Save", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window { Title = "Settings — defaults for newly loaded ROMs", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Icon = Icon };
        browse.Click += async (_, _) =>
        {
            try
            {
                var picked = await dialog.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose default ROM folder", AllowMultiple = false });
                if (picked.Count > 0)
                {
                    var path = picked[0].TryGetLocalPath();
                    if (path == null) ShowErrorDialog("ROM folder", "Please select a local folder.", dialog);
                    else folder.Text = path;
                }
            }
            catch (Exception ex) { ShowErrorDialog("ROM folder", ex.Message, dialog); }
        };
        savesBrowse.Click += async (_, _) =>
        {
            try
            {
                var picked = await dialog.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose default Save States folder", AllowMultiple = false });
                if (picked.Count > 0)
                {
                    var path = picked[0].TryGetLocalPath();
                    if (path == null) ShowErrorDialog("Saves folder", "Please select a local folder.", dialog);
                    else savesFolder.Text = path;
                }
            }
            catch (Exception ex) { ShowErrorDialog("Saves folder", ex.Message, dialog); }
        };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder.Text)) throw new ArgumentException("Choose a ROM folder or enter Roms for the default.");
                if (string.IsNullOrWhiteSpace(savesFolder.Text)) throw new ArgumentException("Choose a Saves folder or enter Saves for the default.");
                int psgPercent = (int)Math.Round(psgVolume.Value);
                int fmPercent = (int)Math.Round(fmVolume.Value);
                var next = new DesktopSettings { VideoStandard = video.SelectedIndex == 1 ? VideoStandard.Pal : VideoStandard.Ntsc, AudioMode = audio.SelectedIndex switch { 1 => SmsAudioMode.ForcePsg, 2 => SmsAudioMode.ForceFm, _ => SmsAudioMode.Auto }, RomFolder = folder.Text.Trim(), SavesFolder = savesFolder.Text.Trim(), PsgVolumePercent = psgPercent, FmVolumePercent = fmPercent };
                _ = next.ResolvedRomFolder;
                _ = next.ResolvedSavesFolder;
                next.Save();
                lock (_emuLock) { _settings = next; }
                dialog.Close();
                ShowToastNotification("Volume applied; defaults saved");
            }
            catch (Exception ex) { ShowErrorDialog("Cannot save settings", ex.Message, dialog); }
        };
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children = {
            new TextBlock { Text = "Volume changes are heard immediately; save to keep them, or cancel to restore. Video and audio mode defaults apply when a ROM is loaded.", TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Video standard" }, video,
            new TextBlock { Text = "Audio mode" }, audio,
            psgLabel, psgVolume,
            fmLabel, fmVolume,
            new TextBlock { Text = "Default ROM folder (relative to the application folder)" }, folder, browse,
            new TextBlock { Text = "Default Save States folder (relative to the application folder)" }, savesFolder, savesBrowse,
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { save, cancel } }
        }};
        try { await dialog.ShowDialog(this); }
        finally
        {
            lock (_emuLock) { _previewPsgVolume = null; _previewFmVolume = null; }
        }
    }
}
