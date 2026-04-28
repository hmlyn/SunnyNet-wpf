using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
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
    private const char ProtobufSchemaPathSeparator = '|';
    private const int MaxInlineTextChars = 131_072;
    private INotifyCollectionChanged? _trackedCollection;
    private CollectionViewSource? _entriesViewSource;
    private readonly Dictionary<string, PacketPayloadSnapshot> _payloadCache = new(StringComparer.Ordinal);
    private bool _syncingSelectedEntry;
    private bool _suspendPacketReplayEditorEvents;
    private bool _packetReplayEditorDirty;
    private int _payloadVersion;
    private string _loadedPacketProtobufSchemaPath = "";
    private SocketEntry? _currentEntry;
    private PacketPayloadSnapshot? _currentSnapshot;

    public PacketMessagesControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyDisplayModeVisualState();
            SetComboBoxSelectionByTag(PacketReplayEncodingComboBox, "HEX");
            SetComboBoxSelectionByTag(PacketReplayDirectionComboBox, "Server");
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

    private bool IsTcpProtocol => string.Equals(ProtocolName, "TCP", StringComparison.OrdinalIgnoreCase);

    private bool IsUdpProtocol => string.Equals(ProtocolName, "UDP", StringComparison.OrdinalIgnoreCase);

    private bool SupportsPacketTools => IsTcpProtocol || IsUdpProtocol;

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
        RefreshPacketToolsAvailability();
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
        RefreshPacketToolsAvailability();
    }

    private void RefreshPacketToolsAvailability()
    {
        if (PacketProtobufTab is null || PacketReplayTab is null)
        {
            return;
        }

        PacketProtobufTab.Visibility = SupportsPacketTools ? Visibility.Visible : Visibility.Collapsed;
        PacketReplayTab.Visibility = SupportsPacketTools ? Visibility.Visible : Visibility.Collapsed;
        if (!SupportsPacketTools && (ReferenceEquals(PacketPayloadTabs.SelectedItem, PacketProtobufTab) || ReferenceEquals(PacketPayloadTabs.SelectedItem, PacketReplayTab)))
        {
            PacketPayloadTabs.SelectedItem = PacketRawTab;
        }

        if (InspectorHintTextBlock is not null)
        {
            InspectorHintTextBlock.Text = SupportsPacketTools
                ? $"左侧选择 {ProtocolName} 数据包后，这里异步载入正文、JSON、HEX 和 ProtoBuf。"
                : $"左侧选择 {ProtocolName} 数据包后，这里异步载入正文、JSON 和 HEX。";
        }
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
        ClearPacketProtobufResult("等待当前包载入完成。");
        ClearPacketReplay("等待当前包载入完成。");
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
        ClearPacketProtobufResult(snapshot.Bytes.Length > 0 ? $"可按结构解析当前 {ProtocolName} 包。" : "当前数据包没有可解析字节。");
        ResetPacketReplayForSelection(entry, snapshot);
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
        ClearPacketProtobufResult($"选择 {ProtocolName} 包后，可按结构解析当前载荷。");
        ClearPacketReplay($"选择 {ProtocolName} 包后会自动回填当前载荷，可修改后发送。");
    }

    private async void BrowsePacketProtoSchema_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择 Protobuf 描述文件",
            Filter = "Protobuf 描述文件 (*.proto;*.pb;*.desc;*.protoset)|*.proto;*.pb;*.desc;*.protoset|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        string currentPath = GetFirstProtoSchemaPath(GetPacketProtoSchemaPath());
        if (File.Exists(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }
        else if (Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        PacketProtoSchemaPathTextBox.Text = BuildProtoSchemaPathValue(dialog.FileNames);
        if (DataContext is MainWindowViewModel viewModel)
        {
            await EnsurePacketProtobufSchemaLoadedAsync(viewModel, forceReload: true);
        }
    }

    private async void ParsePacketProtobuf_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!SupportsPacketTools)
        {
            PacketProtobufStatusTextBlock.Text = "当前协议暂未开启 ProtoBuf 包解析。";
            return;
        }

        PacketPayloadSnapshot? snapshot = await GetSelectedPayloadAsync();
        if (snapshot is null)
        {
            PacketProtobufStatusTextBlock.Text = $"请先选择一个 {ProtocolName} 数据包。";
            return;
        }

        if (snapshot.Bytes.Length == 0)
        {
            ClearPacketProtobufResult("当前数据包没有可解析字节。");
            return;
        }

        if (!TryGetPacketProtobufSkip(out int skip) || skip < 0 || skip > snapshot.Bytes.Length)
        {
            PacketProtobufStatusTextBlock.Text = "头部字节请输入有效范围。";
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            PacketProtobufStatusTextBlock.Text = "后台尚未就绪，无法解析 ProtoBuf。";
            return;
        }

        PacketProtobufStatusTextBlock.Text = $"正在解析 {ProtocolName} 载荷 · {snapshot.Bytes.Length - skip:N0}/{snapshot.Bytes.Length:N0} Bytes";
        string schemaPath = GetPacketProtoSchemaPath();
        string json;
        if (!string.IsNullOrWhiteSpace(schemaPath))
        {
            if (!await EnsurePacketProtobufSchemaLoadedAsync(viewModel))
            {
                return;
            }

            string messageType = GetPacketProtoMessageType();
            if (string.IsNullOrWhiteSpace(messageType))
            {
                ClearPacketProtobufResult("已导入结构，请先选择消息类型。");
                PacketProtobufStatusTextBlock.Text = "请选择消息类型后再解析。";
                return;
            }

            (bool ok, string schemaJson, string error) = await viewModel.ParseProtobufBySchemaAsync(snapshot.Bytes, skip, schemaPath, messageType);
            if (!ok || string.IsNullOrWhiteSpace(schemaJson))
            {
                ClearPacketProtobufResult("按结构解析失败。");
                PacketProtobufStatusTextBlock.Text = string.IsNullOrWhiteSpace(error) ? "按结构解析失败，请检查消息类型、头部字节或载荷。" : error;
                return;
            }

            json = schemaJson;
            PacketProtobufStatusTextBlock.Text = $"结构解析成功 · {messageType}";
        }
        else
        {
            json = await viewModel.ParseProtobufAsync(snapshot.Bytes, skip);
            PacketProtobufStatusTextBlock.Text = "已使用通用 ProtoBuf 解析。";
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            ClearPacketProtobufResult("解析失败，当前数据未识别为有效 ProtoBuf。");
            PacketProtobufStatusTextBlock.Text = "ProtoBuf 解析失败，请检查头部字节或消息类型。";
            return;
        }

        PacketProtobufViewer.JsonText = json;
        PacketProtobufEmptyPanel.Visibility = Visibility.Collapsed;
    }

    private void ClearPacketProtobuf_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ResetPacketProtobufState();
    }

    private void PacketProtobufSkipTextBox_TextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
    {
        UpdatePacketProtobufStatusSummary();
    }

    private void AdjustPacketProtobufSkip_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        int current = TryGetPacketProtobufSkip(out int skip) ? skip : 0;
        int delta = int.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out int parsedDelta) ? parsedDelta : 0;
        int max = _currentSnapshot?.Bytes.Length ?? 0;
        int next = Math.Clamp(current + delta, 0, max);
        PacketProtobufSkipTextBox.Text = next.ToString();
        PacketProtobufSkipTextBox.SelectAll();
        PacketProtobufSkipTextBox.Focus();
    }

    private void ClearPacketProtobufResult(string message)
    {
        if (PacketProtobufViewer is null)
        {
            return;
        }

        PacketProtobufViewer.JsonText = "";
        PacketProtobufEmptyPanel.Visibility = Visibility.Visible;
        PacketProtobufEmptyTextBlock.Text = message;
        UpdatePacketProtobufStatusSummary();
    }

    private void ResetPacketProtobufState()
    {
        _loadedPacketProtobufSchemaPath = "";
        PacketProtoSchemaPathTextBox.Text = "";
        ApplyPacketProtoMessageTypes(Array.Empty<string>(), "");
        PacketProtobufSkipTextBox.Text = "0";
        ClearPacketProtobufResult("已清空 ProtoBuf 配置。");
        PacketProtobufStatusTextBlock.Text = "已清空 ProtoBuf 配置。";
    }

    private async Task<bool> EnsurePacketProtobufSchemaLoadedAsync(MainWindowViewModel viewModel, bool forceReload = false)
    {
        string schemaPath = GetPacketProtoSchemaPath();
        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            PacketProtobufStatusTextBlock.Text = "未选择结构文件，将使用通用 ProtoBuf 解析。";
            return false;
        }

        string normalizedCurrent = NormalizeSchemaPath(schemaPath);
        if (!forceReload
            && !string.IsNullOrWhiteSpace(_loadedPacketProtobufSchemaPath)
            && string.Equals(_loadedPacketProtobufSchemaPath, normalizedCurrent, StringComparison.OrdinalIgnoreCase)
            && PacketProtoMessageTypeComboBox.Items.Count > 0)
        {
            return true;
        }

        string preferredMessageType = GetPacketProtoMessageType();
        PacketProtobufStatusTextBlock.Text = "正在导入 Protobuf 描述...";
        (bool ok, string directory, IReadOnlyList<string> messages, string error) = await viewModel.ImportProtobufSchemaAsync(schemaPath);
        if (!ok || messages.Count == 0)
        {
            _loadedPacketProtobufSchemaPath = "";
            ApplyPacketProtoMessageTypes(Array.Empty<string>(), "");
            ClearPacketProtobufResult("结构导入失败。");
            PacketProtobufStatusTextBlock.Text = string.IsNullOrWhiteSpace(error) ? "导入 Protobuf 描述失败。" : error;
            return false;
        }

        string effectivePath = string.IsNullOrWhiteSpace(directory) ? schemaPath : directory;
        _loadedPacketProtobufSchemaPath = NormalizeSchemaPath(effectivePath);
        PacketProtoSchemaPathTextBox.Text = effectivePath;
        ApplyPacketProtoMessageTypes(messages, preferredMessageType);
        PacketProtobufStatusTextBlock.Text = $"结构已就绪 · {messages.Count:N0} 个消息类型";
        return true;
    }

    private void ApplyPacketProtoMessageTypes(IReadOnlyList<string> messages, string? preferredMessageType)
    {
        string preferred = preferredMessageType?.Trim() ?? "";
        PacketProtoMessageTypeComboBox.ItemsSource = messages;

        if (!string.IsNullOrWhiteSpace(preferred) && messages.Contains(preferred))
        {
            PacketProtoMessageTypeComboBox.SelectedItem = preferred;
            PacketProtoMessageTypeComboBox.Text = preferred;
            return;
        }

        if (messages.Count == 1)
        {
            PacketProtoMessageTypeComboBox.SelectedItem = messages[0];
            PacketProtoMessageTypeComboBox.Text = messages[0];
            return;
        }

        PacketProtoMessageTypeComboBox.SelectedItem = null;
        PacketProtoMessageTypeComboBox.Text = preferred;
    }

    private void UpdatePacketProtobufStatusSummary()
    {
        if (PacketProtobufStatusTextBlock is null || PacketProtobufStatusTextBlock.Text.StartsWith("正在", StringComparison.Ordinal))
        {
            return;
        }

        int total = _currentSnapshot?.Bytes.Length ?? 0;
        if (!TryGetPacketProtobufSkip(out int skip))
        {
            PacketProtobufStatusTextBlock.Text = $"载荷 {total:N0} Bytes · 头部无效";
            return;
        }

        int normalizedSkip = Math.Clamp(skip, 0, total);
        PacketProtobufStatusTextBlock.Text = $"载荷 {total:N0} Bytes · 头部 {normalizedSkip:N0} · 消息体 {Math.Max(total - normalizedSkip, 0):N0}";
    }

    private bool TryGetPacketProtobufSkip(out int skip)
    {
        return int.TryParse(PacketProtobufSkipTextBox.Text?.Trim(), out skip);
    }

    private string GetPacketProtoSchemaPath()
    {
        return PacketProtoSchemaPathTextBox.Text?.Trim() ?? "";
    }

    private string GetPacketProtoMessageType()
    {
        return (PacketProtoMessageTypeComboBox.SelectedItem as string ?? PacketProtoMessageTypeComboBox.Text)?.Trim() ?? "";
    }

    private static string NormalizeSchemaPath(string path)
    {
        string[] paths = SplitProtoSchemaPaths(path);
        if (paths.Length > 1)
        {
            return string.Join(
                ProtobufSchemaPathSeparator,
                paths.Select(NormalizeSingleSchemaPath).OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        }

        return NormalizeSingleSchemaPath(path);
    }

    private static string NormalizeSingleSchemaPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static string BuildProtoSchemaPathValue(IEnumerable<string> paths)
    {
        return string.Join(
            $" {ProtobufSchemaPathSeparator} ",
            paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()));
    }

    private static string GetFirstProtoSchemaPath(string path)
    {
        string[] paths = SplitProtoSchemaPaths(path);
        return paths.Length > 0 ? paths[0] : path;
    }

    private static string[] SplitProtoSchemaPaths(string path)
    {
        return (path ?? "").Split(ProtobufSchemaPathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async void SendPacketReplay_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!SupportsPacketTools)
        {
            PacketReplayStatusTextBlock.Text = "当前协议暂未开启主动发送。";
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel || GetSelectedEntry() is null)
        {
            PacketReplayStatusTextBlock.Text = $"请先选择一个 {ProtocolName} 数据包。";
            return;
        }

        string data = PacketReplayEditorTextBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(data))
        {
            PacketReplayStatusTextBlock.Text = "请输入要发送的数据。";
            return;
        }

        try
        {
            PacketReplayStatusTextBlock.Text = $"正在发送 {ProtocolName} 数据...";
            string sendType = GetSelectedComboTag(PacketReplayEncodingComboBox, "HEX");
            string direction = GetSelectedComboTag(PacketReplayDirectionComboBox, "Server");
            bool sent = IsTcpProtocol
                ? await viewModel.SendTcpPacketAsync(
                    Theology,
                    sendType,
                    direction,
                    data)
                : await viewModel.SendUdpPacketAsync(
                    Theology,
                    sendType,
                    direction,
                    data);
            PacketReplayStatusTextBlock.Text = sent ? "发送成功，消息流会追加手动包。" : "发送失败，请检查连接状态或数据格式。";
            if (sent)
            {
                _packetReplayEditorDirty = false;
            }
        }
        catch (Exception exception)
        {
            PacketReplayStatusTextBlock.Text = $"发送失败：{exception.Message}";
        }
    }

    private void PacketReplayEncodingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (_suspendPacketReplayEditorEvents || _packetReplayEditorDirty || _currentSnapshot is null)
        {
            return;
        }

        FillPacketReplayEditor(_currentSnapshot);
    }

    private void PacketReplayEditorTextBox_TextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
    {
        if (!_suspendPacketReplayEditorEvents)
        {
            _packetReplayEditorDirty = true;
        }
    }

    private void ResetPacketReplayForSelection(SocketEntry entry, PacketPayloadSnapshot snapshot)
    {
        if (!SupportsPacketTools)
        {
            ClearPacketReplay("当前协议暂未开启主动发送。");
            return;
        }

        SetComboBoxSelectionByTag(PacketReplayDirectionComboBox, entry.Icon == "下行" ? "Client" : "Server");
        FillPacketReplayEditor(snapshot);
        PacketReplayStatusTextBlock.Text = snapshot.Bytes.Length > 0
            ? $"已按当前格式回填当前 {ProtocolName} 包，可修改后发送。"
            : "当前数据包没有可发送字节。";
    }

    private void FillPacketReplayEditor(PacketPayloadSnapshot snapshot)
    {
        _suspendPacketReplayEditorEvents = true;
        PacketReplayEditorTextBox.Text = FormatPacketReplayText(snapshot);
        _suspendPacketReplayEditorEvents = false;
        _packetReplayEditorDirty = false;
    }

    private string FormatPacketReplayText(PacketPayloadSnapshot snapshot)
    {
        string encoding = GetSelectedComboTag(PacketReplayEncodingComboBox, "HEX");
        if (encoding.Equals("Base64", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToBase64String(snapshot.Bytes);
        }

        if (encoding.Equals("UTF8", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.IsBinary ? Encoding.UTF8.GetString(snapshot.Bytes) : snapshot.RawText;
        }

        return FormatHexWithSpaces(snapshot.Bytes);
    }

    private void ClearPacketReplay(string message)
    {
        if (PacketReplayEditorTextBox is null)
        {
            return;
        }

        _suspendPacketReplayEditorEvents = true;
        PacketReplayEditorTextBox.Text = "";
        _suspendPacketReplayEditorEvents = false;
        _packetReplayEditorDirty = false;
        PacketReplayStatusTextBlock.Text = message;
    }

    private static string FormatHexWithSpaces(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        StringBuilder builder = new(bytes.Length * 3);
        for (int index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[index].ToString("X2"));
        }

        return builder.ToString();
    }

    private static void SetComboBoxSelectionByTag(ComboBox comboBox, string tag)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static string GetSelectedComboTag(ComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
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
