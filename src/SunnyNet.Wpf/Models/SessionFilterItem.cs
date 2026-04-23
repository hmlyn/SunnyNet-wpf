using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Models;

public sealed class SessionFilterItem : ViewModelBase
{
    private bool _isSelected;

    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public string DisplayText => $"{Name} ({Count})";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
