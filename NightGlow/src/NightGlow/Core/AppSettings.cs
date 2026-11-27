using System.Text.Json;

namespace NightGlow.Core;

/// <summary>User preferences, stored as JSON in %LOCALAPPDATA%\NightGlow\settings.json.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";          // Dark | Light | System
    public string EffectId { get; set; } = "rainbow-wave";
    public string Color1 { get; set; } = "#35D6E8";
    public string Color2 { get; set; } = "#8B5CF6";
    public int Speed { get; set; } = 5;
    public int Brightness { get; set; } = 255;
    public int Duration { get; set; } = 4;               // 1..10, chassis per-light action length
    public Dictionary<string, string> ZoneColors { get; set; } = new(); // "VID:PID/id" -> #RRGGBB

    // spatial effects
    public List<string> SelectedRegions { get; set; } = new() { "Keyboard", "Ring", "Logo", "Other" };
    public string Direction { get; set; } = "LeftToRight";
    public string Palette { get; set; } = "rainbow";
    public bool AutoRain { get; set; } = true;

    /// <summary>"VID:PID/id" -> region assignment ("ring:3", "logo", "other", "keyboard:1", "none").</summary>
    public Dictionary<string, string> LightRegions { get; set; } = new();

    /// <summary>"VID:PID/id" -> key scan code (extended keys + 0xE000), from key calibration.</summary>
    public Dictionary<string, int> KeyMap { get; set; } = new();

    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightGlow");
    private static string FilePath => Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* corrupt file: start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are a convenience */ }
    }
}
