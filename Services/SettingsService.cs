using System.IO;
using System.Text.Json;
using AnkerGamesClient.Models;

namespace AnkerGamesClient.Services;

public class SettingsService
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnkerGamesClient");

    private static readonly string SettingsFile = Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettings Load()
    {
        Directory.CreateDirectory(AppDataDir);

        if (!File.Exists(SettingsFile))
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOpts));
    }
}
