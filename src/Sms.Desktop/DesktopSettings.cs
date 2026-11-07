using System.Text.Json;
using Sms.Core.Audio;
using Sms.Core.System;

namespace Sms.Desktop;

internal sealed class DesktopSettings
{
    public VideoStandard VideoStandard { get; set; } = VideoStandard.Ntsc;
    public SmsAudioMode AudioMode { get; set; } = SmsAudioMode.Auto;
    public string RomFolder { get; set; } = "Roms";
    public string SavesFolder { get; set; } = "Saves";
    public int PsgVolumePercent { get; set; } = 100;
    public int FmVolumePercent { get; set; } = 100;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sms.Desktop", "settings.json");

    public string ResolvedRomFolder => Path.GetFullPath(RomFolder, AppContext.BaseDirectory);
    public string ResolvedSavesFolder => Path.GetFullPath(SavesFolder, AppContext.BaseDirectory);

    public static DesktopSettings Load(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(SettingsPath)) return new DesktopSettings();
            var settings = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(SettingsPath));
            if (settings == null || !Enum.IsDefined(settings.VideoStandard) || !Enum.IsDefined(settings.AudioMode)
                || string.IsNullOrWhiteSpace(settings.RomFolder)
                || settings.PsgVolumePercent is < 0 or > 200 || settings.FmVolumePercent is < 0 or > 200)
                throw new InvalidDataException("Settings contain invalid values.");

            if (string.IsNullOrWhiteSpace(settings.SavesFolder))
            {
                settings.SavesFolder = "Saves";
            }

            _ = settings.ResolvedRomFolder;
            _ = settings.ResolvedSavesFolder;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            error = $"Could not read settings; built-in defaults are in use. {ex.Message}";
            return new DesktopSettings();
        }
    }

    public void Save()
    {
        string path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
