using System.Text.Json.Serialization;
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

    [JsonPropertyName("#")]
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

    [JsonPropertyName("数据")]
    public string Data
    {
        get => _data;
        set => SetProperty(ref _data, value ?? "");
    }

    [JsonPropertyName("ico")]
    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value ?? "");
    }

    [JsonPropertyName("时间")]
    public string Time
    {
        get => _time;
        set => SetProperty(ref _time, value ?? "");
    }

    [JsonPropertyName("长度")]
    public int Length
    {
        get => _length;
        set => SetProperty(ref _length, value);
    }

    [JsonPropertyName("类型")]
    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value ?? "");
    }
}
