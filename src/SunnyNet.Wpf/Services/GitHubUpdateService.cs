using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Services;

public static class GitHubUpdateService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/hmlyn/SunnyNet-wpf/releases/latest";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string CurrentVersionText => NormalizeVersionText(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0");

    public static async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SunnyNet-WPF");

        using HttpResponseMessage response = await client.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        GitHubReleaseDto release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub Release 返回为空。");

        string latestVersion = NormalizeVersionText(release.TagName);
        string currentVersion = CurrentVersionText;
        GitHubReleaseAssetDto? asset = release.Assets?
            .FirstOrDefault(static item => item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets?.FirstOrDefault();

        return new AppUpdateInfo(
            currentVersion,
            latestVersion,
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            release.HtmlUrl,
            release.Body ?? "",
            asset?.Name ?? "",
            asset?.BrowserDownloadUrl ?? "",
            CompareVersions(latestVersion, currentVersion) > 0);
    }

    private static int CompareVersions(string left, string right)
    {
        int[] leftParts = ParseVersionParts(left);
        int[] rightParts = ParseVersionParts(right);
        int count = Math.Max(leftParts.Length, rightParts.Length);
        for (int index = 0; index < count; index++)
        {
            int leftValue = index < leftParts.Length ? leftParts[index] : 0;
            int rightValue = index < rightParts.Length ? rightParts[index] : 0;
            int result = leftValue.CompareTo(rightValue);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private static int[] ParseVersionParts(string version)
    {
        string normalized = NormalizeVersionText(version);
        int suffixIndex = normalized.IndexOfAny(new[] { '-', '+' });
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return normalized
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0)
            .ToArray();
    }

    private static string NormalizeVersionText(string? version)
    {
        string value = (version ?? "").Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        int metadataIndex = value.IndexOf('+');
        if (metadataIndex > 0)
        {
            value = value[..metadataIndex];
        }

        return string.IsNullOrWhiteSpace(value) ? "0.0.0" : value;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";
        public string Name { get; set; } = "";
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
        public string? Body { get; set; }
        public List<GitHubReleaseAssetDto>? Assets { get; set; }
    }

    private sealed class GitHubReleaseAssetDto
    {
        public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
