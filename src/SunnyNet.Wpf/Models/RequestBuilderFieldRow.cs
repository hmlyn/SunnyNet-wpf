using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class RequestBuilderFieldRow : ViewModelBase
{
    private bool _isEnabled = true;
    private string _key = "";
    private string _value = "";
    private string _valueType = "String";

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value ?? "");
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? "");
    }

    public string ValueType
    {
        get => _valueType;
        set => SetProperty(ref _valueType, string.IsNullOrWhiteSpace(value) ? "String" : value);
    }
}
