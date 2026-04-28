using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Controls;

public partial class WebSocketMessagesControl : UserControl
{
    public static readonly DependencyProperty EntriesProperty =
        DependencyProperty.Register(nameof(Entries), typeof(IEnumerable), typeof(WebSocketMessagesControl), new PropertyMetadata(null, OnEntriesChanged));

    public static readonly DependencyProperty TheologyProperty =
        DependencyProperty.Register(nameof(Theology), typeof(int), typeof(WebSocketMessagesControl), new PropertyMetadata(0, OnTheologyChanged));

    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(nameof(DisplayMode), typeof(string), typeof(WebSocketMessagesControl), new PropertyMetadata(DisplayModeFull, OnDisplayModeChanged));

    public static readonly DependencyProperty SelectedEntryProperty =
        DependencyProperty.Register(nameof(SelectedEntry), typeof(SocketEntry), typeof(WebSocketMessagesControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedEntryChanged));

    private const string FilterAll = "All";
    private const string FilterText = "Text";
    private const string FilterBinary = "Binary";
    private const string FilterControl = "Control";
    private const string DisplayModeFull = "Full";
    private const string DisplayModeFlow = "Flow";
    private const string DisplayModeInspector = "Inspector";
    private const string DisplayModePayloadText = "PayloadText";
    private const string DisplayModePayloadHex = "PayloadHex";
    private const string DisplayModePayloadJson = "PayloadJson";
    private const string DisplayModePayloadProtobuf = "PayloadProtobuf";
    private const string DisplayModeReplay = "Replay";
    private const string DirectionAll = "All";
    private const string DirectionSend = "Send";
    private const string DirectionReceive = "Receive";
    private const string PayloadViewText = "Text";
    private const string PayloadViewHex = "Hex";
    private const string PayloadViewJson = "Json";
    private const string PayloadViewProtobuf = "Protobuf";
    private const char ProtobufSchemaPathSeparator = '|';
    private const int MaxRecentSearchTerms = 6;
    private const int MaxAutoSelectFrameCount = 2000;
    private INotifyCollectionChanged? _trackedCollection;
    private CollectionViewSource? _entriesViewSource;
    private ICollectionView? _entriesView;
    private readonly DispatcherTimer _replayAlertTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private readonly Dictionary<string, SocketPayloadSnapshot> _payloadCache = new(StringComparer.Ordinal);
    private readonly List<string> _recentSearchTerms = new();
    private int _payloadVersion;
    private string _currentKindFilter = FilterAll;
    private string _currentDirectionFilter = DirectionAll;
    private bool _manualOnly;
    private bool _nonEmptyOnly;
    private bool _suspendReplayEditorEvents;
    private bool _replayEditorDirty;
    private bool _syncingSelectedEntry;
    private string _loadedProtobufSchemaPath = "";
    private SocketEntry? _currentEntry;
    private SocketPayloadSnapshot? _currentPayloadSnapshot;

    public WebSocketMessagesControl()
    {
        InitializeComponent();
        _replayAlertTimer.Tick += ReplayAlertTimer_Tick;
        Loaded += (_, _) =>
        {
            ApplyDisplayModeVisualState();
            RebuildEntriesView();
            SetDefaultReplaySelections();
            RefreshRecentSearchChips();
            UpdateSearchAffordance();
            RefreshState(autoSelectLatest: !IsInspectorMode);
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

    public string DisplayMode
    {
        get => (string)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public SocketEntry? SelectedEntry
    {
        get => (SocketEntry?)GetValue(SelectedEntryProperty);
        set => SetValue(SelectedEntryProperty, value);
    }

    private bool IsInspectorMode =>
        string.Equals(DisplayMode, DisplayModeInspector, StringComparison.OrdinalIgnoreCase)
        || string.Equals(DisplayMode, DisplayModePayloadText, StringComparison.OrdinalIgnoreCase)
        || string.Equals(DisplayMode, DisplayModePayloadHex, StringComparison.OrdinalIgnoreCase)
        || string.Equals(DisplayMode, DisplayModePayloadJson, StringComparison.OrdinalIgnoreCase)
        || string.Equals(DisplayMode, DisplayModePayloadProtobuf, StringComparison.OrdinalIgnoreCase)
        || string.Equals(DisplayMode, DisplayModeReplay, StringComparison.OrdinalIgnoreCase);

    private bool IsFlowMode => string.Equals(DisplayMode, DisplayModeFlow, StringComparison.OrdinalIgnoreCase);

    private bool IsPayloadTextMode => string.Equals(DisplayMode, DisplayModePayloadText, StringComparison.OrdinalIgnoreCase);

    private bool IsPayloadHexMode => string.Equals(DisplayMode, DisplayModePayloadHex, StringComparison.OrdinalIgnoreCase);

    private bool IsPayloadJsonMode => string.Equals(DisplayMode, DisplayModePayloadJson, StringComparison.OrdinalIgnoreCase);

    private bool IsPayloadProtobufMode => string.Equals(DisplayMode, DisplayModePayloadProtobuf, StringComparison.OrdinalIgnoreCase);

    private bool IsReplayMode => string.Equals(DisplayMode, DisplayModeReplay, StringComparison.OrdinalIgnoreCase);

    private bool IsClassicInspectorMode => string.Equals(DisplayMode, DisplayModeInspector, StringComparison.OrdinalIgnoreCase);

    private void ApplyDisplayModeVisualState()
    {
        if (SummaryPanel is null
            || FlowPanel is null
            || InspectorPanel is null
            || FlowInspectorSplitter is null
            || FlowColumn is null
            || SplitterColumn is null
            || InspectorColumn is null
            || PayloadContentPanel is null
            || ReplayPanel is null)
        {
            return;
        }

        bool flowOnly = IsFlowMode;
        bool inspectorOnly = IsInspectorMode;
        bool full = !flowOnly && !inspectorOnly;
        bool showPayloadContent = !flowOnly && !IsReplayMode;
        bool showReplay = !flowOnly && (IsClassicInspectorMode || IsReplayMode);
        bool showReplayEditor = IsClassicInspectorMode || IsReplayMode;
        bool showProtobufTools = IsClassicInspectorMode || IsPayloadProtobufMode;

        SummaryPanel.Visibility = inspectorOnly ? Visibility.Collapsed : Visibility.Visible;
        FlowPanel.Visibility = inspectorOnly ? Visibility.Collapsed : Visibility.Visible;
        FlowInspectorSplitter.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        InspectorPanel.Visibility = flowOnly ? Visibility.Collapsed : Visibility.Visible;
        PayloadContentPanel.Visibility = showPayloadContent ? Visibility.Visible : Visibility.Collapsed;
        ReplayPanel.Visibility = showReplay ? Visibility.Visible : Visibility.Collapsed;
        PayloadContentRow.Height = showPayloadContent ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PayloadContentRow.MaxHeight = IsClassicInspectorMode ? 168 : double.PositiveInfinity;
        ReplayRow.Height = IsReplayMode ? new GridLength(1, GridUnitType.Star) : showReplay ? GridLength.Auto : new GridLength(0);

        if (ReplayEditorPanel is not null)
        {
            ReplayEditorPanel.Visibility = showReplayEditor ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ReplayToolbarPanel is not null)
        {
            ReplayToolbarPanel.Visibility = showReplayEditor ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ProtobufToolsPanel is not null)
        {
            ProtobufToolsPanel.Visibility = showProtobufTools ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ReplayMainColumn is not null && ReplayGapColumn is not null && ReplaySideColumn is not null)
        {
            ReplayMainColumn.Width = showReplayEditor ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            ReplayGapColumn.Width = showReplayEditor && showProtobufTools ? new GridLength(8) : new GridLength(0);
            ReplaySideColumn.Width = showReplayEditor && showProtobufTools ? new GridLength(198) : new GridLength(0);
        }

        FlowColumn.Width = inspectorOnly ? new GridLength(0) : full ? new GridLength(336) : new GridLength(1, GridUnitType.Star);
        SplitterColumn.Width = full ? new GridLength(10) : new GridLength(0);
        InspectorColumn.Width = flowOnly ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ApplyPayloadDisplayMode();
    }

    private async void ApplyExternalSelection(SocketEntry? entry)
    {
        if (_syncingSelectedEntry)
        {
            return;
        }

        if (!IsInspectorMode && FramesList is not null && !ReferenceEquals(FramesList.SelectedItem, entry))
        {
            _syncingSelectedEntry = true;
            FramesList.SelectedItem = entry;
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

    private static void OnEntriesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not WebSocketMessagesControl control)
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
        control.RefreshState(autoSelectLatest: !control.IsInspectorMode);
    }

    private static void OnTheologyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not WebSocketMessagesControl control)
        {
            return;
        }

        control._payloadCache.Clear();
        control.RefreshState(autoSelectLatest: !control.IsInspectorMode);
    }

    private static void OnDisplayModeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not WebSocketMessagesControl control)
        {
            return;
        }

