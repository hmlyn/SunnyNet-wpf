using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Controls;

public partial class PacketMessagesControl : UserControl
{
    public static readonly DependencyProperty EntriesProperty =
        DependencyProperty.Register(nameof(Entries), typeof(IEnumerable), typeof(PacketMessagesControl), new PropertyMetadata(null, OnEntriesChanged));

    public static readonly DependencyProperty TheologyProperty =
        DependencyProperty.Register(nameof(Theology), typeof(int), typeof(PacketMessagesControl), new PropertyMetadata(0, OnTheologyChanged));

    public static readonly DependencyProperty SelectedEntryProperty =
        DependencyProperty.Register(nameof(SelectedEntry), typeof(SocketEntry), typeof(PacketMessagesControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedEntryChanged));

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(nameof(DisplayMode), typeof(string), typeof(PacketMessagesControl), new PropertyMetadata(DisplayModeFlow, OnDisplayModeChanged));

    public static readonly DependencyProperty ProtocolNameProperty =
        DependencyProperty.Register(nameof(ProtocolName), typeof(string), typeof(PacketMessagesControl), new PropertyMetadata("TCP", OnProtocolNameChanged));

    private const string DisplayModeFlow = "Flow";
    private const string DisplayModeInspector = "Inspector";
    private const int MaxInlineTextChars = 131_072;
    private INotifyCollectionChanged? _trackedCollection;
    private CollectionViewSource? _entriesViewSource;
    private readonly Dictionary<string, PacketPayloadSnapshot> _payloadCache = new(StringComparer.Ordinal);
    private bool _syncingSelectedEntry;
    private int _payloadVersion;
    private SocketEntry? _currentEntry;
    private PacketPayloadSnapshot? _currentSnapshot;

