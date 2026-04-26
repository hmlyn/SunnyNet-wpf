using System.IO;
using System.Text.Json;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Services;

public static class RequestBuilderHistoryStore
{
    private const int MaxHistoryCount = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static List<RequestBuilderHistoryItem> Load()
    {
        try
        {
            string path = GetHistoryPath();
            if (!File.Exists(path))
            {
                return new List<RequestBuilderHistoryItem>();
            }

            string json = File.ReadAllText(path);
            return (JsonSerializer.Deserialize<List<RequestBuilderHistoryItem>>(json, JsonOptions) ?? new List<RequestBuilderHistoryItem>())
                .OrderByDescending(static item => item.CreatedAt)
                .Take(MaxHistoryCount)
                .ToList();
        }
        catch
        {
            return new List<RequestBuilderHistoryItem>();
        }
    }

    public static void Save(IReadOnlyCollection<RequestBuilderHistoryItem> history)
    {
        try
        {
            string path = GetHistoryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            List<RequestBuilderHistoryItem> items = history
                .OrderByDescending(static item => item.CreatedAt)
                .Take(MaxHistoryCount)
                .ToList();
            File.WriteAllText(path, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch
        {
        }
    }

    public static List<RequestBuilderHistoryItem> Add(RequestBuilderHistoryItem item)
    {
        List<RequestBuilderHistoryItem> history = Load();
        history.RemoveAll(existing =>
            string.Equals(existing.Method, item.Method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Url, item.Url, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Headers, item.Headers, StringComparison.Ordinal)
            && string.Equals(existing.Body, item.Body, StringComparison.Ordinal)
            && string.Equals(existing.BodyFormat, item.BodyFormat, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.HttpVersion, item.HttpVersion, StringComparison.OrdinalIgnoreCase));

        item.Id = Guid.NewGuid().ToString("N");
        item.CreatedAt = DateTimeOffset.Now;
        history.Insert(0, item);
        Save(history);
        return history;
    }

    public static List<RequestBuilderHistoryItem> Remove(string id)
    {
        List<RequestBuilderHistoryItem> history = Load();
        history.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        Save(history);
        return history;
    }

    public static List<RequestBuilderHistoryItem> UpdateNotes(string id, string notes)
    {
        List<RequestBuilderHistoryItem> history = Load();
        RequestBuilderHistoryItem? item = history.FirstOrDefault(existing => string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            item.Notes = notes;
            Save(history);
        }

        return history;
    }

    public static void Clear()
    {
        Save(Array.Empty<RequestBuilderHistoryItem>());
    }

    private static string GetHistoryPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "SunnyNet", "request-builder-history.json");
    }
}
