using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.Services;

namespace SunnyNet.Wpf.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private const int LargePayloadThresholdBytes = 512 * 1024;
    private const int DetailFullBodyLoadLimitBytes = 8 * 1024 * 1024;
    private const int DetailTextChunkBytes = 512 * 1024;
    private static readonly string[] ResourceTypeTokens =
    {
        "javascript",
        "ecmascript",
        "css",
        "image/",
        "font",
        "woff",
        "ttf",
        "otf",
        "svg",
        "audio/",
        "video/",
        "wasm"
    };

    private static readonly string[] ResourceUrlExtensions =
    {
        ".js",
        ".mjs",
        ".css",
        ".map",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp",
        ".bmp",
        ".ico",
        ".svg",
        ".avif",
        ".woff",
        ".woff2",
        ".ttf",
        ".otf",
        ".eot",
        ".mp3",
        ".mp4",
        ".m4a",
        ".webm",
        ".wav",
        ".wasm"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HttpClient McpHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly GoBackendClient _backend = new();
    private readonly Dictionary<int, CaptureEntry> _sessionMap = new();
    private readonly HashSet<string> _favoriteKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedProcessFilterKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedDomainFilterKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingInterceptReplay> _pendingInterceptReplays = new();
    private readonly Dictionary<int, List<TrafficRuleHitItem>> _pendingRuleHits = new();
    private readonly DispatcherTimer _sessionFilterRefreshTimer;
    private const string AllProcessFilterKey = "__all_process__";
    private const string AllDomainFilterKey = "__all_domain__";
    private int _nextIndex = 1;
    private int _favoriteCount;
    private CaptureEntry? _selectedSession;
    private bool _showFavoritesOnly;
    private bool _isBusy;
    private bool _autoScroll;
    private bool _isCapturing = true;
    private bool _ieProxyEnabled;
    private bool _isSelectedSessionIntercepted;
    private bool _sessionFilterRefreshPending;
    private bool _sessionViewRefreshPending;
    private bool _proxyClearedOnExit;
    private bool _disposed;
    private int _breakpointMode;
    private int _selectedSessionLoadVersion;
    private bool _processDriverLoaded;
    private bool _captureAllProcesses;
    private readonly HashSet<int> _activeProcessPids = new();
    private string _newProcessName = "";
    private string _statusLeft = "未设置系统IE代理";
    private string _statusMiddle = "正在捕获";
    private string _statusRight = "等待 Go 核心启动";
    private int _runningPort;
    private RunningProcessItem? _selectedRunningProcess;
    private ProcessCaptureNameItem? _selectedProcessCaptureName;
    private SearchRequest? _lastTextSearchRequest;

    public MainWindowViewModel()
    {
        SessionsView = CollectionViewSource.GetDefaultView(Sessions);
        SessionsView.Filter = FilterSession;
        _sessionFilterRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _sessionFilterRefreshTimer.Tick += SessionFilterRefreshTimer_Tick;
        ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
        ReleaseAllCommand = new AsyncRelayCommand(ReleaseAllAsync);
        ToggleCaptureCommand = new AsyncRelayCommand(ToggleCaptureAsync);
        ToggleAutoScrollCommand = new RelayCommand(_ => AutoScroll = !AutoScroll);
        ResendSelectedCommand = new AsyncRelayCommand(ReleaseCurrentInterceptAsync, () => SelectedSession is not null && IsSelectedSessionIntercepted);
        CloseSessionCommand = new AsyncRelayCommand(CloseSelectedSessionAsync, () => SelectedSession is not null && IsSelectedSessionIntercepted);
        ReleaseCurrentCommand = new AsyncRelayCommand(ReleaseCurrentInterceptAsync, () => SelectedSession is not null && IsSelectedSessionIntercepted);
        Mcp.Port = SunnyNetCompatibleMcpServer.DefaultPort;
        UpdateMcpBridgeStatus();
        RefreshSessionFilterItems();
    }

    public event Action<CaptureEntry>? ScrollToEntryRequested;
    public event Action<string, string>? NotificationRequested;

    public ObservableCollection<CaptureEntry> Sessions { get; } = new();
    public ICollectionView SessionsView { get; }
    public SessionDetail Detail { get; } = new();
    public AppConfigState Settings { get; } = new();
    public McpIntegrationState Mcp { get; } = new();
    public ObservableCollection<SessionFilterItem> ProcessFilters { get; } = new();
    public ObservableCollection<SessionFilterItem> DomainFilters { get; } = new();
    public ObservableCollection<HostsRuleItem> HostsRuleItems { get; } = new();
    public ObservableCollection<ReplaceRuleItem> ReplaceRuleItems { get; } = new();
    public ObservableCollection<InterceptRuleItem> InterceptRuleItems { get; } = new();
    public ObservableCollection<RequestBlockRuleItem> RequestBlockRules { get; } = new();
    public ObservableCollection<WebSocketBlockRuleItem> WebSocketBlockRules { get; } = new();
    public ObservableCollection<TcpBlockRuleItem> TcpBlockRules { get; } = new();
    public ObservableCollection<UdpBlockRuleItem> UdpBlockRules { get; } = new();
    public ObservableCollection<RequestRewriteRuleItem> RequestRewriteRules { get; } = new();
    public ObservableCollection<RequestMappingRuleItem> RequestMappingRules { get; } = new();
    public ObservableCollection<RequestDecodeRuleItem> RequestDecodeRules { get; } = new();
    public ObservableCollection<RequestCertificateRuleItem> RequestCertificateItems { get; } = new();
    public ObservableCollection<ProcessCaptureNameItem> ProcessCaptureNames { get; } = new();
    public ObservableCollection<RunningProcessItem> RunningProcesses { get; } = new();
    public bool IsRefreshingSessionFilters { get; private set; }

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
            ScheduleSelectedSessionLoad();
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

    public bool ProcessDriverLoaded
    {
        get => _processDriverLoaded;
        set
        {
            if (!SetProperty(ref _processDriverLoaded, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProcessDriverStatusText));
        }
    }

    public string ProcessDriverStatusText => ProcessDriverLoaded ? "驱动已加载" : "驱动未加载";

    public bool CaptureAllProcesses
    {
        get => _captureAllProcesses;
        set => SetProperty(ref _captureAllProcesses, value);
    }

    public string NewProcessName
    {
        get => _newProcessName;
        set => SetProperty(ref _newProcessName, value ?? "");
    }

    public string CurrentPidCaptureText => _activeProcessPids.Count switch
    {
        <= 0 => "当前 PID 捕获：未设置",
        1 => $"当前 PID 捕获：{_activeProcessPids.First()}",
        _ => $"当前 PID 捕获：已选 {_activeProcessPids.Count} 项"
    };

    public RunningProcessItem? SelectedRunningProcess
    {
        get => _selectedRunningProcess;
        set => SetProperty(ref _selectedRunningProcess, value);
    }

    public ProcessCaptureNameItem? SelectedProcessCaptureName
    {
        get => _selectedProcessCaptureName;
        set => SetProperty(ref _selectedProcessCaptureName, value);
    }

    public string SelectedProcessFilterKey
    {
        get => _selectedProcessFilterKeys.FirstOrDefault() ?? AllProcessFilterKey;
        set => SetSelectedProcessFilters(IsAllFilterKey(value, AllProcessFilterKey) ? Array.Empty<string>() : new[] { value });
    }

    public string SelectedDomainFilterKey
    {
        get => _selectedDomainFilterKeys.FirstOrDefault() ?? AllDomainFilterKey;
        set => SetSelectedDomainFilters(IsAllFilterKey(value, AllDomainFilterKey) ? Array.Empty<string>() : new[] { value });
    }

    public bool HasActiveSessionFilter =>
        ShowFavoritesOnly
        || _selectedProcessFilterKeys.Count > 0
        || _selectedDomainFilterKeys.Count > 0;

    public bool ShowFavoritesOnly
    {
        get => _showFavoritesOnly;
        set
        {
            if (!SetProperty(ref _showFavoritesOnly, value))
            {
                return;
            }

            RefreshSessionFilterItems();
            OnPropertyChanged(nameof(HasActiveSessionFilter));
        }
    }

    public int FavoriteCount => _favoriteCount;

    public string FavoriteSummaryText => FavoriteCount > 0
        ? $"已收藏 {FavoriteCount} 条"
        : "暂无收藏";

    public CaptureEntry[] GetSessionSnapshot()
    {
        return Sessions.ToArray();
    }

    public CaptureEntry? FindSessionEntry(int theology)
    {
        if (theology <= 0)
        {
            return null;
        }

        return _sessionMap.TryGetValue(theology, out CaptureEntry? entry)
            ? entry
            : Sessions.FirstOrDefault(item => item.Theology == theology || item.Index == theology);
    }

    public Task<JsonElement?> InvokeBackendCommandAsync(string command, object? args = null, CancellationToken cancellationToken = default)
    {
        return _backend.InvokeAsync(command, args, cancellationToken);
    }

    public async Task ReloadSessionDetailAsync(int theology)
    {
        CaptureEntry? entry = FindSessionEntry(theology);
        if (entry is null)
        {
            return;
        }

        SelectedSession = entry;
        await LoadSelectedSessionImmediatelyAsync();
    }

    public Task ClearSessionsQuietAsync()
    {
        return ClearAllAsync();
    }

    public Task ReleaseAllQuietAsync()
    {
        return ReleaseAllAsync();
    }

    public async Task<SearchExecutionResult> SearchQuietAsync(SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return new SearchExecutionResult(0, null);
        }

        JsonElement? result = await _backend.InvokeAsync("查找", new
        {
            Options = request.Options,
            request.Value,
            request.Type,
            request.Range,
            request.Color,
            ProtoSkip = request.ProtoSkip
        });

        if (result is null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new SearchExecutionResult(0, null);
        }

        SearchExecutionResult applied = await ApplySearchResultAsync(result.Value, request);
        StatusRight = applied.MatchCount > 0
            ? $"搜索完成：命中 {applied.MatchCount:N0} 条"
            : "搜索完成：没有结果";
        return applied;
    }

    public async Task<int> CancelSearchHighlightQuietAsync()
    {
        int cleared = Sessions.Count(entry => !string.IsNullOrWhiteSpace(entry.SearchColor))
            + Detail.SocketEntries.Count(entry => !string.IsNullOrWhiteSpace(entry.SearchColor));

        await _backend.InvokeAsync("取消搜索颜色标记");
        ClearAllSearchHighlights();
        StatusRight = "搜索颜色标记已清除";
        return cleared;
    }

    public void InitializeFavoriteSettings(IEnumerable<string>? favoriteKeys, bool showFavoritesOnly)
    {
        _favoriteKeys.Clear();
        if (favoriteKeys is not null)
        {
            foreach (string favoriteKey in favoriteKeys)
            {
                string normalized = NormalizeFavoritePart(favoriteKey);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    _favoriteKeys.Add(normalized);
                }
            }
        }

        foreach (CaptureEntry entry in Sessions)
        {
            ApplyFavoriteState(entry, forceRefresh: true);
        }

        _favoriteCount = Sessions.Count(static entry => entry.IsFavorite);
        ShowFavoritesOnly = showFavoritesOnly;
        NotifyFavoriteSummaryChanged();
    }

    public IReadOnlyList<string> GetFavoriteKeys()
    {
        return _favoriteKeys
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void ToggleFavorite(CaptureEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        string favoriteKey = BuildFavoriteKey(entry);
        bool next = !entry.IsFavorite;
        entry.IsFavorite = next;
        if (next)
        {
            _favoriteKeys.Add(favoriteKey);
            _favoriteCount++;
        }
        else
        {
            _favoriteKeys.Remove(favoriteKey);
            _favoriteCount = Math.Max(0, _favoriteCount - 1);
        }

        NotifyFavoriteSummaryChanged();

        if (ShowFavoritesOnly)
        {
            RefreshSessionFilterItems();
        }
    }

    public void SetSelectedProcessFilters(IEnumerable<string> keys)
    {
        if (!ReplaceSelectedFilterKeys(_selectedProcessFilterKeys, keys, AllProcessFilterKey))
        {
            ApplySelectionToFilterItems(ProcessFilters, _selectedProcessFilterKeys);
            return;
        }

        ApplySelectionToFilterItems(ProcessFilters, _selectedProcessFilterKeys);
        RefreshSessionView();
        OnPropertyChanged(nameof(SelectedProcessFilterKey));
        OnPropertyChanged(nameof(HasActiveSessionFilter));
    }

    public void SetSelectedDomainFilters(IEnumerable<string> keys)
    {
        if (!ReplaceSelectedFilterKeys(_selectedDomainFilterKeys, keys, AllDomainFilterKey))
        {
            ApplySelectionToFilterItems(DomainFilters, _selectedDomainFilterKeys);
            return;
        }

        ApplySelectionToFilterItems(DomainFilters, _selectedDomainFilterKeys);
        RefreshSessionView();
        OnPropertyChanged(nameof(SelectedDomainFilterKey));
        OnPropertyChanged(nameof(HasActiveSessionFilter));
    }

    public async Task DeleteSessionsByProcessFiltersAsync(IEnumerable<string> keys)
    {
        HashSet<string> normalizedKeys = NormalizeFilterKeys(keys, AllProcessFilterKey);
        if (normalizedKeys.Count == 0)
        {
            return;
        }

        CaptureEntry[] entries = Sessions
            .Where(entry => normalizedKeys.Contains(NormalizeFilterValue(entry.Process, "未知进程")))
            .ToArray();

        await DeleteSessionsAsync(entries);
    }

    public async Task DeleteSessionsByDomainFiltersAsync(IEnumerable<string> keys)
    {
        HashSet<string> normalizedKeys = NormalizeFilterKeys(keys, AllDomainFilterKey);
        if (normalizedKeys.Count == 0)
        {
            return;
        }

        CaptureEntry[] entries = Sessions
            .Where(entry => normalizedKeys.Contains(NormalizeFilterValue(entry.Host, "无域名")))
            .ToArray();

        await DeleteSessionsAsync(entries);
    }

    public async Task DeleteSessionEntriesAsync(IEnumerable<CaptureEntry> entries)
    {
        await DeleteSessionsAsync(NormalizeSessionEntries(entries));
    }

    public async Task<int> DeleteSessionEntriesByRuleAsync(SessionClearRule rule)
    {
        CaptureEntry[] entries = rule == SessionClearRule.CurrentFilter
            ? SessionsView.OfType<CaptureEntry>().ToArray()
            : Sessions.Where(entry => MatchesClearRule(entry, rule)).ToArray();

        if (entries.Length == 0)
        {
            StatusRight = "没有符合清除条件的会话";
            return 0;
        }

        await DeleteSessionsAsync(entries);
        StatusRight = $"已清除 {entries.Length} 条会话";
        return entries.Length;
    }

    public async Task MarkSessionEntriesAsync(IEnumerable<CaptureEntry> entries, string? tagColor)
    {
        CaptureEntry[] selectedEntries = NormalizeSessionEntries(entries);
        if (selectedEntries.Length == 0)
        {
            return;
        }

        string nextColor = tagColor ?? "";
        int[] theologyIds = GetTheologyIds(selectedEntries);
        if (theologyIds.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(nextColor))
            {
                await _backend.InvokeAsync("标记颜色", new { empty = true, Data = theologyIds });
            }
            else
            {
                await _backend.InvokeAsync("标记颜色", new
                {
                    empty = false,
                    Data = selectedEntries
                        .Where(static entry => entry.Theology > 0)
                        .Select(entry => new { id = entry.Theology, color = nextColor })
                        .ToArray()
                });
            }
        }

        foreach (CaptureEntry entry in selectedEntries)
        {
            entry.TagColor = nextColor;
        }

        StatusRight = string.IsNullOrWhiteSpace(nextColor)
            ? $"已取消 {selectedEntries.Length} 条会话标记"
            : $"已标记 {selectedEntries.Length} 条会话";
    }

    public async Task ResendSessionEntriesAsync(IEnumerable<CaptureEntry> entries, int mode)
    {
        int[] theologyIds = GetTheologyIds(entries);
        if (theologyIds.Length == 0)
        {
            return;
        }

        await _backend.InvokeAsync("重发请求", new { Data = theologyIds, Mode = mode });
    }

    public async Task ResendSessionEntriesWithInterceptEditorAsync(IEnumerable<CaptureEntry> entries, int mode)
    {
        CaptureEntry[] selectedEntries = NormalizeSessionEntries(entries);
        if (selectedEntries.Length == 0)
        {
            return;
        }

        if (mode is not (1 or 2))
        {
            await ResendSessionEntriesAsync(selectedEntries, mode);
            return;
        }

        foreach (CaptureEntry entry in selectedEntries)
        {
            await ResendSingleSessionWithInterceptEditorAsync(entry, mode);
        }
    }

    public async Task CloseSessionEntriesAsync(IEnumerable<CaptureEntry> entries)
    {
        CaptureEntry[] selectedEntries = NormalizeSessionEntries(entries);
        int[] theologyIds = GetTheologyIds(selectedEntries);
        if (theologyIds.Length == 0)
        {
            return;
        }

        await _backend.InvokeAsync("关闭请求会话", new { Data = theologyIds });
        foreach (CaptureEntry entry in selectedEntries)
        {
            entry.BreakMode = 0;
            if (entry.State.Contains("已连接", StringComparison.OrdinalIgnoreCase))
            {
                entry.State = "断开中";
            }
        }

        RefreshSelectedSessionInterceptState();
        StatusRight = "断开会话请求已提交";
    }

    private async Task ResendSingleSessionWithInterceptEditorAsync(CaptureEntry entry, int mode)
    {
        TaskCompletionSource<CaptureEntry> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingInterceptReplay pendingReplay = new(entry, mode, completion);
        _pendingInterceptReplays.Add(pendingReplay);
        await _backend.InvokeAsync("重发请求", new { Data = new[] { entry.Theology }, Mode = mode });

        CaptureEntry interceptedEntry;
        try
        {
            interceptedEntry = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            _pendingInterceptReplays.Remove(pendingReplay);

            NotificationRequested?.Invoke("重放失败", mode == 1 ? "等待上行拦截命中超时。" : "等待下行拦截命中超时。");
            return;
        }

        SelectedSession = interceptedEntry;
        await LoadSelectedSessionImmediatelyAsync();
        Detail.EnableInlineIntercept(mode);
        ScrollToEntryRequested?.Invoke(interceptedEntry);
    }

    private async Task SaveInterceptReplayEditAsync(CaptureEntry entry, int mode, string text)
    {
        string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
        await _backend.InvokeAsync("保存修改数据", new
        {
            Theology = entry.Theology,
            Type = mode == 1 ? "Request" : "Response",
            Tabs = "Raw",
            UTF8 = true,
            Data = data
        });
    }

    private async Task ReleaseSessionAsync(CaptureEntry entry, int nextBreak)
    {
        await _backend.InvokeAsync("断点点击", new { Theology = entry.Theology, NextBreak = nextBreak });
        entry.BreakMode = nextBreak;
        if (SelectedSession?.Theology == entry.Theology)
        {
            RefreshSelectedSessionInterceptState();
        }
    }

    public async Task GenerateRequestCodeAsync(IEnumerable<CaptureEntry> entries, string lang, string module)
    {
        int[] theologyIds = GetTheologyIds(entries);
        if (theologyIds.Length == 0)
        {
            return;
        }

        JsonElement? result = await _backend.InvokeAsync("获取请求代码", new { Data = theologyIds, Lang = lang, Module = module });
        string code = result?.ValueKind == JsonValueKind.String ? result.Value.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("没有生成可复制的请求代码。");
        }

        Clipboard.SetText(code);
        StatusRight = $"已复制请求代码：{lang} / {module}";
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
            await RefreshMcpStatusAsync();
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionFilterRefreshTimer.Stop();
        await DisableSystemProxyOnExitAsync();
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

    public async Task UpdateSessionNotesAsync(IEnumerable<CaptureEntry> entries, string notes)
    {
        CaptureEntry[] selectedEntries = NormalizeSessionEntries(entries);
        if (selectedEntries.Length == 0)
        {
            return;
        }

        string nextNotes = notes ?? "";
        foreach (CaptureEntry entry in selectedEntries)
        {
            if (entry.Theology > 0)
            {
                await _backend.InvokeAsync("更新注释", new { Theology = entry.Theology, Data = nextNotes });
            }

            entry.Notes = nextNotes;
        }

        StatusRight = selectedEntries.Length > 1
            ? $"已更新 {selectedEntries.Length} 条会话备注"
            : "已更新会话备注";
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

    public void AddHostsRule()
    {
        HostsRuleItems.Add(new HostsRuleItem());
        SyncHostsRulesText();
    }

    public void RemoveHostsRule(HostsRuleItem? item)
    {
        if (item is null)
        {
            return;
        }

        HostsRuleItems.Remove(item);
        SyncHostsRulesText();
    }

    public async Task ApplyHostsRulesAsync()
    {
        foreach (HostsRuleItem item in HostsRuleItems)
        {
            item.Hash = EnsureRuleHash(item.Hash);
        }

        JsonElement? result = await _backend.InvokeAsync("保存HOSTS规则", new
        {
            Data = HostsRuleItems.Select(static item => new Dictionary<string, object?>
            {
                ["Hash"] = item.Hash,
                ["源地址"] = item.Source,
                ["新地址"] = item.Target
            }).ToArray()
        });

        HashSet<string> failures = result is { ValueKind: JsonValueKind.Array }
            ? new HashSet<string>(DeserializeList<string>(result.Value), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int failureCount = 0;
        foreach (HostsRuleItem item in HostsRuleItems)
        {
            bool failed = failures.Contains(item.Hash);
            item.State = failed ? "保存失败" : "已保存";
            if (failed)
            {
                failureCount++;
            }
        }

        SyncHostsRulesText();
        StatusRight = failureCount > 0 ? $"HOSTS 规则保存完成，失败 {failureCount} 项" : "HOSTS 规则已保存";
        NotificationRequested?.Invoke(failureCount > 0 ? "错误" : "提示",
            failureCount > 0 ? $"有 {failureCount} 条 HOSTS 规则保存失败，请检查格式。" : "HOSTS 规则已保存。");
    }

    public void AddReplaceRule()
    {
        ReplaceRuleItems.Add(new ReplaceRuleItem());
        SyncReplaceRulesText();
    }

    public void RemoveReplaceRule(ReplaceRuleItem? item)
    {
        if (item is null)
        {
            return;
        }

        ReplaceRuleItems.Remove(item);
        SyncReplaceRulesText();
    }

    public async Task ApplyReplaceRulesAsync()
    {
        foreach (ReplaceRuleItem item in ReplaceRuleItems)
        {
            item.Hash = EnsureRuleHash(item.Hash);
        }

        JsonElement? result = await _backend.InvokeAsync("保存替换规则", new
        {
            Data = ReplaceRuleItems.Select(static item => new Dictionary<string, object?>
            {
                ["Hash"] = item.Hash,
                ["替换类型"] = item.RuleType,
                ["源内容"] = item.SourceContent,
                ["替换内容"] = item.ReplacementContent
            }).ToArray()
        });

        HashSet<string> failures = result is { ValueKind: JsonValueKind.Array }
            ? new HashSet<string>(DeserializeList<string>(result.Value), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int failureCount = 0;
        foreach (ReplaceRuleItem item in ReplaceRuleItems)
        {
            bool failed = failures.Contains(item.Hash);
            item.State = failed ? "保存失败" : "已保存";
            if (failed)
            {
                failureCount++;
            }
        }

        SyncReplaceRulesText();
        StatusRight = failureCount > 0 ? $"替换规则保存完成，失败 {failureCount} 项" : "替换规则已保存";
        NotificationRequested?.Invoke(failureCount > 0 ? "错误" : "提示",
            failureCount > 0 ? $"有 {failureCount} 条替换规则保存失败，请检查内容。" : "替换规则已保存。");
    }

    public void AddInterceptRule()
    {
        InterceptRuleItems.Add(new InterceptRuleItem());
        SyncInterceptRulesText();
    }

    public void RemoveInterceptRule(InterceptRuleItem? item)
    {
        if (item is null)
        {
            return;
        }

        InterceptRuleItems.Remove(item);
        SyncInterceptRulesText();
    }

    public async Task ApplyInterceptRulesAsync()
    {
        foreach (InterceptRuleItem item in InterceptRuleItems)
        {
            item.Hash = EnsureRuleHash(item.Hash);
        }

        SyncInterceptRulesText();
        JsonElement? result = await _backend.InvokeAsync("保存拦截规则", new
        {
            Data = InterceptRuleItems.Select(static item => new Dictionary<string, object?>
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

        bool ok = result is null || result.Value.ValueKind != JsonValueKind.False;
        foreach (InterceptRuleItem item in InterceptRuleItems)
        {
            item.State = ok ? "已保存" : "保存失败";
        }

        StatusRight = ok ? "拦截规则已保存" : "拦截规则保存失败";
        NotificationRequested?.Invoke(ok ? "提示" : "错误", ok ? "拦截规则已保存。" : "拦截规则保存失败，请检查规则。");
    }

    public async Task<bool> ApplyRuleCenterConfigAsync(bool showNotification = false)
    {
        string json = SyncRuleCenterRulesText();
        JsonElement? result = await _backend.InvokeAsync("保存规则中心配置", new
        {
            Data = EncodeTextBase64(json)
        });

        bool ok = result is { ValueKind: JsonValueKind.True };
        string state = ok ? "已保存" : "保存失败";
        foreach (RequestBlockRuleItem item in RequestBlockRules)
        {
            item.State = state;
        }

        foreach (WebSocketBlockRuleItem item in WebSocketBlockRules)
        {
            item.State = state;
        }

        foreach (TcpBlockRuleItem item in TcpBlockRules)
        {
            item.State = state;
        }

        foreach (UdpBlockRuleItem item in UdpBlockRules)
        {
            item.State = state;
        }

        foreach (RequestRewriteRuleItem item in RequestRewriteRules)
        {
            item.State = state;
        }

        foreach (RequestMappingRuleItem item in RequestMappingRules)
        {
            item.State = item.LegacyReplaceRule ? "兼容旧规则" : state;
        }

        foreach (RequestDecodeRuleItem item in RequestDecodeRules)
        {
            item.State = state;
        }

        StatusRight = ok ? "规则中心配置已保存" : "规则中心配置保存失败";
        if (showNotification)
        {
            NotificationRequested?.Invoke(ok ? "提示" : "错误", ok ? "规则中心配置已保存。" : "规则中心配置保存失败。");
        }

        return ok;
    }

    public async Task RestoreDefaultScriptAsync()
    {
        JsonElement? result = await _backend.InvokeAsync("获取默认Go脚本代码");
        if (result is { ValueKind: JsonValueKind.String })
        {
            Settings.ScriptCode = DecodePayloadElement(result.Value);
            StatusRight = "已载入默认脚本";
        }
    }

    public async Task FormatScriptCodeAsync()
    {
        JsonElement? result = await _backend.InvokeAsync("格式化Go脚本代码", new
        {
            code = EncodeTextBase64(Settings.ScriptCode)
        });

        if (result is { ValueKind: JsonValueKind.String })
        {
            Settings.ScriptCode = DecodePayloadElement(result.Value);
            StatusRight = "脚本已格式化";
        }
    }

    public async Task ApplyScriptCodeAsync()
    {
        await FormatScriptCodeAsync();

        JsonElement? result = await _backend.InvokeAsync("保存Go脚本代码", new
        {
            code = EncodeTextBase64(Settings.ScriptCode)
        });

        string message = result is { ValueKind: JsonValueKind.String }
            ? result.Value.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(message))
        {
            StatusRight = "脚本已保存";
            NotificationRequested?.Invoke("提示", "脚本已保存并通过校验。");
            return;
        }

        StatusRight = "脚本保存失败";
        NotificationRequested?.Invoke("错误", message);
    }

    public async Task LoadProcessDriverAsync()
    {
        JsonElement? result = await _backend.InvokeAsync("加载驱动");
        bool ok = result is { ValueKind: JsonValueKind.True };
        ProcessDriverLoaded = ok;

        if (ok)
        {
            StatusRight = "进程驱动已加载";
            await RefreshRunningProcessesAsync();
            return;
        }

        StatusRight = "进程驱动加载失败";
        NotificationRequested?.Invoke("错误", "加载驱动失败，请确认当前程序具有管理员权限。");
    }

    public async Task RefreshRunningProcessesAsync()
    {
        JsonElement? result = await _backend.InvokeAsync("枚举进程");
        if (result is not { ValueKind: JsonValueKind.Object })
        {
            ReplaceRows(RunningProcesses, Array.Empty<RunningProcessItem>());
            SelectedRunningProcess = null;
            return;
        }

        List<RunningProcessItem> items = new();
        foreach (JsonProperty property in result.Value.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out int pid) || pid <= 0)
            {
                continue;
            }

            items.Add(new RunningProcessItem
            {
                Pid = pid,
                Name = ElementToText(property.Value),
                IsCaptured = _activeProcessPids.Contains(pid)
            });
        }

        items.Sort(static (left, right) =>
        {
            int nameResult = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameResult != 0 ? nameResult : left.Pid.CompareTo(right.Pid);
        });

        ReplaceRows(RunningProcesses, items);
        SelectedRunningProcess = RunningProcesses.FirstOrDefault(item => _activeProcessPids.Contains(item.Pid))
            ?? RunningProcesses.FirstOrDefault();
    }

    public async Task SetCaptureAllProcessesAsync(bool enable)
    {
        await _backend.InvokeAsync("进程驱动添加进程名", new { Name = "{OpenALL}", isSet = enable });
        CaptureAllProcesses = enable;
        if (enable)
        {
            await ClearPidCaptureAsync();
            UpdateRunningProcessCaptureState();
        }

        StatusRight = enable ? "已切换为捕获所有进程" : "已关闭捕获所有进程";
    }

    public async Task AddProcessCaptureNameAsync(string? name = null)
    {
        if (CaptureAllProcesses)
        {
            NotificationRequested?.Invoke("错误", "请先关闭“捕获所有进程”，再指定进程名。");
            return;
        }

        string processName = (name ?? NewProcessName).Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            NotificationRequested?.Invoke("错误", "请输入要添加的进程名。");
            return;
        }

        if (ProcessCaptureNames.Any(item => string.Equals(item.Name, processName, StringComparison.OrdinalIgnoreCase)))
        {
            NewProcessName = "";
            return;
        }

        await _backend.InvokeAsync("进程驱动添加进程名", new { Name = processName, isSet = true });
        ProcessCaptureNames.Add(new ProcessCaptureNameItem { Name = processName });
        NewProcessName = "";
        StatusRight = $"已添加进程名：{processName}";
    }

    public async Task RemoveProcessCaptureNameAsync(ProcessCaptureNameItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _backend.InvokeAsync("进程驱动添加进程名", new { Name = item.Name, isSet = false });
        ProcessCaptureNames.Remove(item);
        if (ReferenceEquals(SelectedProcessCaptureName, item))
        {
            SelectedProcessCaptureName = null;
        }

        StatusRight = $"已移除进程名：{item.Name}";
    }

    public Task SetPidCaptureAsync(RunningProcessItem? item)
    {
        return SetPidCaptureAsync(item is null ? Array.Empty<RunningProcessItem>() : new[] { item });
    }

    public async Task SetPidCaptureAsync(IEnumerable<RunningProcessItem> items)
    {
        RunningProcessItem[] targets = items
            .Where(static item => item is not null)
            .DistinctBy(static item => item.Pid)
            .Where(static item => item.Pid > 0)
            .ToArray();

        if (targets.Length == 0)
        {
            return;
        }

        if (CaptureAllProcesses)
        {
            NotificationRequested?.Invoke("错误", "请先关闭“捕获所有进程”，再按 PID 捕获。");
            return;
        }

        foreach (RunningProcessItem item in targets)
        {
            await _backend.InvokeAsync("进程驱动添加PID", new { PID = item.Pid, isSelected = true });
            _activeProcessPids.Add(item.Pid);
        }

        UpdateRunningProcessCaptureState();
        StatusRight = targets.Length == 1
            ? $"已按 PID 捕获：{targets[0].Pid}"
            : $"已按 PID 捕获 {targets.Length} 项";
    }

    public Task ClearPidCaptureAsync()
    {
        return ClearPidCaptureAsync(_activeProcessPids
            .Select(pid => new RunningProcessItem { Pid = pid })
            .ToArray());
    }

    public async Task ClearPidCaptureAsync(IEnumerable<RunningProcessItem> items)
    {
        RunningProcessItem[] targets = items
            .Where(static item => item is not null)
            .DistinctBy(static item => item.Pid)
            .Where(static item => item.Pid > 0)
            .ToArray();

        if (targets.Length == 0)
        {
            return;
        }

        foreach (RunningProcessItem item in targets)
        {
            await _backend.InvokeAsync("进程驱动添加PID", new { PID = item.Pid, isSelected = false });
            _activeProcessPids.Remove(item.Pid);
        }

        UpdateRunningProcessCaptureState();
        StatusRight = targets.Length == 1
            ? $"已清除 PID 捕获：{targets[0].Pid}"
            : $"已清除 {targets.Length} 项 PID 捕获";
    }

    public async Task AddRequestCertificateRuleAsync()
    {
        JsonElement? result = await _backend.InvokeAsync("创建证书管理器");
        int context = result is { ValueKind: JsonValueKind.Number } && result.Value.TryGetInt32(out int id)
            ? id
            : 0;
        if (context <= 0)
        {
            NotificationRequested?.Invoke("错误", "创建请求证书管理器失败。");
            return;
        }

        RequestCertificateItems.Add(new RequestCertificateRuleItem
        {
            Context = context,
            UsageRule = "解析及发送"
        });
        StatusRight = "已新增请求证书规则";
    }

    public async Task RemoveRequestCertificateRuleAsync(RequestCertificateRuleItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _backend.InvokeAsync("删除证书管理器", new
        {
            context = new[]
            {
                new { id = item.Context }
            }
        });

        RequestCertificateItems.Remove(item);
        StatusRight = "已删除请求证书规则";
    }

    public async Task ResolveRequestCertificateCommonNameAsync(RequestCertificateRuleItem? item)
    {
        if (item is null)
        {
            return;
        }

        JsonElement? result = await _backend.InvokeAsync("查询证书CommonName", new
        {
            context = item.Context,
            rule = item.UsageRule
        });

        string host = result is { ValueKind: JsonValueKind.String }
            ? result.Value.GetString() ?? ""
            : "";

        if (string.IsNullOrWhiteSpace(host))
        {
            NotificationRequested?.Invoke("错误", "证书中没有可用的主机名。");
            return;
        }

        item.Host = host;
        StatusRight = "已读取证书主机名";
    }

    public async Task LoadRequestCertificateAsync(RequestCertificateRuleItem? item)
    {
        if (item is null)
        {
            return;
        }

        JsonElement? result = await _backend.InvokeAsync("载入请求证书", new
        {
            Data = new Dictionary<string, object?>
            {
                ["context"] = item.Context,
                ["使用规则"] = item.UsageRule,
                ["证书文件"] = item.CertificateFile,
                ["密码"] = item.Password,
                ["主机名"] = item.Host
            }
        });

        bool ok = result is { ValueKind: JsonValueKind.True };
        item.LoadState = ok ? "已载入" : "载入失败";
        StatusRight = ok ? "请求证书已载入" : "请求证书载入失败";
    }

    public async Task<(byte[] Bytes, string Text, string Json)> LoadSocketPayloadAsync(int theology, int index)
    {
        if (theology <= 0 || index < 0)
        {
            return (Array.Empty<byte>(), "", "");
        }

        JsonElement? result = await _backend.InvokeAsync("socket请求获取", new
        {
            Theology = theology,
            Index = index + 1
        });

        if (result is not { ValueKind: JsonValueKind.String })
        {
            return (Array.Empty<byte>(), "", "");
        }

        byte[] bytes = DecodePayloadBytes(result.Value);
        string text = BytesToDisplayText(bytes);
        string json = LooksBinary(bytes) ? "" : TryFormatJson(text);
        return (bytes, text, json);
    }

    public async Task<string> ParseProtobufAsync(byte[] data, int skip)
    {
        if (data.Length == 0)
        {
            return "";
        }

        JsonElement? result = await _backend.InvokeAsync("protobufToJson", new
        {
            Data = Convert.ToBase64String(data),
            skip
        });

        return result is { ValueKind: JsonValueKind.String }
            ? result.Value.GetString() ?? ""
            : "";
    }

    public async Task<(bool Ok, string Directory, IReadOnlyList<string> Messages, string Error)> ImportProtobufSchemaAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (false, "", Array.Empty<string>(), "请选择 Protobuf 描述目录或 .pb 文件。");
        }

        JsonElement? result = await _backend.InvokeAsync("protobufImportSchema", new
        {
            Directory = path
        });

        if (result is not { ValueKind: JsonValueKind.Object })
        {
            return (false, "", Array.Empty<string>(), "导入 Protobuf 描述失败。");
        }

        bool ok = GetBool(result.Value, "Ok");
        string directory = GetString(result.Value, "Directory", path.Trim());
        string error = GetString(result.Value, "Error");
        IReadOnlyList<string> messages = GetStringArray(result.Value, "Messages");
        return (ok, directory, messages, error);
    }

    public async Task<(bool Ok, string Json, string Error)> ParseProtobufBySchemaAsync(byte[] data, int skip, string path, string messageType)
    {
        if (data.Length == 0)
        {
            return (false, "", "当前数据为空。");
        }

        JsonElement? result = await _backend.InvokeAsync("protobufToJsonBySchema", new
        {
            Data = Convert.ToBase64String(data),
            skip,
            Directory = path,
            MessageType = messageType
        });

        if (result is not { ValueKind: JsonValueKind.Object })
        {
            return (false, "", "按结构解析 Protobuf 失败。");
        }

        return (
            GetBool(result.Value, "Ok"),
            GetString(result.Value, "Json"),
            GetString(result.Value, "Error"));
    }

    public async Task<bool> SendSocketFrameAsync(int theology, string wsType, string sendType, string direction, string text)
    {
        if (theology <= 0)
        {
            return false;
        }

        JsonElement? result = await _backend.InvokeAsync("主动发送", new
        {
            Theology = theology,
            IsWs = true,
            IsTCP = false,
            wsType,
            SendType = sendType,
            direction,
            Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""))
        });

        return result is { ValueKind: JsonValueKind.True };
    }

    public async Task<SearchExecutionResult> SearchAsync(SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            NotificationRequested?.Invoke("查找失败", "请输入要搜索的内容。");
            return new SearchExecutionResult(0, null);
        }

        try
        {
            IsBusy = true;
            StatusRight = "正在搜索...";

            JsonElement? result = await _backend.InvokeAsync("查找", new
            {
                Options = request.Options,
                request.Value,
                request.Type,
                request.Range,
                request.Color,
                ProtoSkip = request.ProtoSkip
            });

            if (result is null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                StatusRight = "搜索未返回结果";
                return new SearchExecutionResult(0, null);
            }

            SearchExecutionResult applied = await ApplySearchResultAsync(result.Value, request);
            StatusRight = applied.MatchCount > 0
                ? $"搜索完成：命中 {applied.MatchCount:N0} 条"
                : "搜索完成：没有结果";
            return applied;
        }
        catch (Exception exception)
        {
            StatusRight = "搜索失败";
            NotificationRequested?.Invoke("搜索失败", exception.Message);
            return new SearchExecutionResult(0, null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CancelSearchHighlightAsync()
    {
        try
        {
            await _backend.InvokeAsync("取消搜索颜色标记");
            ClearAllSearchHighlights();
            StatusRight = "搜索颜色标记已清除";
        }
        catch (Exception exception)
        {
            NotificationRequested?.Invoke("清除搜索标记失败", exception.Message);
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

    public async Task RefreshMcpStatusAsync()
    {
        UpdateMcpBridgeStatus();
        Mcp.ToolCount = 0;
        Mcp.LastError = "";

        if (!Mcp.Enabled)
        {
            Mcp.ServerRunning = false;
            Mcp.ServerStatusText = "已关闭";
            Mcp.LastCheckedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return;
        }

        try
        {
            using HttpResponseMessage healthResponse = await McpHttpClient.GetAsync(Mcp.HealthUrl);
            healthResponse.EnsureSuccessStatusCode();
            string healthJson = await healthResponse.Content.ReadAsStringAsync();
            using JsonDocument healthDocument = JsonDocument.Parse(healthJson);
            JsonElement root = healthDocument.RootElement;
            bool running = GetBool(root, "running");
            string status = GetString(root, "status", running ? "ok" : "offline");

            Mcp.ServerRunning = running;
            Mcp.ServerStatusText = running && status.Equals("ok", StringComparison.OrdinalIgnoreCase)
                ? "内置服务运行中"
                : "内置服务未就绪";

            string toolsRequest = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "tools-list",
                method = "tools/list",
                @params = new { }
            });

            using StringContent content = new(toolsRequest, Encoding.UTF8, "application/json");
            using HttpResponseMessage toolsResponse = await McpHttpClient.PostAsync(Mcp.EndpointUrl, content);
            toolsResponse.EnsureSuccessStatusCode();
            string toolsJson = await toolsResponse.Content.ReadAsStringAsync();
            using JsonDocument toolsDocument = JsonDocument.Parse(toolsJson);
            JsonElement tools = GetProperty(GetProperty(toolsDocument.RootElement, "result"), "tools");
            Mcp.ToolCount = tools.ValueKind == JsonValueKind.Array ? tools.GetArrayLength() : 0;
        }
        catch (Exception exception)
        {
            Mcp.ServerRunning = false;
            Mcp.ServerStatusText = "内置服务不可用";
            Mcp.LastError = exception.Message;
        }
        finally
        {
            Mcp.LastCheckedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
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

    public async Task DisableSystemProxyOnExitAsync()
    {
        if (_proxyClearedOnExit)
        {
            return;
        }

        _proxyClearedOnExit = true;
        if (!_backend.IsRunning)
        {
            return;
        }

        try
        {
            await _backend.InvokeAsync("设置IE代理", new { Set = false });
            IeProxyEnabled = false;
            StatusLeft = "未设置系统IE代理";
        }
        catch
        {
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
        _favoriteCount = 0;
        NotifyFavoriteSummaryChanged();
        RefreshSessionFilterItems();
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
        Detail.DisableInlineIntercept();
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
        if (nextBreak == 0)
        {
            Detail.DisableInlineIntercept();
        }

        RefreshSelectedSessionInterceptState();
    }

    private async Task ReleaseCurrentInterceptAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        int mode = SelectedSession.BreakMode;
        if (mode is 1 or 2)
        {
            string text = mode == 1 ? Detail.EditableRequestRaw : Detail.EditableResponseRaw;
            await SaveInterceptReplayEditAsync(SelectedSession, mode, text);
        }

        await BreakpointAsync(0);
        StatusRight = mode == 1 ? "上行修改已保存并放行" : mode == 2 ? "下行修改已保存并放行" : "已运行到结束";
    }

    private async Task<SearchExecutionResult> ApplySearchResultAsync(JsonElement result, SearchRequest request)
    {
        if (request.ClearPrevious)
        {
            foreach (int theology in GetIntArray(result, "LastSearchResult"))
            {
                if (_sessionMap.TryGetValue(theology, out CaptureEntry? previous))
                {
                    previous.SearchColor = "";
                }
            }

            foreach (SocketEntry socketEntry in Detail.SocketEntries)
            {
                socketEntry.SearchColor = "";
            }
        }

        List<int> matchedTheology = GetIntArray(result, "SearchResult");
        string color = GetString(result, "Color", request.Color);
        CaptureEntry? firstMatch = null;
        _lastTextSearchRequest = matchedTheology.Count > 0 && IsTextDetailSearchRequest(request)
            ? request
            : null;
        if (_lastTextSearchRequest is null && request.ClearPrevious)
        {
            Detail.RequestSearchText = "";
            Detail.ResponseSearchText = "";
            Detail.DetailSearchIgnoreCase = true;
        }

        foreach (int theology in matchedTheology)
        {
            if (!_sessionMap.TryGetValue(theology, out CaptureEntry? entry))
            {
                continue;
            }

            entry.SearchColor = color;
            firstMatch ??= entry;
        }

        if (firstMatch is not null)
        {
            if (SelectedSession?.Theology == firstMatch.Theology)
            {
                await LoadSelectedSessionImmediatelyAsync();
            }
            else
            {
                SelectedSession = firstMatch;
            }

            ScrollToEntryRequested?.Invoke(firstMatch);
        }

        return new SearchExecutionResult(matchedTheology.Count, firstMatch);
    }

    private void ClearAllSearchHighlights()
    {
        foreach (CaptureEntry entry in Sessions)
        {
            entry.SearchColor = "";
        }

        foreach (SocketEntry socketEntry in Detail.SocketEntries)
        {
            socketEntry.SearchColor = "";
        }

        _lastTextSearchRequest = null;
        Detail.RequestSearchText = "";
        Detail.ResponseSearchText = "";
        Detail.DetailSearchIgnoreCase = true;
    }

    private void ApplyDetailTextSearch(CaptureEntry selected)
    {
        if (_lastTextSearchRequest is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.SearchColor))
        {
            Detail.RequestSearchText = "";
            Detail.ResponseSearchText = "";
            Detail.DetailSearchIgnoreCase = true;
            return;
        }

        Detail.DetailSearchIgnoreCase = _lastTextSearchRequest.IgnoreCase;
        bool searchRequest = _lastTextSearchRequest.Range is "全部" or "HTTP请求";
        bool searchResponse = _lastTextSearchRequest.Range is "全部" or "HTTP响应";
        Detail.RequestSearchText = searchRequest ? _lastTextSearchRequest.Value : "";
        Detail.ResponseSearchText = searchResponse ? _lastTextSearchRequest.Value : "";
    }

    private static bool IsTextDetailSearchRequest(SearchRequest request)
    {
        return (request.Type is "UTF8" or "GBK")
            && (request.Range is "全部" or "HTTP请求" or "HTTP响应");
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
            case "规则命中":
                ApplyRuleHit(envelope.Args);
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
        bool changed = false;
        CaptureEntry? interceptedEntry = null;
        foreach (CaptureEntry entry in DeserializeList<CaptureEntry>(args))
        {
            if (entry.Theology == 0)
            {
                continue;
            }

            if (_sessionMap.TryGetValue(entry.Theology, out CaptureEntry? existing))
            {
                existing.UpdateFrom(entry);
                ApplyPendingRuleHits(existing);
                ApplyFavoriteState(existing, forceRefresh: false);
                if (SelectedSession?.Theology == existing.Theology)
                {
                    RefreshSelectedSessionInterceptState();
                    if (existing.BreakMode <= 0)
                    {
                        Detail.DisableInlineIntercept();
                    }
                }

                CompletePendingInterceptReplay(existing);
                changed = true;
                continue;
            }

            AddEntry(entry);
            ApplyPendingRuleHits(entry);
            CompletePendingInterceptReplay(entry);
            if (entry.BreakMode > 0)
            {
                interceptedEntry = entry;
            }
            changed = true;
        }

        if (changed)
        {
            ScheduleSessionFilterRefresh(refreshSessionView: HasActiveSessionFilter);
        }

        OpenInterceptedSession(interceptedEntry);
    }

    private void ApplyUpdateList(JsonElement args)
    {
        bool changed = false;
        CaptureEntry? interceptedEntry = null;
        foreach (CaptureEntry entry in DeserializeList<CaptureEntry>(args))
        {
            if (entry.Theology == 0)
            {
                continue;
            }

            if (_sessionMap.TryGetValue(entry.Theology, out CaptureEntry? existing))
            {
                existing.UpdateFrom(entry);
                ApplyPendingRuleHits(existing);
                ApplyFavoriteState(existing, forceRefresh: false);
                changed = true;
                if (SelectedSession?.Theology == existing.Theology)
                {
                    RefreshSelectedSessionInterceptState();
                }

                if (existing.BreakMode > 0)
                {
                    interceptedEntry = existing;
                }

                CompletePendingInterceptReplay(existing);
            }
            else
            {
                AddEntry(entry);
                ApplyPendingRuleHits(entry);
                CompletePendingInterceptReplay(entry);
                if (entry.BreakMode > 0)
                {
                    interceptedEntry = entry;
                }
                changed = true;
            }
        }

        if (changed)
        {
            ScheduleSessionFilterRefresh(refreshSessionView: HasActiveSessionFilter);
        }

        OpenInterceptedSession(interceptedEntry);
    }

    private void ApplyRuleHit(JsonElement args)
    {
        TrafficRuleHitItem? hit;
        try
        {
            hit = JsonSerializer.Deserialize<TrafficRuleHitItem>(args.GetRawText(), JsonOptions);
        }
        catch
        {
            return;
        }

        if (hit is null || hit.Theology <= 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(hit.Time))
        {
            hit.Time = DateTime.Now.ToString("HH:mm:ss.fff");
        }

        if (_sessionMap.TryGetValue(hit.Theology, out CaptureEntry? entry))
        {
            entry.AddRuleHit(hit);
            return;
        }

        if (!_pendingRuleHits.TryGetValue(hit.Theology, out List<TrafficRuleHitItem>? pending))
        {
            pending = new List<TrafficRuleHitItem>();
            _pendingRuleHits[hit.Theology] = pending;
        }

        pending.Add(hit);
    }

    private void ApplyPendingRuleHits(CaptureEntry entry)
    {
        if (!_pendingRuleHits.Remove(entry.Theology, out List<TrafficRuleHitItem>? hits))
        {
            return;
        }

        foreach (TrafficRuleHitItem hit in hits)
        {
            entry.AddRuleHit(hit);
        }
    }

    private void OpenInterceptedSession(CaptureEntry? entry)
    {
        if (entry is null || _pendingInterceptReplays.Any(static replay => !replay.Completion.Task.IsCompleted))
        {
            return;
        }

        SelectedSession = entry;
        ScrollToEntryRequested?.Invoke(entry);
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
            Detail.HasResponseDisplayBody = false;
            Detail.ResponseHex = ToHex(DecodePayloadBytes(GetProperty(args, "Body")));
            Detail.ResponseStateText = "error";
            Detail.ResponseStateCode = -1;
            ReplaceRows(Detail.ResponseHeaderRows, Array.Empty<DetailNameValueRow>());
            ReplaceRows(Detail.ResponseCookieRows, Array.Empty<DetailNameValueRow>());
            ReplaceRows(Detail.ResponseHexRows, ToHexRows(DecodePayloadBytes(GetProperty(args, "Body"))));
            Detail.ResponseHexBytes = DecodePayloadBytes(GetProperty(args, "Body"));
            Detail.ResponseHexHeaderLength = 0;
            Detail.ResponseHexSource = null;
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
        byte[] responseBytes = DecodePayloadBytes(GetProperty(args, "Body"));
        int responseBodySize = GetInt(args, "BodySize", responseBytes.Length);
        bool responseBodyTruncated = GetBool(args, "BodyTruncated");
        bool hasResponseDisplayBody = GetBool(args, "HasDisplayBody");
        byte[] responseDisplayBytes = hasResponseDisplayBody
            ? DecodePayloadBytes(GetProperty(args, "DisplayBody"))
            : responseBytes;
        int responseDisplayBodySize = hasResponseDisplayBody ? GetInt(args, "DisplayBodySize", responseDisplayBytes.Length) : responseBodySize;
        bool responseDisplayTruncated = hasResponseDisplayBody ? GetBool(args, "DisplayBodyTruncated") : responseBodyTruncated;
        Detail.HasResponseDisplayBody = hasResponseDisplayBody;
        Detail.ResponseBody = WithPreviewNotice(BytesToPreviewText(responseDisplayBytes), responseDisplayTruncated, responseDisplayBytes.Length, responseDisplayBodySize);
        Detail.ResponseText = Detail.ResponseBody;
        Detail.ResponseHex = responseBytes.Length > LargePayloadThresholdBytes ? "" : ToHex(responseBytes);
        string responseOriginalBody = WithPreviewNotice(BytesToPreviewText(responseBytes), responseBodyTruncated, responseBytes.Length, responseBodySize);
        Detail.ResponseRaw = $"HTTP {GetInt(args, "StateCode")} {GetString(args, "StateText")}\r\n{Detail.ResponseHeaders}\r\n\r\n{Detail.ResponseBody}".Trim();
        Detail.ResponseOriginalRaw = $"HTTP {GetInt(args, "StateCode")} {GetString(args, "StateText")}\r\n{Detail.ResponseHeaders}\r\n\r\n{responseOriginalBody}".Trim();
        Detail.ResponseJson = responseDisplayTruncated ? Detail.ResponseBody : BuildJsonViewText(responseDisplayBytes, Detail.ResponseBody);
        Detail.ResponseHtml = Detail.ResponseBody;
        Detail.ResponseCookies = ExtractCookies(responseHeader);
        Detail.ResponseStateText = GetString(args, "StateText");
        Detail.ResponseStateCode = GetInt(args, "StateCode");
        string responseContentType = GetHeaderValue(responseHeader, "Content-Type");
        Detail.ResponseImageBytes = responseDisplayTruncated || responseDisplayBytes.Length > LargePayloadThresholdBytes ? Array.Empty<byte>() : TryGetImageBytes(responseContentType, responseDisplayBytes);
        Detail.ResponseImageType = ExtractImageType(responseContentType);
        (byte[] responseRawBytes, int responseHeaderLength) = BuildHttpBytes($"HTTP {GetInt(args, "StateCode")} {GetString(args, "StateText")}", Detail.ResponseHeaders, responseBytes);
        ApplyResponseRows(responseHeader, responseRawBytes, responseHeaderLength);
        Detail.ResponseHexSource = responseBodyTruncated
            ? CreateHttpHexSource(SelectedSession!, "Response", $"HTTP {GetInt(args, "StateCode")} {GetString(args, "StateText")}", Detail.ResponseHeaders, responseBodySize)
            : null;
        if (SelectedSession is not null && (responseBodyTruncated || responseDisplayTruncated))
        {
            CaptureEntry selected = SelectedSession;
            int loadVersion = _selectedSessionLoadVersion;
            JsonElement responseSnapshot = args.Clone();
            _ = LoadFullResponseBodyFromUpdateAsync(selected, loadVersion, responseSnapshot);
        }
    }

    private void ApplySocketUpdate(JsonElement args)
    {
        List<SocketEntry> newEntries = new();
        foreach (SocketEntry socketEntry in DeserializeList<SocketEntry>(args))
        {
            if (SelectedSession is null || socketEntry.Theology != SelectedSession.Theology)
            {
                continue;
            }

            newEntries.Add(socketEntry);
        }

        if (newEntries.Count == 0)
        {
            return;
        }

        string protocol = SelectedSession is null ? "" : ResolveSocketProtocol(SelectedSession, default);
        if (string.IsNullOrWhiteSpace(protocol))
        {
            protocol = string.IsNullOrWhiteSpace(Detail.SocketProtocol) ? "WebSocket" : Detail.SocketProtocol;
        }

        Detail.SocketProtocol = protocol;
        Detail.IsSocketSession = true;
        Detail.SocketEntries.AddRange(newEntries);
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
        Settings.InterceptRules = ToIndentedJson(GetProperty(args, "InterceptRules"));
        Settings.RuleCenterRules = ToIndentedJson(GetProperty(args, "RuleCenter"));
        ApplyHostsRulesConfig(GetProperty(args, "HostsRules"));
        ApplyReplaceRulesConfig(GetProperty(args, "ReplaceRules"));
        ApplyInterceptRulesConfig(GetProperty(args, "InterceptRules"));
        ApplyRuleCenterConfig(GetProperty(args, "RuleCenter"));
        ApplyRequestCertificateConfig(GetProperty(args, "RequestCertManager"));
    }

    private void ApplyHostsRulesConfig(JsonElement element)
    {
        List<HostsRuleItem> items = new();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                items.Add(new HostsRuleItem
                {
                    Hash = GetString(item, "Hash", Guid.NewGuid().ToString("N")),
                    Source = GetString(item, "Src"),
                    Target = GetString(item, "Dest"),
                    State = "已保存"
                });
            }
        }

        ReplaceRows(HostsRuleItems, items);
        SyncHostsRulesText();
    }

    private void ApplyReplaceRulesConfig(JsonElement element)
    {
        List<ReplaceRuleItem> items = new();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                items.Add(new ReplaceRuleItem
                {
                    Hash = GetString(item, "Hash", Guid.NewGuid().ToString("N")),
                    RuleType = GetString(item, "Type", "String(UTF8)"),
                    SourceContent = GetString(item, "Src"),
                    ReplacementContent = GetString(item, "Dest"),
                    State = "已保存"
                });
            }
        }

        ReplaceRows(ReplaceRuleItems, items);
        SyncReplaceRulesText();
    }

    private void ApplyInterceptRulesConfig(JsonElement element)
    {
        List<InterceptRuleItem> items = new();
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                items.Add(new InterceptRuleItem
                {
                    Hash = GetString(item, "Hash", Guid.NewGuid().ToString("N")),
                    Enabled = GetProperty(item, "Enable").ValueKind == JsonValueKind.Undefined || GetBool(item, "Enable"),
                    Name = GetString(item, "Name", "新规则"),
                    Direction = GetString(item, "Direction", "上行"),
                    Target = GetString(item, "Target", "URL"),
                    Operator = GetString(item, "Operator", "包含"),
                    Value = GetString(item, "Value"),
                    State = "已保存"
                });
            }
        }

        ReplaceRows(InterceptRuleItems, items);
        SyncInterceptRulesText();
    }

    private void ApplyRuleCenterConfig(JsonElement element)
    {
        List<RequestBlockRuleItem> blockRules = new();
        List<WebSocketBlockRuleItem> webSocketBlockRules = new();
        List<TcpBlockRuleItem> tcpBlockRules = new();
        List<UdpBlockRuleItem> udpBlockRules = new();
        List<RequestRewriteRuleItem> rewriteRules = new();
        List<RequestMappingRuleItem> mappingRules = new();
        List<RequestDecodeRuleItem> decodeRules = new();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonElement item in EnumerateArrayProperty(element, "BlockRules"))
            {
                RequestBlockRuleItem rule = new()
                {
                    Action = NormalizeRequestBlockAction(GetString(item, "Action", "断开请求"))
                };
                ApplyCommonTrafficRuleFields(rule, item);
                blockRules.Add(rule);
            }

            foreach (JsonElement item in EnumerateArrayProperty(element, "WebSocketBlockRules"))
            {
                WebSocketBlockRuleItem rule = new()
                {
                    Action = NormalizeWebSocketBlockAction(GetString(item, "Action", "断开连接")),
                    Method = "ANY"
                };
                ApplyCommonTrafficRuleFields(rule, item);
                rule.Method = "ANY";
                webSocketBlockRules.Add(rule);
            }

            foreach (JsonElement item in EnumerateArrayProperty(element, "TcpBlockRules"))
            {
                TcpBlockRuleItem rule = new()
                {
                    Action = NormalizeTcpBlockAction(GetString(item, "Action", "断开连接"))
                };
                ApplyCommonTrafficRuleFields(rule, item);
                tcpBlockRules.Add(rule);
            }

            foreach (JsonElement item in EnumerateArrayProperty(element, "UdpBlockRules"))
            {
                UdpBlockRuleItem rule = new()
                {
                    Action = NormalizeUdpBlockAction(GetString(item, "Action", "丢弃上行包")),
                    Method = "UDP"
                };
                ApplyCommonTrafficRuleFields(rule, item);
                rule.Method = "UDP";
                udpBlockRules.Add(rule);
            }

            foreach (JsonElement item in EnumerateArrayProperty(element, "RewriteRules"))
            {
                RequestRewriteRuleItem rule = new()
                {
                    Direction = GetString(item, "Direction", "请求"),
                    Target = GetString(item, "Target", "协议头"),
                    Operation = GetString(item, "Operation", "设置"),
                    Key = GetString(item, "Key"),
                    Value = GetString(item, "Value"),
                    ValueType = GetString(item, "ValueType", "String(UTF8)"),
                    OperationsJson = GetString(item, "OperationsJson", ToIndentedJson(GetProperty(item, "Operations")))
                };
                ApplyCommonTrafficRuleFields(rule, item);
                rewriteRules.Add(rule);
            }

            foreach (JsonElement item in EnumerateArrayProperty(element, "MappingRules"))
            {
                RequestMappingRuleItem rule = new()
                {
                    MappingType = GetString(item, "MappingType", "本地文件"),
                    SourceContent = GetString(item, "SourceContent"),
                    TargetContent = GetString(item, "TargetContent"),
                    ValueType = GetString(item, "ValueType", "String(UTF8)"),
                    LegacyReplaceRule = GetBool(item, "LegacyReplaceRule")
                };
                ApplyCommonTrafficRuleFields(rule, item);
                rule.State = rule.LegacyReplaceRule ? "兼容旧规则" : "已保存";
                mappingRules.Add(rule);
            }

            foreach (JsonElement item in EnumerateArrayProperty(element, "DecodeRules"))
            {
                RequestDecodeRuleItem rule = new()
                {
                    Direction = GetString(item, "Direction", "响应"),
                    DecoderType = GetString(item, "DecoderType", "自动解压"),
                    ScriptCode = DecodePayloadElement(GetProperty(item, "ScriptCode"))
                };
                ApplyCommonTrafficRuleFields(rule, item);
                decodeRules.Add(rule);
            }
        }

        if (mappingRules.Count == 0 && ReplaceRuleItems.Count > 0)
        {
            mappingRules.AddRange(BuildLegacyMappingRules());
        }

        ReplaceRows(RequestBlockRules, blockRules);
        ReplaceRows(WebSocketBlockRules, webSocketBlockRules);
        ReplaceRows(TcpBlockRules, tcpBlockRules);
        ReplaceRows(UdpBlockRules, udpBlockRules);
        ReplaceRows(RequestRewriteRules, rewriteRules);
        ReplaceRows(RequestMappingRules, mappingRules);
        ReplaceRows(RequestDecodeRules, decodeRules);
        SyncRuleCenterRulesText();
    }

    private IEnumerable<RequestMappingRuleItem> BuildLegacyMappingRules()
    {
        foreach (ReplaceRuleItem item in ReplaceRuleItems)
        {
            RequestMappingRuleItem rule = new()
            {
                Hash = EnsureRuleHash(item.Hash),
                Enabled = true,
                Name = string.IsNullOrWhiteSpace(item.SourceContent) ? "旧替换规则" : $"旧替换：{item.SourceContent}",
                Method = "ANY",
                UrlMatchType = "包含",
                UrlPattern = item.SourceContent,
                Priority = 100,
                MappingType = item.UsesResponseFile ? "本地文件" : "旧替换规则",
                SourceContent = item.SourceContent,
                TargetContent = item.ReplacementContent,
                ValueType = item.RuleType,
                LegacyReplaceRule = true,
                Note = "由旧 ReplaceRules 兼容生成，当前仍由旧替换规则逻辑执行。",
                State = "兼容旧规则"
            };
            yield return rule;
        }
    }

    private static void ApplyCommonTrafficRuleFields(TrafficRuleItemBase rule, JsonElement item)
    {
        rule.Hash = GetString(item, "Hash", Guid.NewGuid().ToString("N"));
        JsonElement enable = GetProperty(item, "Enable");
        rule.Enabled = enable.ValueKind == JsonValueKind.Undefined
            ? GetProperty(item, "Enabled").ValueKind == JsonValueKind.Undefined || GetBool(item, "Enabled")
            : GetBool(item, "Enable");
        rule.Name = GetString(item, "Name", "新规则");
        rule.Method = GetString(item, "Method", "ANY");
        rule.UrlMatchType = GetString(item, "UrlMatchType", "通配");
        rule.UrlPattern = GetString(item, "UrlPattern");
        rule.Priority = GetInt(item, "Priority", 100);
        rule.Note = GetString(item, "Note");
        rule.State = "已保存";
    }

    private static string NormalizeRequestBlockAction(string action)
    {
        return action?.Trim() == "断开响应" ? "断开响应" : "断开请求";
    }

    private static string NormalizeWebSocketBlockAction(string action)
    {
        return action?.Trim() switch
        {
            "丢弃上行帧" => "丢弃上行帧",
            "丢弃下行帧" => "丢弃下行帧",
            _ => "断开连接"
        };
    }

    private static string NormalizeTcpBlockAction(string action)
    {
        return action?.Trim() switch
        {
            "丢弃上行包" => "丢弃上行包",
            "丢弃下行包" => "丢弃下行包",
            _ => "断开连接"
        };
    }

    private static string NormalizeUdpBlockAction(string action)
    {
        return action?.Trim() == "丢弃下行包" ? "丢弃下行包" : "丢弃上行包";
    }

    private static IEnumerable<JsonElement> EnumerateArrayProperty(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();
    }

    private void ApplyRequestCertificateConfig(JsonElement element)
    {
        List<RequestCertificateRuleItem> items = new();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!int.TryParse(property.Name, out int context) || context <= 0)
                {
                    continue;
                }

                items.Add(new RequestCertificateRuleItem
                {
                    Context = context,
                    UsageRule = ToRequestCertificateRuleText(GetInt(property.Value, "rule", 2)),
                    Host = GetString(property.Value, "Host"),
                    CertificateFile = GetString(property.Value, "FilePath"),
                    Password = GetString(property.Value, "PassWord"),
                    LoadState = "未载入"
                });
            }
        }

        items.Sort(static (left, right) => left.Context.CompareTo(right.Context));
        ReplaceRows(RequestCertificateItems, items);
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
        ApplyFavoriteState(entry, forceRefresh: true);
        if (entry.IsFavorite)
        {
            _favoriteCount++;
        }
        Sessions.Add(entry);
        _sessionMap[entry.Theology] = entry;
        NotifyFavoriteSummaryChanged();

        if (AutoScroll)
        {
            ScrollToEntryRequested?.Invoke(entry);
        }
    }

    private void CompletePendingInterceptReplay(CaptureEntry entry)
    {
        if (entry.BreakMode <= 0)
        {
            return;
        }

        PendingInterceptReplay? pendingReplay = _pendingInterceptReplays.FirstOrDefault(item =>
            item.Mode == entry.BreakMode
            && string.Equals(item.SourceEntry.Method, entry.Method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.SourceEntry.Url, entry.Url, StringComparison.OrdinalIgnoreCase));
        if (pendingReplay is null)
        {
            return;
        }

        _pendingInterceptReplays.Remove(pendingReplay);
        pendingReplay.Completion.TrySetResult(entry);
    }

    public void ClearSessionFilters()
    {
        _selectedProcessFilterKeys.Clear();
        _selectedDomainFilterKeys.Clear();
        ApplySelectionToFilterItems(ProcessFilters, _selectedProcessFilterKeys);
        ApplySelectionToFilterItems(DomainFilters, _selectedDomainFilterKeys);
        RefreshSessionFilterItems();
        OnPropertyChanged(nameof(SelectedProcessFilterKey));
        OnPropertyChanged(nameof(SelectedDomainFilterKey));
        OnPropertyChanged(nameof(HasActiveSessionFilter));
    }

    public void ClearSessionDecorations()
    {
        ClearSessionFilters();
        ShowFavoritesOnly = false;
        ClearAllSearchHighlights();
        RefreshSessionView();
    }

    private bool FilterSession(object session)
    {
        if (session is not CaptureEntry entry)
        {
            return false;
        }

        string processKey = NormalizeFilterValue(entry.Process, "未知进程");
        string domainKey = NormalizeFilterValue(entry.Host, "无域名");

        if (_selectedProcessFilterKeys.Count > 0
            && !_selectedProcessFilterKeys.Contains(processKey))
        {
            return false;
        }

        if (_selectedDomainFilterKeys.Count > 0
            && !_selectedDomainFilterKeys.Contains(domainKey))
        {
            return false;
        }

        if (ShowFavoritesOnly && !entry.IsFavorite)
        {
            return false;
        }

        return true;
    }

    private void RefreshSessionView()
    {
        SessionsView.Refresh();
    }

    private void ScheduleSessionFilterRefresh(bool refreshSessionView)
    {
        _sessionFilterRefreshPending = true;
        _sessionViewRefreshPending |= refreshSessionView;
        if (!_sessionFilterRefreshTimer.IsEnabled)
        {
            _sessionFilterRefreshTimer.Start();
        }
    }

    private void SessionFilterRefreshTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _sessionFilterRefreshTimer.Stop();
        if (!_sessionFilterRefreshPending)
        {
            return;
        }

        bool refreshSessionView = _sessionViewRefreshPending;
        _sessionFilterRefreshPending = false;
        _sessionViewRefreshPending = false;
        RefreshSessionFilterItems(refreshSessionView);
    }

    private void RefreshSessionFilterItems(bool refreshSessionView = true)
    {
        IReadOnlyList<CaptureEntry> filterSource = GetSessionFilterSource();
        List<SessionFilterItem> processItems = BuildSessionFilterItems(filterSource, static entry => entry.Process, "未知进程");
        List<SessionFilterItem> domainItems = BuildSessionFilterItems(filterSource, static entry => entry.Host, "无域名");

        PruneSelectedFilterKeys(_selectedProcessFilterKeys, processItems);
        PruneSelectedFilterKeys(_selectedDomainFilterKeys, domainItems);
        ApplySelectionToFilterItems(processItems, _selectedProcessFilterKeys);
        ApplySelectionToFilterItems(domainItems, _selectedDomainFilterKeys);

        IsRefreshingSessionFilters = true;
        try
        {
            ReplaceRows(ProcessFilters, processItems);
            ReplaceRows(DomainFilters, domainItems);
        }
        finally
        {
            IsRefreshingSessionFilters = false;
        }

        if (refreshSessionView)
        {
            RefreshSessionView();
        }

        OnPropertyChanged(nameof(SelectedProcessFilterKey));
        OnPropertyChanged(nameof(SelectedDomainFilterKey));
        OnPropertyChanged(nameof(HasActiveSessionFilter));
    }

    private void ScheduleSelectedSessionLoad()
    {
        int loadVersion = ++_selectedSessionLoadVersion;
        Dispatcher dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            _ = LoadSelectedSessionAfterSelectionPaintAsync(loadVersion);
            return;
        }

        dispatcher.BeginInvoke(
            new Action(() => _ = LoadSelectedSessionAfterSelectionPaintAsync(loadVersion)),
            DispatcherPriority.Background);
    }

    private async Task LoadSelectedSessionAfterSelectionPaintAsync(int loadVersion)
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (loadVersion != _selectedSessionLoadVersion)
        {
            return;
        }

        await LoadSelectedSessionAsync(loadVersion);
    }

    private Task LoadSelectedSessionImmediatelyAsync()
    {
        int loadVersion = ++_selectedSessionLoadVersion;
        return LoadSelectedSessionAsync(loadVersion);
    }

    private async Task LoadSelectedSessionAsync(int loadVersion)
    {
        CaptureEntry? selected = SelectedSession;
        if (loadVersion != _selectedSessionLoadVersion)
        {
            return;
        }

        if (selected is null)
        {
            Detail.Clear();
            return;
        }

        Detail.HasSelection = true;
        Detail.Summary = $"{selected.Method} {selected.Url}";
        Detail.IsSocketSession = false;
        Detail.SocketProtocol = "";
        Detail.RequestHeaders = "正在读取请求数据...";
        Detail.RequestBody = "";
        Detail.ResponseHeaders = "";
        Detail.ResponseBody = "";
        Detail.SocketEntries.ReplaceAll(Array.Empty<SocketEntry>());
        Detail.SelectedSocketEntry = null;
        Detail.RequestHeaderRows.Clear();
        Detail.RequestQueryRows.Clear();
        Detail.RequestCookieRows.Clear();
        Detail.RequestBodyRows.Clear();
        Detail.HasRequestBodyRows = false;
        Detail.RequestHexRows.Clear();
        Detail.RequestHexBytes = Array.Empty<byte>();
        Detail.RequestHexHeaderLength = 0;
        Detail.RequestHexSource = null;
        Detail.RequestImageBytes = Array.Empty<byte>();
        Detail.RequestImageType = "";
        Detail.ResponseHeaderRows.Clear();
        Detail.ResponseCookieRows.Clear();
        Detail.ResponseHexRows.Clear();
        Detail.ResponseHexBytes = Array.Empty<byte>();
        Detail.ResponseHexHeaderLength = 0;
        Detail.ResponseHexSource = null;
        Detail.ResponseImageBytes = Array.Empty<byte>();
        Detail.ResponseImageType = "";

        try
        {
            JsonElement? result = await _backend.InvokeAsync("HTTP请求获取", new { Theology = selected.Theology });
            if (loadVersion != _selectedSessionLoadVersion || SelectedSession?.Theology != selected.Theology || result is null)
            {
                return;
            }

            ApplySessionDetail(selected, result.Value);
            ApplyDetailTextSearch(selected);
            _ = LoadFullHttpBodiesAsync(selected, loadVersion, result.Value);
            _ = LoadRequestMultipartImageAsync(selected);
            if (selected.BreakMode is 1 or 2)
            {
                Detail.EnableInlineIntercept(selected.BreakMode);
            }
            else
            {
                Detail.DisableInlineIntercept();
            }
        }
        catch (Exception exception)
        {
            Detail.RequestHeaders = exception.Message;
        }
    }

    private void UpdateMcpBridgeStatus()
    {
        bool exists = File.Exists(Mcp.BridgeExecutablePath);
        Mcp.BridgeExists = exists;
    }

    private void RefreshSelectedSessionInterceptState()
    {
        IsSelectedSessionIntercepted = SelectedSession?.BreakMode > 0;
    }

    private IReadOnlyList<CaptureEntry> GetSessionFilterSource()
    {
        return ShowFavoritesOnly
            ? Sessions.Where(static entry => entry.IsFavorite)
                .ToArray()
            : Sessions.ToArray();
    }

    private async Task DeleteSessionsAsync(IReadOnlyCollection<CaptureEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        int[] theologyIds = entries
            .Select(static entry => entry.Theology)
            .Where(static theology => theology > 0)
            .Distinct()
            .ToArray();

        if (theologyIds.Length > 0)
        {
            try
            {
                await _backend.InvokeAsync("删除请求会话", new { Data = theologyIds });
            }
            catch (Exception exception)
            {
                NotificationRequested?.Invoke("删除失败", exception.Message);
                return;
            }
        }

        bool deletedSelectedSession = SelectedSession is not null
            && entries.Any(entry => entry.Theology == SelectedSession.Theology);
        int deletedFavorites = entries.Count(static entry => entry.IsFavorite);

        foreach (CaptureEntry entry in entries)
        {
            Sessions.Remove(entry);
            if (entry.Theology > 0)
            {
                _sessionMap.Remove(entry.Theology);
            }
        }

        if (deletedSelectedSession)
        {
            SelectedSession = null;
        }

        if (deletedFavorites > 0)
        {
            _favoriteCount = Math.Max(0, _favoriteCount - deletedFavorites);
        }
        NotifyFavoriteSummaryChanged();
        RefreshSessionFilterItems();
    }

    private static bool MatchesClearRule(CaptureEntry entry, SessionClearRule rule)
    {
        return rule switch
        {
            SessionClearRule.All => true,
            SessionClearRule.Resources => IsResourceSession(entry),
            SessionClearRule.NoResponse => IsNoResponseSession(entry),
            SessionClearRule.Failed => IsFailedSession(entry),
            SessionClearRule.Redirect => IsRedirectSession(entry),
            SessionClearRule.Unfavorite => !entry.IsFavorite,
            SessionClearRule.Untagged => !entry.HasTagColor,
            SessionClearRule.CurrentFilter => true,
            _ => false
        };
    }

    private static bool IsResourceSession(CaptureEntry entry)
    {
        string responseType = entry.ResponseType.Trim();
        if (ResourceTypeTokens.Any(token => responseType.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string icon = entry.Icon.Trim();
        if (icon is "img" or "js" or "css" or "font" or "audio" or "video" or "Flash")
        {
            return true;
        }

        string path = ExtractUrlPath(entry.Url);
        return ResourceUrlExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNoResponseSession(CaptureEntry entry)
    {
        int statusCode = ExtractStatusCode(entry.State);
        if (statusCode > 0)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(entry.ResponseLength)
            || entry.ResponseLength.Equals("0", StringComparison.OrdinalIgnoreCase)
            || entry.ResponseLength.Equals("0/0", StringComparison.OrdinalIgnoreCase)
            || entry.State.Contains("-", StringComparison.OrdinalIgnoreCase)
            || entry.State.Contains("等待", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFailedSession(CaptureEntry entry)
    {
        int statusCode = ExtractStatusCode(entry.State);
        return statusCode >= 400
            || entry.Icon.Equals("error", StringComparison.OrdinalIgnoreCase)
            || entry.ResponseType.Contains("error", StringComparison.OrdinalIgnoreCase)
            || entry.State.Contains("error", StringComparison.OrdinalIgnoreCase)
            || entry.State.Contains("错误", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedirectSession(CaptureEntry entry)
    {
        int statusCode = ExtractStatusCode(entry.State);
        return statusCode is >= 300 and < 400;
    }

    private static string ExtractUrlPath(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return uri.AbsolutePath;
        }

        int queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? value[..queryIndex] : value;
    }

    private static int ExtractStatusCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        for (int index = 0; index <= value.Length - 3; index++)
        {
            if (!char.IsDigit(value[index]) || !char.IsDigit(value[index + 1]) || !char.IsDigit(value[index + 2]))
            {
                continue;
            }

            int statusCode = ((value[index] - '0') * 100) + ((value[index + 1] - '0') * 10) + (value[index + 2] - '0');
            if (statusCode is >= 100 and <= 599)
            {
                return statusCode;
            }
        }

        return 0;
    }

    private static CaptureEntry[] NormalizeSessionEntries(IEnumerable<CaptureEntry> entries)
    {
        List<CaptureEntry> result = new();
        HashSet<int> theologyIds = new();
        HashSet<CaptureEntry> objectSet = new();

        foreach (CaptureEntry entry in entries)
        {
            if (entry.Theology > 0)
            {
                if (!theologyIds.Add(entry.Theology))
                {
                    continue;
                }
            }
            else if (!objectSet.Add(entry))
            {
                continue;
            }

            result.Add(entry);
        }

        return result.ToArray();
    }

    private static int[] GetTheologyIds(IEnumerable<CaptureEntry> entries)
    {
        return entries
            .Select(static entry => entry.Theology)
            .Where(static theology => theology > 0)
            .Distinct()
            .ToArray();
    }

    private static bool ReplaceSelectedFilterKeys(HashSet<string> target, IEnumerable<string> keys, string allKey)
    {
        HashSet<string> next = NormalizeFilterKeys(keys, allKey);

        if (target.SetEquals(next))
        {
            return false;
        }

        target.Clear();
        foreach (string key in next)
        {
            target.Add(key);
        }

        return true;
    }

    private static HashSet<string> NormalizeFilterKeys(IEnumerable<string> keys, string allKey)
    {
        HashSet<string> normalizedKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in keys)
        {
            string normalized = NormalizeFilterValue(key, "");
            if (string.IsNullOrWhiteSpace(normalized) || IsAllFilterKey(normalized, allKey))
            {
                continue;
            }

            normalizedKeys.Add(normalized);
        }

        return normalizedKeys;
    }

    private static void PruneSelectedFilterKeys(HashSet<string> selectedKeys, IEnumerable<SessionFilterItem> availableItems)
    {
        HashSet<string> availableKeys = availableItems.Select(static item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        selectedKeys.RemoveWhere(key => !availableKeys.Contains(key));
    }

    private static void ApplySelectionToFilterItems(IEnumerable<SessionFilterItem> items, ISet<string> selectedKeys)
    {
        foreach (SessionFilterItem item in items)
        {
            item.IsSelected = selectedKeys.Contains(item.Key);
        }
    }

    private static bool IsAllFilterKey(string? value, string allKey)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), allKey, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFavoriteState(CaptureEntry entry, bool forceRefresh)
    {
        if (!forceRefresh && entry.IsFavorite)
        {
            return;
        }

        string favoriteKey = BuildFavoriteKey(entry);
        bool shouldFavorite = _favoriteKeys.Contains(favoriteKey);
        if (entry.IsFavorite != shouldFavorite)
        {
            entry.IsFavorite = shouldFavorite;
        }
    }

    private void NotifyFavoriteSummaryChanged()
    {
        OnPropertyChanged(nameof(FavoriteCount));
        OnPropertyChanged(nameof(FavoriteSummaryText));
    }

    private static string BuildFavoriteKey(CaptureEntry entry)
    {
        string method = NormalizeFavoritePart(entry.Method).ToUpperInvariant();
        string url = NormalizeFavoriteUrl(entry.Url, entry.Host, entry.Query);
        string process = NormalizeFavoritePart(entry.Process).ToLowerInvariant();
        string clientAddress = NormalizeFavoritePart(entry.ClientAddress).ToLowerInvariant();
        string sendTime = NormalizeFavoritePart(entry.SendTime);
        return $"{method}|{url}|{process}|{clientAddress}|{sendTime}";
    }

    private static string NormalizeFavoriteUrl(string? url, string? host, string? query)
    {
        string urlText = NormalizeFavoritePart(url);
        if (Uri.TryCreate(urlText, UriKind.Absolute, out Uri? absoluteUri))
        {
            return absoluteUri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.SafeUnescaped)
                .ToLowerInvariant();
        }

        string hostText = NormalizeFavoritePart(host).ToLowerInvariant();
        string queryText = NormalizeFavoritePart(query);
        if (!string.IsNullOrWhiteSpace(hostText) || !string.IsNullOrWhiteSpace(queryText))
        {
            return $"{hostText}{queryText}".ToLowerInvariant();
        }

        return urlText.ToLowerInvariant();
    }

    private static string NormalizeFavoritePart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private void SyncHostsRulesText()
    {
        Settings.HostsRules = JsonSerializer.Serialize(
            HostsRuleItems.Select(static item => new
            {
                item.Hash,
                Src = item.Source,
                Dest = item.Target
            }),
            JsonOptions);
    }

    private void SyncReplaceRulesText()
    {
        Settings.ReplaceRules = JsonSerializer.Serialize(
            ReplaceRuleItems.Select(static item => new
            {
                Type = item.RuleType,
                Src = item.SourceContent,
                Dest = item.ReplacementContent,
                item.Hash
            }),
            JsonOptions);
    }

    private void SyncInterceptRulesText()
    {
        Settings.InterceptRules = JsonSerializer.Serialize(
            InterceptRuleItems.Select(static item => new
            {
                item.Hash,
                Enable = item.Enabled,
                item.Name,
                item.Direction,
                item.Target,
                item.Operator,
                item.Value
            }),
            JsonOptions);
    }

    private string SyncRuleCenterRulesText()
    {
        foreach (RequestRewriteRuleItem item in RequestRewriteRules)
        {
            item.SyncOperationsJson();
        }

        Settings.RuleCenterRules = JsonSerializer.Serialize(
            new
            {
                BlockRules = RequestBlockRules.Select(static item => new
                {
                    item.Hash,
                    Enable = item.Enabled,
                    item.Name,
                    item.Method,
                    item.UrlMatchType,
                    item.UrlPattern,
                    item.Note,
                    item.Action
                }),
                WebSocketBlockRules = WebSocketBlockRules.Select(static item => new
                {
                    item.Hash,
                    Enable = item.Enabled,
                    item.Name,
                    Method = "ANY",
                    item.UrlMatchType,
                    item.UrlPattern,
                    item.Note,
                    item.Action
                }),
                TcpBlockRules = TcpBlockRules.Select(static item => new
                {
                    item.Hash,
                    Enable = item.Enabled,
                    item.Name,
                    item.Method,
                    item.UrlMatchType,
                    item.UrlPattern,
                    item.Note,
                    item.Action
                }),
                UdpBlockRules = UdpBlockRules.Select(static item => new
                {
                    item.Hash,
                    Enable = item.Enabled,
                    item.Name,
                    Method = "UDP",
                    item.UrlMatchType,
                    item.UrlPattern,
                    item.Note,
                    item.Action
                }),
                RewriteRules = RequestRewriteRules.Select(static item => new
                {
                    item.Hash,
                    Enable = item.Enabled,
                    item.Name,
                    item.Method,
                    item.UrlMatchType,
                    item.UrlPattern,
                    item.Note,
                    item.Direction,
                    item.Target,
                    item.Operation,
                    item.Key,
                    item.Value,
                    item.ValueType,
                    item.OperationsJson
                }),
                MappingRules = RequestMappingRules.Select(static item => new
                {
                    item.Hash,
                    Enable = item.Enabled,
                    item.Name,
                    item.Method,
                    item.UrlMatchType,
                    item.UrlPattern,
                    item.Note,
                    item.MappingType,
                    item.SourceContent,
                    item.TargetContent,
                    item.ValueType,
                    item.LegacyReplaceRule
                }),
                DecodeRules = RequestDecodeRules.Select(static item => new
                {
                    item.Hash,
                    Enable = item.Enabled,
                    item.Name,
                    item.Method,
                    item.UrlMatchType,
                    item.UrlPattern,
                    item.Note,
                    item.Direction,
                    item.DecoderType,
                    ScriptCode = EncodeTextBase64(item.ScriptCode)
                })
            },
            JsonOptions);
        return Settings.RuleCenterRules;
    }

    private void UpdateRunningProcessCaptureState()
    {
        foreach (RunningProcessItem item in RunningProcesses)
        {
            item.IsCaptured = _activeProcessPids.Contains(item.Pid);
        }

        OnPropertyChanged(nameof(CurrentPidCaptureText));
        SelectedRunningProcess = RunningProcesses.FirstOrDefault(item => _activeProcessPids.Contains(item.Pid))
            ?? SelectedRunningProcess;
    }

    private static string EnsureRuleHash(string? hash)
    {
        return string.IsNullOrWhiteSpace(hash) ? Guid.NewGuid().ToString("N") : hash;
    }

    private static string EncodeTextBase64(string? text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));
    }

    private static string ToRequestCertificateRuleText(int rule)
    {
        return rule switch
        {
            3 => "仅解析",
            2 => "解析及发送",
            _ => "仅发送"
        };
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

    private async Task LoadFullHttpBodiesAsync(CaptureEntry selected, int loadVersion, JsonElement data)
    {
        int requestBodySize = GetInt(data, "BodySize");
        bool requestBodyTruncated = GetBool(data, "BodyTruncated");
        bool hasRequestDisplayBody = GetBool(data, "HasDisplayBody");
        int requestDisplayBodySize = GetInt(data, "DisplayBodySize");
        bool requestDisplayBodyTruncated = GetBool(data, "DisplayBodyTruncated");

        if (requestBodyTruncated || requestDisplayBodyTruncated)
        {
            string kind = hasRequestDisplayBody ? "Display" : "Raw";
            int total = hasRequestDisplayBody ? requestDisplayBodySize : requestBodySize;
            await LoadRequestTextProgressivelyAsync(selected, loadVersion, data, kind, total);
        }

        JsonElement response = GetProperty(data, "Response");
        if (response.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        int responseBodySize = GetInt(response, "BodySize");
        bool responseBodyTruncated = GetBool(response, "BodyTruncated");
        bool hasResponseDisplayBody = GetBool(response, "HasDisplayBody");
        int responseDisplayBodySize = GetInt(response, "DisplayBodySize");
        bool responseDisplayBodyTruncated = GetBool(response, "DisplayBodyTruncated");

        if (!responseBodyTruncated && !responseDisplayBodyTruncated)
        {
            return;
        }

        string responseKind = hasResponseDisplayBody ? "Display" : "Raw";
        int responseTotal = hasResponseDisplayBody ? responseDisplayBodySize : responseBodySize;
        await LoadResponseTextProgressivelyAsync(selected, loadVersion, response, responseKind, responseTotal);
    }

    private async Task<byte[]> LoadHttpBodyRangeAsync(int theology, string direction, string kind, int total)
    {
        if (total <= 0 || total > DetailFullBodyLoadLimitBytes)
        {
            return Array.Empty<byte>();
        }

        return await LoadHttpBodyChunkAsync(theology, direction, kind, 0, total);
    }

    private async Task<byte[]> LoadHttpBodyChunkAsync(int theology, string direction, string kind, int offset, int count)
    {
        if (count <= 0 || offset < 0)
        {
            return Array.Empty<byte>();
        }

        JsonElement? result = await _backend.InvokeAsync("HTTP正文范围读取", new
        {
            Theology = theology,
            Direction = direction,
            Kind = kind,
            Offset = offset,
            Count = count
        });
        if (result is null || !GetBool(result.Value, "Ok"))
        {
            return Array.Empty<byte>();
        }

        return DecodePayloadBytes(GetProperty(result.Value, "Body"));
    }

    private async Task LoadFullResponseBodyFromUpdateAsync(CaptureEntry selected, int loadVersion, JsonElement response)
    {
        int responseBodySize = GetInt(response, "BodySize");
        bool responseBodyTruncated = GetBool(response, "BodyTruncated");
        bool hasResponseDisplayBody = GetBool(response, "HasDisplayBody");
        int responseDisplayBodySize = hasResponseDisplayBody ? GetInt(response, "DisplayBodySize") : responseBodySize;
        bool responseDisplayBodyTruncated = hasResponseDisplayBody ? GetBool(response, "DisplayBodyTruncated") : responseBodyTruncated;

        string responseKind = hasResponseDisplayBody ? "Display" : "Raw";
        int responseTotal = hasResponseDisplayBody ? responseDisplayBodySize : responseBodySize;
        await LoadResponseTextProgressivelyAsync(selected, loadVersion, response, responseKind, responseTotal);
    }

    private async Task LoadRequestTextProgressivelyAsync(CaptureEntry selected, int loadVersion, JsonElement data, string kind, int total)
    {
        if (total <= 0 || total > DetailFullBodyLoadLimitBytes)
        {
            return;
        }

        string method = GetString(data, "Method", selected.Method);
        string url = GetString(data, "URL", selected.Url);
        string proto = GetString(data, "Proto", "HTTP/1.1");
        string headers = FormatHeaders(GetProperty(data, "Header"));
        await LoadHttpTextProgressivelyAsync(selected, loadVersion, "Request", kind, total, (text, loaded, complete) =>
        {
            if (!IsCurrentDetailLoad(selected, loadVersion))
            {
                return;
            }

            string body = WithStreamingNotice(text, loaded, total, complete);
            Detail.RequestBody = body;
            Detail.RequestRaw = $"{method} {url} {proto}\r\n{headers}\r\n\r\n{body}".Trim();
            if (string.Equals(kind, "Raw", StringComparison.OrdinalIgnoreCase))
            {
                Detail.RequestOriginalRaw = Detail.RequestRaw;
            }

            Detail.RequestJson = complete && loaded <= LargePayloadThresholdBytes
                ? BuildJsonViewText(Encoding.UTF8.GetBytes(text), text)
                : body;
            ApplyDetailTextSearch(selected);
        });
    }

    private async Task LoadResponseTextProgressivelyAsync(CaptureEntry selected, int loadVersion, JsonElement response, string kind, int total)
    {
        if (total <= 0 || total > DetailFullBodyLoadLimitBytes)
        {
            return;
        }

        string headers = FormatHeaders(GetProperty(response, "Header"));
        string statusLine = $"HTTP {GetInt(response, "StateCode", Detail.ResponseStateCode)} {GetString(response, "StateText", Detail.ResponseStateText)}";
        await LoadHttpTextProgressivelyAsync(selected, loadVersion, "Response", kind, total, (text, loaded, complete) =>
        {
            if (!IsCurrentDetailLoad(selected, loadVersion))
            {
                return;
            }

            string body = WithStreamingNotice(text, loaded, total, complete);
            Detail.ResponseBody = body;
            Detail.ResponseText = body;
            Detail.ResponseRaw = $"{statusLine}\r\n{headers}\r\n\r\n{body}".Trim();
            if (string.Equals(kind, "Raw", StringComparison.OrdinalIgnoreCase))
            {
                Detail.ResponseOriginalRaw = Detail.ResponseRaw;
            }

            Detail.ResponseJson = complete && loaded <= LargePayloadThresholdBytes
                ? BuildJsonViewText(Encoding.UTF8.GetBytes(text), text)
                : body;
            Detail.ResponseHtml = body;
            ApplyDetailTextSearch(selected);
        });
    }

    private async Task LoadHttpTextProgressivelyAsync(
        CaptureEntry selected,
        int loadVersion,
        string direction,
        string kind,
        int total,
        Action<string, int, bool> applyProgress)
    {
        if (total <= 0 || total > DetailFullBodyLoadLimitBytes)
        {
            return;
        }

        Decoder decoder = Encoding.UTF8.GetDecoder();
        StringBuilder builder = new(Math.Min(total, DetailFullBodyLoadLimitBytes));
        int offset = 0;
        while (offset < total)
        {
            if (!IsCurrentDetailLoad(selected, loadVersion))
            {
                return;
            }

            int count = Math.Min(DetailTextChunkBytes, total - offset);
            byte[] chunk = await LoadHttpBodyChunkAsync(selected.Theology, direction, kind, offset, count);
            if (chunk.Length == 0)
            {
                return;
            }

            if (offset == 0 && LooksBinary(chunk))
            {
                return;
            }

            offset += chunk.Length;
            bool complete = offset >= total;
            int charCount = decoder.GetCharCount(chunk, 0, chunk.Length, complete);
            if (charCount > 0)
            {
                char[] chars = new char[charCount];
                decoder.GetChars(chunk, 0, chunk.Length, chars, 0, complete);
                builder.Append(chars);
            }

            applyProgress(builder.ToString(), offset, complete);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    private bool IsCurrentDetailLoad(CaptureEntry selected, int loadVersion)
    {
        return loadVersion == _selectedSessionLoadVersion && SelectedSession?.Theology == selected.Theology;
    }

    private void ApplyFullRequestBody(CaptureEntry selected, JsonElement data, byte[] requestBodyBytes, byte[] requestDisplayBytes)
    {
        string method = GetString(data, "Method", selected.Method);
        string url = GetString(data, "URL", selected.Url);
        string proto = GetString(data, "Proto", "HTTP/1.1");
        JsonElement requestHeader = GetProperty(data, "Header");
        string requestHeadersText = FormatHeaders(requestHeader);
        string requestBody = BytesToPreviewText(requestDisplayBytes);
        Detail.RequestBody = requestBody;
        Detail.RequestRaw = $"{method} {url} {proto}\r\n{requestHeadersText}\r\n\r\n{requestBody}".Trim();
        Detail.RequestOriginalRaw = $"{method} {url} {proto}\r\n{requestHeadersText}\r\n\r\n{BytesToPreviewText(requestBodyBytes)}".Trim();
        Detail.RequestHex = requestBodyBytes.Length > LargePayloadThresholdBytes ? "" : ToHex(requestBodyBytes);
        Detail.RequestHexSource = null;
        Detail.RequestJson = BuildJsonViewText(requestDisplayBytes, requestBody);
        string requestContentType = GetHeaderValue(requestHeader, "Content-Type");
        Detail.RequestImageBytes = TryGetImageBytes(requestContentType, requestDisplayBytes);
        Detail.RequestImageType = ExtractImageType(requestContentType);
        (byte[] requestRawBytes, int requestHeaderLength) = BuildHttpBytes($"{method} {url} {proto}", requestHeadersText, requestBodyBytes);
        ApplyRequestRows(url, requestHeader, requestRawBytes, requestHeaderLength);
        Detail.RequestHexSource = null;
        ApplyDetailTextSearch(selected);
    }

    private void ApplyFullResponseBody(JsonElement response, byte[] responseBodyBytes, byte[] responseDisplayBytes)
    {
        JsonElement responseHeader = GetProperty(response, "Header");
        string responseHeadersText = FormatHeaders(responseHeader);
        string responseBody = BytesToPreviewText(responseDisplayBytes);
        Detail.ResponseBody = responseBody;
        Detail.ResponseText = responseBody;
        Detail.ResponseRaw = $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}\r\n{responseHeadersText}\r\n\r\n{responseBody}".Trim();
        Detail.ResponseOriginalRaw = $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}\r\n{responseHeadersText}\r\n\r\n{BytesToPreviewText(responseBodyBytes)}".Trim();
        Detail.ResponseHex = responseBodyBytes.Length > LargePayloadThresholdBytes ? "" : ToHex(responseBodyBytes);
        Detail.ResponseHexSource = null;
        Detail.ResponseJson = BuildJsonViewText(responseDisplayBytes, responseBody);
        Detail.ResponseHtml = responseBody;
        string responseContentType = GetHeaderValue(responseHeader, "Content-Type");
        Detail.ResponseImageBytes = TryGetImageBytes(responseContentType, responseDisplayBytes);
        Detail.ResponseImageType = ExtractImageType(responseContentType);
        (byte[] responseRawBytes, int responseHeaderLength) = BuildHttpBytes($"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}", responseHeadersText, responseBodyBytes);
        ApplyResponseRows(responseHeader, responseRawBytes, responseHeaderLength);
        if (SelectedSession is not null)
        {
            ApplyDetailTextSearch(SelectedSession);
        }
    }

    private void ApplyRequestRows(string url, JsonElement header, byte[] rawBytes, int headerLength)
    {
        ReplaceRows(Detail.RequestHeaderRows, ToHeaderRows(header));
        ReplaceRows(Detail.RequestQueryRows, ToQueryRows(url));
        ReplaceRows(Detail.RequestCookieRows, ToRequestCookieRows(header));
        ReplaceRows(Detail.RequestBodyRows, ToBodyRows(Detail.RequestBody));
        Detail.HasRequestBodyRows = Detail.RequestBodyRows.Count > 0;
        ReplaceRows(Detail.RequestHexRows, rawBytes.Length > LargePayloadThresholdBytes ? Array.Empty<HexViewRow>() : ToHexRows(rawBytes, headerLength));
        Detail.RequestHexBytes = rawBytes;
        Detail.RequestHexHeaderLength = headerLength;
    }

    private void ApplyResponseRows(JsonElement header, byte[] rawBytes, int headerLength)
    {
        ReplaceRows(Detail.ResponseHeaderRows, ToHeaderRows(header));
        ReplaceRows(Detail.ResponseCookieRows, ToResponseCookieRows(header));
        bool largePayload = rawBytes.Length > LargePayloadThresholdBytes;
        ReplaceRows(Detail.ResponseHexRows, largePayload ? Array.Empty<HexViewRow>() : ToHexRows(rawBytes, headerLength));
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
        JsonElement socketData = GetProperty(data, "SocketData");
        JsonElement requestHeader = GetProperty(data, "Header");
        byte[] requestBodyBytes = DecodePayloadBytes(GetProperty(data, "Body"));
        int requestBodySize = GetInt(data, "BodySize", requestBodyBytes.Length);
        bool requestBodyTruncated = GetBool(data, "BodyTruncated");
        bool hasRequestDisplayBody = GetBool(data, "HasDisplayBody");
        byte[] requestDisplayBytes = hasRequestDisplayBody
            ? DecodePayloadBytes(GetProperty(data, "DisplayBody"))
            : requestBodyBytes;
        int requestDisplayBodySize = hasRequestDisplayBody ? GetInt(data, "DisplayBodySize", requestDisplayBytes.Length) : requestBodySize;
        bool requestDisplayTruncated = hasRequestDisplayBody ? GetBool(data, "DisplayBodyTruncated") : requestBodyTruncated;
        string requestContentType = GetHeaderValue(requestHeader, "Content-Type");
        Detail.RequestMethod = method;
        Detail.RequestUrl = url;
        string socketProtocol = ResolveSocketProtocol(selected, socketData);
        Detail.SocketProtocol = socketProtocol;
        Detail.IsSocketSession = !string.IsNullOrWhiteSpace(socketProtocol);
        Detail.Summary = $"{method} {url}";
        Detail.HasRequestDisplayBody = hasRequestDisplayBody;
        string requestHeadersText = FormatHeaders(requestHeader);
        Detail.RequestHeaders = $"{method} {url} {proto}\r\n{requestHeadersText}".Trim();
        Detail.RequestBody = WithPreviewNotice(BytesToPreviewText(requestDisplayBytes), requestDisplayTruncated, requestDisplayBytes.Length, requestDisplayBodySize);
        Detail.RequestRaw = $"{method} {url} {proto}\r\n{requestHeadersText}\r\n\r\n{Detail.RequestBody}".Trim();
        Detail.RequestOriginalRaw = $"{method} {url} {proto}\r\n{requestHeadersText}\r\n\r\n{WithPreviewNotice(BytesToPreviewText(requestBodyBytes), requestBodyTruncated, requestBodyBytes.Length, requestBodySize)}".Trim();
        Detail.RequestQuery = selected.Query;
        Detail.RequestHex = ToHex(requestBodyBytes);
        Detail.RequestCookies = ExtractCookies(requestHeader);
        Detail.RequestJson = requestDisplayTruncated ? Detail.RequestBody : BuildJsonViewText(requestDisplayBytes, Detail.RequestBody);
        Detail.RequestImageBytes = requestDisplayTruncated || requestDisplayBytes.Length > LargePayloadThresholdBytes ? Array.Empty<byte>() : TryGetImageBytes(requestContentType, requestDisplayBytes);
        Detail.RequestImageType = ExtractImageType(requestContentType);
        (byte[] requestRawBytes, int requestHeaderLength) = BuildHttpBytes($"{method} {url} {proto}", requestHeadersText, requestBodyBytes);
        ApplyRequestRows(url, requestHeader, requestRawBytes, requestHeaderLength);
        Detail.RequestHexSource = requestBodyTruncated
            ? CreateHttpHexSource(selected, "Request", $"{method} {url} {proto}", requestHeadersText, requestBodySize)
            : null;

        JsonElement response = GetProperty(data, "Response");
        if (response.ValueKind == JsonValueKind.Object)
        {
            JsonElement responseHeader = GetProperty(response, "Header");
            byte[] responseBodyBytes = DecodePayloadBytes(GetProperty(response, "Body"));
            int responseBodySize = GetInt(response, "BodySize", responseBodyBytes.Length);
            bool responseBodyTruncated = GetBool(response, "BodyTruncated");
            bool hasResponseDisplayBody = GetBool(response, "HasDisplayBody");
            byte[] responseDisplayBytes = hasResponseDisplayBody
                ? DecodePayloadBytes(GetProperty(response, "DisplayBody"))
                : responseBodyBytes;
            int responseDisplayBodySize = hasResponseDisplayBody ? GetInt(response, "DisplayBodySize", responseDisplayBytes.Length) : responseBodySize;
            bool responseDisplayTruncated = hasResponseDisplayBody ? GetBool(response, "DisplayBodyTruncated") : responseBodyTruncated;
            string responseContentType = GetHeaderValue(responseHeader, "Content-Type");
            Detail.ResponseStateCode = GetInt(response, "StateCode");
            Detail.ResponseStateText = GetString(response, "StateText");
            Detail.HasResponseDisplayBody = hasResponseDisplayBody;
            string responseHeadersText = FormatHeaders(responseHeader);
            Detail.ResponseHeaders = $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}\r\n{responseHeadersText}".Trim();
            Detail.ResponseBody = WithPreviewNotice(BytesToPreviewText(responseDisplayBytes), responseDisplayTruncated, responseDisplayBytes.Length, responseDisplayBodySize);
            Detail.ResponseRaw = $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}\r\n{responseHeadersText}\r\n\r\n{Detail.ResponseBody}".Trim();
            Detail.ResponseOriginalRaw = $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}\r\n{responseHeadersText}\r\n\r\n{WithPreviewNotice(BytesToPreviewText(responseBodyBytes), responseBodyTruncated, responseBodyBytes.Length, responseBodySize)}".Trim();
            Detail.ResponseText = Detail.ResponseBody;
            Detail.ResponseHex = responseBodyBytes.Length > LargePayloadThresholdBytes ? "" : ToHex(responseBodyBytes);
            Detail.ResponseCookies = ExtractCookies(responseHeader);
            Detail.ResponseJson = responseDisplayTruncated ? Detail.ResponseBody : BuildJsonViewText(responseDisplayBytes, Detail.ResponseBody);
            Detail.ResponseHtml = Detail.ResponseBody;
            Detail.ResponseImageBytes = responseDisplayTruncated || responseDisplayBytes.Length > LargePayloadThresholdBytes ? Array.Empty<byte>() : TryGetImageBytes(responseContentType, responseDisplayBytes);
            Detail.ResponseImageType = ExtractImageType(responseContentType);
            (byte[] responseRawBytes, int responseHeaderLength) = BuildHttpBytes($"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}", responseHeadersText, responseBodyBytes);
            ApplyResponseRows(responseHeader, responseRawBytes, responseHeaderLength);
            Detail.ResponseHexSource = responseBodyTruncated
                ? CreateHttpHexSource(selected, "Response", $"HTTP {Detail.ResponseStateCode} {Detail.ResponseStateText}", responseHeadersText, responseBodySize)
                : null;
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
            Detail.HasResponseDisplayBody = false;
            Detail.ResponseOriginalRaw = "";
            Detail.ResponseHexSource = null;
        }

        Detail.SocketEntries.ReplaceAll(DeserializeList<SocketEntry>(socketData));
        Detail.SelectedSocketEntry = null;
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

    private static List<int> GetIntArray(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.Array)
        {
            return new List<int>();
        }

        List<int> values = new();
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int number))
            {
                values.Add(number);
            }
            else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out int stringNumber))
            {
                values.Add(stringNumber);
            }
        }

        return values;
    }

    private static List<SessionFilterItem> BuildSessionFilterItems(
        IEnumerable<CaptureEntry> entries,
        Func<CaptureEntry, string> selector,
        string emptyLabel)
    {
        Dictionary<string, int> counter = new(StringComparer.OrdinalIgnoreCase);
        foreach (CaptureEntry entry in entries)
        {
            string key = NormalizeFilterValue(selector(entry), emptyLabel);
            counter[key] = counter.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        List<SessionFilterItem> items = new();

        foreach ((string key, int count) in counter.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new SessionFilterItem
            {
                Key = key,
                Name = key,
                Count = count
            });
        }

        return items;
    }

    private static string NormalizeFilterValue(string? value, string emptyLabel)
    {
        string trimmed = value?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(trimmed) ? emptyLabel : trimmed;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        JsonElement property = GetProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        List<string> values = new();
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                values.Add(item.GetString()!);
            }
        }

        return values;
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

    private static string BytesToPreviewText(byte[] bytes)
    {
        return BytesToDisplayText(bytes);
    }

    private static string WithPreviewNotice(string text, bool truncated, int previewLength, int totalLength)
    {
        if (!truncated)
        {
            return text;
        }

        string notice = totalLength <= DetailFullBodyLoadLimitBytes
            ? $"已预览前 {previewLength:N0} Bytes，正在后台加载完整内容..."
            : $"已预览前 {previewLength:N0} Bytes，完整内容 {totalLength:N0} Bytes，请切换 HEX 分页视图或使用复制功能。";
        return string.IsNullOrEmpty(text)
            ? notice
            : $"{text}\r\n\r\n{notice}";
    }

    private static string WithStreamingNotice(string text, int loadedLength, int totalLength, bool complete)
    {
        if (complete)
        {
            return text;
        }

        string notice = $"正在分块加载正文... {loadedLength:N0}/{totalLength:N0} Bytes";
        return string.IsNullOrEmpty(text)
            ? notice
            : $"{text}\r\n\r\n{notice}";
    }

    private static string BuildJsonViewText(byte[] bytes, string displayText)
    {
        return bytes.Length > LargePayloadThresholdBytes
            ? displayText
            : TryFormatJson(displayText);
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
        byte[] headerBytes = BuildHttpHeaderBytes(startLine, headers);
        if (bodyBytes.Length == 0)
        {
            return (headerBytes, headerBytes.Length);
        }

        byte[] combined = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, combined, headerBytes.Length, bodyBytes.Length);
        return (combined, headerBytes.Length);
    }

    private HexVirtualDataSource CreateHttpHexSource(CaptureEntry selected, string direction, string startLine, string headers, int bodySize)
    {
        byte[] headerBytes = BuildHttpHeaderBytes(startLine, headers);
        return new HexVirtualDataSource
        {
            HeaderBytes = headerBytes,
            HeaderLength = headerBytes.Length,
            TotalLength = headerBytes.Length + Math.Max(0, bodySize),
            ReadBodyRangeAsync = (offset, count, _) => LoadHttpBodyChunkAsync(selected.Theology, direction, "Raw", offset, count)
        };
    }

    private static byte[] BuildHttpHeaderBytes(string startLine, string headers)
    {
        string prefix = string.IsNullOrWhiteSpace(headers)
            ? $"{startLine}\r\n\r\n"
            : $"{startLine}\r\n{headers}\r\n\r\n";
        return Encoding.UTF8.GetBytes(prefix);
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

    private static string ResolveSocketProtocol(CaptureEntry selected, JsonElement socketData)
    {
        if (selected.Method.Equals("UDP", StringComparison.OrdinalIgnoreCase)
            || selected.ResponseType.Contains("UDP", StringComparison.OrdinalIgnoreCase))
        {
            return "UDP";
        }

        if (selected.Method.Contains("TCP", StringComparison.OrdinalIgnoreCase)
            || selected.ResponseType.Contains("TCP", StringComparison.OrdinalIgnoreCase))
        {
            return "TCP";
        }

        if (selected.Icon is "websocket_connect" or "websocket_close"
            || selected.Method.Equals("Websocket", StringComparison.OrdinalIgnoreCase)
            || selected.Method.Equals("WebSocket", StringComparison.OrdinalIgnoreCase)
            || selected.ResponseType.Contains("Websocket", StringComparison.OrdinalIgnoreCase)
            || selected.ResponseType.Contains("WebSocket", StringComparison.OrdinalIgnoreCase))
        {
            return "WebSocket";
        }

        return socketData.ValueKind == JsonValueKind.Array && socketData.GetArrayLength() > 0
            ? "WebSocket"
            : "";
    }
}

internal sealed record PendingInterceptReplay(
    CaptureEntry SourceEntry,
    int Mode,
    TaskCompletionSource<CaptureEntry> Completion);

public enum SessionClearRule
{
    All,
    Resources,
    NoResponse,
    Failed,
    Redirect,
    Unfavorite,
    Untagged,
    CurrentFilter
}