    public PacketMessagesControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyDisplayModeVisualState();
            RebuildEntriesView();
            RefreshState();
            ApplyExternalSelection(SelectedEntry);
        };
    }

    public IEnumerable? Entries
    {
        get => (IEnumerable?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    public int Theology
    {
        get => (int)GetValue(TheologyProperty);
        set => SetValue(TheologyProperty, value);
    }

    public SocketEntry? SelectedEntry
    {
        get => (SocketEntry?)GetValue(SelectedEntryProperty);
        set => SetValue(SelectedEntryProperty, value);
    }

    public string DisplayMode
    {
        get => (string)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public string ProtocolName
    {
        get => (string)GetValue(ProtocolNameProperty);
        set => SetValue(ProtocolNameProperty, value);
    }

    private bool IsInspectorMode => string.Equals(DisplayMode, DisplayModeInspector, StringComparison.OrdinalIgnoreCase);

    private bool IsFlowMode => string.Equals(DisplayMode, DisplayModeFlow, StringComparison.OrdinalIgnoreCase);

    private static void OnEntriesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not PacketMessagesControl control)
        {
            return;
        }

        if (control._trackedCollection is not null)
        {
            control._trackedCollection.CollectionChanged -= control.Entries_CollectionChanged;
        }

        control._trackedCollection = args.NewValue as INotifyCollectionChanged;
        if (control._trackedCollection is not null)
        {
            control._trackedCollection.CollectionChanged += control.Entries_CollectionChanged;
        }

        control._payloadCache.Clear();
        control.RebuildEntriesView();
        control.RefreshState();
    }

    private static void OnTheologyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is PacketMessagesControl control)
        {
            control._payloadCache.Clear();
            control.ApplyExternalSelection(control.SelectedEntry);
        }
    }

    private static void OnSelectedEntryChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is PacketMessagesControl control)
        {
            control.ApplyExternalSelection(args.NewValue as SocketEntry);
        }
    }

    private static void OnDisplayModeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is PacketMessagesControl control)
        {
            control.ApplyDisplayModeVisualState();
            control.RebuildEntriesView();
            control.RefreshState();
            control.ApplyExternalSelection(control.SelectedEntry);
        }
    }

    private static void OnProtocolNameChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is PacketMessagesControl control)
        {
            control.RefreshProtocolText();
        }
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            _payloadCache.Clear();
        }

        RefreshState();
    }

    private void ApplyDisplayModeVisualState()
    {
        if (FlowPanel is null || InspectorPanel is null)
        {
            return;
        }

        FlowPanel.Visibility = IsInspectorMode ? Visibility.Collapsed : Visibility.Visible;
        InspectorPanel.Visibility = IsFlowMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RebuildEntriesView()
    {
        if (PacketsList is null)
        {
            return;
        }

        if (IsInspectorMode || Entries is null)
        {
            PacketsList.ItemsSource = null;
            _entriesViewSource = null;
            return;
        }

        _entriesViewSource = new CollectionViewSource { Source = Entries };
        PacketsList.ItemsSource = _entriesViewSource.View;
    }

    private void RefreshState()
    {
        if (TotalPacketsTextBlock is null)
        {
            return;
        }

        int total = 0;
        int upstream = 0;
        int downstream = 0;
        foreach (SocketEntry entry in EnumerateEntries())
        {
            total++;
            if (entry.Icon == "上行")
            {
                upstream++;
            }
            else if (entry.Icon == "下行")
            {
                downstream++;
            }
        }

        TotalPacketsTextBlock.Text = $"{total:N0} 包";
        UpstreamPacketsTextBlock.Text = $"上行 {upstream:N0}";
        DownstreamPacketsTextBlock.Text = $"下行 {downstream:N0}";
        PacketsEmptyPanel.Visibility = total == 0 && !IsInspectorMode ? Visibility.Visible : Visibility.Collapsed;
        PacketsEmptyTitleTextBlock.Text = $"暂无 {ProtocolName} 数据包";
        RefreshProtocolText();
    }

    private void RefreshProtocolText()
    {
        if (InspectorHintTextBlock is null)
        {
            return;
        }

        InspectorHintTextBlock.Text = $"左侧选择 {ProtocolName} 数据包后，这里异步载入正文、JSON 和 HEX。";
    }

    private IEnumerable<SocketEntry> EnumerateEntries()
    {
        if (Entries is null)
        {
            yield break;
        }

        foreach (object? item in Entries)
        {
            if (item is SocketEntry entry)
            {
                yield return entry;
            }
        }
    }

    private async void ApplyExternalSelection(SocketEntry? entry)
    {
        if (_syncingSelectedEntry)
        {
            return;
        }

        if (!IsInspectorMode && PacketsList is not null && !ReferenceEquals(PacketsList.SelectedItem, entry))
        {
            _syncingSelectedEntry = true;
            PacketsList.SelectedItem = entry;
            _syncingSelectedEntry = false;
        }

        if (entry is null)
        {
            if (!IsFlowMode)
            {
                ClearPayload();
            }

            return;
        }

        if (IsFlowMode)
        {
            return;
        }

        await ApplyEntrySelectionAsync(entry);
    }

    private async Task ApplyEntrySelectionAsync(SocketEntry entry)
    {
        int version = ++_payloadVersion;
        ApplyPayloadLoadingState(entry);

        try
        {
            PacketPayloadSnapshot snapshot = await LoadPayloadSnapshotAsync(entry);
            if (version != _payloadVersion || !ReferenceEquals(SelectedEntry, entry))
            {
                return;
            }

            _payloadCache[BuildPayloadCacheKey(entry)] = snapshot;
            ApplyPayload(entry, snapshot);
        }
        catch (Exception exception)
        {
            if (version != _payloadVersion)
            {
                return;
            }

            ApplyPayload(entry, new PacketPayloadSnapshot(
                Array.Empty<byte>(),
                "",
                $"载入 {ProtocolName} 数据包失败：{exception.Message}",
                "",
                false,
                false,
                "载入失败"));
        }
    }

    private async Task<PacketPayloadSnapshot> LoadPayloadSnapshotAsync(SocketEntry entry)
    {
        if (TryGetCachedPayload(entry, out PacketPayloadSnapshot? cached))
        {
            return cached;
        }

        if (!entry.IsTrafficFrame || DataContext is not MainWindowViewModel viewModel || Theology <= 0)
        {
            return BuildPayloadSnapshot(entry, Array.Empty<byte>(), entry.PreviewText, "");
        }

        (byte[] bytes, string text, string json) = await viewModel.LoadSocketPayloadAsync(Theology, entry.Index - 1);
        return BuildPayloadSnapshot(entry, bytes, string.IsNullOrWhiteSpace(text) ? entry.PreviewText : text, json);
    }

    private PacketPayloadSnapshot BuildPayloadSnapshot(SocketEntry entry, byte[] bytes, string text, string jsonCandidate)
    {
        bool hasJson = !string.IsNullOrWhiteSpace(jsonCandidate);
        bool isBinary = LooksBinary(bytes);
        string rawText = text ?? "";
        string displayText;
        if (isBinary)
        {
            displayText = BuildBinaryPreview(entry, bytes, rawText);
        }
        else if (rawText.Length > MaxInlineTextChars)
        {
            displayText = rawText[..MaxInlineTextChars]
                + $"\r\n\r\n已预览前 {MaxInlineTextChars:N0} 字符，完整内容请使用 HEX 视图或右键复制。";
        }
        else
        {
            displayText = rawText;
        }

        string status = bytes.Length > 0
            ? $"已加载 {bytes.Length:N0} Bytes"
            : "仅摘要可用";
        return new PacketPayloadSnapshot(bytes, rawText, displayText, jsonCandidate, hasJson, isBinary, status);
    }

    private void PacketsList_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (_syncingSelectedEntry)
        {
            return;
        }

        SocketEntry? entry = PacketsList.SelectedItem as SocketEntry;
        if (!ReferenceEquals(SelectedEntry, entry))
        {
            _syncingSelectedEntry = true;
            SetCurrentValue(SelectedEntryProperty, entry);
            _syncingSelectedEntry = false;
        }
    }

    private void PacketsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        ListBoxItem? item = FindAncestor<ListBoxItem>(mouseButtonEventArgs.OriginalSource as DependencyObject);
        if (item?.DataContext is SocketEntry entry)
        {
            PacketsList.SelectedItem = entry;
        }
    }

    private void ApplyPayloadLoadingState(SocketEntry entry)
    {
        _currentEntry = entry;
        _currentSnapshot = null;
        InspectorEmptyPanel.Visibility = Visibility.Collapsed;
        PacketPayloadTabs.Visibility = Visibility.Visible;
        PacketPayloadTabs.SelectedItem = PacketRawTab;
        PacketJsonTab.Visibility = Visibility.Collapsed;
        PacketRawViewer.SourceText = "正在读取数据包内容...";
        PacketRawViewer.HighlightMode = "Body";
        PacketJsonViewer.JsonText = "";
        PacketHexViewer.Bytes = Array.Empty<byte>();
        PacketHexViewer.HeaderLength = 0;
        ApplyInspectorMeta(entry, "正在载入正文...");
    }

    private void ApplyPayload(SocketEntry entry, PacketPayloadSnapshot snapshot)
    {
        _currentEntry = entry;
        _currentSnapshot = snapshot;
        InspectorEmptyPanel.Visibility = Visibility.Collapsed;
        PacketPayloadTabs.Visibility = Visibility.Visible;
        PacketRawViewer.SourceText = snapshot.DisplayText;
        PacketRawViewer.HighlightMode = snapshot.HasJson ? "Json" : "Body";
        PacketJsonViewer.JsonText = snapshot.Json;
        PacketJsonTab.Visibility = snapshot.HasJson ? Visibility.Visible : Visibility.Collapsed;
        PacketHexViewer.Bytes = snapshot.Bytes;
        PacketHexViewer.HeaderLength = 0;
        PacketPayloadTabs.SelectedItem = snapshot.HasJson ? PacketJsonTab : PacketRawTab;
        ApplyInspectorMeta(entry, snapshot.StatusText);
    }

    private void ApplyInspectorMeta(SocketEntry entry, string statusText)
    {
        InspectorTitleTextBlock.Text = $"第 {entry.DisplayIndex} 包 · {ProtocolName} · {entry.DirectionLabel}";
        InspectorDirectionTextBlock.Text = entry.DirectionLabel;
        InspectorLengthTextBlock.Text = entry.LengthLabel;
        InspectorHintTextBlock.Text = string.IsNullOrWhiteSpace(entry.Time)
            ? statusText
            : $"{entry.Time} · {statusText}";

        bool upstream = entry.Icon == "上行";
        InspectorDirectionBadge.Background = CreateBrush(upstream ? "#EAF8F1" : "#EEF4FF");
        InspectorDirectionBadge.BorderBrush = CreateBrush(upstream ? "#CBE9D8" : "#D8E7FF");
        InspectorDirectionTextBlock.Foreground = upstream ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("AccentBrush");
    }

    private void ClearPayload()
    {
        _payloadVersion++;
        _currentEntry = null;
        _currentSnapshot = null;
        InspectorTitleTextBlock.Text = "请选择一个数据包";
        InspectorDirectionTextBlock.Text = "方向";
        InspectorLengthTextBlock.Text = "0 B";
        InspectorHintTextBlock.Text = $"左侧选择 {ProtocolName} 数据包后，这里异步载入正文、JSON 和 HEX。";
        PacketPayloadTabs.Visibility = Visibility.Collapsed;
        InspectorEmptyPanel.Visibility = Visibility.Visible;
        PacketRawViewer.SourceText = "";
        PacketJsonViewer.JsonText = "";
        PacketJsonTab.Visibility = Visibility.Collapsed;
        PacketHexViewer.Bytes = Array.Empty<byte>();
        PacketHexViewer.HeaderLength = 0;
    }

    private void PacketContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        bool hasSelection = GetSelectedEntry() is not null;
        CopyPacketSummaryMenuItem.IsEnabled = hasSelection;
        CopyPacketTextMenuItem.IsEnabled = hasSelection;
        CopyPacketHexMenuItem.IsEnabled = hasSelection;
        CopyPacketBase64MenuItem.IsEnabled = hasSelection;
    }

    private void CopyPacketSummary_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (GetSelectedEntry() is not { } entry)
        {
            return;
        }

        Clipboard.SetText($"{entry.CompactIndex}\t{entry.TimeLabel}\t{entry.DirectionLabel}\t{entry.LengthLabel}\t{entry.PreviewText}");
    }

    private async void CopyPacketText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        PacketPayloadSnapshot? snapshot = await GetSelectedPayloadAsync();
        if (snapshot is not null)
        {
            Clipboard.SetText(string.IsNullOrEmpty(snapshot.RawText) ? snapshot.DisplayText : snapshot.RawText);
        }
    }

    private async void CopyPacketHex_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        PacketPayloadSnapshot? snapshot = await GetSelectedPayloadAsync();
        if (snapshot is not null)
        {
            Clipboard.SetText(Convert.ToHexString(snapshot.Bytes));
        }
    }

    private async void CopyPacketBase64_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        PacketPayloadSnapshot? snapshot = await GetSelectedPayloadAsync();
        if (snapshot is not null)
        {
            Clipboard.SetText(Convert.ToBase64String(snapshot.Bytes));
        }
    }

    private async Task<PacketPayloadSnapshot?> GetSelectedPayloadAsync()
    {
        SocketEntry? entry = GetSelectedEntry();
        if (entry is null)
        {
            return null;
        }

        if (_currentSnapshot is not null && ReferenceEquals(_currentEntry, entry))
        {
            return _currentSnapshot;
        }

        PacketPayloadSnapshot snapshot = await LoadPayloadSnapshotAsync(entry);
        _payloadCache[BuildPayloadCacheKey(entry)] = snapshot;
        return snapshot;
    }

    private SocketEntry? GetSelectedEntry()
    {
        return SelectedEntry ?? PacketsList.SelectedItem as SocketEntry;
    }

    private bool TryGetCachedPayload(SocketEntry entry, out PacketPayloadSnapshot snapshot)
    {
        return _payloadCache.TryGetValue(BuildPayloadCacheKey(entry), out snapshot!);
    }

    private string BuildPayloadCacheKey(SocketEntry entry)
    {
        return $"{Theology}:{entry.Index}:{entry.Length}:{entry.Type}:{entry.Icon}";
    }

    private static string BuildBinaryPreview(SocketEntry entry, byte[] bytes, string textPreview)
    {
        StringBuilder builder = new();
        builder.AppendLine($"[{entry.DirectionLabel}] {entry.LengthLabel}");
        builder.AppendLine("检测为二进制数据，建议切换 HEX 视图查看完整内容。");

        string compactText = (textPreview ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(compactText))
        {
            builder.AppendLine();
            builder.AppendLine("文本探测:");
            builder.AppendLine(compactText.Length > 4096 ? compactText[..4096] + "..." : compactText);
        }

        if (bytes.Length > 0)
        {
            builder.AppendLine();
            builder.Append("HEX 视图保留完整字节。");
        }

        return builder.ToString();
    }

    private static bool LooksBinary(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return false;
        }

        int sample = Math.Min(bytes.Length, 512);
        int controls = 0;
        for (int index = 0; index < sample; index++)
        {
            byte current = bytes[index];
            if (current == 0)
            {
                return true;
            }

            if (current < 0x09 || current is > 0x0D and < 0x20)
            {
                controls++;
            }
        }

        return controls > sample / 12;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T found)
            {
                return found;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static Brush CreateBrush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFromString(hex)!;
    }

    private sealed record PacketPayloadSnapshot(
        byte[] Bytes,
        string RawText,
        string DisplayText,
        string Json,
        bool HasJson,
        bool IsBinary,
        string StatusText);
}
