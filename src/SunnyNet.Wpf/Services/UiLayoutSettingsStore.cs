using System.IO;
using System.Text.Json;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Services;

public static class UiLayoutSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static UiLayoutSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new UiLayoutSettings();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UiLayoutSettings>(json, JsonOptions) ?? new UiLayoutSettings();
        }
        catch
        {
            return new UiLayoutSettings();
        }
    }

    public static void Save(UiLayoutSettings settings)
    {
        try
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
        }
    }

    private static string GetSettingsPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SunnyNet.Wpf", "layout.json");
    }
}
