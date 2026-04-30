using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SunnyNet.Wpf.Services;

namespace SunnyNet.Wpf.Controls;

public partial class JsonTreeViewControl : UserControl
{
    private const int AutoExpandChildLimit = 200;
    private const int ChildBatchSize = 400;
    private const int MaxExpandAllNodes = 2000;
    private const int MaxPreviewValueLength = 220;

    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(nameof(JsonText), typeof(string), typeof(JsonTreeViewControl), new PropertyMetadata("", OnJsonChanged));

    private static readonly Brush KeyBrush = CreateBrush(0x00, 0x00, 0x00);
    private static readonly Brush StringBrush = CreateBrush(0x0F, 0x7B, 0x63);
    private static readonly Brush NumberBrush = CreateBrush(0xD9, 0x2D, 0x20);
    private static readonly Brush BoolBrush = CreateBrush(0x1E, 0x50, 0xC8);
    private static readonly Brush NullBrush = CreateBrush(0x8C, 0x8C, 0x8C);
    private static readonly Brush SeparatorBrush = CreateBrush(0xC1, 0xCB, 0xD8);
    private static readonly Brush MutedBrush = CreateBrush(0x6B, 0x7C, 0x93);
    private static readonly object LazyPlaceholder = new();
    private JsonDocument? _document;
    private bool _isJson;
    private bool _renderPending;

    private sealed class JsonNodeInfo
    {
        public JsonNodeInfo(string key, JsonElement element, string path)
        {
            Key = key;
            Element = element;
            Path = path;
        }

        public string Key { get; }

        public JsonElement Element { get; }

        public string Path { get; }

        public bool ChildrenInitialized { get; set; }

        public bool AllChildrenLoaded { get; set; }

        public int LoadedChildrenCount { get; set; }

        public string Value => CopyValue(Element);

        public bool CanHaveChildren => Element.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
    }

    private sealed class JsonLoadMoreInfo
    {
    }

