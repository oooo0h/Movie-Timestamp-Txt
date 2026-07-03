using System.IO;
using System.Text.Json;

namespace MovieTimestampNotes.App.Services;

public sealed class AppSettings
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool IsExpanded { get; set; } = true;
    public string? MicrophoneName { get; set; }
    public string TimelineHotkey { get; set; } = "Ctrl+Alt+Space";
    public string PushToTalkHotkey { get; set; } = "Ctrl+Alt+V";
    public string ToggleVoiceHotkey { get; set; } = "Ctrl+Alt+B";
    public string FocusInputHotkey { get; set; } = "Ctrl+Alt+I";
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "电影时间戳文本框");
        _path = Path.Combine(directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, _path, true);
    }
}
