using System.Text.Json.Serialization;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class CaptureEntry : ViewModelBase
{
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
    private int _breakMode;

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
        set => SetProperty(ref _method, value ?? "");
    }

    [JsonPropertyName("状态")]
    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value ?? "");
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
        set => SetProperty(ref _responseType, value ?? "");
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
        set => SetProperty(ref _icon, value ?? "");
    }

    [JsonPropertyName("断点模式")]
    public int BreakMode
    {
        get => _breakMode;
        set => SetProperty(ref _breakMode, value);
    }

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
}
