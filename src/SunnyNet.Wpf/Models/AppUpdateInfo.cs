namespace SunnyNet.Wpf.Models;

public sealed record AppUpdateInfo(
    string CurrentVersion,
    string LatestVersion,
    string ReleaseName,
    string ReleaseUrl,
    string ReleaseNotes,
    string AssetName,
    string AssetUrl,
    bool IsNewVersion);
