using System.Text.Json.Serialization;
using System.Windows.Media;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class SocketEntry : ViewModelBase
{
    private int _index;
    private int _theology;
    private string _data = "";
    private string _icon = "";
    private string _time = "";
    private int _length;
    private string _type = "";
    private string _searchColor = "";

    [JsonPropertyName("#")]
    public int Index
    {
        get => _index;
        set
        {
            if (SetProperty(ref _index, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    [JsonPropertyName("Theology")]
    public int Theology
    {
        get => _theology;
        set => SetProperty(ref _theology, value);
    }

    [JsonPropertyName("数据")]
    public string Data
    {
        get => _data;
        set
        {
            if (SetProperty(ref _data, value ?? ""))
            {
                RaiseDerivedProperties();
            }
        }
    }

    [JsonPropertyName("ico")]
    public string Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value ?? ""))
            {
                RaiseDerivedProperties();
            }
        }
    }

    [JsonPropertyName("时间")]
    public string Time
    {
        get => _time;
        set
        {
            if (SetProperty(ref _time, value ?? ""))
            {
                RaiseDerivedProperties();
            }
        }
    }

    [JsonPropertyName("长度")]
    public int Length
    {
        get => _length;
        set
        {
            if (SetProperty(ref _length, value))
            {
                RaiseDerivedProperties();
            }
        }
    }

    [JsonPropertyName("类型")]
    public string Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value ?? ""))
            {
                RaiseDerivedProperties();
            }
        }
    }

    [JsonPropertyName("background")]
    public string SearchColor
    {
        get => _searchColor;
        set
        {
            if (SetProperty(ref _searchColor, value ?? ""))
            {
                OnPropertyChanged(nameof(CardBackground));
                OnPropertyChanged(nameof(SearchBorderBrush));
                OnPropertyChanged(nameof(HasSearchHighlight));
            }
        }
    }

    [JsonIgnore]
    public int DisplayIndex => Index > 0 ? Index : 0;

    [JsonIgnore]
    public string CompactIndex => $"#{DisplayIndex:0000}";

    [JsonIgnore]
    public string DirectionLabel => Icon switch
    {
        "上行" => "发送",
        "下行" => "接收",
        "websocket_connect" => "连接",
        "websocket_close" => "断开",
        _ => string.IsNullOrWhiteSpace(Icon) ? "消息" : Icon
    };

    [JsonIgnore]
    public string DirectionGlyph => Icon switch
    {
        "上行" => "↑",
        "下行" => "↓",
        "websocket_connect" => "◎",
        "websocket_close" => "×",
        _ => "•"
    };

    [JsonIgnore]
    public string RouteLabel => Icon switch
    {
        "上行" => "CLIENT → SERVER",
        "下行" => "SERVER → CLIENT",
        "websocket_connect" => "OPEN HANDSHAKE",
        "websocket_close" => "CLOSE EVENT",
        _ => "WEBSOCKET"
    };

    [JsonIgnore]
    public string FrameTitle => TypeLabel switch
    {
        "Text" => "文本消息",
        "Binary" => "二进制消息",
        "Ping" => "Ping 心跳",
        "Pong" => "Pong 应答",
        "Close" => "Close 关闭",
        _ => Icon switch
        {
            "websocket_connect" => "连接建立",
            "websocket_close" => "连接断开",
            _ => "WebSocket 消息"
        }
    };

    [JsonIgnore]
    public string OpcodeLabel => TypeLabel switch
    {
        "Text" => "OP 0x1",
        "Binary" => "OP 0x2",
        "Close" => "OP 0x8",
        "Ping" => "OP 0x9",
        "Pong" => "OP 0xA",
        _ => IsStatusFrame ? "EVENT" : "OP ?"
    };

    [JsonIgnore]
    public string FrameGroupLabel => TypeLabel switch
    {
        "Text" => "DATA",
        "Binary" => "BINARY",
        "Ping" or "Pong" or "Close" => "CONTROL",
        _ => IsStatusFrame ? "STATUS" : "FRAME"
    };

    [JsonIgnore]
    public string TimeLabel => string.IsNullOrWhiteSpace(Time) ? "--:--:--.---" : Time;

    [JsonIgnore]
    public string TypeLabel => string.IsNullOrWhiteSpace(Type) ? "未知" : Type;

    [JsonIgnore]
    public string FlowTypeLabel => IsStatusFrame ? "事件" : TypeLabel;

    [JsonIgnore]
    public string LengthLabel => Length <= 0 ? "0 B" : $"{Length:N0} B";

    [JsonIgnore]
    public string PreviewText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Data))
            {
                return Data.Replace("\r", " ").Replace("\n", " ");
            }

            return Icon switch
            {
                "websocket_connect" => "WebSocket 连接已建立",
                "websocket_close" => "WebSocket 连接已关闭",
                _ => "暂无消息内容"
            };
        }
    }

    [JsonIgnore]
    public string PreviewTextShort
    {
        get
        {
            string preview = PreviewText;
            return preview.Length <= 180 ? preview : preview[..180] + "...";
        }
    }

    [JsonIgnore]
    public bool IsStatusFrame => Icon is "websocket_connect" or "websocket_close";

    [JsonIgnore]
    public bool IsTrafficFrame => Icon is "上行" or "下行";

    [JsonIgnore]
    public bool IsTextFrame => string.Equals(Type, "Text", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsBinaryFrame => string.Equals(Type, "Binary", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsControlFrame =>
        string.Equals(Type, "Ping", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Type, "Pong", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Type, "Close", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool HasSearchHighlight => !string.IsNullOrWhiteSpace(SearchColor);

    [JsonIgnore]
    public Brush CardBackground => CreateSearchBrush(SearchColor, Brushes.White, 0.34);

    [JsonIgnore]
    public Brush SearchBorderBrush => CreateSearchBrush(SearchColor, new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE4, 0xEB, 0xF5)), 0.7);

    private void RaiseDerivedProperties()
    {
        OnPropertyChanged(nameof(DisplayIndex));
        OnPropertyChanged(nameof(CompactIndex));
        OnPropertyChanged(nameof(DirectionLabel));
        OnPropertyChanged(nameof(DirectionGlyph));
        OnPropertyChanged(nameof(RouteLabel));
        OnPropertyChanged(nameof(FrameTitle));
        OnPropertyChanged(nameof(OpcodeLabel));
        OnPropertyChanged(nameof(FrameGroupLabel));
        OnPropertyChanged(nameof(TimeLabel));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(FlowTypeLabel));
        OnPropertyChanged(nameof(LengthLabel));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(PreviewTextShort));
        OnPropertyChanged(nameof(IsStatusFrame));
        OnPropertyChanged(nameof(IsTrafficFrame));
        OnPropertyChanged(nameof(IsTextFrame));
        OnPropertyChanged(nameof(IsBinaryFrame));
        OnPropertyChanged(nameof(IsControlFrame));
        OnPropertyChanged(nameof(HasSearchHighlight));
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(SearchBorderBrush));
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
}
