namespace SunnyNet.Wpf.Models;

public sealed class UiLayoutSettings
{
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = double.NaN;
    public double WindowHeight { get; set; } = double.NaN;
    public double WindowWorkAreaWidth { get; set; } = double.NaN;
    public double WindowWorkAreaHeight { get; set; } = double.NaN;
    public bool IsWindowMaximized { get; set; }
    public double WorkspaceSessionRatio { get; set; } = double.NaN;
    public double SessionFilterWidth { get; set; } = 190;
    public double DetailRequestRatio { get; set; } = double.NaN;
    public string CaptureScopeMode { get; set; } = "All";
    public bool ShowFavoritesOnly { get; set; }
    public List<string> FavoriteSessionKeys { get; set; } = new();
    public List<ProcessCaptureNameSetting> ProcessCaptureNames { get; set; } = new();
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;
    public Dictionary<string, bool> SessionColumns { get; set; } = new();
    public Dictionary<string, double> SessionColumnWidths { get; set; } = new();
}

public sealed class ProcessCaptureNameSetting
{
    public string Name { get; set; } = "";
    public bool IsCaptured { get; set; } = true;
}
