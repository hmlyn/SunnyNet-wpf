using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Windows;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Services;

public sealed class SunnyNetCompatibleMcpServer : IAsyncDisposable
{
    public const int DefaultPort = 29999;
    private const int RequestBodyPreviewLimit = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly MainWindowViewModel _viewModel;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, Channel<string>> _sseClients = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<McpToolDefinition> _tools;
    private readonly Dictionary<string, McpToolDefinition> _toolMap;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private long _clientSequence;

    static SunnyNetCompatibleMcpServer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SunnyNetCompatibleMcpServer(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _tools = BuildTools();
        _toolMap = _tools.ToDictionary(static item => item.Name, StringComparer.Ordinal);
    }

    public int Port { get; private set; } = DefaultPort;

    public bool IsRunning => _listener.IsListening;

    public void Start(int port = DefaultPort)
    {
        if (IsRunning)
        {
            return;
        }

        Port = port <= 0 ? DefaultPort : port;
        _listener.Prefixes.Clear();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts?.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            foreach ((_, Channel<string> channel) in _sseClients)
            {
                channel.Writer.TryComplete();
            }

            _sseClients.Clear();

            if (_serverTask is not null)
            {
                await _serverTask.ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _listener.Close();
            _cts?.Dispose();
            _cts = null;
            _serverTask = null;
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested || !_listener.IsListening)
                {
                    break;
                }

                continue;
            }

