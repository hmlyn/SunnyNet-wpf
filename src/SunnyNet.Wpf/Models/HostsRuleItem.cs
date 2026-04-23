using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class HostsRuleItem : ViewModelBase
{
    private string _hash = Guid.NewGuid().ToString("N");
    private string _source = "";
    private string _target = "";
    private string _state = "未保存";

    public string Hash
    {
        get => _hash;
        set => SetProperty(ref _hash, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value);
    }

    public string Source
    {
        get => _source;
        set
        {
            if (SetProperty(ref _source, value ?? ""))
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
            if (SetProperty(ref _target, value ?? ""))
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