    public JsonTreeViewControl()
    {
        InitializeComponent();
        Loaded += JsonTreeViewControl_Loaded;
        Unloaded += (_, _) => DisposeDocument();
        IsVisibleChanged += JsonTreeViewControl_IsVisibleChanged;
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
            control.QueueRenderJson();
        }
    }

    private void JsonTreeViewControl_Loaded(object sender, RoutedEventArgs routedEventArgs)
    {
        QueueRenderJson();
    }

    private void JsonTreeViewControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsReadyToRender() && _renderPending)
        {
            RenderJson();
        }
    }

    private void QueueRenderJson()
    {
        if (!IsReadyToRender())
        {
            _renderPending = true;
            return;
        }

        RenderJson();
    }

    private bool IsReadyToRender()
    {
        return IsLoaded && IsVisible;
    }

    private void RenderJson()
    {
        _renderPending = false;
        JsonTree.Items.Clear();
        DisposeDocument();
        if (string.IsNullOrWhiteSpace(JsonText))
        {
            _isJson = false;
            UpdateToolbarState("无 JSON 内容");
            ShowFallback();
            return;
        }

        try
        {
            _document = JsonDocument.Parse(JsonText);
            TreeViewItem root = CreateNode("root", _document.RootElement, "$");
            JsonTree.Items.Add(root);
            _isJson = true;
            int rootChildCount = GetDirectChildCount(_document.RootElement);
            if (rootChildCount <= AutoExpandChildLimit)
            {
                LoadChildren(root);
                root.IsExpanded = true;
            }

            UpdateToolbarState(_document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => $"对象 · {rootChildCount:N0} 项 · 懒加载",
                JsonValueKind.Array => $"数组 · {rootChildCount:N0} 项 · 懒加载",
                _ => "JSON 值 · 懒加载"
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
        int loadedCount = 0;
        foreach (TreeViewItem item in JsonTree.Items.OfType<TreeViewItem>())
        {
            SetExpanded(item, true, ref loadedCount);
            if (loadedCount >= MaxExpandAllNodes)
            {
                UpdateToolbarState($"已展开前 {MaxExpandAllNodes:N0} 个节点，数据较大请按需展开");
                break;
            }
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

    private void JsonTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> routedEventArgs)
    {
        if (routedEventArgs.NewValue is not TreeViewItem { Tag: JsonLoadMoreInfo } loadMoreItem)
        {
            return;
        }

        if (ItemsControl.ItemsControlFromItemContainer(loadMoreItem) is TreeViewItem parent)
        {
            LoadChildren(parent);
            parent.IsExpanded = true;
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

    private static void JsonNode_Expanded(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!ReferenceEquals(sender, routedEventArgs.OriginalSource) || sender is not TreeViewItem item)
        {
            return;
        }

        LoadChildren(item);
    }

    private void SetExpanded(TreeViewItem item, bool isExpanded)
    {
        item.IsExpanded = isExpanded;
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
        {
            SetExpanded(child, isExpanded);
        }
    }

    private void SetExpanded(TreeViewItem item, bool isExpanded, ref int loadedCount)
    {
        if (loadedCount >= MaxExpandAllNodes)
        {
            return;
        }

        if (isExpanded)
        {
            LoadChildren(item);
            loadedCount++;
        }

        item.IsExpanded = isExpanded;
        foreach (TreeViewItem child in item.Items.OfType<TreeViewItem>())
        {
            SetExpanded(child, isExpanded, ref loadedCount);
        }
    }

    private static TreeViewItem CreateNode(string name, JsonElement element, string path)
    {
        JsonNodeInfo info = new(name, element, path);
        TreeViewItem item = new()
        {
            Header = CreateHeader(name, element),
            Tag = info
        };
        item.Expanded += JsonNode_Expanded;

        if (info.CanHaveChildren && GetDirectChildCount(element) > 0)
        {
            item.Items.Add(LazyPlaceholder);
        }

        return item;
    }

    private static void LoadChildren(TreeViewItem item)
    {
        if (item.Tag is not JsonNodeInfo info || !info.CanHaveChildren || info.AllChildrenLoaded)
        {
            return;
        }

        if (!info.ChildrenInitialized)
        {
            item.Items.Clear();
            info.ChildrenInitialized = true;
            info.LoadedChildrenCount = 0;
        }
        else
        {
            RemoveLoadMoreItem(item);
        }

        int start = info.LoadedChildrenCount;
        int end = Math.Min(start + ChildBatchSize, GetDirectChildCount(info.Element));
        switch (info.Element.ValueKind)
        {
            case JsonValueKind.Object:
                int propertyIndex = 0;
                foreach (JsonProperty property in info.Element.EnumerateObject())
                {
                    if (propertyIndex >= start && propertyIndex < end)
                    {
                        item.Items.Add(CreateNode(property.Name, property.Value, AppendObjectPath(info.Path, property.Name)));
                    }

                    propertyIndex++;
                    if (propertyIndex >= end)
                    {
                        break;
                    }
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement child in info.Element.EnumerateArray())
                {
                    if (index >= start && index < end)
                    {
                        item.Items.Add(CreateNode($"[{index}]", child, $"{info.Path}[{index}]"));
                    }

                    index++;
                    if (index >= end)
                    {
                        break;
                    }
                }
                break;
        }

        info.LoadedChildrenCount = end;
        info.AllChildrenLoaded = end >= GetDirectChildCount(info.Element);
        if (!info.AllChildrenLoaded)
        {
            item.Items.Add(CreateLoadMoreItem(info));
        }
    }

    private static void RemoveLoadMoreItem(TreeViewItem item)
    {
        for (int index = item.Items.Count - 1; index >= 0; index--)
        {
            if (item.Items[index] is TreeViewItem { Tag: JsonLoadMoreInfo })
            {
                item.Items.RemoveAt(index);
            }
        }
    }

    private static TreeViewItem CreateLoadMoreItem(JsonNodeInfo parent)
    {
        int total = GetDirectChildCount(parent.Element);
        TextBlock textBlock = new()
        {
            Text = $"加载更多... {parent.LoadedChildrenCount:N0}/{total:N0}",
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 12,
            Margin = new Thickness(4, 2, 0, 2)
        };

        return new TreeViewItem
        {
            Header = textBlock,
            Tag = new JsonLoadMoreInfo()
        };
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
            JsonValueKind.Object => $"{{ {GetDirectChildCount(element):N0} fields }}",
            JsonValueKind.Array => $"[ {GetDirectChildCount(element):N0} items ]",
            JsonValueKind.String => $"\"{TrimPreview(element.GetString() ?? "")}\"",
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
            ClipboardService.SetText(text);
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

    private static int GetDirectChildCount(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Count(),
            JsonValueKind.Array => element.GetArrayLength(),
            _ => 0
        };
    }

    private static string TrimPreview(string text)
    {
        if (text.Length <= MaxPreviewValueLength)
        {
            return text;
        }

        return text[..MaxPreviewValueLength] + "…";
    }

    private void DisposeDocument()
    {
        _document?.Dispose();
        _document = null;
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
