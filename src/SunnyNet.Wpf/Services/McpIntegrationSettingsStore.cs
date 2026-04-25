using System.IO;
using System.Text.Json;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Services;

public static class McpIntegrationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static McpIntegrationSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new McpIntegrationSettings();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<McpIntegrationSettings>(json, JsonOptions) ?? new McpIntegrationSettings();
        }
        catch
        {
            return new McpIntegrationSettings();
        }
    }

    public static void Save(McpIntegrationSettings settings)
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
        return Path.Combine(appData, "SunnyNet.Wpf", "mcp.json");
    }
}
