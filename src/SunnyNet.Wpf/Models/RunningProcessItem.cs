using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class RunningProcessItem : ViewModelBase
{
    private int _pid;
    private string _name = "";
    private bool _isCaptured;

    public int Pid
    {
        get => _pid;
        set => SetProperty(ref _pid, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }

    public bool IsCaptured
    {
        get => _isCaptured;
        set => SetProperty(ref _isCaptured, value);
    }
}
