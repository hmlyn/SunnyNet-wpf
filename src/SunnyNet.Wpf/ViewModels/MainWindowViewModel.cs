using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Windows;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.Services;

namespace SunnyNet.Wpf.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly GoBackendClient _backend = new();
    private readonly Dictionary<int, CaptureEntry> _sessionMap = new();
    private int _nextIndex = 1;
    private CaptureEntry? _selectedSession;
    private bool _isBusy;
    private bool _autoScroll;
    private bool _isCapturing = true;
    private bool _ieProxyEnabled;
    private bool _isSelectedSessionIntercepted;
    private int _breakpointMode;
    private string _statusLeft = "未设置系统IE代理";
    private string _statusMiddle = "正在捕获";
    private string _statusRight = "等待 Go 核心启动";
    private int _runningPort;

    public MainWindowViewModel()
    {
        ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
        ReleaseAllCommand = new AsyncRelayCommand(ReleaseAllAsync);
        ToggleCaptureCommand = new AsyncRelayCommand(ToggleCaptureAsync);
        ToggleAutoScrollCommand = new RelayCommand(_ => AutoScroll = !AutoScroll);
        ResendSelectedCommand = new AsyncRelayCommand(() => ResendSelectedAsync(3), () => SelectedSession is not null && IsSelectedSessionIntercepted);
        CloseSessionCommand = new AsyncRelayCommand(CloseSelectedSessionAsync, () => SelectedSession is not null && IsSelectedSessionIntercepted);
        ReleaseCurrentCommand = new AsyncRelayCommand(() => BreakpointAsync(0), () => SelectedSession is not null && IsSelectedSessionIntercepted);
    }

    public event Action<CaptureEntry>? ScrollToEntryRequested;
    public event Action<string, string>? NotificationRequested;

    public ObservableCollection<CaptureEntry> Sessions { get; } = new();
    public SessionDetail Detail { get; } = new();
    public AppConfigState Settings { get; } = new();

    public AsyncRelayCommand ClearAllCommand { get; }
    public AsyncRelayCommand ReleaseAllCommand { get; }
    public AsyncRelayCommand ToggleCaptureCommand { get; }
    public RelayCommand ToggleAutoScrollCommand { get; }
    public AsyncRelayCommand ResendSelectedCommand { get; }
    public AsyncRelayCommand CloseSessionCommand { get; }
    public AsyncRelayCommand ReleaseCurrentCommand { get; }

    public CaptureEntry? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (!SetProperty(ref _selectedSession, value))
            {
                return;
            }

            ResendSelectedCommand.RaiseCanExecuteChanged();
            CloseSessionCommand.RaiseCanExecuteChanged();
            ReleaseCurrentCommand.RaiseCanExecuteChanged();
            RefreshSelectedSessionInterceptState();
            _ = LoadSelectedSessionAsync();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set => SetProperty(ref _isCapturing, value);
    }

    public bool IeProxyEnabled
    {
        get => _ieProxyEnabled;
        set => SetProperty(ref _ieProxyEnabled, value);
    }

    public bool IsSelectedSessionIntercepted
    {
        get => _isSelectedSessionIntercepted;
        private set
        {
            if (!SetProperty(ref _isSelectedSessionIntercepted, value))
            {
                return;
            }

            ResendSelectedCommand.RaiseCanExecuteChanged();
            CloseSessionCommand.RaiseCanExecuteChanged();
            ReleaseCurrentCommand.RaiseCanExecuteChanged();
        }
    }

    public int BreakpointMode
    {
        get => _breakpointMode;
        set => SetProperty(ref _breakpointMode, value);
    }

    public string StatusLeft
    {
        get => _statusLeft;
        set => SetProperty(ref _statusLeft, value);
    }

    public string StatusMiddle
    {
        get => _statusMiddle;
        set => SetProperty(ref _statusMiddle, value);
    }

    public string StatusRight
    {
        get => _statusRight;
        set => SetProperty(ref _statusRight, value);
    }

    public int RunningPort
    {
        get => _runningPort;
        set => SetProperty(ref _runningPort, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            StatusRight = "正在启动 Go 核心...";

            _backend.EventReceived += OnBackendEventReceived;
            _backend.TraceReceived += (_, message) => Application.Current.Dispatcher.Invoke(() => StatusRight = message);

            await _backend.StartAsync();
            StatusRight = "Go DLL 已加载";
            await _backend.InvokeAsync("init");
        }
        catch (Exception exception)
        {
            StatusRight = "Go 核心启动失败";
            NotificationRequested?.Invoke("启动失败", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _backend.DisposeAsync();
    }

    public async Task OpenCaptureFileAsync(string filePath)
    {
        await _backend.InvokeAsync("打开记录文件", new { Path = filePath });
    }

    public async Task SaveCaptureFileAsync(string filePath, bool saveAll)
    {
        int[] selectedIds = saveAll || SelectedSession is null ? Array.Empty<int>() : new[] { SelectedSession.Theology };
        await _backend.InvokeAsync("保存文件", new { Path = filePath, ALL = saveAll, Data = selectedIds });
    }

    public async Task UpdateSelectedNotesAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        await _backend.InvokeAsync("更新注释", new { Theology = SelectedSession.Theology, Data = SelectedSession.Notes });
    }

    public async Task ApplyBasicSettingsAsync()
    {
        await _backend.InvokeAsync("修改端口号", new { Settings.Port });
        await _backend.InvokeAsync("禁止TCP", new { DisableTCP = Settings.DisableTcp });
        await _backend.InvokeAsync("禁止UDP", new { DisableUDP = Settings.DisableUdp });
        await _backend.InvokeAsync("禁止缓存", new { DisableBrowserCache = Settings.DisableCache });
        await _backend.InvokeAsync("身份验证模式", new { authentication = Settings.Authentication });
    }

    public async Task ApplyProxySettingsAsync(bool enableProxy)
    {
        await _backend.InvokeAsync("保存上游代理使用规则", new { Data = Settings.GlobalProxyRules });
        await _backend.InvokeAsync("设置上游代理", new { Data = Settings.GlobalProxy, Set = enableProxy });
    }

    public async Task ApplyMustTcpSettingsAsync()
    {
        await _backend.InvokeAsync("保存强制TCP使用规则", new { Data = Settings.MustTcpOpen ? "MustTcp" : "CancelMustTcp" });
        if (!string.IsNullOrWhiteSpace(Settings.MustTcpRules))
        {
            await _backend.InvokeAsync("保存强制TCP使用规则", new { Data = Settings.MustTcpRules });
        }
    }

    public async Task ApplyCertificateSettingsAsync()
    {
        if (Settings.CertMode == "默认证书")
        {
            await _backend.InvokeAsync("应用默认证书");
            return;
        }

        await _backend.InvokeAsync("导入证书", new { CaFilePath = Settings.CaFilePath, KeyFilePath = Settings.KeyFilePath });
    }

    public async Task InstallDefaultCertificateAsync()
    {
        await _backend.InvokeAsync("安装默认证书");
    }

    public async Task ExportDefaultCertificateAsync(string filePath)
    {
        await _backend.InvokeAsync("导出默认证书", new { Path = filePath });
    }

    public async Task ToggleIeProxyAsync()
    {
        bool nextState = !IeProxyEnabled;
        JsonElement? result = await _backend.InvokeAsync("设置IE代理", new { Set = nextState });
        if (result is { ValueKind: JsonValueKind.True } or { ValueKind: JsonValueKind.False })
        {
            IeProxyEnabled = nextState;
            StatusLeft = IeProxyEnabled ? "已设置系统IE代理" : "未设置系统IE代理";
        }
    }

    public async Task CycleBreakpointModeAsync()
    {
        BreakpointMode++;
        if (BreakpointMode > 2)
        {
            BreakpointMode = 0;
        }

        await _backend.InvokeAsync("设置断点模式", new { @break = BreakpointMode });
    }

    public async Task ToggleThemeAsync()
    {
        Settings.IsDarkTheme = false;
        await _backend.InvokeAsync("更新主题", new { Dark = false });
    }

    private async Task ClearAllAsync()
    {
        await _backend.InvokeAsync("清空");
        Sessions.Clear();
        _sessionMap.Clear();
        _nextIndex = 1;
        Detail.Clear();
    }

    private async Task ReleaseAllAsync()
    {
        await _backend.InvokeAsync("全部放行");
    }

    public async Task ToggleCaptureAsync()
    {
        IsCapturing = !IsCapturing;
        await _backend.InvokeAsync("工作状态", new { State = IsCapturing });
        StatusMiddle = IsCapturing ? "正在捕获" : "隐藏捕获";
    }

    private async Task ResendSelectedAsync(int mode)
    {
        if (SelectedSession is null)
        {
            return;
        }

        await _backend.InvokeAsync("重发请求", new { Data = new[] { SelectedSession.Theology }, Mode = mode });
    }

    private async Task CloseSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        await _backend.InvokeAsync("关闭请求会话", new { Data = new[] { SelectedSession.Theology } });
        SelectedSession.BreakMode = 0;
        RefreshSelectedSessionInterceptState();
    }

    private async Task BreakpointAsync(int nextBreak)
    {
        if (SelectedSession is null)
        {
            return;
        }

        await _backend.InvokeAsync("断点点击", new { Theology = SelectedSession.Theology, NextBreak = nextBreak });
        SelectedSession.BreakMode = nextBreak;
        RefreshSelectedSessionInterceptState();
    }

    private void OnBackendEventReceived(object? sender, BackendEventEnvelope envelope)
    {
        Application.Current.Dispatcher.Invoke(() => HandleBackendEvent(envelope));
    }

    private void HandleBackendEvent(BackendEventEnvelope envelope)
    {
        switch (envelope.Command)
        {
            case "弹出成功提示":
            case "弹出成功信息":
            case "弹出成功消息":
                NotificationRequested?.Invoke("提示", ElementToText(envelope.Args));
                break;
            case "弹出错误提示":
            case "弹出错误信息":
            case "弹出错误消息":
                NotificationRequested?.Invoke("错误", ElementToText(envelope.Args));
                break;
            case "弹出提示消息":
            case "弹出提示信息":
                NotificationRequested?.Invoke("提示", FormatMessageObject(envelope.Args));
                break;
            case "更新状态文本":
                StatusRight = ElementToText(envelope.Args);
                break;
            case "启动状态":
                HandleStartState(envelope.Args);
                break;
            case "插入列表":
                ApplyInsertList(envelope.Args);
                break;
            case "更新列表":
                ApplyUpdateList(envelope.Args);
                break;
            case "更新响应长度":
                ApplyResponseLength(envelope.Args);
                break;
            case "更新响应":
                ApplyResponseUpdate(envelope.Args);
                break;
            case "更新Socket":
                ApplySocketUpdate(envelope.Args);
                break;
            case "更新ICO":
                ApplyIconUpdate(envelope.Args);
                break;
            case "加载配置":
                ApplyConfig(envelope.Args);
                break;
            case "更新搜索进度":
                StatusRight = $"搜索进度:{ElementToText(envelope.Args)}%";
                break;
        }
    }

    private void HandleStartState(JsonElement args)
    {
        string text = ElementToText(args);
        if (string.IsNullOrWhiteSpace(text))
        {
            _ = RefreshRunningPortAsync();
            return;
        }

        StatusRight = "启动失败: " + DecodeBase64Text(text);
    }

    private async Task RefreshRunningPortAsync()
    {
        JsonElement? result = await _backend.InvokeAsync("获取运行端口");
        if (result is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out int port))
        {
            RunningPort = port;
            StatusRight = $"启动成功:{port}";
        }
    }

    private void ApplyInsertList(JsonElement args)
    {
        foreach (CaptureEntry entry in DeserializeList<CaptureEntry>(args))
        {
            if (entry.Theology == 0 || _sessionMap.ContainsKey(entry.Theology))
            {
                continue;
            }

            AddEntry(entry);
        }
    }

    private void ApplyUpdateList(JsonElement args)
    {
        foreach (CaptureEntry entry in DeserializeList<CaptureEntry>(args))
        {
            if (entry.Theology == 0)
            {
                continue;
            }

            if (_sessionMap.TryGetValue(entry.Theology, out CaptureEntry? existing))
            {
                existing.UpdateFrom(entry);
                if (SelectedSession?.Theology == existing.Theology)
                {
                    RefreshSelectedSessionInterceptState();
                }
            }
            else
            {
                AddEntry(entry);
            }
        }
    }

    private void ApplyResponseLength(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in args.EnumerateArray())
        {
            int theology = GetInt(item, "Theology");
            if (!_sessionMap.TryGetValue(theology, out CaptureEntry? entry))
            {
                continue;
            }

            entry.ResponseLength = $"{GetInt(item, "Send")}/{GetInt(item, "Rec")}";
        }
    }

    private void ApplyIconUpdate(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in args.EnumerateArray())
        {
            int theology = GetInt(item, "Theology");
            if (!_sessionMap.TryGetValue(theology, out CaptureEntry? entry))
            {
                continue;
            }

            entry.Icon = GetString(item, "ico");
            entry.NormalizeDerivedFields();
        }
    }

    private void ApplyResponseUpdate(JsonElement args)
    {
        int theology = GetInt(args, "Theology");
        if (SelectedSession is null || SelectedSession.Theology != theology)
        {
            return;
        }

        if (GetBool(args, "Error"))
        {
            Detail.ResponseBody = DecodePayloadElement(GetProperty(args, "Body"));
            Detail.ResponseText = Detail.ResponseBody;
            Detail.ResponseHex = ToHex(DecodePayloadBytes(GetProperty(args, "Body")));
            Detail.ResponseStateText = "error";
            Detail.ResponseStateCode = -1;
            ReplaceRows(Detail.ResponseHeaderRows, Array.Empty<DetailNameValueRow>());
            ReplaceRows(Detail.ResponseCookieRows, Array.Empty<DetailNameValueRow>());
            ReplaceRows(Detail.ResponseHexRows, ToHexRows(DecodePayloadBytes(GetProperty(args, "Body"))));
            Detail.ResponseHexBytes = DecodePayloadBytes(GetProperty(args, "Body"));
            Detail.ResponseHexHeaderLength = 0;
            Detail.ResponseImageBytes = Array.Empty<byte>();
            Detail.ResponseImageType = "";
            return;
        }

        if (GetBool(args, "断点状态") && SelectedSession is not null)
        {
            SelectedSession.BreakMode = Math.Max(SelectedSession.BreakMode, 2);
            RefreshSelectedSessionInterceptState();
        }

        JsonElement responseHeader = GetProperty(args, "Header");
        Detail.ResponseHeaders = FormatHeaders(responseHeader);
        Detail.ResponseBody = DecodePayloadElement(GetProperty(args, "Body"));
        Detail.ResponseText = Detail.ResponseBody;
        byte[] responseBytes = DecodePayloadBytes(GetProperty(args, "Body"));
        Detail.ResponseHex = ToHex(responseBytes);
        Detail.ResponseRaw = $"HTTP {GetInt(args, "StateCode")} {GetString(args, "StateText")}\r\n{Detail.ResponseHeaders}\r\n\r\n{Detail.ResponseBody}".Trim();
        Detail.ResponseJson = TryFormatJson(Detail.ResponseBody);
        Detail.ResponseCookies = ExtractCookies(responseHeader);
        Detail.ResponseStateText = GetString(args, "StateText");
        Detail.ResponseStateCode = GetInt(args, "StateCode");
        string responseContentType = GetHeaderValue(responseHeader, "Content-Type");
        Detail.ResponseImageBytes = TryGetImageBytes(responseContentType, responseBytes);
        Detail.ResponseImageType = ExtractImageType(responseContentType);
        (byte[] responseRawBytes, int responseHeaderLength) = BuildHttpBytes($"HTTP {GetInt(args, "StateCode")} {GetString(args, "StateText")}", Detail.ResponseHeaders, responseBytes);
        ApplyResponseRows(responseHeader, responseRawBytes, responseHeaderLength);
    }

    private void ApplySocketUpdate(JsonElement args)
    {
        foreach (SocketEntry socketEntry in DeserializeList<SocketEntry>(args))
        {
            if (SelectedSession is null || socketEntry.Theology != SelectedSession.Theology)
            {
                continue;
            }

            Detail.SocketEntries.Add(socketEntry);
        }
    }

    private void ApplyConfig(JsonElement args)
    {
        Settings.Port = GetInt(args, "Port", Settings.Port);
        Settings.DisableUdp = GetBool(args, "DisableUDP");
        Settings.DisableTcp = GetBool(args, "DisableTCP");
        Settings.DisableCache = GetBool(args, "DisableCache");
        Settings.Authentication = GetBool(args, "OpenAuthentication");
        Settings.GlobalProxy = GetString(args, "GlobalProxy");
        Settings.GlobalProxyRules = GetString(args, "GlobalProxyRules");
        Settings.GOOS = GetString(args, "GOOS", "windows");
        Settings.IsDarkTheme = false;

        JsonElement mustTcp = GetProperty(args, "MustTcp");
        if (mustTcp.ValueKind == JsonValueKind.Object)
        {
            Settings.MustTcpOpen = GetBool(mustTcp, "open");
            Settings.MustTcpRules = GetString(mustTcp, "Rules");
        }

        JsonElement cert = GetProperty(args, "Cert");
        if (cert.ValueKind == JsonValueKind.Object)
        {
            Settings.CertMode = GetBool(cert, "Default") ? "默认证书" : "自定义证书";
            Settings.CaFilePath = GetString(cert, "CaPath");
            Settings.KeyFilePath = GetString(cert, "KeyPath");
        }

        Settings.ScriptCode = DecodePayloadElement(GetProperty(args, "ScriptCode"));
        Settings.HostsRules = ToIndentedJson(GetProperty(args, "HostsRules"));
        Settings.ReplaceRules = ToIndentedJson(GetProperty(args, "ReplaceRules"));
    }

    private void AddEntry(CaptureEntry entry)
    {
        if (entry.Index <= 0)
        {
            entry.Index = _nextIndex++;
        }
        else if (entry.Index >= _nextIndex)
        {
            _nextIndex = entry.Index + 1;
        }

        entry.NormalizeDerivedFields();
        Sessions.Add(entry);
        _sessionMap[entry.Theology] = entry;

        if (AutoScroll)
        {
            ScrollToEntryRequested?.Invoke(entry);
        }
    }

    private async Task LoadSelectedSessionAsync()
    {
        CaptureEntry? selected = SelectedSession;
        if (selected is null)
        {
            Detail.Clear();
            return;
        }

        Detail.HasSelection = true;
        Detail.Summary = $"{selected.Method} {selected.Url}";
        Detail.RequestHeaders = "正在读取请求数据...";
        Detail.RequestBody = "";
        Detail.ResponseHeaders = "";
        Detail.ResponseBody = "";
        Detail.SocketEntries.Clear();
        Detail.RequestHeaderRows.Clear();
        Detail.RequestQueryRows.Clear();
        Detail.RequestCookieRows.Clear();
        Detail.RequestBodyRows.Clear();
        Detail.HasRequestBodyRows = false;
        Detail.RequestHexRows.Clear();
        Detail.RequestHexBytes = Array.Empty<byte>();
        Detail.RequestHexHeaderLength = 0;
        Detail.RequestImageBytes = Array.Empty<byte>();
        Detail.RequestImageType = "";
        Detail.ResponseHeaderRows.Clear();
        Detail.ResponseCookieRows.Clear();
        Detail.ResponseHexRows.Clear();
        Detail.ResponseHexBytes = Array.Empty<byte>();
        Detail.ResponseHexHeaderLength = 0;
        Detail.ResponseImageBytes = Array.Empty<byte>();
        Detail.ResponseImageType = "";

        try
        {
            JsonElement? result = await _backend.InvokeAsync("HTTP请求获取", new { Theology = selected.Theology });
            if (SelectedSession?.Theology != selected.Theology || result is null)
            {
                return;
            }

            ApplySessionDetail(selected, result.Value);
            await LoadRequestMultipartImageAsync(selected);
        }
        catch (Exception exception)
        {
            Detail.RequestHeaders = exception.Message;
        }
    }

    private void RefreshSelectedSessionInterceptState()
    {
        IsSelectedSessionIntercepted = SelectedSession?.BreakMode > 0;
    }

    private async Task LoadRequestMultipartImageAsync(CaptureEntry selected)
    {
        if (SelectedSession?.Theology != selected.Theology || Detail.RequestImageBytes.Length > 0)
        {
            return;
        }

        bool isMultipart = Detail.RequestHeaderRows.Any(row =>
            row.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) &&
            row.Value.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase));
        if (!isMultipart)
        {
            return;
        }

        JsonElement? result = await _backend.InvokeAsync("获取请求图片", new { Theology = selected.Theology });
        if (SelectedSession?.Theology != selected.Theology || result is null || result.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string body = GetString(result.Value, "Body");
        string type = GetString(result.Value, "Type");
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        try
        {
            Detail.RequestImageBytes = Convert.FromBase64String(body);
            Detail.RequestImageType = type;
        }
        catch
        {
        }
    }

    private void ApplyRequestRows(string url, JsonElement header, byte[] rawBytes, int headerLength)
    {
        ReplaceRows(Detail.RequestHeaderRows, ToHeaderRows(header));
        ReplaceRows(Detail.RequestQueryRows, ToQueryRows(url));
        ReplaceRows(Detail.RequestCookieRows, ToRequestCookieRows(header));
        ReplaceRows(Detail.RequestBodyRows, ToBodyRows(Detail.RequestBody));
        Detail.HasRequestBodyRows = Detail.RequestBodyRows.Count > 0;
        ReplaceRows(Detail.RequestHexRows, ToHexRows(rawBytes, headerLength));
        Detail.RequestHexBytes = rawBytes;
        Detail.RequestHexHeaderLength = headerLength;
    }

    private void ApplyResponseRows(JsonElement header, byte[] rawBytes, int headerLength)
    {
        ReplaceRows(Detail.ResponseHeaderRows, ToHeaderRows(header));
        ReplaceRows(Detail.ResponseCookieRows, ToResponseCookieRows(header));
        ReplaceRows(Detail.ResponseHexRows, ToHexRows(rawBytes, headerLength));
        Detail.ResponseHexBytes = rawBytes;
        Detail.ResponseHexHeaderLength = headerLength;
    }

    private void ApplySessionDetail(CaptureEntry selected, JsonElement data)
    {
        if (data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            Detail.Summary = "未读取到会话详情";
            return;
        }

        string method = GetString(data, "Method", selected.Method);
        string url = GetString(data, "URL", selected.Url);
        string proto = GetString(data, "Proto", "HTTP/1.1");
        JsonElement requestHeader = GetProperty(data, "Header");
        byte[] requestBodyBytes = DecodePayloadBytes(GetProperty(data, "Body"));
        string requestContentType = GetHeaderValue(requestHeader, "Content-Type");
        Detail.RequestMethod = method;
        Detail.RequestUrl = url;
        Detail.Summary = $"{method} {url}";
        string requestHeadersText = FormatHeaders(requestHeader);
        Detail.RequestHeaders = $"{method} {url} {proto}\r\n{requestHeadersText}".Trim();
        Detail.RequestBody = DecodePayloadElement(GetProperty(data, "Body"));
        Detail.RequestRaw = $"{method} {url} {proto}\r\n{requestHeadersText}\r\n\r\n{Detail.RequestBody}".Trim();
        Detail.RequestQuery = selected.Query;
        Detail.RequestHex = ToHex(requestBodyBytes);
        Detail.RequestCookies = ExtractCookies(requestHeader);
        Detail.RequestJson = TryFormatJson(Detail.RequestBody);
        Detail.RequestImageBytes = TryGetImageBytes(requestContentType, requestBodyBytes);
        Detail.RequestImageType = ExtractImageType(requestContentType);
        (byte[] requestRawBytes, int requestHeaderLength) = BuildHttpBytes($"{method} {url} {proto}", requestHeadersText, requestBodyBytes);
        ApplyRequestRows(url, requestHeader, requestRawBytes, requestHeaderLength);

        JsonElement response = GetProperty(data, "Response");
        if (response.ValueKind == JsonValueKind.Object)
        {
            JsonElement responseHeader = GetProperty(response, "Header");
            byte[] responseBodyBytes = DecodePayloadBytes(GetProperty(response, "Body"));
            string responseContentType = GetHeaderValue(responseHeader, "Content-Type");
            Detail.ResponseStateCode = GetInt(response, "StateCode");
            Detail.ResponseStateText = GetString(response, "StateText");
            string responseHeadersText = FormatHeaders(responseHeader);
            Detail.ResponseHeaders = $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}\r\n{responseHeadersText}".Trim();
            Detail.ResponseBody = DecodePayloadElement(GetProperty(response, "Body"));
            Detail.ResponseRaw = $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}\r\n{responseHeadersText}\r\n\r\n{Detail.ResponseBody}".Trim();
            Detail.ResponseText = Detail.ResponseBody;
            Detail.ResponseHex = ToHex(responseBodyBytes);
            Detail.ResponseCookies = ExtractCookies(responseHeader);
            Detail.ResponseJson = TryFormatJson(Detail.ResponseBody);
            Detail.ResponseHtml = Detail.ResponseBody;
            Detail.ResponseImageBytes = TryGetImageBytes(responseContentType, responseBodyBytes);
            Detail.ResponseImageType = ExtractImageType(responseContentType);
            (byte[] responseRawBytes, int responseHeaderLength) = BuildHttpBytes($"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}", responseHeadersText, responseBodyBytes);
            ApplyResponseRows(responseHeader, responseRawBytes, responseHeaderLength);
        }
        else
        {
            ReplaceRows(Detail.ResponseHeaderRows, Array.Empty<DetailNameValueRow>());
            ReplaceRows(Detail.ResponseCookieRows, Array.Empty<DetailNameValueRow>());
            ReplaceRows(Detail.ResponseHexRows, Array.Empty<HexViewRow>());
            Detail.ResponseHexBytes = Array.Empty<byte>();
            Detail.ResponseHexHeaderLength = 0;
            Detail.ResponseImageBytes = Array.Empty<byte>();
            Detail.ResponseImageType = "";
        }

        Detail.SocketEntries.Clear();
        foreach (SocketEntry socketEntry in DeserializeList<SocketEntry>(GetProperty(data, "SocketData")))
        {
            Detail.SocketEntries.Add(socketEntry);
        }
    }

    private static List<T> DeserializeList<T>(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return new List<T>();
        }

        return JsonSerializer.Deserialize<List<T>>(element.GetRawText(), JsonOptions) ?? new List<T>();
    }

    private static JsonElement GetProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out JsonElement property))
        {
            return property;
        }

        return default;
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        JsonElement property = GetProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? defaultValue : defaultValue;
    }

    private static int GetInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        JsonElement property = GetProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out int stringValue))
        {
            return stringValue;
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

        return property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out bool value) && value;
    }

    private static string FormatHeaders(JsonElement header)
    {
        if (header.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        StringBuilder builder = new();
        foreach (JsonProperty property in header.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                string values = string.Join("; ", property.Value.EnumerateArray().Select(ElementToText));
                builder.AppendLine($"{property.Name}: {values}");
            }
            else
            {
                builder.AppendLine($"{property.Name}: {ElementToText(property.Value)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatMessageObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ElementToText(element);
        }

        string title = GetString(element, "title");
        string message = GetString(element, "msg");
        return string.IsNullOrWhiteSpace(title) ? message : $"{title}\r\n{message}";
    }

    private static string ElementToText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => element.GetRawText()
        };
    }

    private static string DecodePayloadElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        string value = element.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        try
        {
            return BytesToDisplayText(Convert.FromBase64String(value));
        }
        catch
        {
            return value;
        }
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

    private static string DecodeBase64Text(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch
        {
            return value;
        }
    }

    private static string BytesToDisplayText(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        if (LooksBinary(bytes))
        {
            string preview = Convert.ToHexString(bytes.Take(256).ToArray());
            return $"二进制数据 {bytes.Length} bytes\r\n{preview}";
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool LooksBinary(byte[] bytes)
    {
        int inspected = Math.Min(bytes.Length, 512);
        int controlCount = 0;
        for (int index = 0; index < inspected; index++)
        {
            byte current = bytes[index];
            if (current == 0)
            {
                return true;
            }

            if (current < 8 || current is > 13 and < 32)
            {
                controlCount++;
            }
        }

        return inspected > 0 && controlCount > inspected / 4;
    }

    private static string ToIndentedJson(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "";
        }

        return JsonSerializer.Serialize(element, JsonOptions);
    }

    private static string ToHex(byte[] bytes)
    {
        return bytes.Length == 0 ? "" : Convert.ToHexString(bytes);
    }

    private static string TryFormatJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch
        {
            return text;
        }
    }

    private static string ExtractCookies(JsonElement headers)
    {
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        StringBuilder builder = new();
        foreach (JsonProperty property in headers.EnumerateObject())
        {
            if (!property.Name.Contains("cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    builder.AppendLine(ElementToText(item));
                }
            }
            else
            {
                builder.AppendLine(ElementToText(property.Value));
            }
        }

        return builder.ToString().Trim();
    }

    private static void ReplaceRows<T>(ObservableCollection<T> collection, IEnumerable<T> rows)
    {
        collection.Clear();
        foreach (T row in rows)
        {
            collection.Add(row);
        }
    }

    private static List<DetailNameValueRow> ToHeaderRows(JsonElement headers)
    {
        List<DetailNameValueRow> rows = new();
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return rows;
        }

        foreach (JsonProperty property in headers.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    rows.Add(new DetailNameValueRow { Name = property.Name, Value = ElementToText(item) });
                }
            }
            else
            {
                rows.Add(new DetailNameValueRow { Name = property.Name, Value = ElementToText(property.Value) });
            }
        }

        return rows;
    }

    private static List<DetailNameValueRow> ToQueryRows(string url)
    {
        List<DetailNameValueRow> rows = new();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return rows;
        }

        string query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return rows;
        }

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            rows.Add(new DetailNameValueRow
            {
                Name = DecodeComponent(parts[0]),
                Value = parts.Length > 1 ? DecodeComponent(parts[1]) : ""
            });
        }

        return rows;
    }

    private static List<DetailNameValueRow> ToRequestCookieRows(JsonElement headers)
    {
        List<DetailNameValueRow> rows = new();
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return rows;
        }

        foreach (JsonProperty property in headers.EnumerateObject())
        {
            if (!property.Name.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IEnumerable<string> cookieValues = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().Select(ElementToText)
                : new[] { ElementToText(property.Value) };

            foreach (string cookieValue in cookieValues)
            {
                foreach (string part in cookieValue.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] pieces = part.Trim().Split('=', 2);
                    rows.Add(new DetailNameValueRow
                    {
                        Name = pieces[0].Trim(),
                        Value = pieces.Length > 1 ? pieces[1].Trim() : ""
                    });
                }
            }
        }

        return rows;
    }

    private static List<DetailNameValueRow> ToBodyRows(string body)
    {
        List<DetailNameValueRow> rows = new();
        if (string.IsNullOrWhiteSpace(body) || (!body.Contains('=') && !body.Contains('&')))
        {
            return rows;
        }

        foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            rows.Add(new DetailNameValueRow
            {
                Name = DecodeComponent(parts[0]),
                Value = parts.Length > 1 ? DecodeComponent(parts[1]) : ""
            });
        }

        return rows;
    }

    private static List<DetailNameValueRow> ToResponseCookieRows(JsonElement headers)
    {
        List<DetailNameValueRow> rows = new();
        if (headers.ValueKind != JsonValueKind.Object)
        {
            return rows;
        }

        foreach (JsonProperty property in headers.EnumerateObject())
        {
            if (!property.Name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IEnumerable<string> cookieValues = property.Value.ValueKind == JsonValueKind.Array
                ? property.Value.EnumerateArray().Select(ElementToText)
                : new[] { ElementToText(property.Value) };

            foreach (string cookieValue in cookieValues)
            {
                string[] parts = cookieValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                string[] first = parts[0].Split('=', 2);
                rows.Add(new DetailNameValueRow
                {
                    Name = first[0].Trim(),
                    Value = first.Length > 1 ? first[1].Trim() : "",
                    Extra = parts.Length > 1 ? string.Join("; ", parts.Skip(1)) : ""
                });
            }
        }

        return rows;
    }

    private static (byte[] Bytes, int HeaderLength) BuildHttpBytes(string startLine, string headers, byte[] bodyBytes)
    {
        string prefix = string.IsNullOrWhiteSpace(headers)
            ? $"{startLine}\r\n\r\n"
            : $"{startLine}\r\n{headers}\r\n\r\n";
        byte[] headerBytes = Encoding.UTF8.GetBytes(prefix);
        if (bodyBytes.Length == 0)
        {
            return (headerBytes, headerBytes.Length);
        }

        byte[] combined = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, combined, headerBytes.Length, bodyBytes.Length);
        return (combined, headerBytes.Length);
    }

    private static List<HexViewRow> ToHexRows(byte[] bytes, int headerLength = 0)
    {
        const int bytesPerLine = 21;
        List<HexViewRow> rows = new();
        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            byte[] chunk = bytes.Skip(offset).Take(bytesPerLine).ToArray();
            int headerChunkLength = Math.Clamp(headerLength - offset, 0, chunk.Length);
            byte[] headerChunk = headerChunkLength > 0 ? chunk.Take(headerChunkLength).ToArray() : Array.Empty<byte>();
            byte[] bodyChunk = headerChunkLength < chunk.Length ? chunk.Skip(headerChunkLength).ToArray() : Array.Empty<byte>();
            rows.Add(new HexViewRow
            {
                Offset = offset.ToString("X8"),
                Hex = FormatHex(headerChunk, bodyChunk),
                Text = FormatAscii(headerChunk, bodyChunk),
                Kind = offset < headerLength ? "Header" : "Body",
                HexHeader = FormatHexSegment(headerChunk, appendTrailingSpace: bodyChunk.Length > 0),
                HexBody = FormatHexSegment(bodyChunk),
                TextHeader = ToVisibleText(headerChunk),
                TextBody = ToVisibleText(bodyChunk)
            });
        }

        return rows;
    }

    private static string FormatHex(byte[] headerChunk, byte[] bodyChunk)
    {
        if (headerChunk.Length == 0)
        {
            return FormatHexSegment(bodyChunk);
        }

        return bodyChunk.Length == 0
            ? FormatHexSegment(headerChunk)
            : FormatHexSegment(headerChunk, appendTrailingSpace: true) + FormatHexSegment(bodyChunk);
    }

    private static string FormatAscii(byte[] headerChunk, byte[] bodyChunk)
    {
        return ToVisibleText(headerChunk) + ToVisibleText(bodyChunk);
    }

    private static string FormatHexSegment(byte[] bytes, bool appendTrailingSpace = false)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        string text = string.Join(" ", bytes.Select(static current => current.ToString("X2")));
        return appendTrailingSpace ? text + " " : text;
    }

    private static string ToVisibleText(byte[] bytes)
    {
        return new string(bytes.Select(static current => current is >= 32 and <= 126 ? (char)current : '.').ToArray());
    }

    private static string DecodeComponent(string value)
    {
        string normalized = value.Replace("+", " ");
        try
        {
            return Uri.UnescapeDataString(normalized);
        }
        catch
        {
            return normalized;
        }
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

    private static byte[] TryGetImageBytes(string contentType, byte[] bytes)
    {
        return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? bytes : Array.Empty<byte>();
    }

    private static string ExtractImageType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "";
        }

        string type = contentType.Split(';', 2)[0].Trim();
        return type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? type : "";
    }
}
