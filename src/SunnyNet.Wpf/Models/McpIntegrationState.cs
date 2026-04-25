using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class McpIntegrationState : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private int _port = 29999;
    private bool _serverRunning;
    private int _toolCount;
    private string _serverStatusText = "未检测";
    private string _bridgeExecutablePath = "";
    private bool _bridgeExists;
    private string _bridgeStatusText = "未配置";
    private string _lastCheckedText = "未刷新";
    private string _lastError = "";

    public int Port
    {
        get => _port;
        set
        {
            if (!SetProperty(ref _port, value))
            {
                return;
            }

            RaiseEndpointProperties();
        }
    }

    public bool ServerRunning
    {
        get => _serverRunning;
        set => SetProperty(ref _serverRunning, value);
    }

    public int ToolCount
    {
        get => _toolCount;
        set => SetProperty(ref _toolCount, value);
    }

    public string ServerStatusText
    {
        get => _serverStatusText;
        set => SetProperty(ref _serverStatusText, value ?? "未检测");
    }

    public string BridgeExecutablePath
    {
        get => _bridgeExecutablePath;
        set
        {
            if (!SetProperty(ref _bridgeExecutablePath, value ?? ""))
            {
                return;
            }

            OnPropertyChanged(nameof(BridgeDirectoryPath));
            OnPropertyChanged(nameof(ClientConfigJson));
        }
    }

    public bool BridgeExists
    {
        get => _bridgeExists;
        set => SetProperty(ref _bridgeExists, value);
    }

    public string BridgeStatusText
    {
        get => _bridgeStatusText;
        set => SetProperty(ref _bridgeStatusText, value ?? "未配置");
    }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        set => SetProperty(ref _lastCheckedText, value ?? "未刷新");
    }

    public string LastError
    {
        get => _lastError;
        set => SetProperty(ref _lastError, value ?? "");
    }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public string EndpointUrl => $"{BaseUrl}/mcp";

    public string HealthUrl => $"{BaseUrl}/mcp/health";

    public string SseUrl => $"{BaseUrl}/mcp/sse";

    public string BridgeDirectoryPath => string.IsNullOrWhiteSpace(BridgeExecutablePath)
        ? ""
        : Path.GetDirectoryName(BridgeExecutablePath) ?? "";

    public string ClientConfigJson
    {
        get
        {
            string command = string.IsNullOrWhiteSpace(BridgeExecutablePath)
                ? @"C:\path\to\sunnynet-mcp.exe"
                : BridgeExecutablePath;

            return JsonSerializer.Serialize(new
            {
                mcpServers = new Dictionary<string, object?>
                {
                    ["sunnynet"] = new
                    {
                        command,
                        args = Array.Empty<string>()
                    }
                }
            }, JsonOptions);
        }
    }

    private void RaiseEndpointProperties()
    {
        OnPropertyChanged(nameof(BaseUrl));
        OnPropertyChanged(nameof(EndpointUrl));
        OnPropertyChanged(nameof(HealthUrl));
        OnPropertyChanged(nameof(SseUrl));
    }
}
