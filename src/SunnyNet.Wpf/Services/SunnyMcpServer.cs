using System.Net;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Services;

public sealed class SunnyMcpServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MainWindowViewModel _viewModel;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public SunnyMcpServer(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public int Port { get; private set; }

    public bool IsRunning => _listener.IsListening;

    public void Start(int port = 0)
    {
        if (IsRunning)
        {
            return;
        }

        Port = port <= 0 ? GetFreePort() : port;
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

            _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Headers"] = "content-type";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";

            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            if (context.Request.Url?.AbsolutePath.Equals("/health", StringComparison.OrdinalIgnoreCase) == true)
            {
                await WriteJsonAsync(context, new { ok = true, name = "SunnyNet MCP Server", port = Port }).ConfigureAwait(false);
                return;
            }

            if (context.Request.Url?.AbsolutePath.Equals("/mcp", StringComparison.OrdinalIgnoreCase) != true)
            {
                context.Response.StatusCode = 404;
                await WriteJsonAsync(context, new { error = "Not found" }).ConfigureAwait(false);
                return;
            }

            using StreamReader reader = new(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            McpRequest? request = string.IsNullOrWhiteSpace(body)
                ? new McpRequest { Method = "tools/list" }
                : JsonSerializer.Deserialize<McpRequest>(body, JsonOptions);
            object? result = await DispatchAsync(request ?? new McpRequest { Method = "tools/list" }).ConfigureAwait(false);
            await WriteJsonAsync(context, new { ok = true, result }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context, new { ok = false, error = exception.Message }).ConfigureAwait(false);
        }
    }

    private async Task<object?> DispatchAsync(McpRequest request)
    {
        string method = request.Method ?? request.Tool ?? "tools/list";
        JsonElement args = request.Arguments ?? request.Params ?? default;

        return method switch
        {
            "tools/list" => GetTools(),
            "tools/call" => await CallToolAsync(GetToolCallName(args), GetToolCallArguments(args)).ConfigureAwait(false),
            "status" or "sunny_status" => InvokeOnUi(GetStatus),
            "list_sessions" or "sunny_list_sessions" => InvokeOnUi(() => ListSessions(args)),
            "get_session" or "sunny_get_session" => await InvokeOnUiAsync(() => GetSessionAsync(GetInt(args, "theology", GetInt(args, "id", 0)))).ConfigureAwait(false),
            "search_sessions" or "sunny_search_sessions" => InvokeOnUi(() => SearchSessions(GetString(args, "keyword"))),
            "clear_sessions" or "sunny_clear_sessions" => await InvokeOnUiAsync(async () => { await _viewModel.ClearMcpSessionsAsync(); return new { ok = true }; }).ConfigureAwait(false),
            "release_all" or "sunny_release_all" => await InvokeOnUiAsync(async () => { await _viewModel.ReleaseMcpAllAsync(); return new { ok = true }; }).ConfigureAwait(false),
            "delete_session" or "sunny_delete_session" => await InvokeOnUiAsync(async () => { await _viewModel.DeleteMcpSessionAsync(GetInt(args, "theology", GetInt(args, "id", 0))); return new { ok = true }; }).ConfigureAwait(false),
            "resend_session" or "sunny_resend_session" => await InvokeOnUiAsync(async () => { await _viewModel.ResendMcpSessionAsync(GetInt(args, "theology", GetInt(args, "id", 0)), GetInt(args, "mode", 0)); return new { ok = true }; }).ConfigureAwait(false),
            "close_session" or "sunny_close_session" => await InvokeOnUiAsync(async () => { await _viewModel.CloseMcpSessionAsync(GetInt(args, "theology", GetInt(args, "id", 0))); return new { ok = true }; }).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"未知 MCP 方法：{method}")
        };
    }

    private async Task<object?> CallToolAsync(string name, JsonElement arguments)
    {
        return await DispatchAsync(new McpRequest { Method = name, Tool = name, Arguments = arguments }).ConfigureAwait(false);
    }

    private static string GetToolCallName(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("name", out JsonElement nameElement))
        {
            return nameElement.GetString() ?? string.Empty;
        }

        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("arguments", out JsonElement nested)
            && nested.ValueKind == JsonValueKind.Object && nested.TryGetProperty("name", out JsonElement nestedName))
        {
            return nestedName.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static JsonElement GetToolCallArguments(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("arguments", out JsonElement argumentsElement))
        {
            return argumentsElement;
        }

        return args;
    }

    private static object GetTools()
    {
        return new[]
        {
            Tool("sunny_status", "获取 SunnyNet 当前状态"),
            Tool("sunny_list_sessions", "获取会话列表，参数：limit"),
            Tool("sunny_get_session", "获取会话详情，参数：theology 或 id"),
            Tool("sunny_search_sessions", "搜索会话，参数：keyword"),
            Tool("sunny_clear_sessions", "清空所有会话"),
            Tool("sunny_release_all", "放行所有拦截会话"),
            Tool("sunny_delete_session", "删除会话，参数：theology 或 id"),
            Tool("sunny_resend_session", "重发会话，参数：theology 或 id, mode"),
            Tool("sunny_close_session", "断开会话，参数：theology 或 id")
        };
    }

    private static object Tool(string name, string description) => new { name, description };

    private object GetStatus()
    {
        return new
        {
            mcpPort = Port,
            isCapturing = _viewModel.IsCapturing,
            runningPort = _viewModel.RunningPort,
            sessionCount = _viewModel.Sessions.Count,
            selected = _viewModel.SelectedSession?.Theology,
            statusLeft = _viewModel.StatusLeft,
            statusMiddle = _viewModel.StatusMiddle,
            statusRight = _viewModel.StatusRight
        };
    }

    private object ListSessions(JsonElement args)
    {
        int limit = Math.Clamp(GetInt(args, "limit", 100), 1, 1000);
        return _viewModel.GetMcpSessionsSnapshot()
            .OrderByDescending(static entry => entry.Index)
            .Take(limit)
            .Select(ToSessionSummary)
            .ToArray();
    }

    private async Task<object> GetSessionAsync(int theology)
    {
        SessionDetail detail = await _viewModel.LoadMcpSessionDetailAsync(theology).ConfigureAwait(true);
        CaptureEntry? entry = _viewModel.FindMcpSession(theology);
        return new
        {
            session = entry is null ? null : ToSessionSummary(entry),
            detail = new
            {
                summary = detail.Summary,
                requestHeaders = detail.RequestHeaders,
                requestBody = detail.RequestBody,
                responseHeaders = detail.ResponseHeaders,
                responseBody = detail.ResponseBody,
                isSocketSession = detail.IsSocketSession,
                socketCount = detail.SocketEntries.Count
            }
        };
    }

    private object SearchSessions(string keyword)
    {
        keyword ??= string.Empty;
        return _viewModel.GetMcpSessionsSnapshot()
            .Where(entry => string.IsNullOrWhiteSpace(keyword)
                || entry.Url.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || entry.Host.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || entry.Method.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || entry.Process.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static entry => entry.Index)
            .Take(500)
            .Select(ToSessionSummary)
            .ToArray();
    }

    private static object ToSessionSummary(CaptureEntry entry)
    {
        return new
        {
            index = entry.Index,
            theology = entry.Theology,
            method = entry.Method,
            url = entry.Url,
            host = entry.Host,
            state = entry.State,
            length = entry.ResponseLength,
            type = entry.ResponseType,
            process = entry.Process,
            client = entry.ClientAddress,
            notes = entry.Notes,
            favorite = entry.IsFavorite
        };
    }

    private static T InvokeOnUi<T>(Func<T> action)
    {
        return Application.Current.Dispatcher.Invoke(action);
    }

    private static Task<T> InvokeOnUiAsync<T>(Func<Task<T>> action)
    {
        return Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, object value)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        context.Response.ContentLength64 = data.Length;
        await context.Response.OutputStream.WriteAsync(data).ConfigureAwait(false);
        context.Response.Close();
    }

    private static int GetFreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static JsonElement GetElement(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) ? value : default;
    }

    private static string GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static int GetInt(JsonElement element, string name, int fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out int parsed) ? parsed : fallback;
    }

    private sealed class McpRequest
    {
        public string? Method { get; set; }
        public string? Tool { get; set; }
        public JsonElement? Arguments { get; set; }
        public JsonElement? Params { get; set; }
    }
}

