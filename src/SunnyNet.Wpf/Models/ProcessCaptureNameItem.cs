using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class ProcessCaptureNameItem : ViewModelBase
{
    private string _name = "";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }
}
