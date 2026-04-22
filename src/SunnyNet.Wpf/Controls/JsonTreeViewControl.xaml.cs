using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SunnyNet.Wpf.Controls;

public partial class JsonTreeViewControl : UserControl
{
    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(JsonTreeViewControl), new PropertyMetadata("", OnJsonChanged));

    private static readonly Brush KeyBrush = CreateBrush(0x7C, 0x3A, 0xC8);
    private static readonly Brush StringBrush = CreateBrush(0x0F, 0x7B, 0x63);
    private static readonly Brush NumberBrush = CreateBrush(0xB4, 0x6B, 0x00);
    private static readonly Brush KeywordBrush = CreateBrush(0xD9, 0x2D, 0x20);
    private static readonly Brush MutedBrush = CreateBrush(0x6B, 0x7C, 0x93);
    private bool _isJson;

    public JsonTreeViewControl()
    {
        InitializeComponent();
        RenderJson();
    }

    public string JsonText
    {
        get => (string)GetValue(JsonTextProperty);
        set => SetValue(JsonTextProperty, value);
    }

    private static void OnJsonChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is JsonTreeViewControl control)
        {
            control.RenderJson();
        }
    }

    private void RenderJson()
    {
        JsonTree.Items.Clear();
        if (string.IsNullOrWhiteSpace(JsonText))
        {
            _isJson = false;
            UpdateToolbarState("无 JSON 内容");
            ShowFallback();
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(JsonText);
            JsonTree.Items.Add(CreateNode("root", document.RootElement));
            _isJson = true;
            UpdateToolbarState(document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => $"对象 · {document.RootElement.EnumerateObject().Count()} 项",
                JsonValueKind.Array => $"数组 · {document.RootElement.GetArrayLength()} 项",
                _ => "JSON 值"
            });
            JsonTree.Visibility = Visibility.Visible;
            FallbackViewer.Visibility = Visibility.Collapsed;
            ApplyMode(true, force: true);
        }
        catch
        {
            _isJson = false;
            UpdateToolbarState("非 JSON 文本");
            ShowFallback();
        }
    }

    private void ShowFallback()
    {
        ApplyMode(false, force: true);
    }

    private void TreeButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ApplyMode(true);
    }

    private void RawButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ApplyMode(false);
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        foreach (TreeViewItem item in JsonTree.Items.OfType<TreeViewItem>())
        {
            SetExpanded(item, true);
        }
    }

    private void CollapseAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        foreach (TreeViewItem item in JsonTree.Items.OfType<TreeViewItem>())
        {
            SetExpanded(item, false);
        }
    }

    private void CopyJson_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (string.IsNullOrWhiteSpace(JsonText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(JsonText);
        }
        catch
        {
        }
    }

    private void ApplyMode(bool treeMode, bool force = false)
    {
        bool useTree = _isJson && treeMode;
        if (!force && useTree == (JsonTree.Visibility == Visibility.Visible))
        {
            TreeButton.IsChecked = useTree;
            RawButton.IsChecked = !useTree;
            return;
        }

        TreeButton.IsEnabled = _isJson;
        TreeButton.IsChecked = useTree;
        RawButton.IsChecked = !useTree;
        JsonTree.Visibility = useTree ? Visibility.Visible : Visibility.Collapsed;
        FallbackViewer.Visibility = useTree ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateToolbarState(string text)
    {
        SummaryTextBlock.Text = text;
        TreeButton.IsEnabled = _isJson;
    }

    private static void SetExpanded(TreeViewItem item, bool isExpanded)
    {
        item.IsExpanded = isExpanded;
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
        {
            SetExpanded(child, isExpanded);
        }
    }

    private static TreeViewItem CreateNode(string name, JsonElement element)
    {
        TreeViewItem item = new()
        {
            Header = CreateHeader(name, element)
        };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    item.Items.Add(CreateNode(property.Name, property.Value));
                }
                item.IsExpanded = true;
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement child in element.EnumerateArray())
                {
                    item.Items.Add(CreateNode($"[{index++}]", child));
                }
                item.IsExpanded = true;
                break;
        }

        return item;
    }

    private static StackPanel CreateHeader(string name, JsonElement element)
    {
        StackPanel panel = new()
        {
            Orientation = Orientation.Horizontal
        };

        panel.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = KeyBrush,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI")
        });

        panel.Children.Add(new TextBlock
        {
            Text = ": ",
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI")
        });

        panel.Children.Add(new TextBlock
        {
            Text = PreviewValue(element),
            Foreground = ValueBrush(element),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI")
        });

        return panel;
    }

    private static string PreviewValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => $"{{ {element.EnumerateObject().Count()} fields }}",
            JsonValueKind.Array => $"[ {element.GetArrayLength()} items ]",
            JsonValueKind.String => $"\"{element.GetString()}\"",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private static Brush ValueBrush(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => StringBrush,
            JsonValueKind.Number => NumberBrush,
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => KeywordBrush,
            _ => MutedBrush
        };
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
