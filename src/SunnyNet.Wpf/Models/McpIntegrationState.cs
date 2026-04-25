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

    private bool _enabled;
    private int _port = 29999;
    private bool _serverRunning;
    private int _toolCount;
    private string _serverStatusText = "未检测";
    private bool _bridgeExists;
    private string _lastCheckedText = "未刷新";
    private string _lastError = "";
    private string _clientKind = "Cursor / Claude";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value))
            {
                return;
            }

            RaiseDerivedStateProperties();
        }
    }

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
        set
        {
            if (!SetProperty(ref _serverRunning, value))
            {
                return;
            }

            RaiseDerivedStateProperties();
        }
    }

    public int ToolCount
    {
        get => _toolCount;
        set
        {
            if (!SetProperty(ref _toolCount, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FooterStatusText));
        }
    }

    public string ServerStatusText
    {
        get => _serverStatusText;
        set
        {
            if (!SetProperty(ref _serverStatusText, value ?? "未检测"))
            {
                return;
            }

            OnPropertyChanged(nameof(FooterStatusText));
        }
    }

    public bool BridgeExists
    {
        get => _bridgeExists;
        set
        {
            if (!SetProperty(ref _bridgeExists, value))
            {
                return;
            }

            RaiseDerivedStateProperties();
        }
    }

    public string BridgeStatusText => BridgeExists ? "桥接已就绪" : "缺少 sunnynet-mcp.exe";

    public string ClientKind
    {
        get => _clientKind;
        set
        {
            if (!SetProperty(ref _clientKind, value ?? "Cursor / Claude"))
            {
                return;
            }

            OnPropertyChanged(nameof(ClientConfigText));
            OnPropertyChanged(nameof(ClientConfigHintText));
        }
    }

    public string LastCheckedText
    {
        get => _lastCheckedText;
        set => SetProperty(ref _lastCheckedText, value ?? "未刷新");
    }

    public string LastError
    {
        get => _lastError;
        set
        {
            if (!SetProperty(ref _lastError, value ?? ""))
            {
                return;
            }

            OnPropertyChanged(nameof(FooterStatusText));
        }
    }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public string EndpointUrl => $"{BaseUrl}/mcp";

    public string HealthUrl => $"{BaseUrl}/mcp/health";

    public string SseUrl => $"{BaseUrl}/mcp/sse";

    public string BridgeExecutablePath => Path.Combine(AppContext.BaseDirectory, "mcp", "sunnynet-mcp.exe");

    public string BridgeDirectoryPath => Path.GetDirectoryName(BridgeExecutablePath) ?? "";

    public string FooterStatusText
    {
        get
        {
            if (!Enabled)
            {
                return "MCP 已关闭";
            }

            if (!ServerRunning)
            {
                return string.IsNullOrWhiteSpace(LastError) ? "MCP 未启动" : "MCP 启动失败";
            }

            return BridgeExists
                ? $"MCP 运行中 · {ToolCount} 项工具"
                : "MCP 运行中 · 缺少桥接";
        }
    }

    public string ClientConfigHintText => string.Equals(ClientKind, "Codex", StringComparison.Ordinal)
        ? @"追加到 `%USERPROFILE%\.codex\config.toml` 的 MCP 配置段。"
        : "适用于 Cursor / Claude Desktop 的 MCP 配置段。";

    public string ClientConfigText
    {
        get
        {
            if (string.Equals(ClientKind, "Codex", StringComparison.Ordinal))
            {
                return string.Join(Environment.NewLine,
                [
                    "[mcp_servers.sunnynet]",
                    $"command = \"{EscapeToml(BridgeExecutablePath)}\"",
                    $"args = [\"-port\", \"{Port}\"]"
                ]);
            }

            return JsonSerializer.Serialize(new
            {
                mcpServers = new Dictionary<string, object?>
                {
                    ["sunnynet"] = new
                    {
                        command = BridgeExecutablePath,
                        args = new[] { "-port", Port.ToString() }
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
        OnPropertyChanged(nameof(ClientConfigText));
    }

    private void RaiseDerivedStateProperties()
    {
        OnPropertyChanged(nameof(BridgeStatusText));
        OnPropertyChanged(nameof(FooterStatusText));
    }

    private static string EscapeToml(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