            _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            context.Response.Headers["Access-Control-Max-Age"] = "86400";

            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 200;
                context.Response.Close();
                return;
            }

            string path = context.Request.Url?.AbsolutePath ?? "/";
            switch (path)
            {
                case "/mcp":
                case "/mcp/message":
                    await HandleJsonRpcAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                case "/mcp/health":
                    await WriteJsonAsync(context.Response, new
                    {
                        status = "ok",
                        server = "SunnyNet-MCP",
                        version = "1.0.0",
                        running = IsRunning
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                case "/mcp/sse":
                    await HandleSseAsync(context, cancellationToken).ConfigureAwait(false);
                    return;
                default:
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context.Response, new { error = "Not Found" }, cancellationToken).ConfigureAwait(false);
                    return;
            }
        }
        catch (Exception exception)
        {
            try
            {
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context.Response, new { error = exception.Message }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private async Task HandleSseAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 405;
            context.Response.Close();
            return;
        }

        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";
        context.Response.SendChunked = true;

        string clientId = $"sse-{Interlocked.Increment(ref _clientSequence)}";
        Channel<string> channel = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _sseClients[clientId] = channel;

        try
        {
            await using StreamWriter writer = new(context.Response.OutputStream, new UTF8Encoding(false));
            string endpoint = $"http://127.0.0.1:{Port}/mcp/message?sessionId={clientId}";
            await writer.WriteAsync($"event: endpoint\ndata: {endpoint}\n\n").ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                Task<string> readTask = channel.Reader.ReadAsync(cancellationToken).AsTask();
                Task delayTask = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                Task completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
                if (completed == readTask)
                {
                    string message = await readTask.ConfigureAwait(false);
                    await writer.WriteAsync($"event: message\ndata: {message}\n\n").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteAsync(": heartbeat\n\n").ConfigureAwait(false);
                }

                await writer.FlushAsync().ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _sseClients.TryRemove(clientId, out _);
            channel.Writer.TryComplete();
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }

    private async Task HandleJsonRpcAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 405;
            await WriteJsonAsync(context.Response, CreateErrorResponse(default, -32601, "仅支持 POST 方法", null), cancellationToken).ConfigureAwait(false);
            return;
        }

        JsonRpcRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(context.Request.InputStream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context.Response, CreateErrorResponse(default, -32700, "解析错误", exception.Message), cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request is null)
        {
            context.Response.StatusCode = 400;
            await WriteJsonAsync(context.Response, CreateErrorResponse(default, -32600, "无效的请求", "请求体为空"), cancellationToken).ConfigureAwait(false);
            return;
        }

        object response = await DispatchJsonRpcAsync(request).ConfigureAwait(false);
        string responseJson = JsonSerializer.Serialize(response, JsonOptions);

        string? sessionId = context.Request.QueryString["sessionId"];
        if (!string.IsNullOrWhiteSpace(sessionId) && _sseClients.TryGetValue(sessionId, out Channel<string>? channel))
        {
            channel.Writer.TryWrite(responseJson);
        }

        await WriteRawJsonAsync(context.Response, responseJson, cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> DispatchJsonRpcAsync(JsonRpcRequest request)
    {
        object? id = ToJsonId(request.Id);
        if (!string.Equals(request.JsonRpc, "2.0", StringComparison.Ordinal))
        {
            return CreateErrorResponse(id, -32600, "无效的请求", "必须使用 JSON-RPC 2.0");
        }

        return request.Method switch
        {
            "initialize" => CreateSuccessResponse(id, new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new
                    {
                        listChanged = true
                    }
                },
                serverInfo = new
                {
                    name = "SunnyNet-MCP",
                    version = "1.0.0"
                }
            }),
            "initialized" => CreateSuccessResponse(id, new { }),
            "notifications/initialized" => CreateSuccessResponse(id, new { }),
            "ping" => CreateSuccessResponse(id, new { }),
            "tools/list" => CreateSuccessResponse(id, new
            {
                tools = _tools.Select(static tool => new
                {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = tool.InputSchema
                }).ToArray()
            }),
            "tools/call" => await HandleToolsCallAsync(id, request.Params).ConfigureAwait(false),
            _ => CreateErrorResponse(id, -32601, "方法不存在", $"未知方法: {request.Method}")
        };
    }

    private async Task<object> HandleToolsCallAsync(object? id, JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            return CreateErrorResponse(id, -32602, "无效的参数", "缺少必要参数");
        }

        string toolName = GetString(parameters, "name");
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return CreateErrorResponse(id, -32602, "无效的参数", "缺少工具名称");
        }

        JsonElement arguments = GetProperty(parameters, "arguments");
        if (arguments.ValueKind == JsonValueKind.Undefined)
        {
            arguments = default;
        }

        try
        {
            string content = await ExecuteToolAsync(toolName, arguments).ConfigureAwait(false);
            return CreateSuccessResponse(id, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = content
                    }
                },
                isError = false
            });
        }
        catch (Exception exception)
        {
            return CreateSuccessResponse(id, new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = $"错误: {exception.Message}"
                    }
                },
                isError = true
            });
        }
    }

    private async Task<string> ExecuteToolAsync(string toolName, JsonElement arguments)
    {
        if (!_toolMap.TryGetValue(toolName, out McpToolDefinition? tool))
        {
            throw new InvalidOperationException($"未知工具: {toolName}");
        }

        object result = await InvokeOnUiAsync(() => tool.Handler(arguments)).ConfigureAwait(false);
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private IReadOnlyList<McpToolDefinition> BuildTools()
    {
        return new[]
        {
            Tool("config_get", "获取 SunnyNet 当前配置信息", NoParamsSchema(), ToolConfigGetAsync),

            Tool("proxy_start", "启动 SunnyNet 代理服务", NoParamsSchema(), ToolProxyStartAsync),
            Tool("proxy_stop", "停止 SunnyNet 代理服务", NoParamsSchema(), ToolProxyStopAsync),
            Tool("proxy_set_port", "设置代理端口号", Schema(
                Property("port", "integer", "代理端口号 (1-65535)")), ToolProxySetPortAsync, "port"),
            Tool("proxy_get_status", "获取代理服务状态，包括运行状态、端口号等信息", NoParamsSchema(), ToolProxyGetStatusAsync),
            Tool("proxy_set_ie", "设置系统 IE 代理", NoParamsSchema(), ToolProxySetIeAsync),
            Tool("proxy_unset_ie", "取消系统 IE 代理", NoParamsSchema(), ToolProxyUnsetIeAsync),
            Tool("proxy_pause_capture", "暂停捕获", NoParamsSchema(), ToolProxyPauseCaptureAsync),
            Tool("proxy_resume_capture", "恢复捕获", NoParamsSchema(), ToolProxyResumeCaptureAsync),

            Tool("process_list", "获取当前系统运行的进程列表", NoParamsSchema(), ToolProcessListAsync),
            Tool("process_add_name", "添加要拦截的进程名", Schema(
                Property("name", "string", "进程名称（如：chrome.exe）")), ToolProcessAddNameAsync, "name"),
            Tool("process_remove_name", "移除已添加的拦截进程名", Schema(
                Property("name", "string", "进程名称（如：chrome.exe）")), ToolProcessRemoveNameAsync, "name"),

            Tool("cert_install", "安装 SunnyNet 默认 CA 证书", NoParamsSchema(), ToolCertInstallAsync),
            Tool("cert_export", "导出 SunnyNet 默认 CA 证书到指定路径", Schema(
                Property("path", "string", "证书导出路径")), ToolCertExportAsync, "path"),

            Tool("hosts_list", "列出所有 HOSTS 规则", NoParamsSchema(), ToolHostsListAsync),
            Tool("hosts_add", "添加 HOSTS 规则", Schema(
                Property("source", "string", "源地址正则模式"),
                Property("target", "string", "目标地址")), ToolHostsAddAsync, "source", "target"),
            Tool("hosts_remove", "删除指定索引的 HOSTS 规则", Schema(
                Property("index", "integer", "规则索引")), ToolHostsRemoveAsync, "index"),

            Tool("replace_rules_list", "列出当前所有替换规则", NoParamsSchema(), ToolReplaceRulesListAsync),
            Tool("replace_rules_add", "添加新的替换规则", Schema(
                EnumProperty("type", "string", new[] { "Base64", "HEX", "String(UTF8)", "String(GBK)", "响应文件" }, "替换类型", "String(UTF8)"),
                Property("source", "string", "源内容"),
                Property("target", "string", "替换内容")), ToolReplaceRulesAddAsync, "source"),
            Tool("replace_rules_remove", "删除指定的替换规则", Schema(
                Property("hash", "string", "规则 Hash"),
                Property("index", "integer", "规则索引")), ToolReplaceRulesRemoveAsync),
            Tool("replace_rules_clear", "清空所有替换规则", NoParamsSchema(), ToolReplaceRulesClearAsync),

            Tool("breakpoint_add", "添加断点条件，匹配的请求将被自动拦截", Schema(
                Property("url_pattern", "string", "URL 正则模式")), ToolBreakpointAddAsync, "url_pattern"),
            Tool("breakpoint_list", "列出所有断点条件", NoParamsSchema(), ToolBreakpointListAsync),
            Tool("breakpoint_remove", "删除指定索引的断点条件", Schema(
                Property("index", "integer", "断点条件索引")), ToolBreakpointRemoveAsync, "index"),
            Tool("breakpoint_clear", "清空所有断点条件", NoParamsSchema(), ToolBreakpointClearAsync),

            Tool("request_list", "获取已捕获的 HTTP 请求列表", Schema(
                Property("limit", "integer", "返回最大数量"),
                Property("offset", "integer", "偏移量"),
                Property("favoritesOnly", "boolean", "仅返回已收藏会话")), ToolRequestListAsync),
            Tool("request_favorites_list", "列出已收藏的会话", Schema(
                Property("limit", "integer", "返回最大数量"),
                Property("offset", "integer", "偏移量")), ToolRequestFavoritesListAsync),
            Tool("request_get", "获取指定请求的详细信息", Schema(
                Property("theology", "integer", "请求唯一 ID")), ToolRequestGetAsync, "theology"),
            Tool("request_favorite_add", "收藏指定会话", Schema(
                Property("theology", "integer", "请求唯一 ID")), ToolRequestFavoriteAddAsync, "theology"),
            Tool("request_favorite_remove", "取消收藏指定会话", Schema(
                Property("theology", "integer", "请求唯一 ID")), ToolRequestFavoriteRemoveAsync, "theology"),
            Tool("request_notes_set", "设置指定会话备注，传空字符串可清除备注", Schema(
                Property("theology", "integer", "请求唯一 ID"),
                Property("notes", "string", "备注内容，空字符串表示清除")), ToolRequestNotesSetAsync, "theology", "notes"),
            Tool("request_modify_header", "修改指定请求的请求头", Schema(
                Property("theology", "integer", "请求唯一 ID"),
                Property("key", "string", "请求头名称"),
                Property("value", "string", "请求头值")), ToolRequestModifyHeaderAsync, "theology", "key", "value"),
            Tool("request_modify_body", "修改指定请求的请求体", Schema(
                Property("theology", "integer", "请求唯一 ID"),
                Property("body", "string", "新的请求体内容")), ToolRequestModifyBodyAsync, "theology", "body"),
            Tool("response_modify_header", "修改指定请求的响应头", Schema(
                Property("theology", "integer", "请求唯一 ID"),
                Property("key", "string", "响应头名称"),
                Property("value", "string", "响应头值")), ToolResponseModifyHeaderAsync, "theology", "key", "value"),
            Tool("response_modify_body", "修改指定请求的响应体", Schema(
                Property("theology", "integer", "请求唯一 ID"),
                Property("body", "string", "新的响应体内容")), ToolResponseModifyBodyAsync, "theology", "body"),
            Tool("request_block", "阻断/拦截指定的请求", Schema(
                Property("theology", "integer", "请求唯一 ID")), ToolRequestBlockAsync, "theology"),
            Tool("request_release_all", "放行所有被断点拦截的请求", NoParamsSchema(), ToolRequestReleaseAllAsync),
            Tool("request_save_all", "保存全部已捕获的请求数据到 .syn 文件", Schema(
                Property("path", "string", "保存文件路径")), ToolRequestSaveAllAsync, "path"),
            Tool("request_import", "从 .syn 文件导入抓包记录", Schema(
                Property("path", "string", ".syn 文件路径")), ToolRequestImportAsync, "path"),
            Tool("request_search", "搜索过滤已捕获的请求", Schema(
                Property("url", "string", "URL 关键词"),
                Property("method", "string", "HTTP 方法"),
                Property("status_code", "integer", "状态码"),
                Property("favoritesOnly", "boolean", "仅返回已收藏会话"),
                Property("limit", "integer", "返回最大数量"),
                Property("offset", "integer", "偏移量")), ToolRequestSearchAsync),
            Tool("request_delete", "删除指定的请求记录", Schema(
                ArrayProperty("theologies", "integer", "要删除的请求 ID 列表")), ToolRequestDeleteAsync, "theologies"),
            Tool("request_clear", "清空全部已捕获的请求列表", NoParamsSchema(), ToolRequestClearAsync),

            Tool("request_resend", "重发指定的 HTTP 请求", Schema(
                ArrayProperty("theologies", "integer", "要重发的请求 ID 列表")), ToolRequestResendAsync, "theologies"),
            Tool("request_stats", "获取当前抓包会话的统计信息", NoParamsSchema(), ToolRequestStatsAsync),
            Tool("request_body_search", "在所有已捕获请求的 URL 或 Body 中搜索关键词", Schema(
                Property("keyword", "string", "搜索关键词"),
                EnumProperty("scope", "string", new[] { "url", "request_body", "response_body", "all" }, "搜索范围", "all"),
                Property("favoritesOnly", "boolean", "仅搜索已收藏会话"),
                Property("limit", "integer", "返回最大数量")), ToolRequestBodySearchAsync, "keyword"),
            Tool("request_get_response_body_decoded", "获取指定请求的响应体并自动处理编码", Schema(
                Property("theology", "integer", "请求唯一 ID")), ToolRequestGetResponseBodyDecodedAsync, "theology"),
            Tool("request_highlight_search", "搜索关键词并在 UI 中高亮匹配的请求行", Schema(
                Property("keyword", "string", "搜索关键词"),
                EnumProperty("type", "string", new[] { "UTF8", "GBK", "Hex" }, "搜索类型", "UTF8"),
                Property("color", "string", "高亮颜色")), ToolRequestHighlightSearchAsync, "keyword"),
            Tool("request_highlight_clear", "清除所有搜索高亮标记", NoParamsSchema(), ToolRequestHighlightClearAsync),

            Tool("socket_data_list", "获取 WebSocket/TCP/UDP 连接的数据包列表", Schema(
                Property("theology", "integer", "连接唯一 ID"),
                Property("limit", "integer", "返回最大数量"),
                Property("offset", "integer", "偏移量")), ToolSocketDataListAsync, "theology"),
            Tool("socket_data_get", "获取指定 WebSocket/TCP/UDP 数据包详情", Schema(
                Property("theology", "integer", "连接唯一 ID"),
                Property("index", "integer", "数据包索引")), ToolSocketDataGetAsync, "theology", "index"),
            Tool("socket_data_get_range", "批量获取多个数据包详情", Schema(
                Property("theology", "integer", "连接唯一 ID"),
                Property("start", "integer", "起始索引"),
                Property("end", "integer", "结束索引")), ToolSocketDataGetRangeAsync, "theology", "start"),
            Tool("connection_list", "列出所有 WebSocket/TCP/UDP 长连接", Schema(
                EnumProperty("protocol", "string", new[] { "websocket", "tcp", "udp" }, "协议过滤", ""),
                Property("favoritesOnly", "boolean", "仅返回已收藏会话"),
                Property("limit", "integer", "返回最大数量"),
                Property("offset", "integer", "偏移量")), ToolConnectionListAsync)
        };
    }

    private async Task<object> ToolConfigGetAsync(JsonElement args)
    {
        await Task.CompletedTask;
        return new
        {
            success = true,
            port = _viewModel.Settings.Port,
            disableUDP = _viewModel.Settings.DisableUdp,
            disableTCP = _viewModel.Settings.DisableTcp,
            disableCache = _viewModel.Settings.DisableCache,
            authentication = _viewModel.Settings.Authentication,
            globalProxy = _viewModel.Settings.GlobalProxy,
            globalProxyRules = _viewModel.Settings.GlobalProxyRules,
            mustTcpOpen = _viewModel.Settings.MustTcpOpen,
            mustTcpRules = _viewModel.Settings.MustTcpRules,
            certDefault = string.Equals(_viewModel.Settings.CertMode, "默认证书", StringComparison.OrdinalIgnoreCase),
            certCaPath = _viewModel.Settings.CaFilePath,
            certKeyPath = _viewModel.Settings.KeyFilePath,
            replaceRulesCount = _viewModel.ReplaceRuleItems.Count,
            hostsRulesCount = _viewModel.HostsRuleItems.Count,
            darkTheme = false,
            requestCertManager = _viewModel.RequestCertificateItems.Count
        };
    }

    private async Task<object> ToolProxyStartAsync(JsonElement args)
    {
        if (_viewModel.RunningPort > 0)
        {
            return new
            {
                success = true,
                port = _viewModel.RunningPort,
                message = "代理服务已启动"
            };
        }

        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("启动代理服务");
        bool ok = result is { ValueKind: JsonValueKind.Object } && GetBool(result.Value, "ok");
        string error = result is { ValueKind: JsonValueKind.Object } ? DecodeBase64Text(GetString(result.Value, "err")) : "";
        int port = await RefreshRunningPortAsync().ConfigureAwait(true);
        if (!ok && port <= 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "启动代理服务失败" : error);
        }

        return new
        {
            success = true,
            port = port <= 0 ? _viewModel.Settings.Port : port,
            message = "代理服务已启动"
        };
    }

    private async Task<object> ToolProxyStopAsync(JsonElement args)
    {
        await _viewModel.InvokeBackendCommandAsync("停止代理服务");
        _viewModel.RunningPort = 0;
        _viewModel.StatusRight = "代理服务已停止";
        return new
        {
            success = true,
            message = "代理服务已停止"
        };
    }

    private async Task<object> ToolProxySetPortAsync(JsonElement args)
    {
        int port = RequireInt(args, "port");
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException("端口号必须在 1-65535 之间");
        }

        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("修改端口号", new { Port = port });
        bool ok = result is { ValueKind: JsonValueKind.Object } && GetBool(result.Value, "ok");
        string error = result is { ValueKind: JsonValueKind.Object } ? DecodeBase64Text(GetString(result.Value, "err")) : "";
        if (!ok && !string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(error);
        }

        _viewModel.Settings.Port = port;
        int runningPort = await RefreshRunningPortAsync().ConfigureAwait(true);
        return new
        {
            success = true,
            port = runningPort <= 0 ? port : runningPort,
            message = $"端口已设置为 {port}"
        };
    }

    private async Task<object> ToolProxyGetStatusAsync(JsonElement args)
    {
        await Task.CompletedTask;
        bool running = _viewModel.RunningPort > 0;
        string error = running ? "" : (_viewModel.StatusRight.StartsWith("启动失败", StringComparison.OrdinalIgnoreCase) ? _viewModel.StatusRight : "");
        return new
        {
            running,
            port = running ? _viewModel.RunningPort : _viewModel.Settings.Port,
            error,
            disableUDP = _viewModel.Settings.DisableUdp,
            disableTCP = _viewModel.Settings.DisableTcp,
            disableCache = _viewModel.Settings.DisableCache,
            authentication = _viewModel.Settings.Authentication,
            globalProxy = _viewModel.Settings.GlobalProxy,
            capturing = _viewModel.IsCapturing
        };
    }

    private async Task<object> ToolProxySetIeAsync(JsonElement args)
    {
        if (!_viewModel.IeProxyEnabled)
        {
            JsonElement? result = await _viewModel.InvokeBackendCommandAsync("设置IE代理", new { Set = true });
            if (result is { ValueKind: JsonValueKind.False })
            {
                throw new InvalidOperationException("设置系统 IE 代理失败");
            }
        }

        _viewModel.IeProxyEnabled = true;
        _viewModel.StatusLeft = "已设置系统IE代理";
        return new
        {
            success = true,
            message = "已设置系统IE代理"
        };
    }

    private async Task<object> ToolProxyUnsetIeAsync(JsonElement args)
    {
        if (_viewModel.IeProxyEnabled)
        {
            JsonElement? result = await _viewModel.InvokeBackendCommandAsync("设置IE代理", new { Set = false });
            if (result is { ValueKind: JsonValueKind.False })
            {
                throw new InvalidOperationException("取消系统 IE 代理失败");
            }
        }

        _viewModel.IeProxyEnabled = false;
        _viewModel.StatusLeft = "未设置系统IE代理";
        return new
        {
            success = true,
            message = "已取消系统IE代理"
        };
    }

    private async Task<object> ToolProxyPauseCaptureAsync(JsonElement args)
    {
        if (_viewModel.IsCapturing)
        {
            await _viewModel.InvokeBackendCommandAsync("工作状态", new { State = false });
            _viewModel.IsCapturing = false;
            _viewModel.StatusMiddle = "隐藏捕获";
        }

        return new
        {
            success = true,
            capturing = false,
            message = "已暂停捕获"
        };
    }

    private async Task<object> ToolProxyResumeCaptureAsync(JsonElement args)
    {
        if (!_viewModel.IsCapturing)
        {
            await _viewModel.InvokeBackendCommandAsync("工作状态", new { State = true });
            _viewModel.IsCapturing = true;
            _viewModel.StatusMiddle = "正在捕获";
        }

        return new
        {
            success = true,
            capturing = true,
            message = "已恢复捕获"
        };
    }

    private async Task<object> ToolProcessListAsync(JsonElement args)
    {
        await _viewModel.RefreshRunningProcessesAsync();
        Dictionary<string, string> processes = _viewModel.RunningProcesses
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Pid)
            .ToDictionary(static item => item.Pid.ToString(), static item => item.Name, StringComparer.Ordinal);
        return new
        {
            success = true,
            processes
        };
    }

    private async Task<object> ToolProcessAddNameAsync(JsonElement args)
    {
        string name = RequireString(args, "name").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("进程名称不能为空");
        }

        if (_viewModel.CaptureAllProcesses)
        {
            throw new InvalidOperationException("当前已启用全部进程捕获，请先关闭后再指定进程名");
        }

        if (!_viewModel.ProcessDriverLoaded)
        {
            JsonElement? driver = await _viewModel.InvokeBackendCommandAsync("加载驱动");
            bool driverLoaded = driver is { ValueKind: JsonValueKind.True };
            _viewModel.ProcessDriverLoaded = driverLoaded;
            if (!driverLoaded)
            {
                throw new InvalidOperationException("加载驱动失败，请确认当前程序具有管理员权限");
            }
        }

        if (_viewModel.ProcessCaptureNames.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return new
            {
                success = true,
                name,
                message = $"已添加进程名: {name}"
            };
        }

        await _viewModel.InvokeBackendCommandAsync("进程驱动添加进程名", new { Name = name, isSet = true });
        _viewModel.ProcessCaptureNames.Add(new ProcessCaptureNameItem { Name = name });
        _viewModel.StatusRight = $"已添加进程名：{name}";
        return new
        {
            success = true,
            name,
            message = $"已添加进程名: {name}"
        };
    }

    private async Task<object> ToolProcessRemoveNameAsync(JsonElement args)
    {
        string name = RequireString(args, "name").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("进程名称不能为空");
        }

        await _viewModel.InvokeBackendCommandAsync("进程驱动添加进程名", new { Name = name, isSet = false });
        ProcessCaptureNameItem? item = _viewModel.ProcessCaptureNames.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            _viewModel.ProcessCaptureNames.Remove(item);
        }

        _viewModel.StatusRight = $"已移除进程名：{name}";
        return new
        {
            success = true,
            name,
            message = $"已移除进程名: {name}"
        };
    }

    private async Task<object> ToolCertInstallAsync(JsonElement args)
    {
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("安装默认证书静默");
        string message = result is { ValueKind: JsonValueKind.String } ? result.Value.GetString() ?? "" : "";
        bool success = message.Contains("成功", StringComparison.OrdinalIgnoreCase)
            || message.Contains("success", StringComparison.OrdinalIgnoreCase);
        return new
        {
            success,
            message
        };
    }

    private async Task<object> ToolCertExportAsync(JsonElement args)
    {
        string path = RequireString(args, "path");
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("导出默认证书静默", new { Path = path });
        bool ok = result is { ValueKind: JsonValueKind.Object } && GetBool(result.Value, "Ok");
        string savedPath = result is { ValueKind: JsonValueKind.Object } ? GetString(result.Value, "Path") : path;
        string error = result is { ValueKind: JsonValueKind.Object } ? GetString(result.Value, "Error") : "";
        if (!ok)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "导出证书失败" : error);
        }

        return new
        {
            success = true,
            path = savedPath,
            message = "证书已导出"
        };
    }

    private async Task<object> ToolHostsListAsync(JsonElement args)
    {
        await Task.CompletedTask;
        return new
        {
            success = true,
            rules = _viewModel.HostsRuleItems.Select((item, index) => new
            {
                index,
                source = item.Source,
                target = item.Target,
                hash = item.Hash
            }).ToArray(),
            total = _viewModel.HostsRuleItems.Count
        };
    }

    private async Task<object> ToolHostsAddAsync(JsonElement args)
    {
        HostsRuleItem item = new()
        {
            Source = RequireString(args, "source"),
            Target = RequireString(args, "target")
        };
        _viewModel.HostsRuleItems.Add(item);
        HashSet<string> failures = await SaveHostsRulesAsync();
        if (failures.Contains(item.Hash))
        {
            _viewModel.HostsRuleItems.Remove(item);
            throw new InvalidOperationException("HOSTS 规则保存失败，请检查源地址正则与目标地址");
        }

        return new
        {
            success = true,
            rule = new
            {
                index = _viewModel.HostsRuleItems.IndexOf(item),
                source = item.Source,
                target = item.Target,
                hash = item.Hash
            },
            message = "HOSTS规则已添加并立即生效"
        };
    }

    private async Task<object> ToolHostsRemoveAsync(JsonElement args)
    {
        int index = RequireInt(args, "index");
        if (index < 0 || index >= _viewModel.HostsRuleItems.Count)
        {
            throw new InvalidOperationException($"索引 {index} 超出范围");
        }

        HostsRuleItem item = _viewModel.HostsRuleItems[index];
        _viewModel.HostsRuleItems.RemoveAt(index);
        await SaveHostsRulesAsync();
        return new
        {
            success = true,
            removed = new
            {
                source = item.Source,
                target = item.Target,
                hash = item.Hash
            },
            message = "HOSTS规则已删除并立即生效"
        };
    }

    private async Task<object> ToolReplaceRulesListAsync(JsonElement args)
    {
        await Task.CompletedTask;
        return new
        {
            success = true,
            rules = _viewModel.ReplaceRuleItems.Select((item, index) => new
            {
                index,
                type = item.RuleType,
                src = item.SourceContent,
                dest = item.ReplacementContent,
                hash = item.Hash
            }).ToArray(),
            total = _viewModel.ReplaceRuleItems.Count
        };
    }

    private async Task<object> ToolReplaceRulesAddAsync(JsonElement args)
    {
        ReplaceRuleItem item = new()
        {
            RuleType = GetString(args, "type", "String(UTF8)"),
            SourceContent = RequireString(args, "source"),
            ReplacementContent = GetString(args, "target")
        };
        _viewModel.ReplaceRuleItems.Add(item);
        HashSet<string> failures = await SaveReplaceRulesAsync();
        if (failures.Contains(item.Hash))
        {
            _viewModel.ReplaceRuleItems.Remove(item);
            throw new InvalidOperationException("替换规则保存失败，请检查规则类型与输入内容");
        }

        return new
        {
            success = true,
            rule = new
            {
                index = _viewModel.ReplaceRuleItems.IndexOf(item),
                type = item.RuleType,
                src = item.SourceContent,
                dest = item.ReplacementContent,
                hash = item.Hash
            },
            message = "替换规则已添加"
        };
    }

    private async Task<object> ToolReplaceRulesRemoveAsync(JsonElement args)
    {
        string hash = GetString(args, "hash");
        int index = GetInt(args, "index", -1);
        ReplaceRuleItem? item = null;
        if (index >= 0)
        {
            if (index >= _viewModel.ReplaceRuleItems.Count)
            {
                throw new InvalidOperationException($"索引 {index} 超出范围");
            }

            item = _viewModel.ReplaceRuleItems[index];
        }
        else if (!string.IsNullOrWhiteSpace(hash))
        {
            item = _viewModel.ReplaceRuleItems.FirstOrDefault(rule => string.Equals(rule.Hash, hash, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                throw new InvalidOperationException($"未找到 Hash 为 {hash} 的规则");
            }
        }
        else
        {
            throw new InvalidOperationException("请提供 hash 或 index 参数之一");
        }

        _viewModel.ReplaceRuleItems.Remove(item);
        await SaveReplaceRulesAsync();
        return new
        {
            success = true,
            message = "替换规则已删除"
        };
    }

    private async Task<object> ToolReplaceRulesClearAsync(JsonElement args)
    {
        int count = _viewModel.ReplaceRuleItems.Count;
        _viewModel.ReplaceRuleItems.Clear();
        await SaveReplaceRulesAsync();
        return new
        {
            success = true,
            cleared = count,
            message = $"已清空 {count} 条替换规则"
        };
    }

    private async Task<object> ToolBreakpointAddAsync(JsonElement args)
    {
        string pattern = RequireString(args, "url_pattern");
        InterceptRuleItem item = new()
        {
            Enabled = true,
            Name = $"{BreakpointNamePrefix}{pattern}",
            Direction = "上行",
            Target = "URL",
            Operator = "正则",
            Value = pattern
        };
        _viewModel.InterceptRuleItems.Add(item);
        await SaveInterceptRulesAsync();
        InterceptRuleItem[] breakpoints = GetBreakpointRules();
        return new
        {
            success = true,
            condition = new
            {
                index = Array.IndexOf(breakpoints, item),
                url_pattern = pattern
            },
            message = "断点条件已添加"
        };
    }

    private async Task<object> ToolBreakpointListAsync(JsonElement args)
    {
        await Task.CompletedTask;
        InterceptRuleItem[] items = GetBreakpointRules();
        return new
        {
            success = true,
            conditions = items.Select((item, index) => new
            {
                index,
                url_pattern = item.Value
            }).ToArray(),
            total = items.Length
        };
    }

    private async Task<object> ToolBreakpointRemoveAsync(JsonElement args)
    {
        int index = RequireInt(args, "index");
        InterceptRuleItem[] items = GetBreakpointRules();
        if (index < 0 || index >= items.Length)
        {
            throw new InvalidOperationException($"索引 {index} 超出范围");
        }

        InterceptRuleItem target = items[index];
        _viewModel.InterceptRuleItems.Remove(target);
        await SaveInterceptRulesAsync();
        return new
        {
            success = true,
            removed = new
            {
                url_pattern = target.Value
            },
            message = "断点条件已删除"
        };
    }

    private async Task<object> ToolBreakpointClearAsync(JsonElement args)
    {
        InterceptRuleItem[] items = GetBreakpointRules();
        foreach (InterceptRuleItem item in items)
        {
            _viewModel.InterceptRuleItems.Remove(item);
        }

        await SaveInterceptRulesAsync();
        return new
        {
            success = true,
            cleared = items.Length,
            message = $"已清空 {items.Length} 条断点条件"
        };
    }

    private async Task<object> ToolRequestListAsync(JsonElement args)
    {
        await Task.CompletedTask;
        int limit = Math.Max(1, GetInt(args, "limit", 100));
        int offset = Math.Max(0, GetInt(args, "offset", 0));
        bool favoritesOnly = GetBool(args, "favoritesOnly");
        CaptureEntry[] ordered = _viewModel.GetSessionSnapshot()
            .Where(entry => !favoritesOnly || entry.IsFavorite)
            .OrderByDescending(static entry => entry.Theology)
            .ToArray();
        CaptureEntry[] paged = ordered.Skip(offset).Take(limit).ToArray();
        return new
        {
            success = true,
            total = ordered.Length,
            offset,
            limit,
            requests = paged.Select(ToRequestInfo).ToArray()
        };
    }

    private async Task<object> ToolRequestFavoritesListAsync(JsonElement args)
    {
        await Task.CompletedTask;
        int limit = Math.Max(1, GetInt(args, "limit", 100));
        int offset = Math.Max(0, GetInt(args, "offset", 0));
        CaptureEntry[] ordered = _viewModel.GetSessionSnapshot()
            .Where(static entry => entry.IsFavorite)
            .OrderByDescending(static entry => entry.Theology)
            .ToArray();
        CaptureEntry[] paged = ordered.Skip(offset).Take(limit).ToArray();
        return new
        {
            success = true,
            total = ordered.Length,
            offset,
            limit,
            requests = paged.Select(ToRequestInfo).ToArray()
        };
    }

    private async Task<object> ToolRequestGetAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);

        byte[] requestBodyBytes = DecodePayloadBytes(GetProperty(payload, "Body"));
        JsonElement response = GetProperty(payload, "Response");
        byte[] responseBodyBytes = DecodePayloadBytes(GetProperty(response, "Body"));
        (string requestBodyText, string requestBodyB64) = EncodeBody(requestBodyBytes, RequestBodyPreviewLimit);
        (string responseBodyText, string responseBodyB64) = EncodeBody(responseBodyBytes, RequestBodyPreviewLimit);

        return new
        {
            index = entry.Index,
            theology = entry.Theology,
            method = GetString(payload, "Method", entry.Method),
            url = GetString(payload, "URL", entry.Url),
            proto = GetString(payload, "Proto", "HTTP/1.1"),
            request = new
            {
                headers = ToHeaderDictionary(GetProperty(payload, "Header")),
                body = requestBodyText,
                bodyBase64 = requestBodyB64
            },
            response = new
            {
                statusCode = GetInt(response, "StateCode", ExtractStatusCode(entry.State)),
                headers = ToHeaderDictionary(GetProperty(response, "Header")),
                body = responseBodyText,
                bodyBase64 = responseBodyB64,
                error = GetBool(response, "Error")
            },
            clientIP = entry.ClientAddress,
            pid = entry.Process,
            sendTime = entry.SendTime,
            recTime = entry.ReceiveTime,
            way = GetSessionWay(entry),
            notes = entry.Notes,
            isFavorite = entry.IsFavorite
        };
    }

    private async Task<object> ToolRequestFavoriteAddAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        bool changed = EnsureFavoriteState(entry, shouldFavorite: true);
        PersistFavoriteSettings();
        await Task.CompletedTask;
        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            isFavorite = entry.IsFavorite,
            changed,
            message = changed ? "会话已收藏" : "会话已在收藏列表中"
        };
    }

    private async Task<object> ToolRequestFavoriteRemoveAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        bool changed = EnsureFavoriteState(entry, shouldFavorite: false);
        PersistFavoriteSettings();
        await Task.CompletedTask;
        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            isFavorite = entry.IsFavorite,
            changed,
            message = changed ? "会话已取消收藏" : "会话当前未收藏"
        };
    }

    private async Task<object> ToolRequestNotesSetAsync(JsonElement args)
    {
        if (!HasProperty(args, "notes"))
        {
            throw new InvalidOperationException("缺少参数: notes");
        }

        CaptureEntry entry = RequireSessionEntry(args);
        string notes = GetString(args, "notes");
        await _viewModel.UpdateSessionNotesAsync(new[] { entry }, notes);
        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            notes = entry.Notes,
            cleared = string.IsNullOrWhiteSpace(entry.Notes),
            message = string.IsNullOrWhiteSpace(entry.Notes) ? "会话备注已清除" : "会话备注已更新"
        };
    }

    private async Task<object> ToolRequestModifyHeaderAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);
        List<HeaderPair> headers = ToHeaderPairs(GetProperty(payload, "Header"));
        string key = RequireString(args, "key");
        string value = RequireString(args, "value");
        headers.RemoveAll(item => string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase));
        headers.Add(new HeaderPair(key, value));

        await _viewModel.InvokeBackendCommandAsync("保存修改数据", new
        {
            Theology = entry.Theology,
            Type = "Request",
            Tabs = "Headers",
            UTF8 = true,
            Data = headers.Select(static item => new Dictionary<string, object?>
            {
                ["名称"] = item.Name,
                ["值"] = item.Value
            }).ToArray()
        });

        if (_viewModel.SelectedSession?.Theology == entry.Theology)
        {
            await _viewModel.ReloadSessionDetailAsync(entry.Theology);
        }

        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            key,
            value,
            message = "请求头已修改"
        };
    }

    private async Task<object> ToolRequestModifyBodyAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        string body = RequireString(args, "body");
        await _viewModel.InvokeBackendCommandAsync("保存修改数据", new
        {
            Theology = entry.Theology,
            Type = "Request",
            Tabs = "Body",
            UTF8 = true,
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(body))
        });

        if (_viewModel.SelectedSession?.Theology == entry.Theology)
        {
            await _viewModel.ReloadSessionDetailAsync(entry.Theology);
        }

        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            message = "请求体已修改"
        };
    }

    private async Task<object> ToolResponseModifyHeaderAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);
        JsonElement response = GetProperty(payload, "Response");
        List<HeaderPair> headers = ToHeaderPairs(GetProperty(response, "Header"));
        string key = RequireString(args, "key");
        string value = RequireString(args, "value");
        headers.RemoveAll(item => string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase));
        headers.Add(new HeaderPair(key, value));

        await _viewModel.InvokeBackendCommandAsync("保存修改数据", new
        {
            Theology = entry.Theology,
            Type = "Response",
            Tabs = "Headers",
            UTF8 = true,
            Data = headers.Select(static item => new Dictionary<string, object?>
            {
                ["名称"] = item.Name,
                ["值"] = item.Value
            }).ToArray()
        });

        if (_viewModel.SelectedSession?.Theology == entry.Theology)
        {
            await _viewModel.ReloadSessionDetailAsync(entry.Theology);
        }

        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            key,
            value,
            message = "响应头已修改"
        };
    }

    private async Task<object> ToolResponseModifyBodyAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        string body = RequireString(args, "body");
        await _viewModel.InvokeBackendCommandAsync("保存修改数据", new
        {
            Theology = entry.Theology,
            Type = "Response",
            Tabs = "Body",
            UTF8 = true,
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(body))
        });

        if (_viewModel.SelectedSession?.Theology == entry.Theology)
        {
            await _viewModel.ReloadSessionDetailAsync(entry.Theology);
        }

        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            message = "响应体已修改"
        };
    }

    private async Task<object> ToolRequestBlockAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("设置请求断点", new { Theology = entry.Theology });
        if (result is { ValueKind: JsonValueKind.False })
        {
            throw new InvalidOperationException($"请求 {entry.Theology} 不存在");
        }

        entry.BreakMode = Math.Max(entry.BreakMode, 1);
        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            message = "请求已被阻断，等待处理"
        };
    }

    private async Task<object> ToolRequestReleaseAllAsync(JsonElement args)
    {
        await _viewModel.ReleaseAllQuietAsync();
        return new
        {
            success = true,
            message = "所有请求已放行"
        };
    }

    private async Task<object> ToolRequestSaveAllAsync(JsonElement args)
    {
        string path = RequireString(args, "path");
        await _viewModel.SaveCaptureFileAsync(path, true);
        return new
        {
            success = true,
            path,
            message = "全部抓包数据已保存"
        };
    }

    private async Task<object> ToolRequestImportAsync(JsonElement args)
    {
        string path = RequireString(args, "path");
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("导入记录文件静默", new { Path = path });
        if (result is { ValueKind: JsonValueKind.Object })
        {
            bool ok = GetBool(result.Value, "Ok");
            string error = GetString(result.Value, "Error");
            int importedCount = GetInt(result.Value, "Imported");
            string importedPath = GetString(result.Value, "Path", path);
            if (!ok)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "导入失败" : error);
            }

            return new
            {
                success = true,
                path = importedPath,
                imported = importedCount,
                message = $"已导入 {importedCount} 条记录"
            };
        }

        int before = _viewModel.Sessions.Count;
        await _viewModel.OpenCaptureFileAsync(path);
        await Task.Delay(250).ConfigureAwait(true);
        int imported = Math.Max(0, _viewModel.Sessions.Count - before);
        return new
        {
            success = true,
            path,
            imported,
            message = imported > 0 ? $"已导入 {imported} 条记录" : "导入请求已提交"
        };
    }

    private async Task<object> ToolRequestSearchAsync(JsonElement args)
    {
        await Task.CompletedTask;
        string url = GetString(args, "url");
        string method = GetString(args, "method");
        int limit = Math.Max(1, GetInt(args, "limit", 50));
        int offset = Math.Max(0, GetInt(args, "offset", 0));
        bool filterStatus = HasProperty(args, "status_code");
        int statusCode = GetInt(args, "status_code");
        bool favoritesOnly = GetBool(args, "favoritesOnly");

        CaptureEntry[] matched = _viewModel.GetSessionSnapshot()
            .Where(entry =>
                (!favoritesOnly || entry.IsFavorite)
                &&
                (string.IsNullOrWhiteSpace(url) || entry.Url.Contains(url, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrWhiteSpace(method) || string.Equals(entry.Method, method, StringComparison.OrdinalIgnoreCase) || string.Equals(entry.DisplayMethod, method, StringComparison.OrdinalIgnoreCase))
                && (!filterStatus || ExtractStatusCode(entry.State) == statusCode))
            .OrderByDescending(static entry => entry.Theology)
            .ToArray();

        return new
        {
            success = true,
            total = matched.Length,
            offset,
            limit,
            requests = matched.Skip(offset).Take(limit).Select(ToRequestInfo).ToArray()
        };
    }

    private async Task<object> ToolRequestDeleteAsync(JsonElement args)
    {
        int[] theologies = RequireIntArray(args, "theologies");
        CaptureEntry[] entries = theologies
            .Select(_viewModel.FindSessionEntry)
            .Where(static entry => entry is not null)
            .Cast<CaptureEntry>()
            .ToArray();
        await _viewModel.DeleteSessionEntriesAsync(entries);
        return new
        {
            success = true,
            count = theologies.Length,
            message = $"已删除 {theologies.Length} 条请求记录"
        };
    }

    private async Task<object> ToolRequestClearAsync(JsonElement args)
    {
        await _viewModel.ClearSessionsQuietAsync();
        return new
        {
            success = true,
            message = "已清空全部请求列表"
        };
    }

    private async Task<object> ToolRequestResendAsync(JsonElement args)
    {
        int[] theologies = RequireIntArray(args, "theologies");
        CaptureEntry[] entries = theologies
            .Select(_viewModel.FindSessionEntry)
            .Where(static entry => entry is not null)
            .Cast<CaptureEntry>()
            .ToArray();
        await _viewModel.ResendSessionEntriesAsync(entries, 0);
        return new
        {
            success = true,
            count = entries.Length,
            message = $"已发起 {entries.Length} 个请求的重发"
        };
    }

    private async Task<object> ToolRequestStatsAsync(JsonElement args)
    {
        await Task.CompletedTask;
        Dictionary<string, int> byProtocol = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> byStatus = new(StringComparer.OrdinalIgnoreCase);
        int favoriteCount = 0;
        int total = 0;
        foreach (CaptureEntry entry in _viewModel.GetSessionSnapshot())
        {
            total++;
            if (entry.IsFavorite)
            {
                favoriteCount++;
            }

            string way = GetSessionWay(entry);
            byProtocol[way] = byProtocol.TryGetValue(way, out int protocolCount) ? protocolCount + 1 : 1;

            int statusCode = ExtractStatusCode(entry.State);
            if (statusCode is >= 200 and < 300)
            {
                byStatus["2xx"] = byStatus.TryGetValue("2xx", out int count) ? count + 1 : 1;
            }
            else if (statusCode is >= 300 and < 400)
            {
                byStatus["3xx"] = byStatus.TryGetValue("3xx", out int count) ? count + 1 : 1;
            }
            else if (statusCode is >= 400 and < 500)
            {
                byStatus["4xx"] = byStatus.TryGetValue("4xx", out int count) ? count + 1 : 1;
            }
            else if (statusCode >= 500)
            {
                byStatus["5xx"] = byStatus.TryGetValue("5xx", out int count) ? count + 1 : 1;
            }
            else
            {
                byStatus["pending"] = byStatus.TryGetValue("pending", out int count) ? count + 1 : 1;
            }
        }

        return new
        {
            success = true,
            total,
            favoriteCount,
            byProtocol,
            byStatus
        };
    }

    private async Task<object> ToolRequestBodySearchAsync(JsonElement args)
    {
        string keyword = RequireString(args, "keyword");
        string scope = GetString(args, "scope", "all");
        int limit = Math.Max(1, GetInt(args, "limit", 20));
        bool favoritesOnly = GetBool(args, "favoritesOnly");
        List<CaptureEntry> matchedEntries = new();

        foreach (CaptureEntry entry in _viewModel.GetSessionSnapshot().OrderByDescending(static item => item.Theology))
        {
            if (favoritesOnly && !entry.IsFavorite)
            {
                continue;
            }

            bool matched = false;
            if ((scope == "all" || scope == "url") && entry.Url.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                matched = true;
            }

            if (!matched && scope is "all" or "request_body" or "response_body")
            {
                JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);
                if ((scope == "all" || scope == "request_body") && ContainsBytes(DecodePayloadBytes(GetProperty(payload, "Body")), keyword))
                {
                    matched = true;
                }

                if (!matched)
                {
                    JsonElement response = GetProperty(payload, "Response");
                    if ((scope == "all" || scope == "response_body") && ContainsBytes(DecodePayloadBytes(GetProperty(response, "Body")), keyword))
                    {
                        matched = true;
                    }
                }
            }

            if (!matched)
            {
                continue;
            }

            matchedEntries.Add(entry);
        }

        object[] results = matchedEntries
            .Take(limit)
            .Select(entry => new
            {
                index = entry.Index,
                theology = entry.Theology,
                method = entry.Method,
                url = entry.Url,
                statusCode = ExtractStatusCode(entry.State),
                way = GetSessionWay(entry),
                sendTime = entry.SendTime,
                notes = entry.Notes,
                isFavorite = entry.IsFavorite
            })
            .Cast<object>()
            .ToArray();

        return new
        {
            success = true,
            keyword,
            scope,
            total = matchedEntries.Count,
            requests = results
        };
    }

    private async Task<object> ToolRequestGetResponseBodyDecodedAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);
        JsonElement response = GetProperty(payload, "Response");
        JsonElement responseHeaders = GetProperty(response, "Header");
        byte[] body = DecodePayloadBytes(GetProperty(response, "Body"));
        string contentType = GetHeaderValue(responseHeaders, "Content-Type");
        string contentEncoding = GetHeaderValue(responseHeaders, "Content-Encoding").ToLowerInvariant();

        Dictionary<string, object?> result = new()
        {
            ["index"] = entry.Index,
            ["theology"] = entry.Theology,
            ["statusCode"] = GetInt(response, "StateCode", ExtractStatusCode(entry.State)),
            ["length"] = body.Length,
            ["contentType"] = contentType
        };

        if (body.Length == 0)
        {
            result["body"] = "";
            result["encoding"] = "empty";
            return result;
        }

        byte[] decoded = DecompressBody(body, contentEncoding);
        if (!string.IsNullOrWhiteSpace(contentEncoding) && decoded.Length != body.Length)
        {
            result["decompressed"] = true;
            result["decompressedLength"] = decoded.Length;
        }

        decoded = ConvertCharset(decoded, contentType);
        if (IsPrintable(decoded))
        {
            string text = Encoding.UTF8.GetString(decoded);
            if (LooksLikeJson(contentType, decoded) && TryPrettyJson(decoded, out string pretty))
            {
                text = pretty;
                result["formatted"] = true;
            }

            result["body"] = text;
            result["encoding"] = "text";
        }
        else
        {
            result["bodyBase64"] = Convert.ToBase64String(decoded);
            result["encoding"] = "base64";
        }

        return result;
    }

    private async Task<object> ToolRequestHighlightSearchAsync(JsonElement args)
    {
        string keyword = RequireString(args, "keyword");
        SearchExecutionResult result = await _viewModel.SearchQuietAsync(new SearchRequest(
            keyword,
            GetString(args, "type", "UTF8"),
            "全部",
            GetString(args, "color", "#FFFF00"),
            false,
            false,
            false,
            0));

        return new
        {
            success = true,
            keyword,
            type = GetString(args, "type", "UTF8"),
            color = GetString(args, "color", "#FFFF00"),
            result = new
            {
                matchCount = result.MatchCount,
                firstMatch = result.FirstMatch is null ? null : new
                {
                    theology = result.FirstMatch.Theology,
                    url = result.FirstMatch.Url
                }
            },
            message = "搜索完成，匹配行已高亮"
        };
    }

    private async Task<object> ToolRequestHighlightClearAsync(JsonElement args)
    {
        int cleared = await _viewModel.CancelSearchHighlightQuietAsync();
        return new
        {
            success = true,
            cleared,
            message = $"已清除 {cleared} 条搜索高亮"
        };
    }

    private async Task<object> ToolSocketDataListAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);
        SocketEntry[] packets = GetSocketEntries(payload);
        int limit = Math.Max(1, GetInt(args, "limit", 100));
        int offset = Math.Max(0, GetInt(args, "offset", 0));
        (int sendNum, int recNum) = ParseSocketCounters(entry.ResponseLength);
        int end = Math.Min(packets.Length, offset + limit);
        List<object> packetList = new();

        for (int index = offset; index < end; index++)
        {
            SocketEntry packet = packets[index];
            (byte[] bytes, string text, _) = await _viewModel.LoadSocketPayloadAsync(entry.Theology, index);
            string preview = BuildSocketPreview(bytes, text);
            packetList.Add(new
            {
                index,
                length = bytes.Length,
                direction = packet.Icon,
                time = packet.Time,
                type = NormalizeSocketPacketType(packet),
                preview
            });
        }

        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            protocol = GetSessionWay(entry),
            url = entry.Url,
            total = packets.Length,
            sendNum,
            recNum,
            offset,
            limit,
            packets = packetList
        };
    }

    private async Task<object> ToolSocketDataGetAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        int index = RequireInt(args, "index");
        (byte[] bytes, string text, _) = await _viewModel.LoadSocketPayloadAsync(entry.Theology, index);
        SocketEntry[] packets = GetSocketEntries(await RequireSessionPayloadAsync(entry).ConfigureAwait(true));
        if (index < 0 || index >= packets.Length)
        {
            throw new InvalidOperationException($"索引 {index} 超出范围");
        }

        SocketEntry packet = packets[index];
        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            protocol = GetSessionWay(entry),
            packet = new
            {
                index,
                length = bytes.Length,
                direction = packet.Icon,
                time = packet.Time,
                type = NormalizeSocketPacketType(packet),
                body = IsPrintable(bytes) ? text : "[binary]",
                bodyBase64 = Convert.ToBase64String(bytes)
            }
        };
    }

    private async Task<object> ToolSocketDataGetRangeAsync(JsonElement args)
    {
        CaptureEntry entry = RequireSessionEntry(args);
        int start = Math.Max(0, RequireInt(args, "start"));
        int end = GetInt(args, "end", start + 20);
        JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);
        SocketEntry[] packets = GetSocketEntries(payload);
        if (end > packets.Length)
        {
            end = packets.Length;
        }

        if (start >= packets.Length)
        {
            return new
            {
                success = true,
                index = entry.Index,
                theology = entry.Theology,
                total = packets.Length,
                packets = Array.Empty<object>()
            };
        }

        List<object> results = new();
        for (int index = start; index < end; index++)
        {
            (byte[] bytes, string text, _) = await _viewModel.LoadSocketPayloadAsync(entry.Theology, index);
            SocketEntry packet = packets[index];
            results.Add(new
            {
                index,
                length = bytes.Length,
                direction = packet.Icon,
                time = packet.Time,
                type = NormalizeSocketPacketType(packet),
                body = IsPrintable(bytes) ? text : "[binary]",
                bodyBase64 = Convert.ToBase64String(bytes)
            });
        }

        return new
        {
            success = true,
            index = entry.Index,
            theology = entry.Theology,
            protocol = GetSessionWay(entry),
            total = packets.Length,
            range = new[] { start, end },
            packets = results
        };
    }

    private async Task<object> ToolConnectionListAsync(JsonElement args)
    {
        await Task.CompletedTask;
        string protocol = GetString(args, "protocol").ToLowerInvariant();
        int limit = Math.Max(1, GetInt(args, "limit", 50));
        int offset = Math.Max(0, GetInt(args, "offset", 0));
        bool favoritesOnly = GetBool(args, "favoritesOnly");

        CaptureEntry[] matched = _viewModel.GetSessionSnapshot()
            .Where(static entry => IsSocketSession(entry))
            .Where(entry => !favoritesOnly || entry.IsFavorite)
            .Where(entry => string.IsNullOrWhiteSpace(protocol) || ProtocolMatches(entry, protocol))
            .OrderByDescending(static entry => entry.Theology)
            .ToArray();

        List<object> connections = new();
        foreach (CaptureEntry entry in matched.Skip(offset).Take(limit))
        {
            JsonElement payload = await RequireSessionPayloadAsync(entry).ConfigureAwait(true);
            SocketEntry[] packets = GetSocketEntries(payload);
            (int sendNum, int recNum) = ParseSocketCounters(entry.ResponseLength);
            connections.Add(new
            {
                index = entry.Index,
                theology = entry.Theology,
                protocol = GetSessionWay(entry),
                url = entry.Url,
                status = entry.State,
                sendNum,
                recNum,
                packetCount = packets.Length,
                pid = entry.Process,
                clientIP = entry.ClientAddress,
                sendTime = entry.SendTime,
                notes = entry.Notes,
                isFavorite = entry.IsFavorite
            });
        }

        return new
        {
            success = true,
            total = matched.Length,
            offset,
            limit,
            connections
        };
    }

    private async Task<HashSet<string>> SaveHostsRulesAsync()
    {
        EnsureRuleHashes(_viewModel.HostsRuleItems.Select(static item => item.Hash).ToArray(), _viewModel.HostsRuleItems.Select(static item => item).ToArray());
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("保存HOSTS规则", new
        {
            Data = _viewModel.HostsRuleItems.Select(static item => new Dictionary<string, object?>
            {
                ["Hash"] = item.Hash,
                ["源地址"] = item.Source,
                ["新地址"] = item.Target
            }).ToArray()
        });
        HashSet<string> failures = result is { ValueKind: JsonValueKind.Array }
            ? new HashSet<string>(result.Value.EnumerateArray().Select(static item => item.GetString() ?? "").Where(static item => !string.IsNullOrWhiteSpace(item)), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (HostsRuleItem item in _viewModel.HostsRuleItems)
        {
            item.State = failures.Contains(item.Hash) ? "保存失败" : "已保存";
        }

        _viewModel.Settings.HostsRules = JsonSerializer.Serialize(_viewModel.HostsRuleItems.Select(static item => new
        {
            item.Hash,
            Src = item.Source,
            Dest = item.Target
        }), JsonOptions);
        return failures;
    }

    private async Task<HashSet<string>> SaveReplaceRulesAsync()
    {
        EnsureRuleHashes(_viewModel.ReplaceRuleItems.Select(static item => item.Hash).ToArray(), _viewModel.ReplaceRuleItems.Select(static item => item).ToArray());
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("保存替换规则", new
        {
            Data = _viewModel.ReplaceRuleItems.Select(static item => new Dictionary<string, object?>
            {
                ["Hash"] = item.Hash,
                ["替换类型"] = item.RuleType,
                ["源内容"] = item.SourceContent,
                ["替换内容"] = item.ReplacementContent
            }).ToArray()
        });
        HashSet<string> failures = result is { ValueKind: JsonValueKind.Array }
            ? new HashSet<string>(result.Value.EnumerateArray().Select(static item => item.GetString() ?? "").Where(static item => !string.IsNullOrWhiteSpace(item)), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ReplaceRuleItem item in _viewModel.ReplaceRuleItems)
        {
            item.State = failures.Contains(item.Hash) ? "保存失败" : "已保存";
        }

        _viewModel.Settings.ReplaceRules = JsonSerializer.Serialize(_viewModel.ReplaceRuleItems.Select(static item => new
        {
            Type = item.RuleType,
            Src = item.SourceContent,
            Dest = item.ReplacementContent,
            item.Hash
        }), JsonOptions);
        return failures;
    }

    private async Task SaveInterceptRulesAsync()
    {
        EnsureRuleHashes(_viewModel.InterceptRuleItems.Select(static item => item.Hash).ToArray(), _viewModel.InterceptRuleItems.Select(static item => item).ToArray());
        await _viewModel.InvokeBackendCommandAsync("保存拦截规则", new
        {
            Data = _viewModel.InterceptRuleItems.Select(static item => new Dictionary<string, object?>
            {
                ["Hash"] = item.Hash,
                ["Enable"] = item.Enabled,
                ["Name"] = item.Name,
                ["Direction"] = item.Direction,
                ["Target"] = item.Target,
                ["Operator"] = item.Operator,
                ["Value"] = item.Value
            }).ToArray()
        });
        foreach (InterceptRuleItem item in _viewModel.InterceptRuleItems)
        {
            item.State = "已保存";
        }

        _viewModel.Settings.InterceptRules = JsonSerializer.Serialize(_viewModel.InterceptRuleItems.Select(static item => new
        {
            item.Hash,
            Enable = item.Enabled,
            item.Name,
            item.Direction,
            item.Target,
            item.Operator,
            item.Value
        }), JsonOptions);
    }

    private async Task<int> RefreshRunningPortAsync()
    {
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("获取运行端口");
        if (result is { ValueKind: JsonValueKind.Number } && result.Value.TryGetInt32(out int port))
        {
            _viewModel.RunningPort = port;
            _viewModel.StatusRight = $"启动成功:{port}";
            return port;
        }

        return _viewModel.RunningPort;
    }

    private bool EnsureFavoriteState(CaptureEntry entry, bool shouldFavorite)
    {
        if (entry.IsFavorite == shouldFavorite)
        {
            return false;
        }

        _viewModel.ToggleFavorite(entry);
        return true;
    }

    private void PersistFavoriteSettings()
    {
        UiLayoutSettings settings = UiLayoutSettingsStore.Load();
        settings.ShowFavoritesOnly = _viewModel.ShowFavoritesOnly;
        settings.FavoriteSessionKeys = _viewModel.GetFavoriteKeys().ToList();
        UiLayoutSettingsStore.Save(settings);
    }

    private CaptureEntry RequireSessionEntry(JsonElement args)
    {
        CaptureEntry? entry = null;
        if (HasProperty(args, "index"))
        {
            int index = RequireInt(args, "index");
            entry = _viewModel.GetSessionSnapshot().FirstOrDefault(item => item.Index == index);
            if (entry is null)
            {
                throw new InvalidOperationException($"请求序号 {index} 不存在");
            }

            return entry;
        }

        int theology = RequireInt(args, "theology");
        entry = _viewModel.FindSessionEntry(theology);
        if (entry is null)
        {
            throw new InvalidOperationException($"请求 {theology} 不存在");
        }

        return entry;
    }

    private async Task<JsonElement> RequireSessionPayloadAsync(CaptureEntry entry)
    {
        JsonElement? result = await _viewModel.InvokeBackendCommandAsync("HTTP请求获取", new { Theology = entry.Theology });
        if (result is not { ValueKind: JsonValueKind.Object })
        {
            throw new InvalidOperationException($"请求 {entry.Theology} 详情读取失败");
        }

        return result.Value;
    }

    private static object CreateSuccessResponse(object? id, object result)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            result
        };
    }

    private static object CreateErrorResponse(object? id, int code, string message, object? data)
    {
        return new
        {
            jsonrpc = "2.0",
            id,
            error = new
            {
                code,
                message,
                data
            }
        };
    }

    private static McpToolDefinition Tool(string name, string description, Dictionary<string, object?> inputSchema, Func<JsonElement, Task<object>> handler, params string[] required)
    {
        if (required.Length > 0)
        {
            inputSchema["required"] = required;
        }

        return new McpToolDefinition(name, description, inputSchema, handler);
    }

    private static Dictionary<string, object?> NoParamsSchema()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>(),
            ["required"] = Array.Empty<string>()
        };
    }

    private static Dictionary<string, object?> Schema(params KeyValuePair<string, object?>[] properties)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties.ToDictionary(static item => item.Key, static item => item.Value),
            ["required"] = Array.Empty<string>()
        };
    }

    private static KeyValuePair<string, object?> Property(string name, string type, string description)
    {
        return new KeyValuePair<string, object?>(name, new Dictionary<string, object?>
        {
            ["type"] = type,
            ["description"] = description
        });
    }

    private static KeyValuePair<string, object?> EnumProperty(string name, string type, IEnumerable<string> values, string description, string defaultValue)
    {
        return new KeyValuePair<string, object?>(name, new Dictionary<string, object?>
        {
            ["type"] = type,
            ["description"] = description,
            ["enum"] = values.ToArray(),
            ["default"] = defaultValue
        });
    }

    private static KeyValuePair<string, object?> ArrayProperty(string name, string itemType, string description)
    {
        return new KeyValuePair<string, object?>(name, new Dictionary<string, object?>
        {
            ["type"] = "array",
            ["items"] = new Dictionary<string, object?>
            {
                ["type"] = itemType
            },
            ["description"] = description
        });
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value, CancellationToken cancellationToken)
    {
        await WriteRawJsonAsync(response, JsonSerializer.Serialize(value, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRawJsonAsync(HttpListenerResponse response, string json, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
        response.Close();
    }

    private static Task<T> InvokeOnUiAsync<T>(Func<Task<T>> action)
    {
        return Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private static object? ToJsonId(JsonElement id)
    {
        return id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number when id.TryGetInt64(out long longValue) => longValue,
            JsonValueKind.Number when id.TryGetDouble(out double doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => JsonSerializer.Deserialize<object>(id.GetRawText(), JsonOptions)
        };
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        JsonElement property = GetProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? defaultValue;
        }

        return property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
            ? property.ToString()
            : defaultValue;
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        JsonElement property = GetProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out int parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out bool parsed) && parsed;
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out _);
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out JsonElement property)
            ? property
            : default;
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        string value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"缺少参数: {propertyName}");
        }

        return value;
    }

    private static int RequireInt(JsonElement element, string propertyName)
    {
        if (!HasProperty(element, propertyName))
        {
            throw new InvalidOperationException($"缺少参数: {propertyName}");
        }

        return GetInt(element, propertyName);
    }

    private static int[] RequireIntArray(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"缺少参数: {propertyName}");
        }

        int[] values = property.EnumerateArray()
            .Select(static item =>
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int intValue))
                {
                    return intValue;
                }

                if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out int stringValue))
                {
                    return stringValue;
                }

                return 0;
            })
            .Where(static item => item > 0)
            .ToArray();

        if (values.Length == 0)
        {
            throw new InvalidOperationException($"{propertyName} 不能为空");
        }

        return values;
    }

    private static object ToRequestInfo(CaptureEntry entry)
    {
        return new
        {
            index = entry.Index,
            theology = entry.Theology,
            method = entry.Method,
            url = entry.Url,
            statusCode = ExtractStatusCode(entry.State),
            clientIP = entry.ClientAddress,
            pid = entry.Process,
            sendTime = entry.SendTime,
            recTime = entry.ReceiveTime,
            way = GetSessionWay(entry),
            notes = entry.Notes,
            isFavorite = entry.IsFavorite
        };
    }

    private static string GetSessionWay(CaptureEntry entry)
    {
        if (entry.DisplayMethod.Equals("WS", StringComparison.OrdinalIgnoreCase)
            || entry.Method.Equals("Websocket", StringComparison.OrdinalIgnoreCase)
            || entry.Method.Equals("WebSocket", StringComparison.OrdinalIgnoreCase)
            || entry.ResponseType.Contains("Websocket", StringComparison.OrdinalIgnoreCase)
            || entry.ResponseType.Contains("WebSocket", StringComparison.OrdinalIgnoreCase))
        {
            return "Websocket";
        }

        if (entry.Method.Contains("TCP", StringComparison.OrdinalIgnoreCase) || entry.ResponseType.Contains("TCP", StringComparison.OrdinalIgnoreCase))
        {
            return "TCP";
        }

        if (entry.Method.Equals("UDP", StringComparison.OrdinalIgnoreCase) || entry.ResponseType.Contains("UDP", StringComparison.OrdinalIgnoreCase))
        {
            return "UDP";
        }

        return "HTTP";
    }

    private static bool IsSocketSession(CaptureEntry entry)
    {
        string way = GetSessionWay(entry);
        return way is "Websocket" or "TCP" or "UDP";
    }

    private static bool ProtocolMatches(CaptureEntry entry, string protocol)
    {
        string way = GetSessionWay(entry).ToLowerInvariant();
        return protocol switch
        {
            "websocket" or "ws" => way == "websocket",
            "tcp" => way == "tcp",
            "udp" => way == "udp",
            _ => true
        };
    }

    private static int ExtractStatusCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        for (int index = 0; index <= text.Length - 3; index++)
        {
            if (!char.IsDigit(text[index]) || !char.IsDigit(text[index + 1]) || !char.IsDigit(text[index + 2]))
            {
                continue;
            }

            int value = ((text[index] - '0') * 100) + ((text[index + 1] - '0') * 10) + (text[index + 2] - '0');
            if (value is >= 100 and <= 599)
            {
                return value;
            }
        }

        return 0;
    }

    private static Dictionary<string, string[]> ToHeaderDictionary(JsonElement headers)
    {
        Dictionary<string, string[]> result = new(StringComparer.OrdinalIgnoreCase);
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in headers.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                result[property.Name] = property.Value.EnumerateArray()
                    .Select(ElementToText)
                    .ToArray();
            }
            else
            {
                result[property.Name] = new[] { ElementToText(property.Value) };
            }
        }

        return result;
    }

    private static List<HeaderPair> ToHeaderPairs(JsonElement headers)
    {
        List<HeaderPair> result = new();
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty property in headers.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    result.Add(new HeaderPair(property.Name, ElementToText(item)));
                }
            }
            else
            {
                result.Add(new HeaderPair(property.Name, ElementToText(property.Value)));
            }
        }

        return result;
    }

    private static string ElementToText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => element.ToString()
        };
    }

    private static byte[] DecodePayloadBytes(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            return Array.Empty<byte>();
        }

        string value = element.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch
        {
            return Encoding.UTF8.GetBytes(value);
        }
    }

    private static (string Text, string Base64) EncodeBody(byte[] bytes, int maxSize)
    {
        if (bytes.Length == 0)
        {
            return ("", "");
        }

        bool truncated = false;
        byte[] buffer = bytes;
        if (maxSize > 0 && buffer.Length > maxSize)
        {
            buffer = buffer.Take(maxSize).ToArray();
            truncated = true;
        }

        string suffix = truncated ? $"\n... [truncated, total {bytes.Length} bytes]" : "";
        if (IsPrintable(buffer))
        {
            return (Encoding.UTF8.GetString(buffer) + suffix, "");
        }

        return ("", Convert.ToBase64String(buffer));
    }

    private static bool IsPrintable(byte[] data)
    {
        if (data.Length == 0)
        {
            return true;
        }

        int nonPrintable = 0;
        int max = Math.Min(data.Length, 512);
        for (int index = 0; index < max; index++)
        {
            byte value = data[index];
            if (value < 0x20 && value != '\n' && value != '\r' && value != '\t')
            {
                nonPrintable++;
            }
        }

        return (double)nonPrintable / max < 0.1;
    }

    private static byte[] DecompressBody(byte[] data, string encoding)
    {
        if (data.Length == 0)
        {
            return data;
        }

        try
        {
            using MemoryStream input = new(data);
            using Stream stream = encoding switch
            {
                "gzip" => new GZipStream(input, CompressionMode.Decompress, false),
                "br" => new BrotliStream(input, CompressionMode.Decompress, false),
                "deflate" => new DeflateStream(input, CompressionMode.Decompress, false),
                "zlib" => new ZLibStream(input, CompressionMode.Decompress, false),
                _ => input
            };
            if (ReferenceEquals(stream, input))
            {
                return data;
            }

            using MemoryStream output = new();
            stream.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return data;
        }
    }

    private static byte[] ConvertCharset(byte[] data, string contentType)
    {
        string text = contentType.ToLowerInvariant();
        if (!text.Contains("gbk") && !text.Contains("gb2312") && !text.Contains("gb18030"))
        {
            return data;
        }

        try
        {
            Encoding source = Encoding.GetEncoding("GBK");
            string decoded = source.GetString(data);
            return Encoding.UTF8.GetBytes(decoded);
        }
        catch
        {
            return data;
        }
    }

    private static bool LooksLikeJson(string contentType, byte[] data)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        byte[] trimmed = data.SkipWhile(static current => char.IsWhiteSpace((char)current)).ToArray();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private static bool TryPrettyJson(byte[] data, out string pretty)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            return true;
        }
        catch
        {
            pretty = "";
            return false;
        }
    }

    private static bool ContainsBytes(byte[] data, string keyword)
    {
        if (data.Length == 0 || string.IsNullOrEmpty(keyword))
        {
            return false;
        }

        byte[] keywordBytes = Encoding.UTF8.GetBytes(keyword);
        return data.AsSpan().IndexOf(keywordBytes) >= 0;
    }

    private static string GetHeaderValue(JsonElement headers, string name)
    {
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        foreach (JsonProperty property in headers.EnumerateObject())
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                JsonElement first = property.Value.EnumerateArray().FirstOrDefault();
                return ElementToText(first);
            }

            return ElementToText(property.Value);
        }

        return "";
    }

    private static SocketEntry[] GetSocketEntries(JsonElement payload)
    {
        JsonElement socketData = GetProperty(payload, "SocketData");
        if (socketData.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SocketEntry>();
        }

        return JsonSerializer.Deserialize<SocketEntry[]>(socketData.GetRawText(), JsonOptions) ?? Array.Empty<SocketEntry>();
    }

    private static (int Send, int Rec) ParseSocketCounters(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0, 0);
        }

        string[] parts = text.Split('/', 2);
        return (
            parts.Length > 0 && int.TryParse(parts[0], out int send) ? send : 0,
            parts.Length > 1 && int.TryParse(parts[1], out int rec) ? rec : 0);
    }

    private static string DecodeBase64Text(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch
        {
            return value;
        }
    }

    private static readonly string BreakpointNamePrefix = "[MCP断点] ";

    private InterceptRuleItem[] GetBreakpointRules()
    {
        return _viewModel.InterceptRuleItems
            .Where(static item => item.Target.Equals("URL", StringComparison.OrdinalIgnoreCase)
                && item.Operator.Equals("正则", StringComparison.OrdinalIgnoreCase)
                && item.Name.StartsWith(BreakpointNamePrefix, StringComparison.Ordinal))
            .ToArray();
    }

    private static string BuildSocketPreview(byte[] bytes, string text)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        if (!IsPrintable(bytes))
        {
            return $"[binary {bytes.Length} bytes]";
        }

        string normalized = string.IsNullOrWhiteSpace(text)
            ? Encoding.UTF8.GetString(bytes)
            : text;
        normalized = normalized.Replace("\r", " ").Replace("\n", " ");
        if (normalized.Length > 200)
        {
            normalized = normalized[..200] + "...";
        }

        return normalized;
    }

    private static string NormalizeSocketPacketType(SocketEntry packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.Type))
        {
            return packet.Type;
        }

        return packet.Icon switch
        {
            "websocket_close" => "Close",
            "websocket_connect" => "Open",
            _ => packet.Type
        };
    }

    private static void EnsureRuleHashes(string[] existingHashes, object[] items)
    {
        _ = existingHashes;
        _ = items;
    }

    private sealed record McpToolDefinition(
        string Name,
        string Description,
        Dictionary<string, object?> InputSchema,
        Func<JsonElement, Task<object>> Handler);

    private sealed record HeaderPair(string Name, string Value);

    private sealed class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "";

        [JsonPropertyName("id")]
        public JsonElement Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        public JsonElement Params { get; set; }
    }
}