        control.ApplyDisplayModeVisualState();
        control.RebuildEntriesView();
        control.RefreshState(autoSelectLatest: !control.IsInspectorMode);
    }

    private static void OnSelectedEntryChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not WebSocketMessagesControl control)
        {
            return;
        }

        control.ApplyExternalSelection(args.NewValue as SocketEntry);
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            _payloadCache.Clear();
        }

        if (IsInspectorMode)
        {
            return;
        }

        bool shouldAutoSelectLatest = FramesList.SelectedItem is null
            || FramesList.SelectedIndex >= Math.Max(FramesList.Items.Count - 2, 0);

        ApplyActiveFilters(autoSelectLatest: !IsInspectorMode && shouldAutoSelectLatest && args.Action == NotifyCollectionChangedAction.Add);
    }

    private async void FramesList_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (_syncingSelectedEntry)
        {
            return;
        }

        if (FramesList.SelectedItem is not SocketEntry entry)
        {
            if (!Equals(SelectedEntry, null))
            {
                _syncingSelectedEntry = true;
                SetCurrentValue(SelectedEntryProperty, null);
                _syncingSelectedEntry = false;
            }

            if (!IsFlowMode)
            {
                ClearPayload();
            }

            return;
        }

        if (!ReferenceEquals(SelectedEntry, entry))
        {
            _syncingSelectedEntry = true;
            SetCurrentValue(SelectedEntryProperty, entry);
            _syncingSelectedEntry = false;
        }

        if (IsFlowMode)
        {
            return;
        }

        await ApplyEntrySelectionAsync(entry);
    }

    private async Task ApplyEntrySelectionAsync(SocketEntry entry)
    {
        PayloadEmptyPanel.Visibility = Visibility.Collapsed;
        PayloadViewsHost.Visibility = Visibility.Visible;
        ApplySelectedMeta(entry);

        if (TryGetCachedPayload(entry, out SocketPayloadSnapshot? cached))
        {
            ApplyPayload(entry, cached);
            return;
        }

        ApplyPayloadLoadingState(entry);
        int payloadVersion = ++_payloadVersion;

        try
        {
            SocketPayloadSnapshot snapshot = await LoadPayloadSnapshotAsync(entry);
            if (payloadVersion != _payloadVersion)
            {
                return;
            }

            _payloadCache[BuildPayloadCacheKey(entry)] = snapshot;
            ApplyPayload(entry, snapshot);
        }
        catch
        {
            if (payloadVersion != _payloadVersion)
            {
                return;
            }

            SocketPayloadSnapshot fallback = BuildPayloadSnapshot(entry, Array.Empty<byte>(), entry.PreviewText, "");
            _payloadCache[BuildPayloadCacheKey(entry)] = fallback;
            ApplyPayload(entry, fallback);
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyActiveFilters(autoSelectLatest: false);
    }

    private void KindFilterToggle_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not ToggleButton toggleButton)
        {
            return;
        }

        SetKindFilter(toggleButton.Tag?.ToString() ?? FilterAll);
        ApplyActiveFilters(autoSelectLatest: false);
    }

    private void DirectionFilterToggle_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not ToggleButton toggleButton)
        {
            return;
        }

        string direction = toggleButton.Tag?.ToString() ?? DirectionAll;
        SetDirectionFilter(toggleButton.IsChecked == true ? direction : DirectionAll);
        ApplyActiveFilters(autoSelectLatest: false);
    }

    private void QuickFilterToggle_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _manualOnly = ManualReplayFilterButton.IsChecked == true;
        _nonEmptyOnly = NonEmptyFilterButton.IsChecked == true;
        ApplyActiveFilters(autoSelectLatest: false);
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SearchTextBox.Text = "";
        SetKindFilter(FilterAll);
        SetDirectionFilter(DirectionAll);
        SetQuickFilters(manualOnly: false, nonEmptyOnly: false);
        ApplyActiveFilters(autoSelectLatest: false);
        if (!IsSearchFlyoutOpen)
        {
            SetSearchFlyoutOpen(true);
        }

        FocusSearchBox();
    }

    private void ToggleSearchPopup_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SetSearchFlyoutOpen(!IsSearchFlyoutOpen);
        if (IsSearchFlyoutOpen)
        {
            FocusSearchBox();
        }
    }

    private void CloseSearchPopup_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SetSearchFlyoutOpen(false);
    }

    private void ClearRecentSearch_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _recentSearchTerms.Clear();
        RefreshRecentSearchChips();
    }

    private void RecentSearchChip_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not Button button)
        {
            return;
        }

        string query = button.Tag?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        SearchTextBox.Text = query;
        SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
        FocusSearchBox(selectAll: false);
        CommitSearchTerm();
    }

    private void JumpToLatest_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SelectLatestVisibleFrame();
    }

    private void ReplayWsTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (_suspendReplayEditorEvents || !IsLoaded)
        {
            return;
        }

        string wsType = GetSelectedComboTag(ReplayWsTypeComboBox, "Text");
        if (!_replayEditorDirty)
        {
            SetReplayEncoding(DefaultEncodingForWsType(wsType));
            FillReplayEditorFromSelected();
        }

        UpdateReplayHint();
        UpdateReplayEditorStatus();
    }

    private void ReplayEncodingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (_suspendReplayEditorEvents || !IsLoaded)
        {
            return;
        }

        if (!_replayEditorDirty)
        {
            FillReplayEditorFromSelected();
        }

        UpdateReplayHint();
        UpdateReplayEditorStatus();
        UpdateProtobufPayloadSummary();
    }

    private void ReplayEditorTextBox_TextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
    {
        if (_suspendReplayEditorEvents)
        {
            return;
        }

        _replayEditorDirty = true;
        UpdateReplayEditorStatus();
        UpdateProtobufPayloadSummary();
    }

    private void ProtobufSkipTextBox_TextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
    {
        UpdateProtobufPayloadSummary();
    }

    private void AdjustProtobufSkip_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        int current = TryGetSkipBytes(out int skip) ? skip : 0;
        int delta = int.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out int parsedDelta) ? parsedDelta : 0;
        int max = GetProtobufPayloadBytesSafely().Length;
        int next = Math.Clamp(current + delta, 0, max);
        ProtobufSkipTextBox.Text = next.ToString();
        ProtobufSkipTextBox.SelectAll();
        ProtobufSkipTextBox.Focus();
    }

    private async void ParseProtobuf_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            byte[] bytes = GetProtobufPayloadBytes();
            if (!TryGetSkipBytes(out int skip))
            {
                ParserStatusTextBlock.Text = "Protobuf 跳过字节请输入有效整数。";
                return;
            }

            if (skip < 0 || skip > bytes.Length)
            {
                ParserStatusTextBlock.Text = "Protobuf 跳过字节超出当前数据范围。";
                return;
            }

            if (bytes.Length == 0)
            {
                ParserStatusTextBlock.Text = "当前消息没有可解析的字节数据。";
                return;
            }

            ParserStatusTextBlock.Text = $"正在解析 Protobuf 消息体 · {bytes.Length - skip:N0}/{bytes.Length:N0} Bytes";
            string schemaPath = GetProtoSchemaPath();
            string json;
            if (!string.IsNullOrWhiteSpace(schemaPath))
            {
                if (!await EnsureProtobufSchemaLoadedAsync(viewModel))
                {
                    SetActivePayloadView(PayloadViewProtobuf);
                    return;
                }

                string messageType = GetProtoMessageType();
                if (string.IsNullOrWhiteSpace(messageType))
                {
                    PayloadProtobufViewer.JsonText = "";
                    PayloadProtobufEmptyPanel.Visibility = Visibility.Visible;
                    PayloadProtobufStatusTextBlock.Text = "已导入结构，请先选择消息类型。";
                    ParserStatusTextBlock.Text = "请选择消息类型后再解析。";
                    SetActivePayloadView(PayloadViewProtobuf);
                    return;
                }

                (bool ok, string schemaJson, string error) = await viewModel.ParseProtobufBySchemaAsync(bytes, skip, schemaPath, messageType);
                if (!ok || string.IsNullOrWhiteSpace(schemaJson))
                {
                    PayloadProtobufViewer.JsonText = "";
                    PayloadProtobufEmptyPanel.Visibility = Visibility.Visible;
                    PayloadProtobufStatusTextBlock.Text = "按结构解析失败。";
                    ParserStatusTextBlock.Text = string.IsNullOrWhiteSpace(error) ? "按结构解析失败，请检查消息类型、跳过字节或载荷内容。" : error;
                    SetActivePayloadView(PayloadViewProtobuf);
                    return;
                }

                json = schemaJson;
                PayloadProtobufStatusTextBlock.Text = $"结构解析成功 · {messageType}";
                ParserStatusTextBlock.Text = $"结构解析成功 · {messageType} · Skip {skip} · {bytes.Length:N0} Bytes";
            }
            else
            {
                json = await viewModel.ParseProtobufAsync(bytes, skip);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                PayloadProtobufViewer.JsonText = "";
                PayloadProtobufEmptyPanel.Visibility = Visibility.Visible;
                PayloadProtobufStatusTextBlock.Text = "解析失败，当前数据未识别为有效 Protobuf。";
                ParserStatusTextBlock.Text = "Protobuf 解析失败，请检查编码或跳过字节。";
                SetActivePayloadView(PayloadViewProtobuf);
                return;
            }

            PayloadProtobufViewer.JsonText = json;
            PayloadProtobufEmptyPanel.Visibility = Visibility.Collapsed;
            SetActivePayloadView(PayloadViewProtobuf);
            if (string.IsNullOrWhiteSpace(schemaPath))
            {
                PayloadProtobufStatusTextBlock.Text = $"解析成功 · Skip {skip} · {bytes.Length:N0} Bytes";
                ParserStatusTextBlock.Text = $"Protobuf 解析成功 · Skip {skip} · {bytes.Length:N0} Bytes";
            }
        }
        catch (Exception exception)
        {
            PayloadProtobufViewer.JsonText = "";
            PayloadProtobufEmptyPanel.Visibility = Visibility.Visible;
            PayloadProtobufStatusTextBlock.Text = "解析失败。";
            ParserStatusTextBlock.Text = exception.Message;
        }
    }

    private byte[] GetProtobufPayloadBytes()
    {
        if (IsReplayMode || IsClassicInspectorMode)
        {
            return DecodeReplayEditorBytes();
        }

        return _currentPayloadSnapshot?.Bytes ?? Array.Empty<byte>();
    }

    private byte[] GetProtobufPayloadBytesSafely()
    {
        try
        {
            return GetProtobufPayloadBytes();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private void ClearProtobuf_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ResetProtobufState();
    }

    private async void BrowseProtoSchemaDirectory_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择 Protobuf 描述文件",
            Filter = "Protobuf 描述文件 (*.proto;*.pb;*.desc;*.protoset)|*.proto;*.pb;*.desc;*.protoset|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        string currentPath = GetFirstProtoSchemaPath(GetProtoSchemaPath());
        if (File.Exists(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }
        else if (Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog() == true)
        {
            ProtoSchemaPathTextBox.Text = BuildProtoSchemaPathValue(dialog.FileNames);
            if (DataContext is MainWindowViewModel viewModel)
            {
                await EnsureProtobufSchemaLoadedAsync(viewModel, forceReload: true);
            }
        }
    }

    private async void ReplaySend_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel || GetSelectedEntry() is not { } entry)
        {
            return;
        }

        try
        {
            HideReplayAlert();
            ReplayActionStatusTextBlock.Text = "正在发送重放消息...";
            bool sent = await viewModel.SendSocketFrameAsync(
                Theology,
                GetSelectedComboTag(ReplayWsTypeComboBox, entry.TypeLabel),
                GetSelectedComboTag(ReplayEncodingComboBox, "UTF8"),
                GetSelectedComboTag(ReplayDirectionComboBox, entry.Icon == "下行" ? "Client" : "Server"),
                ReplayEditorTextBox.Text ?? "");

            ReplayActionStatusTextBlock.Text = sent ? "发送成功" : "发送失败";
            ShowReplayAlert(sent ? "发送成功" : "发送失败：请检查连接状态或数据格式。", sent ? ReplayAlertKind.Success : ReplayAlertKind.Error);
            _replayEditorDirty = false;
        }
        catch (Exception exception)
        {
            ReplayActionStatusTextBlock.Text = exception.Message;
            ShowReplayAlert($"发送失败：{exception.Message}", ReplayAlertKind.Error);
        }
    }

    private void ShowReplayAlert(string message, ReplayAlertKind kind)
    {
        ApplyReplayAlertPalette(kind);
        ReplayAlertTextBlock.Text = message;
        ReplayAlertBorder.Visibility = Visibility.Visible;
        _replayAlertTimer.Stop();
        _replayAlertTimer.Start();
    }

    private void HideReplayAlert()
    {
        _replayAlertTimer.Stop();
        ReplayAlertBorder.Visibility = Visibility.Collapsed;
    }

    private void ReplayAlertTimer_Tick(object? sender, EventArgs eventArgs)
    {
        HideReplayAlert();
    }

    private void ApplyReplayAlertPalette(ReplayAlertKind kind)
    {
        string background;
        string border;
        string icon;
        string foreground;
        string glyph;

        if (kind == ReplayAlertKind.Success)
        {
            background = "#F0F9EB";
            border = "#E1F3D8";
            icon = "#67C23A";
            foreground = "#2F8F45";
            glyph = "✓";
        }
        else
        {
            background = "#FEF0F0";
            border = "#FDE2E2";
            icon = "#F56C6C";
            foreground = "#F56C6C";
            glyph = "×";
        }

        ReplayAlertBorder.Background = CreateBrush(background);
        ReplayAlertBorder.BorderBrush = CreateBrush(border);
        ReplayAlertIconBorder.Background = CreateBrush(icon);
        ReplayAlertTextBlock.Foreground = CreateBrush(foreground);
        ReplayAlertGlyphTextBlock.Text = glyph;
    }

    private void FramesList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (FindParent<ListBoxItem>(mouseButtonEventArgs.OriginalSource as DependencyObject) is not { } item)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
    }

    private void FramesContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        SocketEntry? entry = GetSelectedEntry();
        bool hasEntry = entry is not null;

        CopyFrameSummaryMenuItem.IsEnabled = hasEntry;
        CopyFrameTextMenuItem.IsEnabled = hasEntry;
        CopyFrameJsonMenuItem.IsEnabled = hasEntry && entry is { IsBinaryFrame: false, IsTrafficFrame: true };
        CopyFrameHexMenuItem.IsEnabled = hasEntry && entry is { IsTrafficFrame: true };
        JumpToLatestMenuItem.IsEnabled = FramesList.Items.Count > 0;

        if (entry is null)
        {
            CopyFrameTextMenuItem.Header = "复制消息文本";
            CopyFrameJsonMenuItem.Header = "复制 JSON";
            CopyFrameHexMenuItem.Header = "复制消息 HEX";
            return;
        }

        CopyFrameTextMenuItem.Header = entry.IsBinaryFrame || entry.IsControlFrame ? "复制消息摘要" : "复制消息文本";
        if (TryGetCachedPayload(entry, out SocketPayloadSnapshot? snapshot))
        {
            CopyFrameJsonMenuItem.IsEnabled = snapshot.HasJson;
            CopyFrameHexMenuItem.Header = $"复制消息 HEX ({snapshot.Bytes.Length:N0} Bytes)";
        }
        else
        {
            CopyFrameHexMenuItem.Header = "复制消息 HEX";
        }
    }

    private void PayloadContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        SocketEntry? entry = GetSelectedEntry();
        bool hasEntry = entry is not null;

        CopyCurrentTextMenuItem.IsEnabled = hasEntry;
        CopyCurrentJsonMenuItem.IsEnabled = hasEntry && entry is { IsBinaryFrame: false, IsTrafficFrame: true };
        CopyCurrentHexMenuItem.IsEnabled = hasEntry && entry is { IsTrafficFrame: true };

        if (entry is null)
        {
            CopyCurrentTextMenuItem.Header = "复制当前文本";
            CopyCurrentJsonMenuItem.Header = "复制当前 JSON";
            CopyCurrentHexMenuItem.Header = "复制当前 HEX";
            return;
        }

        CopyCurrentTextMenuItem.Header = entry.IsBinaryFrame || entry.IsControlFrame ? "复制当前摘要" : "复制当前文本";
        if (TryGetCachedPayload(entry, out SocketPayloadSnapshot? snapshot))
        {
            CopyCurrentJsonMenuItem.IsEnabled = snapshot.HasJson;
            CopyCurrentHexMenuItem.Header = $"复制当前 HEX ({snapshot.Bytes.Length:N0} Bytes)";
        }
        else
        {
            CopyCurrentHexMenuItem.Header = "复制当前 HEX";
        }
    }

    private void CopySelectedFrameSummary_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (GetSelectedEntry() is not { } entry)
        {
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine($"第 {entry.DisplayIndex} 帧");
        builder.AppendLine($"方向: {entry.DirectionLabel}");
        builder.AppendLine($"类型: {entry.TypeLabel}");
        builder.AppendLine($"长度: {entry.LengthLabel}");
        if (!string.IsNullOrWhiteSpace(entry.Time))
        {
            builder.AppendLine($"时间: {entry.Time}");
        }

        builder.AppendLine($"摘要: {entry.PreviewText}");
        CopyText(builder.ToString().TrimEnd());
    }

    private async void CopySelectedFrameText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (await GetSelectedPayloadAsync() is not { } payload)
        {
            return;
        }

        CopyText(payload.DisplayText);
    }

    private async void CopySelectedFrameJson_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (await GetSelectedPayloadAsync() is not { } payload || !payload.HasJson)
        {
            return;
        }

        CopyText(payload.Json);
    }

    private async void CopySelectedFrameHex_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (await GetSelectedPayloadAsync() is not { } payload || payload.Bytes.Length == 0)
        {
            return;
        }

        CopyText(FormatHexLines(payload.Bytes));
    }

    private void RebuildEntriesView()
    {
        if (FramesList is null)
        {
            return;
        }

        if (IsInspectorMode)
        {
            _entriesViewSource = null;
            _entriesView = null;
            FramesList.ItemsSource = null;
            return;
        }

        _entriesViewSource = Entries is null ? null : new CollectionViewSource { Source = Entries };
        _entriesView = _entriesViewSource?.View;
        if (_entriesView is not null)
        {
            _entriesView.Filter = HasActiveFrameFilter() ? MatchesEntry : null;
        }

        FramesList.ItemsSource = _entriesView ?? Entries;
    }

    private void ApplyActiveFilters(bool autoSelectLatest)
    {
        if (IsInspectorMode)
        {
            UpdateSearchAffordance();
            RefreshState(autoSelectLatest: false);
            return;
        }

        if (_entriesView is not null)
        {
            _entriesView.Filter = HasActiveFrameFilter() ? MatchesEntry : null;
            _entriesView.Refresh();
        }

        UpdateSearchAffordance();
        RefreshState(autoSelectLatest);
    }

    private bool MatchesEntry(object candidate)
    {
        if (candidate is not SocketEntry entry)
        {
            return false;
        }

        if (_currentKindFilter == FilterText && !entry.IsTextFrame)
        {
            return false;
        }

        if (_currentKindFilter == FilterBinary && !entry.IsBinaryFrame)
        {
            return false;
        }

        if (_currentKindFilter == FilterControl && !entry.IsControlFrame)
        {
            return false;
        }

        if (_currentDirectionFilter == DirectionSend && entry.Icon != "上行")
        {
            return false;
        }

        if (_currentDirectionFilter == DirectionReceive && entry.Icon != "下行")
        {
            return false;
        }

        if (_manualOnly && !IsManualReplayFrame(entry))
        {
            return false;
        }

        if (_nonEmptyOnly && entry.Length <= 0)
        {
            return false;
        }

        string query = SearchTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return EntryMatchesQuery(entry, query);
    }

    private void RefreshState(bool autoSelectLatest)
    {
        if (IsInspectorMode)
        {
            if (SelectedEntry is not null)
            {
                ApplyExternalSelection(SelectedEntry);
            }
            else
            {
                ClearPayload();
            }

            return;
        }

        FrameStatistics statistics = CountFrameStatistics();
        int total = statistics.Total;
        int upstream = statistics.Upstream;
        int downstream = statistics.Downstream;
        int textFrames = statistics.Text;
        int visible = HasActiveFrameFilter() ? CountVisibleEntries() : total;

        TotalFramesTextBlock.Text = total.ToString();
        UpstreamFramesTextBlock.Text = upstream.ToString();
        DownstreamFramesTextBlock.Text = downstream.ToString();
        TextFramesTextBlock.Text = textFrames.ToString();
        FlowSummaryTextBlock.Text = visible == total ? $"{total} 帧" : $"{visible} / {total} 帧";
        FilterStateTextBlock.Text = BuildFilterStateText(visible, total);

        bool hasAnyEntry = total > 0;
        FramesEmptyPanel.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        FramesEmptyTitleTextBlock.Text = hasAnyEntry ? "当前搜索没有命中消息" : "暂无 WebSocket 消息";
        FramesEmptyHintTextBlock.Text = hasAnyEntry ? "试试切换类型、方向、快捷条件，或清空搜索" : "等待会话产生 WebSocket 帧";

        if (visible == 0)
        {
            if (FramesList.SelectedItem is not null)
            {
                FramesList.SelectedItem = null;
            }
            if (!IsFlowMode)
            {
                ClearPayload();
            }
            return;
        }

        if (IsInspectorMode)
        {
            if (SelectedEntry is not null)
            {
                ApplyExternalSelection(SelectedEntry);
            }

            return;
        }

        if (autoSelectLatest)
        {
            if (visible <= MaxAutoSelectFrameCount)
            {
                SelectLatestVisibleFrame();
            }
            return;
        }

        if (FramesList.SelectedItem is null && FramesList.Items.Count > 0 && visible <= MaxAutoSelectFrameCount)
        {
            FramesList.SelectedIndex = FramesList.Items.Count - 1;
        }
    }

    private void SelectLatestVisibleFrame()
    {
        if (FramesList.Items.Count <= 0)
        {
            return;
        }

        FramesList.SelectedIndex = FramesList.Items.Count - 1;
        FramesList.ScrollIntoView(FramesList.SelectedItem);
    }

    private void ApplySelectedMeta(SocketEntry entry)
    {
        SelectedMetaPanel.DataContext = entry;
        SelectedDirectionBadge.DataContext = entry;
        SelectedDirectionTextBlock.DataContext = entry;
        SelectedTypeBadge.DataContext = entry;
        SelectedTypeTextBlock.DataContext = entry;

        SelectedDirectionTextBlock.Text = entry.DirectionLabel;
        SelectedTypeTextBlock.Text = entry.TypeLabel;
        SelectedLengthTextBlock.Text = entry.LengthLabel;
        SelectedTitleTextBlock.Text = $"第 {entry.DisplayIndex} 帧 · {entry.DirectionLabel}";
        SelectedSubtitleTextBlock.Text = string.IsNullOrWhiteSpace(entry.Time)
            ? entry.PreviewText
            : $"{entry.Time} · {entry.PreviewText}";
    }

    private void ApplyPayloadLoadingState(SocketEntry entry)
    {
        PayloadEmptyPanel.Visibility = Visibility.Collapsed;
        PayloadViewsHost.Visibility = Visibility.Visible;
        ApplySelectedMeta(entry);
        PayloadRawViewer.SourceText = "正在读取消息内容...";
        PayloadRawViewer.HighlightMode = "Body";
        PayloadJsonViewer.JsonText = "";
        PayloadHexViewer.Bytes = Array.Empty<byte>();
        PayloadHexViewer.HeaderLength = 0;
        ApplyPayloadDisplayMode();
        PayloadKindTextBlock.Text = $"消息类型 · {BuildPayloadKindLabel(entry, hasJson: false, isBinary: entry.IsBinaryFrame || entry.IsControlFrame)}";
        PayloadPreviewModeTextBlock.Text = "预览模式 · 读取中";
        PayloadStatusTextBlock.Text = "载荷状态 · 正在从核心加载";
        PayloadHintTextBlock.Text = entry.IsControlFrame ? "控制帧会在消息类型区域显示专用说明，原始字节请切换到 HEX 视图查看。" : "下方依次提供内容查看、编辑重放和协议解析。";
        SpecialFrameCard.Visibility = Visibility.Collapsed;
        ClearProtobufView();
        ResetReplayForSelection(entry, null);
    }

    private void ApplyPayload(SocketEntry entry, SocketPayloadSnapshot snapshot)
    {
        _currentEntry = entry;
        _currentPayloadSnapshot = snapshot;

        ApplySelectedMeta(entry);
        PayloadRawViewer.SourceText = snapshot.DisplayText;
        PayloadRawViewer.HighlightMode = snapshot.HasJson ? "Json" : "Body";
        PayloadJsonViewer.JsonText = snapshot.Json;
        PayloadHexViewer.Bytes = snapshot.Bytes;
        PayloadHexViewer.HeaderLength = 0;
        PayloadKindTextBlock.Text = $"消息类型 · {snapshot.PayloadKind}";
        PayloadPreviewModeTextBlock.Text = $"预览模式 · {snapshot.PreviewMode}";
        PayloadStatusTextBlock.Text = $"载荷状态 · {snapshot.StatusText}";
        PayloadHintTextBlock.Text = snapshot.HintText;
        UpdateSpecialFrame(entry, snapshot);
        ClearProtobufView();
        ResetReplayForSelection(entry, snapshot);
        ApplyPayloadDisplayMode();
    }

    private void ClearPayload()
    {
        _payloadVersion++;
        _currentEntry = null;
        _currentPayloadSnapshot = null;
        SelectedMetaPanel.DataContext = null;
        SelectedDirectionBadge.DataContext = null;
        SelectedDirectionTextBlock.DataContext = null;
        SelectedTypeBadge.DataContext = null;
        SelectedTypeTextBlock.DataContext = null;
        SelectedTitleTextBlock.Text = "请选择一条消息";
        SelectedSubtitleTextBlock.Text = "下方显示完整消息内容与重放操作";
        SelectedDirectionTextBlock.Text = "消息";
        SelectedTypeTextBlock.Text = "Text";
        SelectedLengthTextBlock.Text = "0 B";
        PayloadViewsHost.Visibility = Visibility.Collapsed;
        PayloadEmptyPanel.Visibility = Visibility.Visible;
        PayloadRawViewer.SourceText = "";
        PayloadRawViewer.HighlightMode = "Body";
        PayloadJsonViewer.JsonText = "";
        PayloadHexViewer.Bytes = Array.Empty<byte>();
        PayloadHexViewer.HeaderLength = 0;
        ApplyPayloadDisplayMode();
        PayloadKindTextBlock.Text = "消息类型 · 待选择";
        PayloadPreviewModeTextBlock.Text = "预览模式 · 待选择";
        PayloadStatusTextBlock.Text = "载荷状态 · 等待选择";
        PayloadHintTextBlock.Text = "选择消息后查看内容、修改并重放。";
        SpecialFrameCard.Visibility = Visibility.Collapsed;
        ClearProtobufView();
        ClearReplayEditor();
    }

    private void ApplyPayloadDisplayMode()
    {
        if (PayloadRawViewer is null || PayloadJsonViewer is null || PayloadProtobufPanel is null || PayloadHexViewer is null)
        {
            return;
        }

        if (IsPayloadHexMode)
        {
            SetActivePayloadView(PayloadViewHex);
            return;
        }

        if (IsPayloadJsonMode)
        {
            SetActivePayloadView(PayloadViewJson);
            return;
        }

        if (IsPayloadProtobufMode)
        {
            SetActivePayloadView(PayloadViewProtobuf);
            return;
        }

        if (IsClassicInspectorMode)
        {
            SetActivePayloadView(_currentPayloadSnapshot?.HasJson == true ? PayloadViewJson : PayloadViewText);
            return;
        }

        SetActivePayloadView(PayloadViewText);
    }

    private void SetActivePayloadView(string view)
    {
        PayloadRawViewer.Visibility = view == PayloadViewText ? Visibility.Visible : Visibility.Collapsed;
        PayloadJsonViewer.Visibility = view == PayloadViewJson ? Visibility.Visible : Visibility.Collapsed;
        PayloadProtobufPanel.Visibility = view == PayloadViewProtobuf ? Visibility.Visible : Visibility.Collapsed;
        PayloadHexViewer.Visibility = view == PayloadViewHex ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<SocketPayloadSnapshot?> GetSelectedPayloadAsync()
    {
        if (GetSelectedEntry() is not { } entry)
        {
            return null;
        }

        if (TryGetCachedPayload(entry, out SocketPayloadSnapshot? snapshot))
        {
            return snapshot;
        }

        SocketPayloadSnapshot loaded = await LoadPayloadSnapshotAsync(entry);
        _payloadCache[BuildPayloadCacheKey(entry)] = loaded;
        return loaded;
    }

    private async Task<SocketPayloadSnapshot> LoadPayloadSnapshotAsync(SocketEntry entry)
    {
        if (!entry.IsTrafficFrame || DataContext is not MainWindowViewModel viewModel || Theology <= 0)
        {
            return BuildPayloadSnapshot(entry, Array.Empty<byte>(), entry.PreviewText, "");
        }

        (byte[] bytes, string text, string json) = await viewModel.LoadSocketPayloadAsync(Theology, entry.Index - 1);
        if (bytes.Length == 0 && string.IsNullOrWhiteSpace(text))
        {
            return BuildPayloadSnapshot(entry, Array.Empty<byte>(), entry.PreviewText, "");
        }

        string displayText = ResolveWebSocketDisplayText(entry, bytes, text, entry.PreviewText);
        return BuildPayloadSnapshot(entry, bytes, displayText, json);
    }

    private static string ResolveWebSocketDisplayText(SocketEntry entry, byte[] bytes, string text, string fallback)
    {
        if (entry.IsTextFrame && bytes.Length > 0)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static SocketPayloadSnapshot BuildPayloadSnapshot(SocketEntry entry, byte[] bytes, string text, string jsonCandidate)
    {
        bool hasJson = TryNormalizeJson(jsonCandidate, out string normalizedJson);
        bool isBinary = entry.IsBinaryFrame || entry.IsControlFrame || LooksBinary(bytes);
        string payloadKind = BuildPayloadKindLabel(entry, hasJson, isBinary);
        string previewMode = hasJson
            ? "JSON / 文本 / HEX / Protobuf"
            : entry.IsControlFrame
                ? "HEX / 控制帧 / Protobuf"
                : "文本 / HEX / Protobuf";
        string status = entry.IsStatusFrame
            ? "连接状态事件"
            : bytes.Length > 0
                ? $"已加载 {bytes.Length:N0} Bytes"
                : "仅摘要可用";
        string hint = entry.IsStatusFrame
            ? "这是连接状态事件，不包含可编辑载荷。"
            : hasJson
                ? "识别为 JSON，默认切到 JSON 视图。"
                : entry.IsControlFrame
                    ? "控制帧在上方显示专用解释，HEX 中保留原始载荷。"
                    : "消息流显示摘要，右侧通过文本、HEX、JSON/Protobuf 视图切换查看。";

        return new SocketPayloadSnapshot(bytes, text, text, normalizedJson, hasJson, isBinary, payloadKind, previewMode, status, hint);
    }

    private static string BuildPayloadKindLabel(SocketEntry entry, bool hasJson, bool isBinary)
    {
        if (entry.IsStatusFrame)
        {
            return "状态事件";
        }

        if (hasJson)
        {
            return "JSON 文本";
        }

        if (entry.IsControlFrame)
        {
            return $"{entry.TypeLabel} 控制帧";
        }

        if (isBinary)
        {
            return "二进制帧";
        }

        return "文本帧";
    }

    private static bool TryNormalizeJson(string candidate, out string normalizedJson)
    {
        normalizedJson = "";
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(candidate);
            normalizedJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSpecialFrame(SocketEntry entry, SocketPayloadSnapshot snapshot)
    {
        if (!entry.IsControlFrame && !entry.IsStatusFrame)
        {
            SpecialFrameCard.Visibility = Visibility.Collapsed;
            return;
        }

        SpecialFrameCard.Visibility = Visibility.Visible;

        if (entry.Type == "Ping")
        {
            ApplySpecialFrameVisual("#EEF4FF", "#D8E7FF", "#2F7CF6", "PING");
            SpecialFrameTitleTextBlock.Text = "Ping 心跳帧";
            SpecialFrameDescriptionTextBlock.Text = "Ping 通常用于探活或时延检测，对端一般会自动回 Pong。";
            SpecialFrameChipOneTextBlock.Text = $"方向 · {entry.DirectionLabel}";
            SpecialFrameChipTwoTextBlock.Text = $"载荷 · {entry.LengthLabel}";
            SpecialFrameChipThreeTextBlock.Text = "字节 · 查看 HEX 视图";
            return;
        }

        if (entry.Type == "Pong")
        {
            ApplySpecialFrameVisual("#EEF4FF", "#D8E7FF", "#2F7CF6", "PONG");
            SpecialFrameTitleTextBlock.Text = "Pong 应答帧";
            SpecialFrameDescriptionTextBlock.Text = "Pong 一般用来响应 Ping，也可携带少量诊断载荷。";
            SpecialFrameChipOneTextBlock.Text = $"方向 · {entry.DirectionLabel}";
            SpecialFrameChipTwoTextBlock.Text = $"载荷 · {entry.LengthLabel}";
            SpecialFrameChipThreeTextBlock.Text = "字节 · 查看 HEX 视图";
            return;
        }

        if (entry.Type == "Close")
        {
            ParseCloseFrame(snapshot.Bytes, out string codeText, out string reasonText);
            ApplySpecialFrameVisual("#FDECEC", "#F7CDCF", "#D92D20", "CLOSE");
            SpecialFrameTitleTextBlock.Text = "Close 关闭帧";
            SpecialFrameDescriptionTextBlock.Text = "Close 会请求关闭当前 WebSocket 连接，可带关闭码与原因。";
            SpecialFrameChipOneTextBlock.Text = $"方向 · {entry.DirectionLabel}";
            SpecialFrameChipTwoTextBlock.Text = $"关闭码 · {codeText}";
            SpecialFrameChipThreeTextBlock.Text = $"原因 · {reasonText}";
            return;
        }

        if (entry.Icon == "websocket_connect")
        {
            ApplySpecialFrameVisual("#F3EDFF", "#E2D7FF", "#7B4DE0", "OPEN");
            SpecialFrameTitleTextBlock.Text = "连接建立事件";
            SpecialFrameDescriptionTextBlock.Text = "该会话的 WebSocket 连接已经建立，后续开始进入消息收发阶段。";
            SpecialFrameChipOneTextBlock.Text = $"方向 · {entry.DirectionLabel}";
            SpecialFrameChipTwoTextBlock.Text = $"时间 · {entry.Time}";
            SpecialFrameChipThreeTextBlock.Text = "状态 · 已连接";
            return;
        }

        if (entry.Icon == "websocket_close")
        {
            ApplySpecialFrameVisual("#FDECEC", "#F7CDCF", "#D92D20", "DONE");
            SpecialFrameTitleTextBlock.Text = "连接断开事件";
            SpecialFrameDescriptionTextBlock.Text = "该连接已经关闭，之后不再有新的帧进入当前会话。";
            SpecialFrameChipOneTextBlock.Text = $"方向 · {entry.DirectionLabel}";
            SpecialFrameChipTwoTextBlock.Text = $"时间 · {entry.Time}";
            SpecialFrameChipThreeTextBlock.Text = "状态 · 已断开";
            return;
        }

        SpecialFrameCard.Visibility = Visibility.Collapsed;
    }

    private void ApplySpecialFrameVisual(string background, string border, string foreground, string glyph)
    {
        SpecialFrameIconBorder.Background = CreateBrush(background);
        SpecialFrameIconBorder.BorderBrush = CreateBrush(border);
        SpecialFrameGlyphTextBlock.Foreground = CreateBrush(foreground);
        SpecialFrameGlyphTextBlock.Text = glyph;
    }

    private void ResetReplayForSelection(SocketEntry entry, SocketPayloadSnapshot? snapshot)
    {
        _suspendReplayEditorEvents = true;
        SetReplayDirection(entry.Icon == "下行" ? "Client" : "Server");
        SetReplayWsType(DefaultWsTypeForEntry(entry));
        SetReplayEncoding(DefaultEncodingForEntry(entry));
        _suspendReplayEditorEvents = false;
        _replayEditorDirty = false;
        FillReplayEditorFromSelected();
        UpdateReplayHint();
        UpdateReplayEditorStatus();
        ReplayActionStatusTextBlock.Text = "";
        ParserStatusTextBlock.Text = snapshot is null
            ? "选择消息后会自动填入编辑区，可切换 HEX / Base64 后解析。"
            : "当前帧已自动填入编辑区，可切换 HEX / Base64 后解析。";
    }

    private void FillReplayEditorFromSelected()
    {
        if (_currentEntry is null)
        {
            ClearReplayEditor();
            return;
        }

        string sendType = GetSelectedComboTag(ReplayEncodingComboBox, DefaultEncodingForEntry(_currentEntry));
        string text = BuildReplayEditorText(_currentEntry, _currentPayloadSnapshot, sendType);

        _suspendReplayEditorEvents = true;
        ReplayEditorTextBox.Text = text;
        _suspendReplayEditorEvents = false;
        _replayEditorDirty = false;
        UpdateReplayEditorStatus();
    }

    private void ClearReplayEditor()
    {
        _suspendReplayEditorEvents = true;
        SetDefaultReplaySelections();
        ReplayEditorTextBox.Text = "";
        _suspendReplayEditorEvents = false;
        _replayEditorDirty = false;
        ReplayHintTextBlock.Text = "文本帧默认 UTF8，二进制与控制帧默认 HEX。";
        ReplayEditorStatusTextBlock.Text = "等待选择消息后填充编辑区。";
        ReplayActionStatusTextBlock.Text = "";
    }

    private static string BuildReplayEditorText(SocketEntry entry, SocketPayloadSnapshot? snapshot, string sendType)
    {
        if (snapshot is null)
        {
            return entry.PreviewText;
        }

        if (sendType == "HEX")
        {
            return FormatEditableHex(snapshot.Bytes);
        }

        if (sendType == "Base64")
        {
            return snapshot.Bytes.Length == 0 ? "" : Convert.ToBase64String(snapshot.Bytes);
        }

        if (sendType == "GBK")
        {
            return string.IsNullOrWhiteSpace(snapshot.RawText) ? snapshot.DisplayText : snapshot.RawText;
        }

        return entry.IsBinaryFrame || entry.IsControlFrame
            ? BuildUtf8Probe(snapshot.Bytes)
            : snapshot.RawText;
    }

    private void UpdateReplayHint()
    {
        string wsType = GetSelectedComboTag(ReplayWsTypeComboBox, "Text");
        string sendType = GetSelectedComboTag(ReplayEncodingComboBox, "UTF8");

        ReplayHintTextBlock.Text = wsType switch
        {
            "Ping" => "Ping 建议使用 HEX 或小文本载荷，便于模拟心跳探测。",
            "Pong" => "Pong 常用于模拟应答，选择当前帧后可直接修改再发送。",
            "Close" => "Close 建议使用 HEX，前 2 字节通常是关闭码，后续可带原因文本。",
            "Binary" when sendType == "HEX" => "Binary + HEX 最适合精确修改原始字节内容。",
            "Text" when sendType == "UTF8" => "Text + UTF8 适合直接编辑文本消息。",
            _ => "选择消息后会自动填入编辑区，可直接修改后按当前方向与帧类型发送。"
        };
    }

    private void UpdateReplayEditorStatus()
    {
        try
        {
            string sendType = GetSelectedComboTag(ReplayEncodingComboBox, "UTF8");
            if (sendType == "GBK")
            {
                ReplayEditorStatusTextBlock.Text = $"当前内容 {ReplayEditorTextBox.Text.Length:N0} 字符 · 发送时由核心转为 GBK";
                return;
            }

            byte[] bytes = DecodeReplayEditorBytes();
            ReplayEditorStatusTextBlock.Text = bytes.Length == 0
                ? "当前内容为空，WebSocket 允许发送空载荷。"
                : $"当前内容可生成 {bytes.Length:N0} Bytes · 编码 {sendType}";
        }
        catch (Exception exception)
        {
            ReplayEditorStatusTextBlock.Text = exception.Message;
        }
    }

    private void ClearProtobufView()
    {
        PayloadProtobufViewer.JsonText = "";
        PayloadProtobufEmptyPanel.Visibility = Visibility.Visible;
        PayloadProtobufStatusTextBlock.Text = "设置头部字节后，点击上方“解析当前帧”";
        UpdateProtobufPayloadSummary();
    }

    private void ResetProtobufState()
    {
        _loadedProtobufSchemaPath = "";
        ProtoSchemaPathTextBox.Text = "";
        ApplyProtoMessageTypes(Array.Empty<string>(), "");
        ProtobufSkipTextBox.Text = "0";
        ClearProtobufView();
        ParserStatusTextBlock.Text = "已清空 Protobuf 配置。";
        UpdateProtobufPayloadSummary();
    }

    private bool TryGetSkipBytes(out int skip)
    {
        return int.TryParse(ProtobufSkipTextBox.Text?.Trim(), out skip);
    }

    private void UpdateProtobufPayloadSummary()
    {
        if (ProtobufPayloadSummaryTextBlock is null)
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = GetProtobufPayloadBytes();
        }
        catch (Exception exception)
        {
            ProtobufPayloadSummaryTextBlock.Text = "载荷无法解析";
            ProtobufHeaderPreviewTextBlock.Text = "头部 HEX：无";
            ProtobufBodyPreviewTextBlock.Text = $"消息体：{exception.Message}";
            return;
        }

        int total = bytes.Length;
        if (!TryGetSkipBytes(out int skip))
        {
            ProtobufPayloadSummaryTextBlock.Text = $"载荷 {total:N0} Bytes · 头部无效";
            ProtobufHeaderPreviewTextBlock.Text = "头部 HEX：请输入整数";
            ProtobufBodyPreviewTextBlock.Text = "消息体 HEX：等待有效头部长度";
            return;
        }

        int normalizedSkip = Math.Clamp(skip, 0, total);
        int bodyLength = Math.Max(total - normalizedSkip, 0);
        string rangeHint = skip == normalizedSkip ? "" : " · 已按范围修正";
        ProtobufPayloadSummaryTextBlock.Text = $"载荷 {total:N0} Bytes · 头部 {normalizedSkip:N0} · 消息体 {bodyLength:N0}{rangeHint}";
        ProtobufHeaderPreviewTextBlock.Text = normalizedSkip > 0
            ? $"头部 HEX：{FormatHexPreview(bytes, 0, normalizedSkip, 16)}"
            : "头部 HEX：无";
        ProtobufBodyPreviewTextBlock.Text = bodyLength > 0
            ? $"消息体 HEX：{FormatHexPreview(bytes, normalizedSkip, bodyLength, 24)}"
            : "消息体 HEX：无";
    }

    private static string FormatHexPreview(byte[] bytes, int offset, int count, int maxBytes)
    {
        if (bytes.Length == 0 || count <= 0 || offset < 0 || offset >= bytes.Length)
        {
            return "无";
        }

        int safeCount = Math.Min(Math.Min(count, maxBytes), bytes.Length - offset);
        StringBuilder builder = new();
        for (int index = 0; index < safeCount; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[offset + index].ToString("X2"));
        }

        if (count > safeCount && offset + safeCount < bytes.Length)
        {
            builder.Append(" ...");
        }

        return builder.ToString();
    }

    private byte[] DecodeReplayEditorBytes()
    {
        string sendType = GetSelectedComboTag(ReplayEncodingComboBox, "UTF8");
        string text = ReplayEditorTextBox.Text ?? "";
        return sendType switch
        {
            "HEX" => DecodeHexString(text),
            "Base64" => DecodeBase64String(text),
            "UTF8" => Encoding.UTF8.GetBytes(text),
            "GBK" => throw new InvalidOperationException("GBK 模式暂不支持本地解析，请直接发送验证。"),
            _ => Encoding.UTF8.GetBytes(text)
        };
    }

    private bool TryGetCachedPayload(SocketEntry entry, out SocketPayloadSnapshot snapshot)
    {
        return _payloadCache.TryGetValue(BuildPayloadCacheKey(entry), out snapshot!);
    }

    private string BuildPayloadCacheKey(SocketEntry entry)
    {
        int theology = entry.Theology > 0 ? entry.Theology : Theology;
        return $"{theology}:{entry.Index}";
    }

    private SocketEntry? GetSelectedEntry()
    {
        return _currentEntry ?? SelectedEntry ?? (FramesList.SelectedItem as SocketEntry);
    }

    private int CountVisibleEntries()
    {
        if (_entriesView is null)
        {
            return CountEntries(Entries);
        }

        int count = 0;
        foreach (object? _ in _entriesView)
        {
            count++;
        }

        return count;
    }

    private FrameStatistics CountFrameStatistics()
    {
        if (Entries is null)
        {
            return new FrameStatistics();
        }

        int total = 0;
        int upstream = 0;
        int downstream = 0;
        int text = 0;

        foreach (object? item in Entries)
        {
            if (item is not SocketEntry entry)
            {
                continue;
            }

            total++;
            if (entry.Icon == "上行")
            {
                upstream++;
            }
            else if (entry.Icon == "下行")
            {
                downstream++;
            }

            if (entry.IsTextFrame)
            {
                text++;
            }
        }

        return new FrameStatistics(total, upstream, downstream, text);
    }

    private static bool EntryMatchesQuery(SocketEntry entry, string query)
    {
        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0 || tokens.All(token => EntryMatchesToken(entry, token));
    }

    private static bool EntryMatchesToken(SocketEntry entry, string token)
    {
        return entry.PreviewText.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.TypeLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.DirectionLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.FrameTitle.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.FrameGroupLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.OpcodeLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.RouteLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.LengthLabel.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.Time.Contains(token, StringComparison.OrdinalIgnoreCase)
            || entry.DisplayIndex.ToString().Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManualReplayFrame(SocketEntry entry)
    {
        return entry.PreviewText.Contains("[手动", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildFilterStateText(int visible, int total)
    {
        if (total == 0)
        {
            return "当前没有可显示的消息";
        }

        List<string> segments = new();
        if (_currentKindFilter != FilterAll)
        {
            segments.Add(_currentKindFilter switch
            {
                FilterText => "仅文本帧",
                FilterBinary => "仅二进制帧",
                FilterControl => "仅控制帧",
                _ => "全部消息"
            });
        }

        if (_currentDirectionFilter != DirectionAll)
        {
            segments.Add(_currentDirectionFilter == DirectionSend ? "仅发送" : "仅接收");
        }

        if (_manualOnly)
        {
            segments.Add("手动重放");
        }

        if (_nonEmptyOnly)
        {
            segments.Add("有载荷");
        }

        string query = SearchTextBox.Text?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(query))
        {
            segments.Add($"搜索：{query}");
        }

        return segments.Count == 0
            ? $"当前显示全部消息 · {visible:N0} 条"
            : $"当前显示 {visible:N0}/{total:N0} 条 · {string.Join(" · ", segments)}";
    }

    private void SetKindFilter(string filter)
    {
        _currentKindFilter = filter;
        AllFilterButton.IsChecked = filter == FilterAll;
        TextFilterButton.IsChecked = filter == FilterText;
        BinaryFilterButton.IsChecked = filter == FilterBinary;
        ControlFilterButton.IsChecked = filter == FilterControl;
        UpdateSearchAffordance();
    }

    private void SetDirectionFilter(string filter)
    {
        _currentDirectionFilter = filter;
        SendDirectionFilterButton.IsChecked = filter == DirectionSend;
        ReceiveDirectionFilterButton.IsChecked = filter == DirectionReceive;
        UpdateSearchAffordance();
    }

    private void SetQuickFilters(bool manualOnly, bool nonEmptyOnly)
    {
        _manualOnly = manualOnly;
        _nonEmptyOnly = nonEmptyOnly;
        ManualReplayFilterButton.IsChecked = manualOnly;
        NonEmptyFilterButton.IsChecked = nonEmptyOnly;
        UpdateSearchAffordance();
    }

    private void SetDefaultReplaySelections()
    {
        SetReplayDirection("Server");
        SetReplayWsType("Text");
        SetReplayEncoding("UTF8");
    }

    private void SetReplayDirection(string tag)
    {
        SetComboBoxSelectionByTag(ReplayDirectionComboBox, tag);
    }

    private void SetReplayWsType(string tag)
    {
        SetComboBoxSelectionByTag(ReplayWsTypeComboBox, tag);
    }

    private void SetReplayEncoding(string tag)
    {
        SetComboBoxSelectionByTag(ReplayEncodingComboBox, tag);
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

    private async Task<bool> EnsureProtobufSchemaLoadedAsync(MainWindowViewModel viewModel, bool forceReload = false)
    {
        string schemaPath = GetProtoSchemaPath();
        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            ParserStatusTextBlock.Text = "未填写结构路径，将使用通用 Protobuf 解析。";
            return false;
        }

        string normalizedCurrent = NormalizeSchemaPath(schemaPath);
        if (!forceReload
            && !string.IsNullOrWhiteSpace(_loadedProtobufSchemaPath)
            && string.Equals(_loadedProtobufSchemaPath, normalizedCurrent, StringComparison.OrdinalIgnoreCase)
            && ProtoMessageTypeComboBox.Items.Count > 0)
        {
            return true;
        }

        string preferredMessageType = GetProtoMessageType();
        ParserStatusTextBlock.Text = "正在导入 Protobuf 描述...";
        (bool ok, string directory, IReadOnlyList<string> messages, string error) = await viewModel.ImportProtobufSchemaAsync(schemaPath);
        if (!ok || messages.Count == 0)
        {
            _loadedProtobufSchemaPath = "";
            ApplyProtoMessageTypes(Array.Empty<string>(), "");
            ParserStatusTextBlock.Text = string.IsNullOrWhiteSpace(error) ? "导入 Protobuf 描述失败。" : error;
            PayloadProtobufStatusTextBlock.Text = "结构导入失败。";
            return false;
        }

        string effectivePath = string.IsNullOrWhiteSpace(directory) ? schemaPath : directory;
        _loadedProtobufSchemaPath = NormalizeSchemaPath(effectivePath);
        ProtoSchemaPathTextBox.Text = effectivePath;
        ApplyProtoMessageTypes(messages, preferredMessageType);
        ParserStatusTextBlock.Text = $"已导入 {messages.Count:N0} 个消息类型。";
        PayloadProtobufStatusTextBlock.Text = $"结构已就绪 · {messages.Count:N0} 个消息类型";
        return true;
    }

    private void ApplyProtoMessageTypes(IReadOnlyList<string> messages, string? preferredMessageType)
    {
        string preferred = preferredMessageType?.Trim() ?? "";
        ProtoMessageTypeComboBox.ItemsSource = messages;

        if (!string.IsNullOrWhiteSpace(preferred) && messages.Contains(preferred))
        {
            ProtoMessageTypeComboBox.SelectedItem = preferred;
            ProtoMessageTypeComboBox.Text = preferred;
            return;
        }

        if (messages.Count == 1)
        {
            ProtoMessageTypeComboBox.SelectedItem = messages[0];
            ProtoMessageTypeComboBox.Text = messages[0];
            return;
        }

        ProtoMessageTypeComboBox.SelectedItem = null;
        ProtoMessageTypeComboBox.Text = preferred;
    }

    private string GetProtoSchemaPath()
    {
        return ProtoSchemaPathTextBox.Text?.Trim() ?? "";
    }

    private string GetProtoMessageType()
    {
        return (ProtoMessageTypeComboBox.SelectedItem as string ?? ProtoMessageTypeComboBox.Text)?.Trim() ?? "";
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

    private static string GetSelectedComboTag(ComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    }

    private int CountEntries(Func<SocketEntry, bool> predicate)
    {
        if (Entries is null)
        {
            return 0;
        }

        int count = 0;
        foreach (object? item in Entries)
        {
            if (item is SocketEntry entry && predicate(entry))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountEntries(IEnumerable? entries)
    {
        if (entries is null)
        {
            return 0;
        }

        if (entries is ICollection collection)
        {
            return collection.Count;
        }

        int count = 0;
        foreach (object? _ in entries)
        {
            count++;
        }

        return count;
    }

    private static string DefaultWsTypeForEntry(SocketEntry entry)
    {
        return entry.TypeLabel switch
        {
            "Text" => "Text",
            "Binary" => "Binary",
            "Ping" => "Ping",
            "Pong" => "Pong",
            "Close" => "Close",
            _ => entry.IsBinaryFrame || entry.IsControlFrame ? "Binary" : "Text"
        };
    }

    private static string DefaultEncodingForEntry(SocketEntry entry)
    {
        return entry.IsBinaryFrame || entry.IsControlFrame ? "HEX" : "UTF8";
    }

    private static string DefaultEncodingForWsType(string wsType)
    {
        return wsType == "Text" ? "UTF8" : "HEX";
    }

    private static string BuildUtf8Probe(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        string text = Encoding.UTF8.GetString(bytes);
        return text.Replace("\0", "");
    }

    private static string BuildPayloadPreview(byte[] bytes, string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && !LooksBinary(bytes))
        {
            string compact = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length > 24 ? compact[..24] + "..." : compact;
        }

        if (bytes.Length == 0)
        {
            return "无载荷";
        }

        string hex = Convert.ToHexString(bytes.Length > 8 ? bytes[..8] : bytes);
        return hex.Length > 20 ? hex[..20] + "..." : hex;
    }

    private static void ParseCloseFrame(byte[] bytes, out string codeText, out string reasonText)
    {
        if (bytes.Length < 2)
        {
            codeText = "无";
            reasonText = "未携带关闭码";
            return;
        }

        int code = (bytes[0] << 8) | bytes[1];
        string reason = bytes.Length > 2 ? Encoding.UTF8.GetString(bytes, 2, bytes.Length - 2).Trim() : "";
        string description = code switch
        {
            1000 => "正常关闭",
            1001 => "终端离开",
            1002 => "协议错误",
            1003 => "不支持的数据",
            1007 => "无效载荷",
            1008 => "策略冲突",
            1009 => "消息过大",
            1010 => "缺少扩展",
            1011 => "服务内部错误",
            1012 => "服务重启",
            1013 => "稍后重试",
            _ => "未知关闭码"
        };

        codeText = $"{code} · {description}";
        reasonText = string.IsNullOrWhiteSpace(reason) ? "无原因" : reason;
    }

    private static string FormatEditableHex(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        StringBuilder builder = new();
        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            if (offset > 0)
            {
                builder.AppendLine();
            }

            int count = Math.Min(16, bytes.Length - offset);
            for (int index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[offset + index].ToString("X2"));
            }
        }

        return builder.ToString();
    }

    private static string FormatHexLines(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        StringBuilder builder = new();
        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            int count = Math.Min(16, bytes.Length - offset);
            builder.AppendLine(BitConverter.ToString(bytes, offset, count).Replace("-", " "));
        }

        return builder.ToString().TrimEnd();
    }

    private static byte[] DecodeHexString(string text)
    {
        string cleaned = new string((text ?? "").Where(current => !char.IsWhiteSpace(current)).ToArray());
        if (string.IsNullOrEmpty(cleaned))
        {
            return Array.Empty<byte>();
        }

        if (cleaned.Length % 2 != 0)
        {
            throw new InvalidOperationException("HEX 内容长度必须为偶数。");
        }

        try
        {
            return Convert.FromHexString(cleaned);
        }
        catch
        {
            throw new InvalidOperationException("HEX 内容无效，请检查是否包含非法字符。");
        }
    }

    private static byte[] DecodeBase64String(string text)
    {
        string cleaned = (text ?? "").Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(cleaned);
        }
        catch
        {
            throw new InvalidOperationException("Base64 内容无效，请检查填入的数据。");
        }
    }

    private static bool LooksBinary(byte[] bytes)
    {
        int inspected = Math.Min(bytes.Length, 256);
        int controlCount = 0;
        for (int index = 0; index < inspected; index++)
        {
            byte current = bytes[index];
            if (current == 0)
            {
                return true;
            }

            if (current < 8 || current is > 13 and < 32)
            {
                controlCount++;
            }
        }

        return inspected > 0 && controlCount > inspected / 4;
    }

    private static bool CopyText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static T? FindParent<T>(DependencyObject? dependencyObject) where T : DependencyObject
    {
        while (dependencyObject is not null)
        {
            if (dependencyObject is T typed)
            {
                return typed;
            }

            dependencyObject = dependencyObject switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(dependencyObject),
                ContentElement contentElement => ContentOperations.GetParent(contentElement) ?? (contentElement as FrameworkContentElement)?.Parent,
                _ => null
            };
        }

        return null;
    }

    private void CommitSearchTerm()
    {
        string query = SearchTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        int existingIndex = _recentSearchTerms.FindIndex(term => string.Equals(term, query, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _recentSearchTerms.RemoveAt(existingIndex);
        }

        _recentSearchTerms.Insert(0, query);
        if (_recentSearchTerms.Count > MaxRecentSearchTerms)
        {
            _recentSearchTerms.RemoveRange(MaxRecentSearchTerms, _recentSearchTerms.Count - MaxRecentSearchTerms);
        }

        RefreshRecentSearchChips();
    }

    private void RefreshRecentSearchChips()
    {
        if (RecentSearchPanel is null || RecentSearchWrapPanel is null)
        {
            return;
        }

        RecentSearchPanel.Visibility = _recentSearchTerms.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentSearchWrapPanel.Children.Clear();

        foreach (string term in _recentSearchTerms)
        {
            Button button = new()
            {
                Content = term,
                Tag = term,
                Style = (Style)FindResource("SearchChipButtonStyle"),
                MaxWidth = 118
            };
            button.Click += RecentSearchChip_Click;
            RecentSearchWrapPanel.Children.Add(button);
        }
    }

    private void WebSocketMessagesControl_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key == Key.Enter && IsSearchFlyoutOpen)
        {
            if (SearchTextBox.IsKeyboardFocusWithin)
            {
                CommitSearchTerm();
                keyEventArgs.Handled = true;
                return;
            }

            FocusSearchBox();
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.Escape && IsSearchFlyoutOpen)
        {
            SetSearchFlyoutOpen(false);
            keyEventArgs.Handled = true;
        }
    }

    private void FocusSearchBox(bool selectAll = true)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SearchTextBox.Focus();
            if (selectAll)
            {
                SearchTextBox.SelectAll();
            }
            else
            {
                SearchTextBox.CaretIndex = SearchTextBox.Text.Length;
            }
        }, DispatcherPriority.Input);
    }

    private void UpdateSearchAffordance()
    {
        bool active = HasActiveFrameFilter();
        bool popupOpen = IsSearchFlyoutOpen;
        SearchActiveDot.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        SearchToggleButton.Background = popupOpen || active ? CreateBrush("#EEF4FF") : Brushes.Transparent;
        SearchToggleButton.BorderBrush = popupOpen || active ? CreateBrush("#D8E7FF") : Brushes.Transparent;
        SearchToggleGlyphTextBlock.Foreground = popupOpen || active ? CreateBrush("#2F7CF6") : CreateBrush("#6B7C93");
        SearchHintTextBlock.Text = popupOpen
            ? "Enter 记录搜索，Esc 关闭面板"
            : active
                ? "当前已有筛选条件生效"
                : "Ctrl+F 打开快速筛选";
    }

    private bool HasActiveFrameFilter()
    {
        return !string.IsNullOrWhiteSpace(SearchTextBox?.Text)
            || _currentKindFilter != FilterAll
            || _currentDirectionFilter != DirectionAll
            || _manualOnly
            || _nonEmptyOnly;
    }

    private bool IsSearchFlyoutOpen => SearchFlyoutPanel.Visibility == Visibility.Visible;

    private void SetSearchFlyoutOpen(bool isOpen)
    {
        if (!isOpen)
        {
            CommitSearchTerm();
        }

        SearchFlyoutPanel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        UpdateSearchAffordance();
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    private sealed record SocketPayloadSnapshot(
        byte[] Bytes,
        string RawText,
        string DisplayText,
        string Json,
        bool HasJson,
        bool IsBinary,
        string PayloadKind,
        string PreviewMode,
        string StatusText,
        string HintText);

    private enum ReplayAlertKind
    {
        Error,
        Success
    }

    private readonly record struct FrameStatistics(int Total = 0, int Upstream = 0, int Downstream = 0, int Text = 0);
}
