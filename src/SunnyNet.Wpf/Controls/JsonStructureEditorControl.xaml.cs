using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Controls;

public partial class JsonStructureEditorControl : UserControl
{
    public static readonly DependencyProperty AllowCreateNodesProperty =
        DependencyProperty.Register(nameof(AllowCreateNodes), typeof(bool), typeof(JsonStructureEditorControl), new PropertyMetadata(true));

    private readonly ObservableCollection<JsonEditorNode> _rootNodes = new();
    private readonly RangeObservableCollection<JsonEditorNode> _visibleNodes = new();
    private readonly Dictionary<JsonEditorNode, string> _editingValues = new();
    private readonly Dictionary<JsonEditorNode, string> _editingTypes = new();
    private bool _isRefreshingNodes;
    private Point _dragStartPoint;
    private JsonEditorNode? _dragSourceNode;
    private JsonEditorNode? _dropTargetNode;

    public JsonStructureEditorControl()
    {
        InitializeComponent();
        JsonNodeListBox.ItemsSource = _visibleNodes;
    }

    public bool HasJsonContent => _rootNodes.Count > 0;

    public bool AllowCreateNodes
    {
        get => (bool)GetValue(AllowCreateNodesProperty);
        set => SetValue(AllowCreateNodesProperty, value);
    }

    public void Clear()
    {
        _rootNodes.Clear();
        RefreshVisibleNodes();
    }

    public void InitializeObject()
    {
        _rootNodes.Clear();
        _rootNodes.Add(new JsonEditorNode("root", "object") { IsExpanded = true });
        RefreshVisibleNodes();
    }

    public void LoadJson(string? text)
    {
        using JsonDocument document = JsonDocument.Parse(text ?? string.Empty);
        _rootNodes.Clear();
        JsonEditorNode root = CreateNode("root", document.RootElement, null, 0);
        root.IsExpanded = true;
        _rootNodes.Add(root);
        RefreshVisibleNodes();
    }

