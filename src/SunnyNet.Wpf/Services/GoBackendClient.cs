using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Services;

public sealed class GoBackendClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly object NativeLock = new();
    private static SunnyNetNativeLibrary? _nativeLibrary;

    private CancellationTokenSource? _eventsCancellation;
    private Task? _eventsTask;
    private bool _started;

    public event EventHandler<BackendEventEnvelope>? EventReceived;
    public event EventHandler<string>? TraceReceived;

    public int Port { get; private set; }
    public bool IsRunning => _started;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_started)
        {
            return Task.CompletedTask;
        }

        _ = GetNativeLibrary();

        _started = true;
        Port = 0;
        _eventsCancellation = new CancellationTokenSource();
        _eventsTask = Task.Run(() => ListenEventsAsync(_eventsCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task<JsonElement?> InvokeAsync(string command, object? args = null, CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Go 后台尚未启动。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string payload = JsonSerializer.Serialize(new
        {
            Command = command,
            Args = args
        }, JsonOptions);

        return await Task.Run<JsonElement?>(() =>
        {
            string responseJson = GetNativeLibrary().Invoke(payload);
            using JsonDocument document = JsonDocument.Parse(responseJson);

            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("ok", out JsonElement okElement) || !okElement.GetBoolean())
            {
                throw new InvalidOperationException(ExtractError(root));
            }

            if (!root.TryGetProperty("data", out JsonElement dataElement) || dataElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return dataElement.Clone();
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _eventsCancellation?.Cancel();
            if (_eventsTask is not null)
            {
                await Task.WhenAny(_eventsTask, Task.Delay(1000));
            }
        }
        catch
        {
        }
        finally
        {
            _eventsCancellation?.Dispose();
            _eventsCancellation = null;
            _eventsTask = null;
        }

        if (_started)
        {
            try
            {
                GetNativeLibrary().Shutdown();
            }
            catch (Exception exception)
            {
                TraceReceived?.Invoke(this, exception.Message);
            }
            finally
            {
                _started = false;
                Port = 0;
            }
        }
    }

    private static string ExtractError(JsonElement root)
    {
        if (!root.TryGetProperty("err", out JsonElement errorElement))
        {
            return "Go 调用失败。";
        }

        return errorElement.ValueKind switch
        {
            JsonValueKind.String => errorElement.GetString() ?? "Go 调用失败。",
            JsonValueKind.Object or JsonValueKind.Array => errorElement.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => "Go 调用失败。",
            _ => errorElement.ToString()
        };
    }

    private SunnyNetNativeLibrary GetNativeLibrary()
    {
        lock (NativeLock)
        {
            _nativeLibrary ??= new SunnyNetNativeLibrary(ResolveBackendPath());
            return _nativeLibrary;
        }
    }

    private static string ResolveBackendPath()
    {
        string outputPath = Path.Combine(AppContext.BaseDirectory, "backend", "SunnyNetBridge.dll");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }
        throw new FileNotFoundException("找不到 Go 后台动态库 SunnyNetBridge.dll。", outputPath);
    }

    private async Task ListenEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? json = await Task.Run(() => GetNativeLibrary().PollEvent(500), cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                BackendEventEnvelope? envelope = JsonSerializer.Deserialize<BackendEventEnvelope>(json, JsonOptions);
                if (envelope is not null)
                {
                    EventReceived?.Invoke(this, envelope);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            TraceReceived?.Invoke(this, exception.Message);
        }
    }

    private sealed class SunnyNetNativeLibrary
    {
        private readonly nint _handle;
        private readonly SunnyNetInvokeDelegate _invoke;
        private readonly SunnyNetPollEventDelegate _pollEvent;
        private readonly SunnyNetShutdownDelegate _shutdown;
        private readonly SunnyNetFreeStringDelegate _freeString;

        public SunnyNetNativeLibrary(string path)
        {
            _handle = NativeLibrary.Load(path);
            _invoke = LoadDelegate<SunnyNetInvokeDelegate>("SunnyNetInvoke");
            _pollEvent = LoadDelegate<SunnyNetPollEventDelegate>("SunnyNetPollEvent");
            _shutdown = LoadDelegate<SunnyNetShutdownDelegate>("SunnyNetShutdown");
            _freeString = LoadDelegate<SunnyNetFreeStringDelegate>("SunnyNetFreeString");
        }

        public string Invoke(string requestJson)
        {
            nint requestPtr = Marshal.StringToCoTaskMemUTF8(requestJson);
            try
            {
                return ReadAndFree(_invoke(requestPtr)) ?? "{\"ok\":false,\"err\":\"Go 后台返回空响应。\"}";
            }
            finally
            {
                Marshal.FreeCoTaskMem(requestPtr);
            }
        }

        public string? PollEvent(int timeoutMs)
        {
            return ReadAndFree(_pollEvent(timeoutMs));
        }

        public void Shutdown()
        {
            _shutdown();
        }

        private TDelegate LoadDelegate<TDelegate>(string name) where TDelegate : Delegate
        {
            nint export = NativeLibrary.GetExport(_handle, name);
            return Marshal.GetDelegateForFunctionPointer<TDelegate>(export);
        }

        private string? ReadAndFree(nint value)
        {
            if (value == nint.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUTF8(value);
            }
            finally
            {
                _freeString(value);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint SunnyNetInvokeDelegate(nint requestJson);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint SunnyNetPollEventDelegate(int timeoutMs);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SunnyNetShutdownDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SunnyNetFreeStringDelegate(nint value);
    }
}
