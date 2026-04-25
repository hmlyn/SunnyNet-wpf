using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SunnyNet.Wpf.Models;

public sealed class JsonEditorNode : INotifyPropertyChanged
{
    private string _name;
    private string _type;
    private string _value;
    private bool _isExpanded = true;
    private bool _isSelected;
    private double _indent;
    private bool _canToggle;

    public JsonEditorNode(string name, string type, string value = "")
    {
        _name = name;
        _type = type;
        _value = value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Type
    {
        get => _type;
        set
        {
            if (SetField(ref _type, value) && !CanHaveChildren)
            {
                Children.Clear();
            }

            OnPropertyChanged(nameof(CanHaveChildren));
            OnPropertyChanged(nameof(TypeBadge));
            OnPropertyChanged(nameof(ValueBrush));
            OnPropertyChanged(nameof(HasChildren));
        }
    }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public int Level { get; set; }

    public double Indent
    {
        get => _indent;
        set => SetField(ref _indent, value);
    }

    public JsonEditorNode? Parent { get; set; }

    public ObservableCollection<JsonEditorNode> Children { get; } = new();

    public bool CanHaveChildren => Type is "object" or "array";

    public bool HasChildren => Children.Count > 0;

    public bool CanToggle
    {
        get => _canToggle;
        set => SetField(ref _canToggle, value);
    }

    public string TypeBadge => Type switch
    {
        "object" => "object",
        "array" => "array",
        "number" => "num",
        "bool" => "bool",
        "null" => "null",
        _ => "str"
    };

    public string ValueBrush => Type switch
    {
        "string" => "#138A3D",
        "number" => "#D13B2E",
        "bool" => "#1E50C8",
        "null" => "#8A97A8",
        "object" => "#D98510",
        "array" => "#486DD3",
        _ => "#1F2D3D"
    };

    public JsonEditorNode Clone()
    {
        JsonEditorNode clone = new(Name, Type, Value) { IsExpanded = IsExpanded };
        foreach (JsonEditorNode child in Children)
        {
            JsonEditorNode childClone = child.Clone();
            childClone.Parent = clone;
            clone.Children.Add(childClone);
        }

        return clone;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