    public bool TryLoadJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Clear();
            return true;
        }

        try
        {
            LoadJson(text);
            return true;
        }
        catch
        {
            Clear();
            return false;
        }
    }

    public string BuildJson(bool indented = true)
    {
        JsonEditorNode? root = _rootNodes.FirstOrDefault();
        if (root is null)
        {
            return string.Empty;
        }

        object? value = BuildJsonValue(root);
        return JsonSerializer.Serialize(value, CreateJsonOptions(indented));
    }

    private void AddRootChild_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!AllowCreateNodes)
        {
            return;
        }

        JsonEditorNode parent = _rootNodes.FirstOrDefault() ?? CreateRootObject();
        AddChildNode(parent);
    }

    private void AddChild_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!AllowCreateNodes)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is JsonEditorNode node)
        {
            AddChildNode(node);
        }
    }

    private void DuplicateNode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!AllowCreateNodes)
        {
            return;
        }

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
    }

    private void JsonContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        AddRootChildMenuItem.IsEnabled = AllowCreateNodes;
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
            return;
        }

        node.Parent.Children.Remove(node);
        RefreshVisibleNodes();
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

        ToggleVisibleNode(node);
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
        }
    }

    private void JsonValueTextBox_KeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.Enter)
        {
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is JsonEditorNode { IsStringValue: true } && Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        JsonNodeChanged(sender, new RoutedEventArgs());
        keyEventArgs.Handled = true;
    }

    private void JsonNodeRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        _dragStartPoint = mouseButtonEventArgs.GetPosition(JsonNodeListBox);
        _dragSourceNode = (sender as FrameworkElement)?.DataContext as JsonEditorNode;
    }

    private void JsonNodeRow_PreviewMouseMove(object sender, MouseEventArgs mouseEventArgs)
    {
        if (mouseEventArgs.LeftButton != MouseButtonState.Pressed || _dragSourceNode is null)
        {
            return;
        }

        Point currentPoint = mouseEventArgs.GetPosition(JsonNodeListBox);
        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (mouseEventArgs.OriginalSource is DependencyObject source && IsTextInputElement(source))
        {
            return;
        }

        DragDrop.DoDragDrop(JsonNodeListBox, _dragSourceNode, DragDropEffects.Move);
        ClearDropMarker();
        _dragSourceNode = null;
    }

    private void JsonNodeRow_DragOver(object sender, DragEventArgs dragEventArgs)
    {
        JsonEditorNode? sourceNode = dragEventArgs.Data.GetData(typeof(JsonEditorNode)) as JsonEditorNode;
        JsonEditorNode? targetNode = (sender as FrameworkElement)?.DataContext as JsonEditorNode;
        if (!CanDropSibling(sourceNode, targetNode))
        {
            dragEventArgs.Effects = DragDropEffects.None;
            ClearDropMarker();
            dragEventArgs.Handled = true;
            return;
        }

        bool dropAfter = IsDropAfter(sender, dragEventArgs);
        SetDropMarker(targetNode!, dropAfter);
        dragEventArgs.Effects = DragDropEffects.Move;
        dragEventArgs.Handled = true;
    }

    private void JsonNodeRow_DragLeave(object sender, DragEventArgs dragEventArgs)
    {
        if (ReferenceEquals((sender as FrameworkElement)?.DataContext, _dropTargetNode))
        {
            ClearDropMarker();
        }
    }

    private void JsonNodeRow_Drop(object sender, DragEventArgs dragEventArgs)
    {
        JsonEditorNode? sourceNode = dragEventArgs.Data.GetData(typeof(JsonEditorNode)) as JsonEditorNode;
        JsonEditorNode? targetNode = (sender as FrameworkElement)?.DataContext as JsonEditorNode;
        bool dropAfter = IsDropAfter(sender, dragEventArgs);
        ClearDropMarker();

        if (!CanDropSibling(sourceNode, targetNode))
        {
            dragEventArgs.Handled = true;
            return;
        }

        MoveSiblingNode(sourceNode!, targetNode!, dropAfter);
        dragEventArgs.Handled = true;
    }

    private void JsonNodeListBox_DragOver(object sender, DragEventArgs dragEventArgs)
    {
        JsonEditorNode? sourceNode = dragEventArgs.Data.GetData(typeof(JsonEditorNode)) as JsonEditorNode;
        (JsonEditorNode? targetNode, bool dropAfter) = FindDropTarget(dragEventArgs.GetPosition(JsonNodeListBox));
        if (!CanDropSibling(sourceNode, targetNode))
        {
            dragEventArgs.Effects = DragDropEffects.None;
            ClearDropMarker();
            dragEventArgs.Handled = true;
            return;
        }

        SetDropMarker(targetNode!, dropAfter);
        dragEventArgs.Effects = DragDropEffects.Move;
        dragEventArgs.Handled = true;
    }

    private void JsonNodeListBox_DragLeave(object sender, DragEventArgs dragEventArgs)
    {
        if (!JsonNodeListBox.IsMouseOver)
        {
            ClearDropMarker();
        }
    }

    private void JsonNodeListBox_Drop(object sender, DragEventArgs dragEventArgs)
    {
        JsonEditorNode? sourceNode = dragEventArgs.Data.GetData(typeof(JsonEditorNode)) as JsonEditorNode;
        (JsonEditorNode? targetNode, bool dropAfter) = FindDropTarget(dragEventArgs.GetPosition(JsonNodeListBox));
        ClearDropMarker();

        if (CanDropSibling(sourceNode, targetNode))
        {
            MoveSiblingNode(sourceNode!, targetNode!, dropAfter);
        }

        dragEventArgs.Handled = true;
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
            List<JsonEditorNode> visibleNodes = new();
            foreach (JsonEditorNode node in _rootNodes)
            {
                AddVisibleNode(node, visibleNodes);
            }

            _visibleNodes.ReplaceAll(visibleNodes);
        }
        finally
        {
            _isRefreshingNodes = false;
        }
    }

    private void ToggleVisibleNode(JsonEditorNode node)
    {
        int index = _visibleNodes.IndexOf(node);
        if (index < 0)
        {
            node.IsExpanded = !node.IsExpanded;
            RefreshVisibleNodes();
            return;
        }

        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            int removeCount = CountVisibleDescendants(index + 1, node.Level);
            if (removeCount > 0)
            {
                _visibleNodes.RemoveRange(index + 1, removeCount);
            }

            return;
        }

        node.IsExpanded = true;
        List<JsonEditorNode> descendants = new();
        foreach (JsonEditorNode child in node.Children)
        {
            AddVisibleNode(child, descendants);
        }

        if (descendants.Count > 0)
        {
            _visibleNodes.InsertRange(index + 1, descendants);
        }
    }

    private int CountVisibleDescendants(int startIndex, int parentLevel)
    {
        int count = 0;
        for (int index = startIndex; index < _visibleNodes.Count; index++)
        {
            if (_visibleNodes[index].Level <= parentLevel)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static void AddVisibleNode(JsonEditorNode node, ICollection<JsonEditorNode> visibleNodes)
    {
        node.Indent = Math.Max(0, node.Level * 18);
        node.CanToggle = node.Children.Count > 0;
        visibleNodes.Add(node);
        if (!node.IsExpanded)
        {
            return;
        }

        foreach (JsonEditorNode child in node.Children)
        {
            AddVisibleNode(child, visibleNodes);
        }
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

    private static bool CanDropSibling(JsonEditorNode? sourceNode, JsonEditorNode? targetNode)
    {
        if (sourceNode is null || targetNode is null || ReferenceEquals(sourceNode, targetNode))
        {
            return false;
        }

        return ReferenceEquals(sourceNode.Parent, targetNode.Parent);
    }

    private static bool IsDropAfter(object sender, DragEventArgs dragEventArgs)
    {
        if (sender is not FrameworkElement element || element.ActualHeight <= 0)
        {
            return false;
        }

        return dragEventArgs.GetPosition(element).Y >= element.ActualHeight / 2;
    }

    private (JsonEditorNode? TargetNode, bool DropAfter) FindDropTarget(Point listPoint)
    {
        JsonEditorNode? nearestNode = null;
        bool dropAfter = false;
        double nearestDistance = double.MaxValue;

        for (int index = 0; index < JsonNodeListBox.Items.Count; index++)
        {
            if (JsonNodeListBox.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item
                || JsonNodeListBox.Items[index] is not JsonEditorNode node)
            {
                continue;
            }

            Point itemPoint = item.TranslatePoint(new Point(0, 0), JsonNodeListBox);
            double top = itemPoint.Y;
            double bottom = top + item.ActualHeight;
            if (listPoint.Y >= top && listPoint.Y <= bottom)
            {
                return (node, listPoint.Y >= top + item.ActualHeight / 2);
            }

            double distanceToTop = Math.Abs(listPoint.Y - top);
            if (distanceToTop < nearestDistance)
            {
                nearestDistance = distanceToTop;
                nearestNode = node;
                dropAfter = false;
            }

            double distanceToBottom = Math.Abs(listPoint.Y - bottom);
            if (distanceToBottom < nearestDistance)
            {
                nearestDistance = distanceToBottom;
                nearestNode = node;
                dropAfter = true;
            }
        }

        return (nearestNode, dropAfter);
    }

    private void SetDropMarker(JsonEditorNode targetNode, bool dropAfter)
    {
        if (!ReferenceEquals(_dropTargetNode, targetNode))
        {
            ClearDropMarker();
        }

        _dropTargetNode = targetNode;
        targetNode.ShowDropBefore = !dropAfter;
        targetNode.ShowDropAfter = dropAfter;
    }

    private void ClearDropMarker()
    {
        if (_dropTargetNode is not null)
        {
            _dropTargetNode.ShowDropBefore = false;
            _dropTargetNode.ShowDropAfter = false;
        }

        _dropTargetNode = null;
    }

    private void MoveSiblingNode(JsonEditorNode sourceNode, JsonEditorNode targetNode, bool dropAfter)
    {
        ObservableCollection<JsonEditorNode> siblings = sourceNode.Parent?.Children ?? _rootNodes;
        int sourceIndex = siblings.IndexOf(sourceNode);
        int targetIndex = siblings.IndexOf(targetNode);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        int insertIndex = dropAfter ? targetIndex + 1 : targetIndex;
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (sourceIndex == insertIndex)
        {
            return;
        }

        siblings.RemoveAt(sourceIndex);
        siblings.Insert(Math.Clamp(insertIndex, 0, siblings.Count), sourceNode);
        NormalizeLevels();
        RefreshVisibleNodes();
    }

    private static bool IsTextInputElement(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is TextBox or ComboBox)
            {
                return true;
            }

            current = LogicalTreeHelper.GetParent(current) ?? System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return false;
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
        node.IsExpanded = ShouldAutoExpandNode(level, node.Children.Count);
        return node;
    }

    private static bool ShouldAutoExpandNode(int level, int childCount)
    {
        if (childCount == 0)
        {
            return false;
        }

        if (level == 0)
        {
            return true;
        }

        return level == 1 && childCount <= 50;
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

    private static JsonSerializerOptions CreateJsonOptions(bool indented)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
    }

    private sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotifications;

        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            _suppressNotifications = true;
            try
            {
                Items.Clear();
                foreach (T item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _suppressNotifications = false;
            }

            RaiseReset();
        }

        public void InsertRange(int index, IEnumerable<T> items)
        {
            List<T> itemList = items as List<T> ?? items.ToList();
            if (itemList.Count == 0)
            {
                return;
            }

            CheckReentrancy();
            _suppressNotifications = true;
            try
            {
                for (int offset = 0; offset < itemList.Count; offset++)
                {
                    Items.Insert(index + offset, itemList[offset]);
                }
            }
            finally
            {
                _suppressNotifications = false;
            }

            RaiseReset();
        }

        public void RemoveRange(int index, int count)
        {
            if (count <= 0)
            {
                return;
            }

            CheckReentrancy();
            _suppressNotifications = true;
            try
            {
                for (int offset = 0; offset < count; offset++)
                {
                    Items.RemoveAt(index);
                }
            }
            finally
            {
                _suppressNotifications = false;
            }

            RaiseReset();
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotifications)
            {
                base.OnCollectionChanged(e);
            }
        }

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_suppressNotifications)
            {
                base.OnPropertyChanged(e);
            }
        }

        private void RaiseReset()
        {
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
