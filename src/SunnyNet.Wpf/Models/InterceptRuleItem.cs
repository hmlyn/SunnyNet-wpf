using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class InterceptRuleItem : ViewModelBase
{
    private string _hash = Guid.NewGuid().ToString("N");
    private bool _enabled = true;
    private string _name = "新规则";
    private string _direction = "上行";
    private string _target = "URL";
    private string _operator = "包含";
    private string _value = "";
    private string _state = "未保存";

    public string Hash
    {
        get => _hash;
        set => SetProperty(ref _hash, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                State = "未保存";
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "新规则" : value))
            {
                State = "未保存";
            }
        }
    }

    public string Direction
    {
        get => _direction;
        set
        {
            if (SetProperty(ref _direction, string.IsNullOrWhiteSpace(value) ? "上行" : value))
            {
                State = "未保存";
            }
        }
    }

    public string Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, string.IsNullOrWhiteSpace(value) ? "URL" : value))
            {
                State = "未保存";
            }
        }
    }

    public string Operator
    {
        get => _operator;
        set
        {
            if (SetProperty(ref _operator, string.IsNullOrWhiteSpace(value) ? "包含" : value))
            {
                State = "未保存";
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? ""))
            {
                State = "未保存";
            }
        }
    }

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value ?? "");
    }
}
