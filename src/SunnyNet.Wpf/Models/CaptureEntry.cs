using System.Text.Json.Serialization;
using System.Windows.Media;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class CaptureEntry : ViewModelBase
{
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
    private string _searchColor = "";
    private CaptureEntryColor _color = new();
    private int _breakMode;
    private bool _isFavorite;

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
            if (!string.IsNullOrWhiteSpace(_color.Search))
            {
                SetSearchColor(_color.Search, updateColor: false);
            }
        }
    }

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
    public Brush RowBackground => HasSearchHighlight ? SearchBackground : GetRowBackground();

    [JsonIgnore]
    public Brush StatusBrush => GetStatusBrush();

    [JsonIgnore]
    public string StatusIconToolTip => GetStatusIconToolTip();

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
        if (!string.IsNullOrWhiteSpace(entry.SearchColor))
        {
            SearchColor = entry.SearchColor;
        }

        BreakMode = entry.BreakMode;
        NormalizeDerivedFields();
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
    }

    private Brush GetStatusBrush()
    {
        if (IsIntercepted)
        {
            return InterceptStatusBrush;
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

        int statusCode = ExtractStatusCode(State);
        if (statusCode > 0)
        {
            return $"HTTP {statusCode}";
        }

        return string.IsNullOrWhiteSpace(State) ? "等待状态" : State;
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

    private static Brush CreateFrozenBrush(string color)
    {
        SolidColorBrush brush = new((System.Windows.Media.Color)ColorConverter.ConvertFromString(color)!);
        brush.Freeze();
        return brush;
    }

    private static Brush CreateSearchBrush(string color, Brush fallback)
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
                Opacity = 0.38
            };
            brush.Freeze();
            return brush;
        }
        catch
        {
            return fallback;
        }
    }
}

public sealed class CaptureEntryColor
{
    [JsonPropertyName("TagColor")]
    public string TagColor { get; set; } = "";

    [JsonPropertyName("search")]
    public string Search { get; set; } = "";
}
