using System.IO;
using System.Text.Json;

namespace MaddenSaveConverter;

/// <summary>
/// Tiny persisted app settings (settings.json in the app folder) so configuration
/// travel with the portable app. Uses a capped-size JSON file; safe if missing or
/// corrupt (defaults apply).
/// </summary>
public static class AppSettings
{
    static string Path_ => Path.Combine(AppContext.BaseDirectory, "settings.json");

    static bool _showIntro = false;  // default = off
    static bool _hasIntroPref;       // false until the user decisively chooses

    public static bool ShowIntroOnBoot => _showIntro;

    /// <summary>True once the user has answered the intro prompt (default first-boot state).</summary>
    public static bool HasIntroPref => _hasIntroPref;

    static AppSettings()
    {
        try
        {
            if (!File.Exists(Path_)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path_));
            var root = doc.RootElement;
            if (root.TryGetProperty("ShowIntroOnBoot", out var s))
            {
                if (s.ValueKind == JsonValueKind.Number) _showIntro = s.GetInt32() != 0;
                else if (s.ValueKind == JsonValueKind.True || s.ValueKind == JsonValueKind.False) _showIntro = s.GetBoolean();
                _hasIntroPref = true;
            }
        }
        catch { /* corrupt settings -> defaults */ }
    }

    /// <summary>Persist the intro preference (true = play each boot, false = don't).</summary>
    public static void SetIntroEnabled(bool enabled)
    {
        _showIntro = enabled;
        _hasIntroPref = true;
        try
        {
            var data = new { ShowIntroOnBoot = enabled ? 1 : 0 };
            File.WriteAllText(Path_, JsonSerializer.Serialize(data));
        }
        catch { /* best-effort persist */ }
    }
}