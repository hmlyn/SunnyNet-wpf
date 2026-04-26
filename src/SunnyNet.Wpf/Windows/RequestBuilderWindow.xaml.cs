using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.Services;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Windows;

public partial class RequestBuilderWindow : Window
{
    private static readonly HashSet<string> ContentHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Allow",
        "Content-Disposition",
        "Content-Encoding",
        "Content-Language",
        "Content-Length",
        "Content-Location",
        "Content-MD5",
        "Content-Range",
        "Content-Type",
        "Expires",
        "Last-Modified"
    };

    private readonly MainWindowViewModel _viewModel;
    private readonly CaptureEntry? _sourceEntry;
    private RequestEditorMode _headerMode = RequestEditorMode.Raw;
    private RequestEditorMode _bodyMode = RequestEditorMode.Raw;
    private string _bodyFormat = "文本";
    private bool _isLoadingHistory;
    private bool _isSwitchingTabs;
    private bool _isChangingBodyFormat;
    private bool _controlsReady;
    private bool _bodyPropertiesCanSync = true;
    private bool _bodyJsonCanSync = true;
    private Point _dragStartPoint;
    private RequestBuilderFieldRow? _dragRow;

    public RequestBuilderWindow(MainWindowViewModel viewModel, CaptureEntry? sourceEntry = null)
    {
        _viewModel = viewModel;
        _sourceEntry = sourceEntry;
        HistoryItems = new ObservableCollection<RequestBuilderHistoryItem>(RequestBuilderHistoryStore.Load());
        InitializeComponent();
        _controlsReady = true;
        _bodyFormat = GetComboText(BodyFormatComboBox);
        UpdateBodyAvailability();
        Loaded += RequestBuilderWindow_Loaded;
    }

    public ObservableCollection<RequestBuilderHistoryItem> HistoryItems { get; }

    public ObservableCollection<RequestBuilderFieldRow> HeaderFields { get; } = new();

    public ObservableCollection<RequestBuilderFieldRow> BodyFields { get; } = new();

    private async void RequestBuilderWindow_Loaded(object sender, RoutedEventArgs routedEventArgs)
    {
        if (_sourceEntry is null)
        {
            return;
        }

        await LoadFromSessionAsync(_sourceEntry);
    }

    private async Task LoadFromSessionAsync(CaptureEntry entry)
    {
        BuilderSummaryTextBlock.Text = $"正在从会话 #{entry.Index} 读取请求...";
        try
        {
            JsonElement? result = await _viewModel.InvokeBackendCommandAsync("HTTP请求获取", new { Theology = entry.Theology });
            if (result is null || result.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                BuilderSummaryTextBlock.Text = "未读取到会话详情。";
                return;
            }

            JsonElement data = result.Value;
            SetMethod(GetString(data, "Method", entry.Method));
            UrlTextBox.Text = GetString(data, "URL", entry.Url);
            SetComboText(HttpVersionComboBox, "HTTP/1.1");
            HeadersTextBox.Text = FormatHeaders(GetProperty(data, "Header"));
            SetBodyFromBytes(DecodePayloadBytes(GetProperty(data, "Body")));
            ResetStructuredEditors();
            BuilderSummaryTextBlock.Text = $"已从会话 #{entry.Index} 回填请求，可修改后发送。";
        }
        catch (Exception exception)
        {
            BuilderSummaryTextBlock.Text = $"读取会话失败：{exception.Message}";
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await SendCurrentRequestAsync();
    }

    private async Task SendCurrentRequestAsync()
    {
        string method = GetMethodText();
        string url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(method))
        {
            BuilderSummaryTextBlock.Text = "发送失败：请输入请求方法。";
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            BuilderSummaryTextBlock.Text = "发送失败：请输入完整 URL。";
            return;
        }

        try
        {
            CommitFieldGrids();
            bool allowBody = AllowsRequestBody(method);
            byte[] body = allowBody ? BuildBodyBytesFromCurrentView() : Array.Empty<byte>();
            string headers = BuildHeaderTextFromCurrentView();
            BuilderSummaryTextBlock.Text = $"正在发送 {method} {url}";

            JsonElement? result = await _viewModel.InvokeBackendCommandAsync("构造请求发送", new
            {
                Method = method,
                URL = url,
                HttpVersion = GetComboText(HttpVersionComboBox),
                Headers = headers,
                BodyBase64 = Convert.ToBase64String(body)
            });
            if (result is null)
            {
                BuilderSummaryTextBlock.Text = "发送失败：Sunny 后台无返回。";
                return;
            }

            JsonElement data = result.Value;
            bool ok = GetBool(data, "Ok");
            if (!ok)
            {
                string error = GetString(data, "Err", "Sunny 后台发送失败。");
                BuilderSummaryTextBlock.Text = $"发送失败：{error}";
                return;
            }

            BuilderSummaryTextBlock.Text =
                $"发送完成：{GetInt(data, "StatusCode")} {GetString(data, "Status")} · {GetString(data, "Proto")} · {GetInt(data, "Length"):N0} Bytes";
            AddCurrentToHistory();
        }
        catch (Exception exception)
        {
            BuilderSummaryTextBlock.Text = $"发送失败：{exception.Message}";
        }
    }

    private void AddCurrentToHistory()
    {
        string method = GetMethodText();
        bool allowBody = AllowsRequestBody(method);
        BodyBuildResult body = allowBody
            ? BuildBodyTextFromCurrentView()
            : new BodyBuildResult("", "文本");
        RequestBuilderHistoryItem item = new()
        {
            Method = method,
            Url = UrlTextBox.Text.Trim(),
            Headers = BuildHeaderTextFromCurrentView(),
            Body = body.Text,
            BodyFormat = body.Format,
            HttpVersion = GetComboText(HttpVersionComboBox)
        };

        ReplaceHistory(RequestBuilderHistoryStore.Add(item));
    }

    private void ClearCurrent_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SetMethod("GET");
        SetComboText(HttpVersionComboBox, "HTTP/1.1");
        UrlTextBox.Clear();
        HeadersTextBox.Clear();
        BodyTextBox.Clear();
        SetBodyFormat("文本");
        ResetStructuredEditors();
        BuilderSummaryTextBlock.Text = "已清空当前构造内容。";
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RequestBuilderHistoryStore.Clear();
        HistoryItems.Clear();
        BuilderSummaryTextBlock.Text = "历史记录已清空。";
    }

    private void HistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs routedEventArgs)
    {
        if (_isLoadingHistory || HistoryListBox.SelectedItem is not RequestBuilderHistoryItem item)
        {
            return;
        }

        LoadHistoryItem(item);
    }

    private void HistoryListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (FindVisualParent<ListBoxItem>(mouseButtonEventArgs.OriginalSource as DependencyObject) is not { } item)
        {
            return;
        }

        item.IsSelected = true;
    }

    private void HistoryContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        bool hasItem = HistoryListBox.SelectedItem is RequestBuilderHistoryItem;
        HistoryNotesMenuItem.IsEnabled = hasItem;
        HistoryRemoveMenuItem.IsEnabled = hasItem;
    }

    private void HistoryNotes_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (HistoryListBox.SelectedItem is not RequestBuilderHistoryItem item)
        {
            return;
        }

        SessionNotesWindow notesWindow = new(item.Notes, 1)
        {
            Owner = this
        };
        if (notesWindow.ShowDialog() != true)
        {
            return;
        }

        item.Notes = notesWindow.NotesText;
        RequestBuilderHistoryStore.Save(HistoryItems);
    }

    private void HistoryRemove_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (HistoryListBox.SelectedItem is not RequestBuilderHistoryItem item)
        {
            return;
        }

        HistoryItems.Remove(item);
        RequestBuilderHistoryStore.Save(HistoryItems);
        BuilderSummaryTextBlock.Text = "已移除历史记录。";
    }

    private void LoadHistoryItem(RequestBuilderHistoryItem item)
    {
        SetMethod(item.Method);
        UrlTextBox.Text = item.Url;
        HeadersTextBox.Text = item.Headers;
        BodyTextBox.Text = item.Body;
        SetComboText(HttpVersionComboBox, string.IsNullOrWhiteSpace(item.HttpVersion) ? "HTTP/1.1" : item.HttpVersion);
        SetBodyFormat(string.IsNullOrWhiteSpace(item.BodyFormat) ? "文本" : item.BodyFormat);
        ResetStructuredEditors();
        BuilderSummaryTextBlock.Text = "已载入历史请求。";
    }

    private void MethodComboBox_MethodChanged(object sender, RoutedEventArgs routedEventArgs)
    {
        UpdateBodyAvailability();
        Dispatcher.BeginInvoke(UpdateBodyAvailability, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ReplaceHistory(IEnumerable<RequestBuilderHistoryItem> items)
    {
        _isLoadingHistory = true;
        try
        {
            HistoryItems.Clear();
            foreach (RequestBuilderHistoryItem item in items)
            {
                HistoryItems.Add(item);
            }
        }
        finally
        {
            _isLoadingHistory = false;
        }
    }

    private void HeaderTabControl_SelectionChanged(object sender, SelectionChangedEventArgs routedEventArgs)
    {
        if (!_controlsReady || _isSwitchingTabs || !ReferenceEquals(routedEventArgs.Source, HeaderTabControl))
        {
            return;
        }

        CommitFieldGrids();
        RequestEditorMode nextMode = GetSelectedMode(HeaderTabControl);
        string headers = BuildHeaderTextFromMode(_headerMode);
        HeadersTextBox.Text = headers;
        _headerMode = nextMode;

        if (nextMode == RequestEditorMode.Properties)
        {
            ReplaceRows(HeaderFields, ParseHeaderRows(headers));
        }
    }

    private void BodyTabControl_SelectionChanged(object sender, SelectionChangedEventArgs routedEventArgs)
    {
        if (!_controlsReady || _isSwitchingTabs || !ReferenceEquals(routedEventArgs.Source, BodyTabControl))
        {
            return;
        }

        CommitFieldGrids();
        RequestEditorMode nextMode = GetSelectedMode(BodyTabControl);
        BodyBuildResult body = BuildBodyTextFromMode(_bodyMode);
        BodyTextBox.Text = body.Text;
        SetBodyFormat(body.Format);
        _bodyMode = nextMode;
        UpdateBodyAvailability();

        if (nextMode == RequestEditorMode.Properties)
        {
            BodyJsonEditor.AllowCreateNodes = true;
            if (!CanLoadBodyProperties(BodyTextBox.Text, GetComboText(BodyFormatComboBox)))
            {
                BodyFields.Clear();
                _bodyPropertiesCanSync = string.IsNullOrWhiteSpace(BodyTextBox.Text);
                BuilderSummaryTextBlock.Text = "Body 是 JSON 或非文本格式，属性框不载入数据。";
            }
            else
            {
                ReplaceRows(BodyFields, ParseBodyFormRows(BodyTextBox.Text));
                _bodyPropertiesCanSync = true;
            }
        }
        else if (nextMode == RequestEditorMode.Json)
        {
            if (string.IsNullOrWhiteSpace(BodyTextBox.Text))
            {
                BodyJsonEditor.AllowCreateNodes = true;
                BodyJsonEditor.InitializeObject();
                _bodyJsonCanSync = true;
                BuilderSummaryTextBlock.Text = "已初始化空 JSON 对象。";
            }
            else if (!BodyJsonEditor.TryLoadJson(BodyTextBox.Text))
            {
                BodyJsonEditor.AllowCreateNodes = false;
                BodyJsonEditor.Clear();
                _bodyJsonCanSync = false;
                BuilderSummaryTextBlock.Text = "Body 不是有效 JSON，已保留原文内容。";
            }
            else
            {
                BodyJsonEditor.AllowCreateNodes = true;
                _bodyJsonCanSync = true;
            }
        }
    }

    private void BodyFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs routedEventArgs)
    {
        if (!_controlsReady || _isChangingBodyFormat || BodyFormatComboBox is null || _bodyMode != RequestEditorMode.Raw)
        {
            return;
        }

        string nextFormat = GetComboText(BodyFormatComboBox);
        if (string.Equals(nextFormat, _bodyFormat, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            byte[] bytes = DecodeData(BodyTextBox.Text, _bodyFormat);
            BodyTextBox.Text = EncodeData(bytes, nextFormat);
            _bodyFormat = nextFormat;
        }
        catch (Exception exception)
        {
            BuilderSummaryTextBlock.Text = $"Body 格式切换失败：{exception.Message}";
            SetBodyFormat(_bodyFormat);
        }
    }

    private void HeaderAddField_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        EnsureHeaderStructuredMode();
        AddRow(HeaderFields, HeaderFieldsGrid);
    }

    private void HeaderDeleteField_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DeleteSelectedRows(HeaderFields, HeaderFieldsGrid);
    }

    private void HeaderMoveUp_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRow(HeaderFields, HeaderFieldsGrid, -1);
    }

    private void HeaderMoveDown_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRow(HeaderFields, HeaderFieldsGrid, 1);
    }

    private void HeaderSort_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        EnsureHeaderStructuredMode();
        SortRows(HeaderFields);
    }

    private void BodyAddField_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        EnsureBodyStructuredMode();
        if (!_bodyPropertiesCanSync)
        {
            return;
        }

        _bodyPropertiesCanSync = true;
        AddRow(BodyFields, BodyFieldsGrid);
    }

    private void BodyDeleteField_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DeleteSelectedRows(BodyFields, BodyFieldsGrid);
    }

    private void BodyMoveUp_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRow(BodyFields, BodyFieldsGrid, -1);
    }

    private void BodyMoveDown_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRow(BodyFields, BodyFieldsGrid, 1);
    }

    private void BodySort_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        EnsureBodyStructuredMode();
        SortRows(BodyFields);
    }

    private void FieldGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        _dragStartPoint = mouseButtonEventArgs.GetPosition(null);
        _dragRow = FindVisualParent<DataGridRow>(mouseButtonEventArgs.OriginalSource as DependencyObject)?.Item as RequestBuilderFieldRow;
    }

    private void FieldGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(mouseButtonEventArgs.OriginalSource as DependencyObject) is { Item: RequestBuilderFieldRow row })
        {
            grid.SelectedItem = row;
        }
    }

    private void FieldGridContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not DataGrid grid)
        {
            return;
        }

        ObservableCollection<RequestBuilderFieldRow> rows = GetRowsForGrid(grid);
        RequestBuilderFieldRow? selectedRow = grid.SelectedItem as RequestBuilderFieldRow;
        bool hasSelection = selectedRow is not null;
        int selectedIndex = selectedRow is null ? -1 : rows.IndexOf(selectedRow);
        foreach (object item in menu.Items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            string header = menuItem.Header?.ToString() ?? "";
            menuItem.IsEnabled = header switch
            {
                "新增" => !ReferenceEquals(grid, BodyFieldsGrid) || _bodyPropertiesCanSync,
                "删除" => hasSelection,
                "上移" => hasSelection && selectedIndex > 0,
                "下移" => hasSelection && selectedIndex >= 0 && selectedIndex < rows.Count - 1,
                "按键名排序" => rows.Count > 1,
                _ => menuItem.IsEnabled
            };
        }
    }

    private void FieldGrid_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (sender is not DataGrid grid || IsFieldEditorFocused(grid))
        {
            return;
        }

        ObservableCollection<RequestBuilderFieldRow> rows = GetRowsForGrid(grid);
        switch (keyEventArgs.Key)
        {
            case Key.Delete:
                DeleteSelectedRows(rows, grid);
                keyEventArgs.Handled = true;
                break;
            case Key.Up:
                MoveSelectedRow(rows, grid, -1);
                keyEventArgs.Handled = true;
                break;
            case Key.Down:
                MoveSelectedRow(rows, grid, 1);
                keyEventArgs.Handled = true;
                break;
        }
    }

    private void FieldAddMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!TryGetFieldMenuContext(sender, out DataGrid? grid, out ObservableCollection<RequestBuilderFieldRow>? rows))
        {
            return;
        }

        if (ReferenceEquals(rows, BodyFields))
        {
            if (!_bodyPropertiesCanSync)
            {
                return;
            }

            _bodyPropertiesCanSync = true;
        }

        AddRow(rows, grid);
    }

    private void FieldDeleteMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!TryGetFieldMenuContext(sender, out DataGrid? grid, out ObservableCollection<RequestBuilderFieldRow>? rows))
        {
            return;
        }

        DeleteSelectedRows(rows, grid);
    }

    private void FieldMoveUpMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!TryGetFieldMenuContext(sender, out DataGrid? grid, out ObservableCollection<RequestBuilderFieldRow>? rows))
        {
            return;
        }

        MoveSelectedRow(rows, grid, -1);
    }

    private void FieldMoveDownMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!TryGetFieldMenuContext(sender, out DataGrid? grid, out ObservableCollection<RequestBuilderFieldRow>? rows))
        {
            return;
        }

        MoveSelectedRow(rows, grid, 1);
    }

    private void FieldSortMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!TryGetFieldMenuContext(sender, out _, out ObservableCollection<RequestBuilderFieldRow>? rows))
        {
            return;
        }

        SortRows(rows);
    }

    private void FieldGrid_MouseMove(object sender, MouseEventArgs mouseEventArgs)
    {
        if (mouseEventArgs.LeftButton != MouseButtonState.Pressed || _dragRow is null || sender is not DataGrid grid)
        {
            return;
        }

        Point current = mouseEventArgs.GetPosition(null);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(grid, _dragRow, DragDropEffects.Move);
        _dragRow = null;
    }

    private void FieldGrid_Drop(object sender, DragEventArgs dragEventArgs)
    {
        if (sender is not DataGrid grid
            || dragEventArgs.Data.GetData(typeof(RequestBuilderFieldRow)) is not RequestBuilderFieldRow source
            || FindVisualParent<DataGridRow>(dragEventArgs.OriginalSource as DependencyObject)?.Item is not RequestBuilderFieldRow target
            || ReferenceEquals(source, target))
        {
            return;
        }

        ObservableCollection<RequestBuilderFieldRow> rows = GetRowsForGrid(grid);
        int sourceIndex = rows.IndexOf(source);
        int targetIndex = rows.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        rows.Move(sourceIndex, targetIndex);
        grid.SelectedItem = source;
        BuilderSummaryTextBlock.Text = "已调整键值顺序。";
    }

    private void EnsureHeaderStructuredMode()
    {
        if (_headerMode != RequestEditorMode.Raw)
        {
            return;
        }

        HeaderTabControl.SelectedIndex = 1;
    }

    private void EnsureBodyStructuredMode()
    {
        if (_bodyMode != RequestEditorMode.Raw)
        {
            return;
        }

        BodyTabControl.SelectedIndex = 1;
    }

    private ObservableCollection<RequestBuilderFieldRow> GetRowsForGrid(DataGrid grid)
    {
        if (ReferenceEquals(grid, BodyFieldsGrid))
        {
            return BodyFields;
        }

        return HeaderFields;
    }

    private bool TryGetFieldMenuContext(object sender, out DataGrid grid, out ObservableCollection<RequestBuilderFieldRow> rows)
    {
        grid = null!;
        rows = null!;
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: DataGrid targetGrid } })
        {
            return false;
        }

        grid = targetGrid;
        rows = GetRowsForGrid(grid);
        return true;
    }

    private static bool IsFieldEditorFocused(DataGrid grid)
    {
        if (Keyboard.FocusedElement is not DependencyObject focusedElement || !IsDescendantOf(focusedElement, grid))
        {
            return false;
        }

        return FindVisualParent<TextBox>(focusedElement) is not null
            || FindVisualParent<ComboBox>(focusedElement) is not null
            || FindVisualParent<CheckBox>(focusedElement) is not null;
    }

    private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static void AddRow(ObservableCollection<RequestBuilderFieldRow> rows, DataGrid grid)
    {
        RequestBuilderFieldRow row = new();
        rows.Add(row);
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
        grid.BeginEdit();
    }

    private void DeleteSelectedRows(ObservableCollection<RequestBuilderFieldRow> rows, DataGrid grid)
    {
        CommitFieldGrids();
        RequestBuilderFieldRow[] selectedRows = grid.SelectedItems
            .OfType<RequestBuilderFieldRow>()
            .ToArray();
        foreach (RequestBuilderFieldRow row in selectedRows)
        {
            rows.Remove(row);
        }
    }

    private void MoveSelectedRow(ObservableCollection<RequestBuilderFieldRow> rows, DataGrid grid, int direction)
    {
        CommitFieldGrids();
        if (grid.SelectedItem is not RequestBuilderFieldRow row)
        {
            return;
        }

        int index = rows.IndexOf(row);
        int nextIndex = index + direction;
        if (index < 0 || nextIndex < 0 || nextIndex >= rows.Count)
        {
            return;
        }

        rows.Move(index, nextIndex);
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
    }

    private static void SortRows(ObservableCollection<RequestBuilderFieldRow> rows)
    {
        RequestBuilderFieldRow[] orderedRows = rows
            .OrderBy(static row => row.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        rows.Clear();
        foreach (RequestBuilderFieldRow row in orderedRows)
        {
            rows.Add(row);
        }
    }

    private void ResetStructuredEditors()
    {
        _isSwitchingTabs = true;
        try
        {
            HeaderTabControl.SelectedIndex = 0;
            BodyTabControl.SelectedIndex = 0;
            HeaderFields.Clear();
            BodyFields.Clear();
            BodyJsonEditor.Clear();
            BodyJsonEditor.AllowCreateNodes = true;
            _headerMode = RequestEditorMode.Raw;
            _bodyMode = RequestEditorMode.Raw;
            _bodyPropertiesCanSync = true;
            _bodyJsonCanSync = true;
            UpdateBodyAvailability();
        }
        finally
        {
            _isSwitchingTabs = false;
        }
    }

    private void CommitFieldGrids()
    {
        CommitFieldGrid(HeaderFieldsGrid);
        CommitFieldGrid(BodyFieldsGrid);
    }

    private static void CommitFieldGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private string BuildHeaderTextFromCurrentView()
    {
        string text = BuildHeaderTextFromMode(_headerMode);
        HeadersTextBox.Text = text;
        return text;
    }

    private string BuildHeaderTextFromMode(RequestEditorMode mode)
    {
        return mode switch
        {
            RequestEditorMode.Properties => BuildHeaderText(HeaderFields),
            _ => HeadersTextBox.Text ?? ""
        };
    }

    private byte[] BuildBodyBytesFromCurrentView()
    {
        BodyBuildResult result = BuildBodyTextFromCurrentView();
        return DecodeData(result.Text, result.Format);
    }

    private BodyBuildResult BuildBodyTextFromCurrentView()
    {
        BodyBuildResult result = BuildBodyTextFromMode(_bodyMode);
        BodyTextBox.Text = result.Text;
        SetBodyFormat(result.Format);
        return result;
    }

    private BodyBuildResult BuildBodyTextFromMode(RequestEditorMode mode)
    {
        return mode switch
        {
            RequestEditorMode.Properties when _bodyPropertiesCanSync => new BodyBuildResult(BuildFormUrlEncoded(BodyFields), "文本"),
            RequestEditorMode.Json when _bodyJsonCanSync => new BodyBuildResult(BodyJsonEditor.BuildJson(), "文本"),
            _ => new BodyBuildResult(BodyTextBox.Text ?? "", GetComboText(BodyFormatComboBox))
        };
    }

    private void SetBodyFromBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            BodyTextBox.Text = "";
            SetBodyFormat("文本");
            return;
        }

        if (TryDecodeUtf8(bytes, out string text) && LooksLikeText(text))
        {
            BodyTextBox.Text = text;
            SetBodyFormat("文本");
            return;
        }

        BodyTextBox.Text = EncodeHex(bytes);
        SetBodyFormat("HEX");
        BuilderSummaryTextBlock.Text = "Body 检测为二进制数据，已自动切换为 HEX 输入。";
    }

    private void SetBodyFormat(string format)
    {
        _isChangingBodyFormat = true;
        try
        {
            SetComboText(BodyFormatComboBox, string.IsNullOrWhiteSpace(format) ? "文本" : format);
            _bodyFormat = GetComboText(BodyFormatComboBox);
            UpdateBodyAvailability();
        }
        finally
        {
            _isChangingBodyFormat = false;
        }
    }

    private void UpdateBodyAvailability()
    {
        if (!_controlsReady || BodySectionBorder is null || BodyTabControl is null || BodyFormatComboBox is null)
        {
            return;
        }

        bool allowBody = AllowsRequestBody(GetMethodText());
        BodySectionBorder.Opacity = allowBody ? 1 : 0.56;
        BodyTabControl.IsEnabled = allowBody;
        BodyFormatComboBox.IsEnabled = allowBody && _bodyMode == RequestEditorMode.Raw;
    }

    private static bool AllowsRequestBody(string? method)
    {
        string normalized = (method ?? "").Trim().ToUpperInvariant();
        return normalized is not "" and not "GET" and not "HEAD";
    }

    private static List<RequestBuilderFieldRow> ParseHeaderRows(string text)
    {
        List<RequestBuilderFieldRow> rows = new();
        foreach ((string key, string value) in ParseHeaderPairs(text))
        {
            rows.Add(new RequestBuilderFieldRow
            {
                Key = key,
                Value = value,
                ValueType = "String"
            });
        }

        return rows;
    }

    private static List<RequestBuilderFieldRow> ParseBodyFormRows(string text)
    {
        List<RequestBuilderFieldRow> rows = new();
        string source = text ?? "";
        if (!CanLoadBodyProperties(source, "文本"))
        {
            return rows;
        }

        string[] segments = source.Contains('&', StringComparison.Ordinal)
            ? source.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : source.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string segment in segments)
        {
            int separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                continue;
            }

            string key = WebUtility.UrlDecode(segment[..separatorIndex].Trim());
            string value = WebUtility.UrlDecode(segment[(separatorIndex + 1)..].Trim());
            rows.Add(new RequestBuilderFieldRow
            {
                Key = key,
                Value = value,
                ValueType = "String"
            });
        }

        return rows;
    }

    private static bool CanLoadBodyProperties(string? text, string format)
    {
        if (!string.Equals(format, "文本", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(text)
            || IsJsonText(text))
        {
            return false;
        }

        return text.Replace("\r\n", "\n")
            .Split(text.Contains('&', StringComparison.Ordinal) ? '&' : '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static segment => segment.IndexOf('=', StringComparison.Ordinal) > 0);
    }

    private static bool IsJsonText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<RequestBuilderFieldRow> ParseJsonRows(string text)
    {
        List<RequestBuilderFieldRow> rows = new();
        if (string.IsNullOrWhiteSpace(text))
        {
            return rows;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                rows.Add(new RequestBuilderFieldRow
                {
                    Key = "value",
                    Value = root.GetRawText(),
                    ValueType = "JSON"
                });
                return rows;
            }

            foreach (JsonProperty property in root.EnumerateObject())
            {
                rows.Add(new RequestBuilderFieldRow
                {
                    Key = property.Name,
                    Value = GetJsonEditorValue(property.Value),
                    ValueType = GetJsonEditorType(property.Value)
                });
            }
        }
        catch
        {
        }

        return rows;
    }

    private static void ReplaceRows(ObservableCollection<RequestBuilderFieldRow> target, IEnumerable<RequestBuilderFieldRow> rows)
    {
        target.Clear();
        foreach (RequestBuilderFieldRow row in rows)
        {
            target.Add(row);
        }
    }

    private static string BuildHeaderText(IEnumerable<RequestBuilderFieldRow> rows)
    {
        StringBuilder builder = new();
        foreach (RequestBuilderFieldRow row in rows.Where(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key)))
        {
            builder.Append(row.Key.Trim()).Append(": ").Append(row.Value ?? "").AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildFormUrlEncoded(IEnumerable<RequestBuilderFieldRow> rows)
    {
        return string.Join("&", rows
            .Where(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key))
            .Select(static item => $"{WebUtility.UrlEncode(item.Key.Trim())}={WebUtility.UrlEncode(item.Value ?? "")}"));
    }

    private static string BuildJsonText(IEnumerable<RequestBuilderFieldRow> rows)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (RequestBuilderFieldRow row in rows.Where(static item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Key)))
            {
                writer.WritePropertyName(row.Key.Trim());
                WriteJsonValue(writer, row.ValueType, row.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, string type, string? value)
    {
        value ??= "";
        switch (type)
        {
            case "Number":
                if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue))
                {
                    writer.WriteNumberValue(decimalValue);
                }
                else
                {
                    writer.WriteStringValue(value);
                }

                break;
            case "Bool":
                writer.WriteBooleanValue(value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
                break;
            case "Null":
                writer.WriteNullValue();
                break;
            case "JSON":
                try
                {
                    using JsonDocument document = JsonDocument.Parse(value);
                    document.RootElement.WriteTo(writer);
                }
                catch
                {
                    writer.WriteStringValue(value);
                }

                break;
            default:
                writer.WriteStringValue(value);
                break;
        }
    }

    private static string GetJsonEditorType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => "Number",
            JsonValueKind.True or JsonValueKind.False => "Bool",
            JsonValueKind.Null => "Null",
            JsonValueKind.Object or JsonValueKind.Array => "JSON",
            _ => "String"
        };
    }

    private static string GetJsonEditorValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Null => "",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number or JsonValueKind.Object or JsonValueKind.Array => element.GetRawText(),
            _ => element.ToString()
        };
    }

    private static Dictionary<string, List<string>> ParseHeaders(string text)
    {
        Dictionary<string, List<string>> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in ParseHeaderPairs(text))
        {
            if (!headers.TryGetValue(name, out List<string>? values))
            {
                values = new List<string>();
                headers[name] = values;
            }

            values.Add(value);
        }

        return headers;
    }

    private static IEnumerable<(string Name, string Value)> ParseHeaderPairs(string text)
    {
        foreach (string rawLine in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            yield return (line[..separatorIndex].Trim(), line[(separatorIndex + 1)..].Trim());
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, List<string>> headers)
    {
        foreach ((string name, List<string> values) in headers)
        {
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Host = values.LastOrDefault();
                continue;
            }

            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ContentHeaderNames.Contains(name) && request.Content is not null)
            {
                _ = request.Content.Headers.TryAddWithoutValidation(name, values);
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(name, values) && request.Content is not null)
            {
                _ = request.Content.Headers.TryAddWithoutValidation(name, values);
            }
        }
    }

    private static string FormatHeaders(JsonElement header)
    {
        if (header.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        StringBuilder builder = new();
        foreach (JsonProperty property in header.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement value in property.Value.EnumerateArray())
                {
                    builder.Append(property.Name).Append(": ").Append(value.GetString()).AppendLine();
                }

                continue;
            }

            builder.Append(property.Name).Append(": ").Append(property.Value.ToString()).AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static byte[] DecodeData(string? text, string format)
    {
        text ??= "";
        return format switch
        {
            "Base64" => Convert.FromBase64String(RemoveWhitespace(text)),
            "HEX" => FromHex(text),
            _ => Encoding.UTF8.GetBytes(text)
        };
    }

    private static string EncodeData(byte[] bytes, string format)
    {
        return format switch
        {
            "Base64" => Convert.ToBase64String(bytes),
            "HEX" => EncodeHex(bytes),
            _ => Encoding.UTF8.GetString(bytes)
        };
    }

    private static string EncodeHex(byte[] bytes)
    {
        return string.Join(" ", Convert.ToHexString(bytes).ToLowerInvariant().Chunk(2).Select(static chars => new string(chars)));
    }

    private static byte[] DecodePayloadBytes(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => Convert.FromBase64String(element.GetString() ?? ""),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(static item => (byte)item.GetInt32())
                .ToArray(),
            _ => Array.Empty<byte>()
        };
    }

    private static byte[] FromHex(string text)
    {
        string hex = RemoveWhitespace(text).Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length % 2 != 0)
        {
            hex = "0" + hex;
        }

        return Convert.FromHexString(hex);
    }

    private static string RemoveWhitespace(string text)
    {
        return string.Concat(text.Where(static character => !char.IsWhiteSpace(character)));
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch
        {
            text = "";
            return false;
        }
    }

    private static bool LooksLikeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        int controlCount = text.Count(static character =>
            char.IsControl(character)
            && character is not '\r'
            && character is not '\n'
            && character is not '\t');
        return controlCount <= Math.Max(2, text.Length / 100);
    }

    private static Version ParseHttpVersion(string text)
    {
        return text switch
        {
            "HTTP/1.0" => HttpVersion.Version10,
            "HTTP/2.0" => HttpVersion.Version20,
            "HTTP/1.2" => new Version(1, 2),
            _ => HttpVersion.Version11
        };
    }

    private static RequestEditorMode GetSelectedMode(TabControl tabControl)
    {
        return tabControl.SelectedIndex switch
        {
            1 => RequestEditorMode.Properties,
            2 => RequestEditorMode.Json,
            _ => RequestEditorMode.Raw
        };
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        return element.TryGetProperty(name, out JsonElement value) ? value : default;
    }

    private static string GetString(JsonElement element, string name, string fallback = "")
    {
        JsonElement value = GetProperty(element, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        JsonElement value = GetProperty(element, name);
        return value.ValueKind == JsonValueKind.True
            || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed) && parsed);
    }

    private static int GetInt(JsonElement element, string name)
    {
        JsonElement value = GetProperty(element, name);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed)
            ? parsed
            : 0;
    }

    private void SetMethod(string method)
    {
        SetComboText(MethodComboBox, string.IsNullOrWhiteSpace(method) ? "GET" : method.Trim().ToUpperInvariant());
        UpdateBodyAvailability();
    }

    private string GetMethodText()
    {
        if (MethodComboBox is null)
        {
            return "";
        }

        if (MethodComboBox.SelectedItem is ComboBoxItem comboBoxItem && !string.IsNullOrWhiteSpace(comboBoxItem.Content?.ToString()))
        {
            return comboBoxItem.Content.ToString()!.Trim().ToUpperInvariant();
        }

        return GetComboText(MethodComboBox).Trim().ToUpperInvariant();
    }

    private static void SetComboText(ComboBox comboBox, string text)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem && string.Equals(comboBoxItem.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }

        comboBox.Text = text;
    }

    private static string GetComboText(ComboBox comboBox)
    {
        if (comboBox.IsEditable && !string.IsNullOrWhiteSpace(comboBox.Text))
        {
            return comboBox.Text;
        }

        return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? comboBox.Text
            ?? string.Empty;
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private sealed record BodyBuildResult(string Text, string Format);

    private enum RequestEditorMode
    {
        Raw,
        Properties,
        Json
    }
}
