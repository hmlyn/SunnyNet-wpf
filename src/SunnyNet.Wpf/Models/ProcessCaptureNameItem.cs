using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class ProcessCaptureNameItem : ViewModelBase
{
    private string _name = "";
    private bool _isCaptured = true;

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
