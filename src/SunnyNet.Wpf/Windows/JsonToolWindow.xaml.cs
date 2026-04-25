using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;

namespace SunnyNet.Wpf.Windows;

public partial class JsonToolWindow : Window
{
    private readonly JsonBuilderNode _jsonRoot = new("root", "object", null);
    private JsonBuilderNode? _selectedJsonNode;

    public JsonToolWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshJsonBuilderTree();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        JsonInputTextBox.Clear();
        JsonOutputTextBox.Clear();
        JsonParseStatusTextBlock.Text = "输入 JSON 后点击解析。";
        ClearJsonBuilder();
        ToolSummaryTextBlock.Text = "支持 JSON 格式化、压缩和可视化构造。";
    }

    private void FormatJson_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(JsonInputTextBox.Text ?? string.Empty);
            JsonOutputTextBox.Text = JsonSerializer.Serialize(document.RootElement, CreateJsonOptions(indented: true));
            JsonParseStatusTextBlock.Text = "JSON 格式化完成。";
            ToolSummaryTextBlock.Text = "JSON 已解析并格式化输出。";
        }
        catch (Exception exception)
        {
            JsonParseStatusTextBlock.Text = $"解析失败：{exception.Message}";
        }
    }

    private void MinifyJson_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(JsonInputTextBox.Text ?? string.Empty);
            JsonOutputTextBox.Text = JsonSerializer.Serialize(document.RootElement, CreateJsonOptions(indented: false));
            JsonParseStatusTextBlock.Text = "JSON 压缩完成。";
            ToolSummaryTextBlock.Text = "JSON 已压缩为单行输出。";
        }
        catch (Exception exception)
        {
            JsonParseStatusTextBlock.Text = $"压缩失败：{exception.Message}";
        }
    }

    private void AddJsonNode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        JsonBuilderNode parent = _selectedJsonNode ?? _jsonRoot;
        if (parent.Type is not ("object" or "array"))
        {
            parent = _jsonRoot;
        }

        string type = GetComboText(JsonNodeTypeComboBox);
        string name = JsonNodeNameTextBox.Text?.Trim() ?? string.Empty;
        string? value = JsonNodeValueTextBox.Text;

        if (parent.Type == "object" && string.IsNullOrWhiteSpace(name))
        {
            ToolSummaryTextBlock.Text = "对象节点需要填写属性名。";
            return;
        }

        parent.Children.Add(new JsonBuilderNode(name, type, value));
        JsonNodeNameTextBox.Clear();
        JsonNodeValueTextBox.Clear();
        RefreshJsonBuilderTree();
        ToolSummaryTextBlock.Text = "JSON 节点已添加。";
    }

    private void RemoveJsonNode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (_selectedJsonNode is null || ReferenceEquals(_selectedJsonNode, _jsonRoot))
        {
            ToolSummaryTextBlock.Text = "请选择要删除的节点。";
            return;
        }

        if (RemoveJsonNode(_jsonRoot, _selectedJsonNode))
        {
            _selectedJsonNode = null;
            RefreshJsonBuilderTree();
            ToolSummaryTextBlock.Text = "JSON 节点已删除。";
        }
    }

    private void ClearJsonBuilder_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ClearJsonBuilder();
        ToolSummaryTextBlock.Text = "JSON 构造器已清空。";
    }

    private void JsonBuilderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> routedPropertyChangedEventArgs)
    {
        if (routedPropertyChangedEventArgs.NewValue is TreeViewItem { Tag: JsonBuilderNode node })
        {
            _selectedJsonNode = node;
            JsonNodeNameTextBox.Text = node == _jsonRoot ? string.Empty : node.Name;
            JsonNodeValueTextBox.Text = node.Value ?? string.Empty;
        }
    }

    private void ClearJsonBuilder()
    {
        _jsonRoot.Children.Clear();
        _selectedJsonNode = null;
        JsonNodeNameTextBox.Clear();
        JsonNodeValueTextBox.Clear();
        RefreshJsonBuilderTree();
    }

    private void RefreshJsonBuilderTree()
    {
        JsonBuilderTreeView.Items.Clear();
        JsonBuilderTreeView.Items.Add(CreateTreeItem(_jsonRoot));
        RefreshJsonBuilderOutput();
    }

    private TreeViewItem CreateTreeItem(JsonBuilderNode node)
    {
        TreeViewItem item = new()
        {
            Header = node == _jsonRoot ? "root {}" : BuildNodeHeader(node),
            Tag = node,
            IsExpanded = true
        };

        foreach (JsonBuilderNode child in node.Children)
        {
            item.Items.Add(CreateTreeItem(child));
        }

        return item;
    }

    private static string BuildNodeHeader(JsonBuilderNode node)
    {
        string name = string.IsNullOrWhiteSpace(node.Name) ? "[]" : node.Name;
        return node.Type is "object" or "array"
            ? $"{name}: {node.Type}"
            : $"{name}: {node.Type} = {node.Value}";
    }

    private void RefreshJsonBuilderOutput()
    {
        object? value = BuildJsonValue(_jsonRoot);
        JsonBuilderOutputTextBox.Text = JsonSerializer.Serialize(value, CreateJsonOptions(indented: true));
    }

    private static object? BuildJsonValue(JsonBuilderNode node)
    {
        return node.Type switch
        {
            "object" => node.Children.ToDictionary(static child => child.Name, BuildJsonValue),
            "array" => node.Children.Select(BuildJsonValue).ToList(),
            "number" => ParseNumber(node.Value),
            "bool" => bool.TryParse(node.Value, out bool boolValue) && boolValue,
            "null" => null,
            _ => node.Value ?? string.Empty
        };
    }

    private static object ParseNumber(string? value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
        {
            return longValue;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue))
        {
            return decimalValue;
        }

        return 0;
    }

    private static bool RemoveJsonNode(JsonBuilderNode parent, JsonBuilderNode target)
    {
        if (parent.Children.Remove(target))
        {
            return true;
        }

        foreach (JsonBuilderNode child in parent.Children)
        {
            if (RemoveJsonNode(child, target))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonSerializerOptions CreateJsonOptions(bool indented)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
    }

    private static string GetComboText(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
    }

    private sealed class JsonBuilderNode(string name, string type, string? value)
    {
        public string Name { get; set; } = name;
        public string Type { get; set; } = type;
        public string? Value { get; set; } = value;
        public List<JsonBuilderNode> Children { get; } = new();
    }
}
