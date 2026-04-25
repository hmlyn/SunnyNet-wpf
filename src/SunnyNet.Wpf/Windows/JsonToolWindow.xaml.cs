using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Windows;

public partial class JsonToolWindow : Window
{
    private readonly ObservableCollection<JsonEditorNode> _rootNodes = new();
    private readonly ObservableCollection<JsonEditorNode> _visibleNodes = new();
    private readonly Dictionary<JsonEditorNode, string> _editingValues = new();
    private readonly Dictionary<JsonEditorNode, string> _editingTypes = new();
    private bool _isRefreshingNodes;

    public JsonToolWindow()
    {
        InitializeComponent();
        JsonNodeListBox.ItemsSource = _visibleNodes;
        Loaded += (_, _) => Dispatcher.BeginInvoke(InitializeDefaultTree, DispatcherPriority.Background);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        JsonInputTextBox.Clear();
        _rootNodes.Clear();
        RefreshVisibleNodes();
        JsonParseStatusTextBlock.Text = "输入 JSON 后可格式化或解析到右侧结构。";
        ToolSummaryTextBlock.Text = "左侧编辑原文，右侧以纯 WPF 节点表格编辑 JSON 结构。";
    }

    private void FormatJson_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            JsonInputTextBox.Text = FormatJson(JsonInputTextBox.Text, indented: true);
            JsonParseStatusTextBlock.Text = "JSON 格式化完成。";
            ToolSummaryTextBlock.Text = "JSON 已格式化，可继续解析到右侧结构。";
        }
        catch (Exception exception)
        {
            ShowJsonError("格式化失败", exception);
        }
    }

    private void MinifyJson_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            JsonInputTextBox.Text = FormatJson(JsonInputTextBox.Text, indented: false);
            JsonParseStatusTextBlock.Text = "JSON 压缩完成。";
            ToolSummaryTextBlock.Text = "JSON 已压缩为单行。";
        }
        catch (Exception exception)
        {
            ShowJsonError("压缩失败", exception);
        }
    }

    private void ParseToTree_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(JsonInputTextBox.Text ?? string.Empty);
            _rootNodes.Clear();
            JsonEditorNode root = CreateNode("root", document.RootElement, null, 0);
            root.IsExpanded = true;
            _rootNodes.Add(root);
            RefreshVisibleNodes();
            JsonParseStatusTextBlock.Text = "JSON 已解析到结构编辑器。";
            ToolSummaryTextBlock.Text = "可在右侧直接编辑节点，点击“同步原文”写回左侧。";
        }
        catch (Exception exception)
        {
            ShowJsonError("解析失败", exception);
        }
    }

    private void TreeToText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            JsonInputTextBox.Text = BuildTreeJson(indented: true);
            JsonParseStatusTextBlock.Text = "结构已同步到 JSON 原文。";
            ToolSummaryTextBlock.Text = "结构编辑器内容已写回左侧原文。";
        }
        catch (Exception exception)
        {
            ShowJsonError("同步失败", exception);
        }
    }

    private void AddRootChild_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        JsonEditorNode parent = _rootNodes.FirstOrDefault() ?? CreateRootObject();
        AddChildNode(parent);
    }

    private void AddChild_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is JsonEditorNode node)
        {
            AddChildNode(node);
        }
    }

    private void DuplicateNode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is not JsonEditorNode node)
        {
            return;
        }

        JsonEditorNode clone = node.Clone();
        if (node.Parent is null)
        {
            clone.Name = "root";
            _rootNodes.Clear();
            _rootNodes.Add(clone);
        }
        else
        {
            int index = node.Parent.Children.IndexOf(node);
            node.Parent.Children.Insert(index + 1, clone);
        }

        NormalizeLevels();
        RefreshVisibleNodes();
        ToolSummaryTextBlock.Text = "节点已复制。";
    }

    private void RemoveNode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is not JsonEditorNode node)
        {
            return;
        }

        if (node.Parent is null)
        {
            _rootNodes.Clear();
            RefreshVisibleNodes();
            ToolSummaryTextBlock.Text = "根节点已清空。";
            return;
        }

        node.Parent.Children.Remove(node);
        RefreshVisibleNodes();
        ToolSummaryTextBlock.Text = "节点已删除。";
    }

    private void ExpandAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        foreach (JsonEditorNode node in _rootNodes)
        {
            SetExpanded(node, true);
        }

        RefreshVisibleNodes();
    }

    private void CollapseAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        foreach (JsonEditorNode node in _rootNodes)
        {
            SetExpanded(node, false);
        }

        RefreshVisibleNodes();
    }

    private void ToggleNode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is not JsonEditorNode node || node.Children.Count == 0)
        {
            return;
        }

        node.IsExpanded = !node.IsExpanded;
        RefreshVisibleNodes();
    }

    private void JsonNodeListBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        foreach (JsonEditorNode node in selectionChangedEventArgs.RemovedItems.OfType<JsonEditorNode>())
        {
            node.IsSelected = false;
        }

        foreach (JsonEditorNode node in selectionChangedEventArgs.AddedItems.OfType<JsonEditorNode>())
        {
            node.IsSelected = true;
        }
    }

    private void JsonValueTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs keyboardFocusChangedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is JsonEditorNode node)
        {
            _editingValues[node] = node.Value;
            _editingTypes[node] = node.Type;
        }
    }

    private void JsonNodeChanged(object sender, RoutedEventArgs routedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is not JsonEditorNode node || node.CanHaveChildren)
        {
            return;
        }

        if (!_editingValues.TryGetValue(node, out string? originalValue) || string.Equals(originalValue, node.Value, StringComparison.Ordinal))
        {
            _editingValues.Remove(node);
            _editingTypes.Remove(node);
            return;
        }

        _editingValues.Remove(node);
        string originalType = _editingTypes.TryGetValue(node, out string? cachedType) ? cachedType : node.Type;
        _editingTypes.Remove(node);

        string inferredType = InferValueType(node.Value, originalType);
        if (node.Type != inferredType)
        {
            node.Type = inferredType;
            ToolSummaryTextBlock.Text = $"已根据输入值自动识别为 {FormatTypeName(inferredType)}。";
        }
    }

    private void JsonValueTextBox_KeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.Enter)
        {
            return;
        }

        JsonNodeChanged(sender, new RoutedEventArgs());
        keyEventArgs.Handled = true;
    }

    private void JsonTypeDropDownClosed(object sender, EventArgs eventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is not JsonEditorNode node)
        {
            return;
        }

        if (node.Type == "null")
        {
            node.Value = string.Empty;
        }

        if (node.CanHaveChildren)
        {
            node.Value = string.Empty;
            node.IsExpanded = true;
        }

        RefreshVisibleNodes();
    }

    private void InitializeDefaultTree()
    {
        if (_rootNodes.Count > 0)
        {
            return;
        }

        JsonInputTextBox.Text = "{\r\n  \"name\": \"JsonLa\",\r\n  \"url\": \"https://json.la\",\r\n  \"page\": 88,\r\n  \"isNonProfit\": true,\r\n  \"links\": [\r\n    {\r\n      \"name\": \"SunnyNet\",\r\n      \"url\": \"https://github.com/qtgolang/SunnyNetTools\"\r\n    }\r\n  ]\r\n}";
        ParseToTree_Click(this, new RoutedEventArgs());
    }

    private JsonEditorNode CreateRootObject()
    {
        JsonEditorNode root = new("root", "object") { IsExpanded = true };
        _rootNodes.Add(root);
        RefreshVisibleNodes();
        return root;
    }

    private void AddChildNode(JsonEditorNode parent)
    {
        if (!parent.CanHaveChildren)
        {
            parent.Type = "object";
            parent.Value = string.Empty;
        }

        string name = parent.Type == "array" ? $"[{parent.Children.Count}]" : CreateNextName(parent);
        JsonEditorNode child = new(name, "string", string.Empty)
        {
            Parent = parent,
            Level = parent.Level + 1
        };
        parent.Children.Add(child);
        parent.IsExpanded = true;
        NormalizeLevels();
        RefreshVisibleNodes();
        ToolSummaryTextBlock.Text = "已新增节点，可直接编辑键名和值。";
    }

    private static string CreateNextName(JsonEditorNode parent)
    {
        int index = parent.Children.Count + 1;
        string name = $"key{index}";
        while (parent.Children.Any(child => child.Name == name))
        {
            index++;
            name = $"key{index}";
        }

        return name;
    }

    private void RefreshVisibleNodes()
    {
        if (_isRefreshingNodes)
        {
            return;
        }

        _isRefreshingNodes = true;
        try
        {
        NormalizeLevels();
        _visibleNodes.Clear();
        foreach (JsonEditorNode node in _rootNodes)
        {
            AddVisibleNode(node);
        }
        }
        finally
        {
            _isRefreshingNodes = false;
        }
    }

    private void AddVisibleNode(JsonEditorNode node)
    {
        node.Indent = Math.Max(0, node.Level * 18);
        node.CanToggle = node.Children.Count > 0;
        _visibleNodes.Add(node);
        if (!node.IsExpanded)
        {
            return;
        }

        foreach (JsonEditorNode child in node.Children)
        {
            AddVisibleNode(child);
        }
    }

    private string BuildTreeJson(bool indented)
    {
        JsonEditorNode? root = _rootNodes.FirstOrDefault();
        if (root is null)
        {
            return string.Empty;
        }

        object? value = BuildJsonValue(root);
        return JsonSerializer.Serialize(value, CreateJsonOptions(indented));
    }

    private static object? BuildJsonValue(JsonEditorNode node)
    {
        return node.Type switch
        {
            "object" => node.Children
                .Where(static child => !string.IsNullOrWhiteSpace(child.Name))
                .GroupBy(static child => child.Name)
                .ToDictionary(static group => group.Key, static group => BuildJsonValue(group.Last())),
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

    private static string InferValueType(string? value, string originalType)
    {
        string text = value?.Trim() ?? string.Empty;
        if (originalType == "number" && IsJsonNumber(text))
        {
            return "number";
        }

        if (originalType == "bool" && (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)))
        {
            return "bool";
        }

        if (originalType == "null" && string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
        {
            return "null";
        }

        if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
        {
            return "null";
        }

        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "bool";
        }

        return "string";
    }

    private static bool IsJsonNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || double.IsNaN(number)
            || double.IsInfinity(number))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Number;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatTypeName(string type)
    {
        return type switch
        {
            "number" => "数字",
            "bool" => "布尔值",
            "null" => "空值",
            _ => "字符串"
        };
    }

    private static JsonEditorNode CreateNode(string name, JsonElement element, JsonEditorNode? parent, int level)
    {
        JsonEditorNode node = element.ValueKind switch
        {
            JsonValueKind.Object => new JsonEditorNode(name, "object"),
            JsonValueKind.Array => new JsonEditorNode(name, "array"),
            JsonValueKind.Number => new JsonEditorNode(name, "number", element.GetRawText()),
            JsonValueKind.True => new JsonEditorNode(name, "bool", "true"),
            JsonValueKind.False => new JsonEditorNode(name, "bool", "false"),
            JsonValueKind.Null => new JsonEditorNode(name, "null"),
            JsonValueKind.String => new JsonEditorNode(name, "string", element.GetString() ?? string.Empty),
            _ => new JsonEditorNode(name, "string", element.GetRawText())
        };

        node.Parent = parent;
        node.Level = level;
        node.Indent = Math.Max(0, level * 18);
        node.IsExpanded = level < 2;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                node.Children.Add(CreateNode(property.Name, property.Value, node, level + 1));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement childElement in element.EnumerateArray())
            {
                node.Children.Add(CreateNode($"[{index}]", childElement, node, level + 1));
                index++;
            }
        }

        node.CanToggle = node.Children.Count > 0;
        return node;
    }

    private static void SetExpanded(JsonEditorNode node, bool expanded)
    {
        node.IsExpanded = expanded;
        foreach (JsonEditorNode child in node.Children)
        {
            SetExpanded(child, expanded);
        }
    }

    private void NormalizeLevels()
    {
        foreach (JsonEditorNode node in _rootNodes)
        {
            NormalizeLevel(node, null, 0);
        }
    }

    private static void NormalizeLevel(JsonEditorNode node, JsonEditorNode? parent, int level)
    {
        node.Parent = parent;
        node.Level = level;
        node.Indent = Math.Max(0, level * 18);
        node.CanToggle = node.Children.Count > 0;
        for (int index = 0; index < node.Children.Count; index++)
        {
            if (node.Type == "array")
            {
                node.Children[index].Name = $"[{index}]";
            }

            NormalizeLevel(node.Children[index], node, level + 1);
        }

        node.CanToggle = node.Children.Count > 0;
    }

    private static string FormatJson(string? text, bool indented)
    {
        using JsonDocument document = JsonDocument.Parse(text ?? string.Empty);
        return JsonSerializer.Serialize(document.RootElement, CreateJsonOptions(indented));
    }

    private static JsonSerializerOptions CreateJsonOptions(bool indented)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
    }

    private void ShowJsonError(string title, Exception exception)
    {
        JsonParseStatusTextBlock.Text = $"{title}：{exception.Message}";
        ToolSummaryTextBlock.Text = title;
    }
}
