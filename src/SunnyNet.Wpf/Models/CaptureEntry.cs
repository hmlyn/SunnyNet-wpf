using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class CaptureEntry : ViewModelBase
{
    public const string StrikeTagColor = "__strike__";
    private static readonly Brush DefaultStatusBrush = CreateFrozenBrush("#8B9AAF");
    private static readonly Brush SuccessStatusBrush = CreateFrozenBrush("#198754");
    private static readonly Brush RedirectStatusBrush = CreateFrozenBrush("#2F7CF6");
    private static readonly Brush WarningStatusBrush = CreateFrozenBrush("#B46B00");
    private static readonly Brush DangerStatusBrush = CreateFrozenBrush("#E5484D");
    private static readonly Brush InfoStatusBrush = CreateFrozenBrush("#2F7CF6");
    private static readonly Brush InterceptStatusBrush = CreateFrozenBrush("#FF6A00");
    private static readonly Brush FavoriteActiveBrush = CreateFrozenBrush("#F4B400");
    private static readonly Brush FavoriteInactiveBrush = CreateFrozenBrush("#B7C3D0");
    private static readonly Brush DefaultRowBackgroundBrush = Brushes.Transparent;
    private static readonly Brush InterceptRowBackgroundBrush = CreateFrozenBrush("#FFE0BD");
    private static readonly Brush SuccessRowBackgroundBrush = CreateFrozenBrush("#F3FBF7");
    private static readonly Brush RedirectRowBackgroundBrush = CreateFrozenBrush("#F3F7FF");
    private static readonly Brush WarningRowBackgroundBrush = CreateFrozenBrush("#FFF8EA");
    private static readonly Brush DangerRowBackgroundBrush = CreateFrozenBrush("#FFF1F1");
    private static readonly Brush InfoRowBackgroundBrush = CreateFrozenBrush("#F4F8FF");
    private static readonly Brush RuleHitRowBackgroundBrush = CreateFrozenBrush("#E7F7FF");
    private static readonly Brush RuleHitAccentBrush = CreateFrozenBrush("#0088A8");
    private static readonly Brush BlockRuleHitRowBackgroundBrush = CreateFrozenBrush("#FFF0DD");
    private static readonly Brush BlockRuleHitStatusBrush = CreateFrozenBrush("#C2410C");
    private static readonly IReadOnlyDictionary<string, ImageSource> SessionIconImages = CreateSessionIconImages();
    private int _index;
    private int _theology;
    private string _method = "";
    private string _state = "";
    private string _url = "";
    private string _host = "";
    private string _query = "";
    private string _responseLength = "";
    private string _responseType = "";
    private string _process = "";
    private string _clientAddress = "";
    private string _sendTime = "";
    private string _receiveTime = "";
    private string _notes = "";
    private string _icon = "";
    private string _tagColor = "";
    private string _searchColor = "";
    private CaptureEntryColor _color = new();
    private int _breakMode;
    private bool _isFavorite;
    private List<TrafficRuleHitItem> _ruleHits = new();

    [JsonPropertyName("序号")]
    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, value);
    }

    [JsonPropertyName("Theology")]
    public int Theology
    {
        get => _theology;
        set => SetProperty(ref _theology, value);
    }

    [JsonPropertyName("方式")]
    public string Method
    {
        get => _method;
        set
        {
            if (SetProperty(ref _method, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayMethod));
                NotifyStatusVisualsChanged();
            }
        }
    }

    [JsonIgnore]
    public string DisplayMethod => Method.Equals("WebSocket", StringComparison.OrdinalIgnoreCase)
        || Method.Equals("Websocket", StringComparison.OrdinalIgnoreCase)
            ? "WS"
            : Method;

    [JsonPropertyName("状态")]
    public string State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value ?? ""))
            {
                NotifyStatusVisualsChanged();
            }
        }
    }

    [JsonPropertyName("请求地址")]
    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value ?? "");
    }

    [JsonPropertyName("HOST")]
    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value ?? "");
    }

    [JsonPropertyName("Query")]
    public string Query
    {
        get => _query;
        set => SetProperty(ref _query, value ?? "");
    }

    [JsonPropertyName("响应长度")]
    public string ResponseLength
    {
        get => _responseLength;
        set => SetProperty(ref _responseLength, value ?? "");
    }

    [JsonPropertyName("响应类型")]
    public string ResponseType
    {
        get => _responseType;
        set
        {
            if (SetProperty(ref _responseType, value ?? ""))
            {
                NotifyStatusVisualsChanged();
            }
        }
    }

    [JsonPropertyName("进程")]
    public string Process
    {
        get => _process;
        set => SetProperty(ref _process, value ?? "");
    }

    [JsonPropertyName("来源地址")]
    public string ClientAddress
    {
        get => _clientAddress;
        set => SetProperty(ref _clientAddress, value ?? "");
    }

    [JsonPropertyName("请求时间")]
    public string SendTime
    {
        get => _sendTime;
        set => SetProperty(ref _sendTime, value ?? "");
    }

    [JsonPropertyName("响应时间")]
    public string ReceiveTime
    {
        get => _receiveTime;
        set => SetProperty(ref _receiveTime, value ?? "");
    }

    [JsonPropertyName("注释")]
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? "");
    }

    [JsonPropertyName("ico")]
    public string Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value ?? ""))
            {
                NotifyStatusVisualsChanged();
            }
        }
    }

    [JsonPropertyName("color")]
    public CaptureEntryColor Color
    {
        get => _color;
        set
        {
            _color = value ?? new CaptureEntryColor();
            if (!string.IsNullOrWhiteSpace(_color.TagColor))
            {
                SetTagColor(_color.TagColor, updateColor: false);
            }

            if (!string.IsNullOrWhiteSpace(_color.Search))
            {
                SetSearchColor(_color.Search, updateColor: false);
            }
        }
    }

    [JsonIgnore]
    public string TagColor
    {
        get => _tagColor;
        set => SetTagColor(value, updateColor: true);
    }

    [JsonIgnore]
    public bool HasTagColor => !string.IsNullOrWhiteSpace(TagColor);

    [JsonIgnore]
    public bool IsStrikeMarked => string.Equals(TagColor, StrikeTagColor, StringComparison.Ordinal);

    [JsonIgnore]
    public Brush TagBackground => IsStrikeMarked ? Brushes.Transparent : CreateSearchBrush(TagColor, Brushes.Transparent, 0.12);

    [JsonIgnore]
    public Brush TagAccentBrush => IsStrikeMarked || string.IsNullOrWhiteSpace(TagColor)
        ? Brushes.Transparent
        : CreateFrozenBrush(TagColor);

    [JsonIgnore]
    public Brush RowAccentBrush => HasTagColor && !IsStrikeMarked
        ? TagAccentBrush
        : HasBlockRuleHits
            ? Brushes.Transparent
            : HasRuleHits
                ? RuleHitAccentBrush
                : Brushes.Transparent;

    [JsonIgnore]
    public Brush UrlForeground => HasTagColor && !IsStrikeMarked
        ? CreateReadableTagForeground(TagColor)
        : HasBlockRuleHits
            ? BlockRuleHitStatusBrush
        : CreateFrozenBrush("#1F2D3D");

    [JsonIgnore]
    public FontWeight UrlFontWeight => HasTagColor && !IsStrikeMarked || HasBlockRuleHits ? FontWeights.SemiBold : FontWeights.Normal;

    [JsonIgnore]
    public TextDecorationCollection? RowTextDecorations => IsStrikeMarked ? TextDecorations.Strikethrough : null;

    [JsonIgnore]
    public string SearchColor
    {
        get => _searchColor;
        set => SetSearchColor(value, updateColor: true);
    }

    [JsonIgnore]
    public Brush SearchBackground => CreateSearchBrush(SearchColor, Brushes.Transparent);

    [JsonIgnore]
    public bool HasSearchHighlight => !string.IsNullOrWhiteSpace(SearchColor);

    [JsonIgnore]
    public Brush RowBackground => HasSearchHighlight ? SearchBackground : HasTagColor && !IsStrikeMarked ? TagBackground : GetRowBackground();

    [JsonPropertyName("RuleHits")]
    public List<TrafficRuleHitItem> RuleHits
    {
        get => _ruleHits;
        set
        {
            _ruleHits = value ?? new List<TrafficRuleHitItem>();
            NotifyRuleHitVisualsChanged();
        }
    }

    [JsonIgnore]
    public bool HasRuleHits => RuleHits.Count > 0;

    [JsonIgnore]
    public bool HasBlockRuleHits => RuleHits.Any(IsBlockRuleHit);

    [JsonIgnore]
    public string RuleHitSummary => HasRuleHits
        ? string.Join(Environment.NewLine, RuleHits.TakeLast(4).Select(static hit => $"{hit.Time} {hit.Summary} {hit.RuleName}".Trim()))
        : "";

    [JsonIgnore]
    public string RuleHitBadgeText => HasBlockRuleHits
        ? $"屏蔽×{RuleHits.Count(IsBlockRuleHit)}"
        : HasRuleHits
            ? $"规则×{RuleHits.Count}"
            : "";

    [JsonIgnore]
    public Brush StatusBrush => GetStatusBrush();

    [JsonIgnore]
    public string StatusIconToolTip => GetStatusIconToolTip();

    [JsonIgnore]
    public string SessionIconGlyph => GetSessionIconVisual().Glyph;

    [JsonIgnore]
    public string SessionIconToolTip => GetSessionIconVisual().ToolTip;

    [JsonIgnore]
    public ImageSource SessionIconImage => GetSessionIconImage(SessionIconGlyph);

    [JsonIgnore]
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (!SetProperty(ref _isFavorite, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteBrush));
            OnPropertyChanged(nameof(FavoriteToolTip));
        }
    }

    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    [JsonIgnore]
    public Brush FavoriteBrush => IsFavorite ? FavoriteActiveBrush : FavoriteInactiveBrush;

    [JsonIgnore]
    public string FavoriteToolTip => IsFavorite ? "取消收藏" : "标记收藏";

    [JsonPropertyName("断点模式")]
    public int BreakMode
    {
        get => _breakMode;
        set
        {
            if (SetProperty(ref _breakMode, value))
            {
                OnPropertyChanged(nameof(IsIntercepted));
                NotifyStatusVisualsChanged();
            }
        }
    }

    [JsonIgnore]
    public bool IsIntercepted => BreakMode > 0;

    public void UpdateFrom(CaptureEntry entry)
    {
        if (entry.Index > 0)
        {
            Index = entry.Index;
        }

        Theology = entry.Theology;
        Method = entry.Method;
        State = entry.State;
        Url = entry.Url;
        Host = string.IsNullOrWhiteSpace(entry.Host) ? Host : entry.Host;
        Query = string.IsNullOrWhiteSpace(entry.Query) ? Query : entry.Query;
        ResponseLength = entry.ResponseLength;
        ResponseType = entry.ResponseType;
        Process = entry.Process;
        ClientAddress = entry.ClientAddress;
        SendTime = entry.SendTime;
        ReceiveTime = entry.ReceiveTime;
        Notes = entry.Notes;
        Icon = entry.Icon;
        if (!string.IsNullOrWhiteSpace(entry.TagColor))
        {
            TagColor = entry.TagColor;
        }

        if (!string.IsNullOrWhiteSpace(entry.SearchColor))
        {
            SearchColor = entry.SearchColor;
        }

        if (entry.RuleHits.Count > 0)
        {
            MergeRuleHits(entry.RuleHits);
        }

        BreakMode = entry.BreakMode;
        NormalizeDerivedFields();
    }

    public void AddRuleHit(TrafficRuleHitItem hit)
    {
        MergeRuleHits(new[] { hit });
    }

    private void MergeRuleHits(IEnumerable<TrafficRuleHitItem> hits)
    {
        bool changed = false;
        foreach (TrafficRuleHitItem hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.RuleType) && string.IsNullOrWhiteSpace(hit.RuleName))
            {
                continue;
            }

            bool exists = RuleHits.Any(existing =>
                string.Equals(existing.Time, hit.Time, StringComparison.Ordinal)
                && string.Equals(existing.RuleHash, hit.RuleHash, StringComparison.Ordinal)
                && string.Equals(existing.RuleType, hit.RuleType, StringComparison.Ordinal)
                && string.Equals(existing.Direction, hit.Direction, StringComparison.Ordinal)
                && string.Equals(existing.Action, hit.Action, StringComparison.Ordinal));
            if (exists)
            {
                continue;
            }

            RuleHits.Add(hit);
            changed = true;
        }

        if (changed)
        {
            NotifyRuleHitVisualsChanged();
        }
    }

    public void NormalizeDerivedFields()
    {
        if (string.IsNullOrWhiteSpace(Host) && Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri))
        {
            Host = uri.Host;
        }

        if (string.IsNullOrWhiteSpace(Query) && Uri.TryCreate(Url, UriKind.Absolute, out Uri? parsedUri))
        {
            Query = parsedUri.PathAndQuery;
        }

        if (Icon == "websocket_connect")
        {
            State = "已连接";
        }
        else if (Icon == "websocket_close")
        {
            State = "已断开";
        }
    }

    private void SetTagColor(string? value, bool updateColor)
    {
        string next = value ?? "";
        if (!SetProperty(ref _tagColor, next, nameof(TagColor)))
        {
            return;
        }

        if (updateColor)
        {
            _color.TagColor = next;
        }

        OnPropertyChanged(nameof(HasTagColor));
        OnPropertyChanged(nameof(IsStrikeMarked));
        OnPropertyChanged(nameof(TagBackground));
        OnPropertyChanged(nameof(TagAccentBrush));
        OnPropertyChanged(nameof(RowAccentBrush));
        OnPropertyChanged(nameof(UrlForeground));
        OnPropertyChanged(nameof(UrlFontWeight));
        OnPropertyChanged(nameof(RowTextDecorations));
        OnPropertyChanged(nameof(RowBackground));
    }

    private void SetSearchColor(string? value, bool updateColor)
    {
        string next = value ?? "";
        if (!SetProperty(ref _searchColor, next, nameof(SearchColor)))
        {
            return;
        }

        if (updateColor)
        {
            _color.Search = next;
        }

        OnPropertyChanged(nameof(SearchBackground));
        OnPropertyChanged(nameof(HasSearchHighlight));
        OnPropertyChanged(nameof(RowBackground));
    }

    private void NotifyStatusVisualsChanged()
    {
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusIconToolTip));
        OnPropertyChanged(nameof(SessionIconGlyph));
        OnPropertyChanged(nameof(SessionIconToolTip));
        OnPropertyChanged(nameof(SessionIconImage));
    }

    private void NotifyRuleHitVisualsChanged()
    {
        OnPropertyChanged(nameof(HasRuleHits));
        OnPropertyChanged(nameof(HasBlockRuleHits));
        OnPropertyChanged(nameof(RuleHitSummary));
        OnPropertyChanged(nameof(RuleHitBadgeText));
        OnPropertyChanged(nameof(RowAccentBrush));
        OnPropertyChanged(nameof(RowBackground));
        OnPropertyChanged(nameof(UrlForeground));
        OnPropertyChanged(nameof(UrlFontWeight));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusIconToolTip));
        OnPropertyChanged(nameof(SessionIconGlyph));
        OnPropertyChanged(nameof(SessionIconToolTip));
        OnPropertyChanged(nameof(SessionIconImage));
    }

    private Brush GetStatusBrush()
    {
        if (IsIntercepted)
        {
            return InterceptStatusBrush;
        }

        if (HasBlockRuleHits)
        {
            return BlockRuleHitStatusBrush;
        }

        if (IsErrorState())
        {
            return DangerStatusBrush;
        }

        int statusCode = ExtractStatusCode(State);
        if (statusCode >= 500)
        {
            return DangerStatusBrush;
        }

        if (statusCode >= 400)
        {
            return DangerStatusBrush;
        }

        if (statusCode >= 300)
        {
            return RedirectStatusBrush;
        }

        if (statusCode >= 200)
        {
            return SuccessStatusBrush;
        }

        if (Icon is "websocket_connect")
        {
            return InfoStatusBrush;
        }

        if (Icon is "websocket_close" || State.Contains("断开", StringComparison.OrdinalIgnoreCase))
        {
            return WarningStatusBrush;
        }

        return DefaultStatusBrush;
    }

    private Brush GetRowBackground()
    {
        if (IsIntercepted)
        {
            return InterceptRowBackgroundBrush;
        }

        if (HasBlockRuleHits)
        {
            return BlockRuleHitRowBackgroundBrush;
        }

        if (HasRuleHits)
        {
            return RuleHitRowBackgroundBrush;
        }

        if (IsErrorState())
        {
            return DangerRowBackgroundBrush;
        }

        int statusCode = ExtractStatusCode(State);
        if (statusCode >= 500)
        {
            return DangerRowBackgroundBrush;
        }

        if (statusCode >= 400)
        {
            return DangerRowBackgroundBrush;
        }

        if (statusCode >= 300)
        {
            return RedirectRowBackgroundBrush;
        }

        if (statusCode >= 200)
        {
            return SuccessRowBackgroundBrush;
        }

        if (Icon is "websocket_connect")
        {
            return InfoRowBackgroundBrush;
        }

        if (Icon is "websocket_close" || State.Contains("断开", StringComparison.OrdinalIgnoreCase))
        {
            return WarningRowBackgroundBrush;
        }

        return DefaultRowBackgroundBrush;
    }

    private string GetStatusIconToolTip()
    {
        if (IsIntercepted)
        {
            return BreakMode == 2 ? "已拦截下行响应" : "已拦截上行请求";
        }

        if (HasBlockRuleHits)
        {
            return RuleHitSummary;
        }

        int statusCode = ExtractStatusCode(State);
        if (statusCode > 0)
        {
            return $"HTTP {statusCode}";
        }

        return string.IsNullOrWhiteSpace(State) ? "等待状态" : State;
    }

    private SessionIconVisual GetSessionIconVisual()
    {
        if (IsIntercepted)
        {
            return BreakMode == 2
                ? new SessionIconVisual("↓", "响应断点：已拦截下行响应")
                : new SessionIconVisual("↑", "请求断点：已拦截上行请求");
        }

        if (HasBlockRuleHits)
        {
            return new SessionIconVisual("RB", string.IsNullOrWhiteSpace(RuleHitSummary) ? "屏蔽规则命中" : RuleHitSummary);
        }

        if (Icon.Equals("websocket_close", StringComparison.OrdinalIgnoreCase)
            || State.Contains("断开", StringComparison.OrdinalIgnoreCase))
        {
            return new SessionIconVisual("×", BuildSessionIconToolTip("连接已断开"));
        }

        if (Icon.Equals("websocket_connect", StringComparison.OrdinalIgnoreCase)
            || Method.Equals("WebSocket", StringComparison.OrdinalIgnoreCase)
            || Method.Equals("Websocket", StringComparison.OrdinalIgnoreCase)
            || ResponseType.Contains("WebSocket", StringComparison.OrdinalIgnoreCase)
            || ResponseType.Contains("Websocket", StringComparison.OrdinalIgnoreCase))
        {
            return new SessionIconVisual("WS", BuildSessionIconToolTip("WebSocket 会话"));
        }

        int statusCode = ExtractStatusCode(State);
        if (IsErrorState() || statusCode >= 500)
        {
            return new SessionIconVisual("!", BuildSessionIconToolTip("服务器错误"));
        }

        if (statusCode == 401 || statusCode == 407)
        {
            return new SessionIconVisual(statusCode.ToString(), BuildSessionIconToolTip("认证请求"));
        }

        if (statusCode >= 400)
        {
            return new SessionIconVisual("!", BuildSessionIconToolTip("请求错误"));
        }

        if (statusCode == 304)
        {
            return new SessionIconVisual("304", BuildSessionIconToolTip("缓存未修改"));
        }

        if (statusCode >= 300)
        {
            return new SessionIconVisual("↪", BuildSessionIconToolTip("重定向响应"));
        }

        SessionIconVisual? iconVisual = TryGetKnownSessionIcon(Icon);
        if (iconVisual.HasValue)
        {
            return iconVisual.Value;
        }

        iconVisual = TryGetContentTypeSessionIcon(ResponseType);
        if (iconVisual.HasValue)
        {
            return iconVisual.Value;
        }

        iconVisual = TryGetMethodSessionIcon(Method);
        if (iconVisual.HasValue)
        {
            return iconVisual.Value;
        }

        if (statusCode >= 200)
        {
            return new SessionIconVisual("✓", BuildSessionIconToolTip("请求完成"));
        }

        if (Icon.Contains("上行", StringComparison.OrdinalIgnoreCase) || State.Contains("-", StringComparison.OrdinalIgnoreCase))
        {
            return new SessionIconVisual("↑", "请求已发送，等待响应");
        }

        return new SessionIconVisual("↔", string.IsNullOrWhiteSpace(State) ? "普通会话" : State);
    }

    private SessionIconVisual? TryGetKnownSessionIcon(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string key = value.Trim();
        if (key.Contains("上行", StringComparison.OrdinalIgnoreCase))
        {
            return new SessionIconVisual("↑", "请求已发送，等待响应");
        }

        return key.ToLowerInvariant() switch
        {
            "img" => new SessionIconVisual("▧", BuildSessionIconToolTip("图片响应")),
            "js" => new SessionIconVisual("JS", BuildSessionIconToolTip("JavaScript 响应")),
            "css" => new SessionIconVisual("CSS", BuildSessionIconToolTip("CSS 样式响应")),
            "xml" => new SessionIconVisual("<>", BuildSessionIconToolTip("XML 响应")),
            "json" => new SessionIconVisual("{}", BuildSessionIconToolTip("JSON 响应")),
            "html" => new SessionIconVisual("H", BuildSessionIconToolTip("HTML 响应")),
            "audio" => new SessionIconVisual("♪", BuildSessionIconToolTip("音频响应")),
            "video" => new SessionIconVisual("▶", BuildSessionIconToolTip("视频响应")),
            "font" => new SessionIconVisual("Aa", BuildSessionIconToolTip("字体资源")),
            "flash" => new SessionIconVisual("F", BuildSessionIconToolTip("Flash 资源")),
            "post" => new SessionIconVisual("P", BuildSessionIconToolTip("POST/PUT 请求")),
            "302" => new SessionIconVisual("↪", BuildSessionIconToolTip("重定向响应")),
            "401" => new SessionIconVisual("401", BuildSessionIconToolTip("认证请求")),
            "stop" => new SessionIconVisual("!", BuildSessionIconToolTip("请求被拒绝或不存在")),
            "error" => new SessionIconVisual("!", BuildSessionIconToolTip("请求失败")),
            _ => null
        };
    }

    private SessionIconVisual? TryGetContentTypeSessionIcon(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string contentType = value.Trim().ToLowerInvariant();
        if (contentType.Contains("image/"))
        {
            return new SessionIconVisual("▧", BuildSessionIconToolTip("图片响应"));
        }

        if (contentType.Contains("javascript") || contentType.Contains("ecmascript"))
        {
            return new SessionIconVisual("JS", BuildSessionIconToolTip("JavaScript 响应"));
        }

        if (contentType.Contains("css"))
        {
            return new SessionIconVisual("CSS", BuildSessionIconToolTip("CSS 样式响应"));
        }

        if (contentType.Contains("json"))
        {
            return new SessionIconVisual("{}", BuildSessionIconToolTip("JSON 响应"));
        }

        if (contentType.Contains("html"))
        {
            return new SessionIconVisual("H", BuildSessionIconToolTip("HTML 响应"));
        }

        if (contentType.Contains("xml"))
        {
            return new SessionIconVisual("<>", BuildSessionIconToolTip("XML 响应"));
        }

        if (contentType.Contains("audio/"))
        {
            return new SessionIconVisual("♪", BuildSessionIconToolTip("音频响应"));
        }

        if (contentType.Contains("video/"))
        {
            return new SessionIconVisual("▶", BuildSessionIconToolTip("视频响应"));
        }

        if (contentType.Contains("font") || contentType.Contains("woff") || contentType.Contains("ttf"))
        {
            return new SessionIconVisual("Aa", BuildSessionIconToolTip("字体资源"));
        }

        if (contentType.Contains("text/plain"))
        {
            return new SessionIconVisual("TXT", BuildSessionIconToolTip("纯文本响应"));
        }

        if (contentType.Contains("octet-stream"))
        {
            return new SessionIconVisual("BIN", BuildSessionIconToolTip("二进制响应"));
        }

        return null;
    }

    private SessionIconVisual? TryGetMethodSessionIcon(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "CONNECT" => new SessionIconVisual("T", "CONNECT 隧道"),
            "HEAD" => new SessionIconVisual("H", "HEAD 请求"),
            "POST" => new SessionIconVisual("P", "POST 请求"),
            "PUT" => new SessionIconVisual("PU", "PUT 请求"),
            "DELETE" => new SessionIconVisual("D", "DELETE 请求"),
            "PATCH" => new SessionIconVisual("PA", "PATCH 请求"),
            _ => null
        };
    }

    private string BuildSessionIconToolTip(string text)
    {
        int statusCode = ExtractStatusCode(State);
        return statusCode > 0 ? $"{text}（HTTP {statusCode}）" : text;
    }

    private static ImageSource GetSessionIconImage(string glyph)
    {
        return SessionIconImages.TryGetValue(glyph, out ImageSource? image)
            ? image
            : SessionIconImages["↔"];
    }

    private bool IsErrorState()
    {
        return Icon.Equals("error", StringComparison.OrdinalIgnoreCase)
            || State.Contains("error", StringComparison.OrdinalIgnoreCase)
            || State.Contains("错误", StringComparison.OrdinalIgnoreCase)
            || ResponseType.Contains("error", StringComparison.OrdinalIgnoreCase);
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

    private static IReadOnlyDictionary<string, ImageSource> CreateSessionIconImages()
    {
        return new Dictionary<string, ImageSource>(StringComparer.Ordinal)
        {
            ["↑"] = CreateArrowIcon("#F97316", true),
            ["↓"] = CreateArrowIcon("#F97316", false),
            ["RB"] = CreateRuleBlockIcon(),
            ["WS"] = CreateWebSocketIcon(),
            ["×"] = CreateCloseIcon(),
            ["!"] = CreateErrorIcon(),
            ["401"] = CreateLockIcon("401"),
            ["304"] = CreateDocumentIcon("#EEF5FF", "#5B8DEF", "304", "#1D4ED8", 5.2),
            ["↪"] = CreateRedirectIcon(),
            ["▧"] = CreateImageIcon(),
            ["JS"] = CreateDocumentIcon("#FFF4BF", "#D9A321", "JS", "#7C4A03", 6.0),
            ["CSS"] = CreateDocumentIcon("#E6F0FF", "#4F7DF3", "CSS", "#1D4ED8", 4.8),
            ["<>"] = CreateDocumentIcon("#F0EBFF", "#8B5CF6", "<>", "#5B21B6", 6.0),
            ["{}"] = CreateDocumentIcon("#E9F8EF", "#33A66A", "{}", "#147A43", 6.0),
            ["H"] = CreateDocumentIcon("#FFEBDD", "#F97316", "H", "#B45309", 7.0),
            ["♪"] = CreateMediaTextIcon("#ECF2FF", "#4F7DF3", "♪", "#1D4ED8", 9.2),
            ["▶"] = CreatePlayIcon(),
            ["Aa"] = CreateDocumentIcon("#F7EDFF", "#A855F7", "Aa", "#7E22CE", 5.8),
            ["F"] = CreateDocumentIcon("#F3F4F6", "#8996A8", "F", "#475569", 7.0),
            ["P"] = CreateMethodIcon("#EEF5FF", "#2F7CF6", "P", "#1D4ED8"),
            ["PU"] = CreateMethodIcon("#EEF5FF", "#2F7CF6", "PU", "#1D4ED8"),
            ["D"] = CreateMethodIcon("#FFE8E8", "#E5484D", "D", "#B42318"),
            ["PA"] = CreateMethodIcon("#FFF2D8", "#F59E0B", "PA", "#92400E"),
            ["T"] = CreateTunnelIcon(),
            ["TXT"] = CreateDocumentIcon("#F4F7FB", "#94A3B8", "TXT", "#475569", 4.8),
            ["BIN"] = CreateDocumentIcon("#EEF1F5", "#64748B", "BIN", "#334155", 4.8),
            ["✓"] = CreateSuccessIcon(),
            ["↔"] = CreateGenericIcon()
        };
    }

    private static ImageSource CreateDocumentIcon(string fill, string stroke, string label, string labelColor, double fontSize)
    {
        return CreateIconImage(
            CreatePathDrawing("M3,1.5 L10.1,1.5 L13.5,4.9 L13.5,14.5 L3,14.5 Z", fill, stroke, 0.85),
            CreatePathDrawing("M10.1,1.7 L10.1,4.9 L13.3,4.9 Z", "#FFFFFF", stroke, 0.7),
            CreateTextDrawing(label, fontSize, labelColor, 9.2, 11.2));
    }

    private static ImageSource CreateMethodIcon(string fill, string stroke, string label, string labelColor)
    {
        double fontSize = label.Length > 1 ? 5.4 : 7.2;
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, fill, stroke, 1.0),
            CreateTextDrawing(label, fontSize, labelColor, 8.4, 10.6));
    }

    private static ImageSource CreateArrowIcon(string color, bool up)
    {
        string arrow = up
            ? "M8,12.5 L8,4.2 M4.8,7.4 L8,4.2 L11.2,7.4"
            : "M8,3.5 L8,11.8 M4.8,8.6 L8,11.8 L11.2,8.6";

        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, "#FFF4E8", color, 0.95),
            CreatePathDrawing(arrow, null, color, 1.65));
    }

    private static ImageSource CreateWebSocketIcon()
    {
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.5, "#EAF1FF", "#2F7CF6", 0.9),
            CreatePathDrawing("M5.2,9.7 C3.9,9.7 2.9,8.7 2.9,7.4 C2.9,6.1 3.9,5.1 5.2,5.1 L7.1,5.1", null, "#2563EB", 1.35),
            CreatePathDrawing("M8.9,10.9 L10.8,10.9 C12.1,10.9 13.1,9.9 13.1,8.6 C13.1,7.3 12.1,6.3 10.8,6.3 L8.9,6.3", null, "#2563EB", 1.35),
            CreatePathDrawing("M6.4,8 L9.6,8", null, "#2563EB", 1.35));
    }

    private static ImageSource CreateRuleBlockIcon()
    {
        return CreateIconImage(
            CreatePathDrawing("M6,1.6 L10,1.6 L14.4,6 L14.4,10 L10,14.4 L6,14.4 L1.6,10 L1.6,6 Z", "#FFF1E8", "#F05A24", 1.0),
            CreatePathDrawing("M5.1,5.1 L10.9,10.9 M10.9,5.1 L5.1,10.9", null, "#C2410C", 1.65));
    }

    private static ImageSource CreateCloseIcon()
    {
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, "#F1F5F9", "#64748B", 0.95),
            CreatePathDrawing("M5.2,5.2 L10.8,10.8 M10.8,5.2 L5.2,10.8", null, "#475569", 1.65));
    }

    private static ImageSource CreateErrorIcon()
    {
        return CreateIconImage(
            CreatePathDrawing("M6,1.6 L10,1.6 L14.4,6 L14.4,10 L10,14.4 L6,14.4 L1.6,10 L1.6,6 Z", "#E5484D", "#B42318", 0.75),
            CreateTextDrawing("!", 9.0, "#FFFFFF", 8.2, 8.0));
    }

    private static ImageSource CreateLockIcon(string label)
    {
        return CreateIconImage(
            CreateRoundedRectDrawing(new Rect(3.2, 6.8, 9.6, 6.8), 1.6, "#FBBF24", "#B7791F", 0.85),
            CreatePathDrawing("M5.3,7 L5.3,5.4 C5.3,3.6 6.4,2.5 8,2.5 C9.6,2.5 10.7,3.6 10.7,5.4 L10.7,7", null, "#B7791F", 1.15),
            CreateTextDrawing(label, 4.5, "#7C2D12", 10.0, 8.8));
    }

    private static ImageSource CreateRedirectIcon()
    {
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, "#EAF1FF", "#2F7CF6", 0.95),
            CreatePathDrawing("M4.5,10.8 C5.1,7 7.2,5.2 10.7,5.2 L10.7,3.4 L13.2,5.8 L10.7,8.2 L10.7,6.4 C8.1,6.4 6.6,7.7 5.9,10.8 Z", "#2F7CF6", null, 0));
    }

    private static ImageSource CreateImageIcon()
    {
        return CreateIconImage(
            CreateRoundedRectDrawing(new Rect(2.5, 3.0, 11.0, 10.0), 1.4, "#E8F8F0", "#23A66D", 0.9),
            CreateEllipseDrawing(5.5, 5.8, 1.1, "#FBBF24", null, 0),
            CreatePathDrawing("M3.4,11.9 L6.1,8.6 L7.9,10.7 L9.6,8.1 L12.7,11.9 Z", "#23A66D", null, 0));
    }

    private static ImageSource CreateMediaTextIcon(string fill, string stroke, string label, string labelColor, double fontSize)
    {
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, fill, stroke, 0.95),
            CreateTextDrawing(label, fontSize, labelColor, 8.0, 10.5));
    }

    private static ImageSource CreatePlayIcon()
    {
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, "#F1ECFF", "#8B5CF6", 0.95),
            CreatePathDrawing("M6.5,4.9 L11.2,8 L6.5,11.1 Z", "#7C3AED", null, 0));
    }

    private static ImageSource CreateSuccessIcon()
    {
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, "#198754", "#147045", 0.85),
            CreatePathDrawing("M4.3,8.3 L6.7,10.7 L11.8,5.4", null, "#FFFFFF", 1.75));
    }

    private static ImageSource CreateTunnelIcon()
    {
        return CreateIconImage(
            CreateEllipseDrawing(8, 8, 6.4, "#EEF5FF", "#2F7CF6", 0.95),
            CreatePathDrawing("M3.7,6.1 L12.3,6.1 M3.7,9.9 L12.3,9.9 M5.2,4.4 L3.7,6.1 L5.2,7.8 M10.8,8.2 L12.3,9.9 L10.8,11.6", null, "#2563EB", 1.25));
    }

    private static ImageSource CreateGenericIcon()
    {
        return CreateIconImage(
            CreatePathDrawing("M3,1.5 L10.1,1.5 L13.5,4.9 L13.5,14.5 L3,14.5 Z", "#F5F7FA", "#94A3B8", 0.85),
            CreatePathDrawing("M10.1,1.7 L10.1,4.9 L13.3,4.9 Z", "#FFFFFF", "#94A3B8", 0.7),
            CreatePathDrawing("M5,8 L11,8 M8.5,5.5 L11,8 L8.5,10.5", null, "#64748B", 1.2));
    }

    private static ImageSource CreateIconImage(params Drawing[] drawings)
    {
        DrawingGroup group = new();
        foreach (Drawing drawing in drawings)
        {
            if (drawing is DrawingGroup childGroup)
            {
                foreach (Drawing childDrawing in childGroup.Children)
                {
                    group.Children.Add(childDrawing);
                }

                continue;
            }

            group.Children.Add(drawing);
        }

        group.Freeze();
        DrawingImage image = new(group);
        image.Freeze();
        return image;
    }

    private static Drawing CreatePathDrawing(string data, string? fill, string? stroke, double strokeThickness)
    {
        Geometry geometry = Geometry.Parse(data);
        return CreateGeometryDrawing(
            geometry,
            fill is null ? null : CreateFrozenBrush(fill),
            stroke is null ? null : CreateFrozenPen(stroke, strokeThickness));
    }

    private static Drawing CreateEllipseDrawing(double centerX, double centerY, double radius, string fill, string? stroke, double strokeThickness)
    {
        return CreateGeometryDrawing(
            new EllipseGeometry(new Point(centerX, centerY), radius, radius),
            CreateFrozenBrush(fill),
            stroke is null ? null : CreateFrozenPen(stroke, strokeThickness));
    }

    private static Drawing CreateRoundedRectDrawing(Rect rect, double radius, string fill, string? stroke, double strokeThickness)
    {
        return CreateGeometryDrawing(
            new RectangleGeometry(rect, radius, radius),
            CreateFrozenBrush(fill),
            stroke is null ? null : CreateFrozenPen(stroke, strokeThickness));
    }

    private static Drawing CreateTextDrawing(string text, double fontSize, string color, double centerY, double maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            DrawingGroup empty = new();
            empty.Freeze();
            return empty;
        }

        Brush brush = CreateFrozenBrush(color);
        FormattedText formattedText = new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            fontSize,
            brush,
            1.0);

        if (formattedText.WidthIncludingTrailingWhitespace > maxWidth)
        {
            formattedText.SetFontSize(Math.Max(3.8, fontSize * maxWidth / formattedText.WidthIncludingTrailingWhitespace));
        }

        double left = (16 - formattedText.WidthIncludingTrailingWhitespace) / 2;
        double top = centerY - (formattedText.Height / 2);
        Geometry textGeometry = formattedText.BuildGeometry(new Point(left, top));
        return CreateGeometryDrawing(textGeometry, brush, null);
    }

    private static Drawing CreateGeometryDrawing(Geometry geometry, Brush? fill, Pen? pen)
    {
        geometry.Freeze();
        GeometryDrawing drawing = new(fill, pen, geometry);
        drawing.Freeze();
        return drawing;
    }

    private static Pen CreateFrozenPen(string color, double thickness)
    {
        Pen pen = new(CreateFrozenBrush(color), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Brush CreateFrozenBrush(string color)
    {
        SolidColorBrush brush = new((System.Windows.Media.Color)ColorConverter.ConvertFromString(color)!);
        brush.Freeze();
        return brush;
    }

    private static Brush CreateSearchBrush(string color, Brush fallback)
    {
        return CreateSearchBrush(color, fallback, 0.38);
    }

    private static Brush CreateSearchBrush(string color, Brush fallback, double opacity)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return fallback;
        }

        try
        {
            System.Windows.Media.Color parsed = (System.Windows.Media.Color)ColorConverter.ConvertFromString(color)!;
            SolidColorBrush brush = new(parsed)
            {
                Opacity = opacity
            };
            brush.Freeze();
            return brush;
        }
        catch
        {
            return fallback;
        }
    }

    private static Brush CreateReadableTagForeground(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return CreateFrozenBrush("#1F2D3D");
        }

        try
        {
            System.Windows.Media.Color parsed = (System.Windows.Media.Color)ColorConverter.ConvertFromString(color)!;
            double factor = GetRelativeLuminance(parsed) > 0.55 ? 0.45 : 0.72;
            System.Windows.Media.Color darkened = System.Windows.Media.Color.FromRgb(
                (byte)Math.Clamp((int)Math.Round(parsed.R * factor), 0, 255),
                (byte)Math.Clamp((int)Math.Round(parsed.G * factor), 0, 255),
                (byte)Math.Clamp((int)Math.Round(parsed.B * factor), 0, 255));
            SolidColorBrush brush = new(darkened);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return CreateFrozenBrush("#1F2D3D");
        }
    }

    private static double GetRelativeLuminance(System.Windows.Media.Color color)
    {
        return ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;
    }

    private static bool IsBlockRuleHit(TrafficRuleHitItem hit)
    {
        return hit.RuleType.Contains("屏蔽", StringComparison.OrdinalIgnoreCase)
            || hit.Action.Contains("断开", StringComparison.OrdinalIgnoreCase)
            || hit.Action.Contains("丢弃", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct SessionIconVisual(string Glyph, string ToolTip);
}

public sealed class CaptureEntryColor
{
    [JsonPropertyName("TagColor")]
    public string TagColor { get; set; } = "";

    [JsonPropertyName("search")]
    public string Search { get; set; } = "";
}
