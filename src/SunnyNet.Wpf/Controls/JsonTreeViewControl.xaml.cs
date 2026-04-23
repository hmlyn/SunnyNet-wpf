using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SunnyNet.Wpf.Controls;

public partial class JsonTreeViewControl : UserControl
{
    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(JsonTreeViewControl), new PropertyMetadata("", OnJsonChanged));

    private static readonly Brush KeyBrush = CreateBrush(0x00, 0x00, 0x00);
    private static readonly Brush StringBrush = CreateBrush(0x0F, 0x7B, 0x63);
    private static readonly Brush NumberBrush = CreateBrush(0xD9, 0x2D, 0x20);
    private static readonly Brush BoolBrush = CreateBrush(0x1E, 0x50, 0xC8);
    private static readonly Brush NullBrush = CreateBrush(0x8C, 0x8C, 0x8C);
    private static readonly Brush SeparatorBrush = CreateBrush(0xC1, 0xCB, 0xD8);
    private static readonly Brush MutedBrush = CreateBrush(0x6B, 0x7C, 0x93);
    private bool _isJson;

    private sealed record JsonNodeInfo(string Key, string Value, string Path);

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
            JsonTree.Items.Add(CreateNode("root", document.RootElement, "$"));
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
        CopyText(JsonText);
    }

    private void CopySelectedKey_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (GetSelectedNodeInfo() is { } node)
        {
            CopyText(node.Key);
        }
    }

    private void CopySelectedValue_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (GetSelectedNodeInfo() is { } node)
        {
            CopyText(node.Value);
        }
    }

    private void CopySelectedPath_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (GetSelectedNodeInfo() is { } node)
        {
            CopyText(node.Path);
        }
    }

    private void JsonTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (FindParent<TreeViewItem>(mouseButtonEventArgs.OriginalSource as DependencyObject) is not { } item)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
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

    private static TreeViewItem CreateNode(string name, JsonElement element, string path)
    {
        TreeViewItem item = new()
        {
            Header = CreateHeader(name, element),
            Tag = new JsonNodeInfo(name, CopyValue(element), path)
        };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    item.Items.Add(CreateNode(property.Name, property.Value, AppendObjectPath(path, property.Name)));
                }
                item.IsExpanded = true;
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement child in element.EnumerateArray())
                {
                    item.Items.Add(CreateNode($"[{index}]", child, $"{path}[{index}]"));
                    index++;
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
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            UseLayoutRounding = true
        };

        panel.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = KeyBrush,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = ": ",
            Foreground = SeparatorBrush,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = PreviewValue(element),
            Foreground = ValueBrush(element),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
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
            JsonValueKind.True or JsonValueKind.False => BoolBrush,
            JsonValueKind.Null => NullBrush,
            _ => MutedBrush
        };
    }

    private JsonNodeInfo? GetSelectedNodeInfo()
    {
        return (JsonTree.SelectedItem as TreeViewItem)?.Tag as JsonNodeInfo;
    }

    private static void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
        }
    }

    private static string CopyValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private static string AppendObjectPath(string parentPath, string propertyName)
    {
        if (IsPathIdentifier(propertyName))
        {
            return $"{parentPath}.{propertyName}";
        }

        return $"{parentPath}['{EscapePathSegment(propertyName)}']";
    }

    private static bool IsPathIdentifier(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!IsPathIdentifierStart(text[0]))
        {
            return false;
        }

        for (int index = 1; index < text.Length; index++)
        {
            if (!IsPathIdentifierPart(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPathIdentifierStart(char character)
    {
        return char.IsLetter(character) || character is '_' or '$';
    }

    private static bool IsPathIdentifierPart(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '$';
    }

    private static string EscapePathSegment(string text)
    {
        return text.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
