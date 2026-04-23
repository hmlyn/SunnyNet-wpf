using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.Services;
using SunnyNet.Wpf.ViewModels;
using SunnyNet.Wpf.Windows;

namespace SunnyNet.Wpf;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;
    private readonly MainWindowViewModel _viewModel = new();
    private readonly UiLayoutSettings _layoutSettings = UiLayoutSettingsStore.Load();
    private SearchWindow? _searchWindow;
    private SettingsWindow? _processSettingsWindow;
    private DetailZoomMode _detailZoomMode;
    private bool _isInitializingLayout = true;
    private bool _restoreWindowMaximized;
    private bool _isUpdatingCaptureScope;
    private bool _isCloseConfirmed;
    private bool _isCloseCleanupRunning;

    public MainWindow()
    {
        InitializeComponent();
        ApplySavedWindowLayout();
        ApplySavedInternalLayout();
        ApplySavedColumnLayout();
        ApplySavedCaptureScope();
        ApplySavedFavoriteSettings();
        _isInitializingLayout = false;
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;
        _viewModel.NotificationRequested += ViewModel_NotificationRequested;
        _viewModel.ScrollToEntryRequested += ViewModel_ScrollToEntryRequested;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Detail.PropertyChanged += Detail_PropertyChanged;
        UpdateFooterState();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs eventArgs)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            AdjustMaximizedBounds(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        MonitorInfo monitorInfo = new()
        {
            CbSize = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        MinMaxInfo minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        RectInt workArea = monitorInfo.RcWork;
        RectInt monitorArea = monitorInfo.RcMonitor;

        minMaxInfo.PtMaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.PtMaxPosition.Y = workArea.Top - monitorArea.Top;
        minMaxInfo.PtMaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.PtMaxSize.Y = workArea.Bottom - workArea.Top;

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs routedEventArgs)
    {
        if (_restoreWindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        await _viewModel.InitializeAsync();
        UpdateFooterState();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs cancelEventArgs)
    {
        if (_isCloseConfirmed)
        {
            return;
        }

        cancelEventArgs.Cancel = true;
        if (_isCloseCleanupRunning)
        {
            return;
        }

        _isCloseCleanupRunning = true;
        try
        {
            SaveLayoutSettings();
            await _viewModel.DisableSystemProxyOnExitAsync();
            await _viewModel.DisposeAsync();
        }
        finally
        {
            _isCloseConfirmed = true;
            Close();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        if (propertyChangedEventArgs.PropertyName is nameof(MainWindowViewModel.BreakpointMode) or nameof(MainWindowViewModel.IeProxyEnabled) or nameof(MainWindowViewModel.IsCapturing) or nameof(MainWindowViewModel.Settings))
        {
            UpdateFooterState();
        }
    }

    private void Detail_PropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        if (propertyChangedEventArgs.PropertyName != nameof(SessionDetail.IsSocketSession))
        {
            return;
        }

        if (_viewModel.Detail.IsSocketSession)
        {
            RequestTabControl.SelectedItem = RequestWebSocketTab;
            ResponseTabControl.SelectedItem = ResponseWebSocketTab;
        }
        else
        {
            if (RequestWebSocketTab.IsSelected)
            {
                RequestTabControl.SelectedIndex = 0;
            }

            if (ResponseWebSocketTab.IsSelected)
            {
                ResponseTabControl.SelectedIndex = 0;
            }
        }

        ApplyDetailZoomMode();
    }

    private void UpdateFooterState()
    {
        if (_viewModel.BreakpointMode == 1)
        {
            BreakpointGlyph.Text = "\uE7BA";
            BreakpointLabel.Text = "拦截上行";
        }
        else if (_viewModel.BreakpointMode == 2)
        {
            BreakpointGlyph.Text = "\uE7BF";
            BreakpointLabel.Text = "拦截下行";
        }
        else
        {
            BreakpointGlyph.Text = "\uF127";
            BreakpointLabel.Text = "空白";
        }
    }

    private void ViewModel_NotificationRequested(string title, string message)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, title == "错误" ? MessageBoxImage.Error : MessageBoxImage.Information);
    }

    private void ViewModel_ScrollToEntryRequested(CaptureEntry entry)
    {
        SessionsGrid.ScrollIntoView(entry);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (mouseButtonEventArgs.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void WindowControls_MouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        mouseButtonEventArgs.Handled = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ToggleWindowState();
    }

    private void Close_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs eventArgs)
    {
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE739";
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private async void OpenFile_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenFileDialog dialog = new()
        {
            Title = "请选择抓包记录文件",
            Filter = "SunnyNet抓包文件 (*.syn)|*.syn|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.OpenCaptureFileAsync(dialog.FileName);
        }
    }

    private async void SaveSelected_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await SaveCaptureAsync(false);
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await SaveCaptureAsync(true);
    }

    private async Task SaveCaptureAsync(bool saveAll)
    {
        SaveFileDialog dialog = new()
        {
            Title = "请选择文件保存位置",
            Filter = "SunnyNet抓包文件 (*.syn)|*.syn",
            DefaultExt = ".syn"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.SaveCaptureFileAsync(dialog.FileName, saveAll);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        new SettingsWindow(_viewModel) { Owner = this }.Show();
    }

    private void SettingsProcess_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ShowProcessSettingsWindow();
    }

    private void SettingsScript_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SettingsWindow window = new(_viewModel, "脚本编辑") { Owner = this };
        window.Show();
    }

    private void TextCompare_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        new TextCompareWindow { Owner = this }.Show();
    }

    private void CertificateGuide_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        new CertificateGuideWindow(_viewModel) { Owner = this }.Show();
    }

    private void OpenSource_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        new OpenSourceWindow { Owner = this }.Show();
    }

    private void Find_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ShowSearchWindow();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        ShowSearchWindow();
        keyEventArgs.Handled = true;
    }

    private void ShowSearchWindow()
    {
        if (_searchWindow is { IsVisible: true })
        {
            _searchWindow.Activate();
            _searchWindow.FocusSearchInput();
            return;
        }

        _searchWindow = new SearchWindow(_viewModel)
        {
            Owner = this
        };
        _searchWindow.Closed += (_, _) => _searchWindow = null;
        _searchWindow.Show();
        _searchWindow.Activate();
    }

    private void RequestTabControl_SelectionChanged(object sender, SelectionChangedEventArgs routedEventArgs)
    {
        if (!ReferenceEquals(sender, routedEventArgs.Source))
        {
            return;
        }

        if (_viewModel.Detail.IsSocketSession && RequestWebSocketTab.IsSelected)
        {
            ResponseTabControl.SelectedItem = ResponseWebSocketTab;
        }
    }

    private void RequestPanelButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RequestDataPanel.Visibility = Visibility.Visible;
        ColumnPickerPanel.Visibility = Visibility.Collapsed;
        RequestPanelButton.IsChecked = true;
        ColumnPanelButton.IsChecked = false;
    }

    private void ColumnPanelButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RequestDataPanel.Visibility = Visibility.Collapsed;
        ColumnPickerPanel.Visibility = Visibility.Visible;
        RequestPanelButton.IsChecked = false;
        ColumnPanelButton.IsChecked = true;
    }

    private void RequestZoom_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ToggleDetailZoom(DetailZoomMode.Request);
    }

    private void ResponseZoom_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ToggleDetailZoom(DetailZoomMode.Response);
    }

    private void ToggleDetailZoom(DetailZoomMode zoomMode)
    {
        if (_detailZoomMode == DetailZoomMode.None)
        {
            CaptureInternalLayoutSettings();
        }

        _detailZoomMode = _detailZoomMode == zoomMode ? DetailZoomMode.None : zoomMode;
        ApplyDetailZoomMode();
    }

    private void ApplyDetailZoomMode()
    {
        bool requestExpanded = _detailZoomMode == DetailZoomMode.Request;
        bool responseExpanded = _detailZoomMode == DetailZoomMode.Response;
        bool isZoomed = _detailZoomMode != DetailZoomMode.None;

        SessionListPanel.Visibility = isZoomed ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceSplitter.Visibility = isZoomed ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(DetailHostGrid, isZoomed ? 0 : 2);
        Grid.SetColumnSpan(DetailHostGrid, isZoomed ? 3 : 1);

        RequestSectionBorder.Visibility = responseExpanded ? Visibility.Collapsed : Visibility.Visible;
        ResponseSectionBorder.Visibility = requestExpanded ? Visibility.Collapsed : Visibility.Visible;
        RequestResponseSplitter.Visibility = isZoomed ? Visibility.Collapsed : Visibility.Visible;

        RequestPanelRow.Height = responseExpanded ? new GridLength(0) : GetDefaultRequestPanelHeight();
        DetailSplitterRow.Height = isZoomed ? new GridLength(0) : new GridLength(10);
        ResponsePanelRow.Height = requestExpanded ? new GridLength(0) : GetDefaultResponsePanelHeight();

        RequestZoomGlyph.Text = requestExpanded ? "\uE923" : "\uE740";
        ResponseZoomGlyph.Text = responseExpanded ? "\uE923" : "\uE740";
    }

    private GridLength GetDefaultRequestPanelHeight()
    {
        if (IsValidRatio(_layoutSettings.DetailRequestRatio))
        {
            return new GridLength(_layoutSettings.DetailRequestRatio, GridUnitType.Star);
        }

        return _viewModel.Detail.IsSocketSession
            ? new GridLength(0.95, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
    }

    private GridLength GetDefaultResponsePanelHeight()
    {
        if (IsValidRatio(_layoutSettings.DetailRequestRatio))
        {
            return new GridLength(1 - _layoutSettings.DetailRequestRatio, GridUnitType.Star);
        }

        return _viewModel.Detail.IsSocketSession
            ? new GridLength(1.15, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
    }

    private void ApplySavedWindowLayout()
    {
        Rect workArea = SystemParameters.WorkArea;
        double width = IsValidLength(_layoutSettings.WindowWidth)
            ? _layoutSettings.WindowWidth
            : Width;
        double height = IsValidLength(_layoutSettings.WindowHeight)
            ? _layoutSettings.WindowHeight
            : Height;

        width = Clamp(width, MinWidth, Math.Max(MinWidth, workArea.Width));
        height = Clamp(height, MinHeight, Math.Max(MinHeight, workArea.Height));
        Width = width;
        Height = height;

        if (IsValidPosition(_layoutSettings.WindowLeft, _layoutSettings.WindowTop, width, height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Clamp(_layoutSettings.WindowLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
            Top = Clamp(_layoutSettings.WindowTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _restoreWindowMaximized = _layoutSettings.IsWindowMaximized;
    }

    private void ApplySavedInternalLayout()
    {
        ApplyColumnRatio(WorkspaceSessionColumn, WorkspaceDetailColumn, _layoutSettings.WorkspaceSessionRatio, 0.4);

        double filterWidth = IsValidLength(_layoutSettings.SessionFilterWidth)
            ? _layoutSettings.SessionFilterWidth
            : 190;
        SessionFilterColumn.Width = new GridLength(Clamp(filterWidth, SessionFilterColumn.MinWidth, 420), GridUnitType.Pixel);

        if (IsValidRatio(_layoutSettings.DetailRequestRatio))
        {
            RequestPanelRow.Height = new GridLength(_layoutSettings.DetailRequestRatio, GridUnitType.Star);
            ResponsePanelRow.Height = new GridLength(1 - _layoutSettings.DetailRequestRatio, GridUnitType.Star);
        }
    }

    private void ApplySavedColumnLayout()
    {
        if (_layoutSettings.SessionColumns.Count == 0)
        {
            return;
        }

        Dictionary<string, DataGridColumn> columns = GetSessionColumnMap();
        foreach ((string key, DataGridColumn column) in columns)
        {
            if (!_layoutSettings.SessionColumns.TryGetValue(key, out bool isVisible))
            {
                continue;
            }

            column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        SyncColumnPickerChecks();
    }

    private void ApplySavedCaptureScope()
    {
        SetCaptureScopeMode(_layoutSettings.CaptureScopeMode, openSettings: false);
    }

    private void ApplySavedFavoriteSettings()
    {
        _viewModel.InitializeFavoriteSettings(_layoutSettings.FavoriteSessionKeys, _layoutSettings.ShowFavoritesOnly);
    }

    private void SaveLayoutSettings()
    {
        CaptureInternalLayoutSettings();

        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (IsValidSize(bounds.Width, bounds.Height))
        {
            _layoutSettings.WindowWidth = bounds.Width;
            _layoutSettings.WindowHeight = bounds.Height;
        }

        if (IsValidPosition(bounds.Left, bounds.Top, bounds.Width, bounds.Height))
        {
            _layoutSettings.WindowLeft = bounds.Left;
            _layoutSettings.WindowTop = bounds.Top;
        }

        _layoutSettings.IsWindowMaximized = WindowState == WindowState.Maximized;
        foreach ((string key, DataGridColumn column) in GetSessionColumnMap())
        {
            _layoutSettings.SessionColumns[key] = column.Visibility == Visibility.Visible;
        }

        _layoutSettings.ShowFavoritesOnly = _viewModel.ShowFavoritesOnly;
        _layoutSettings.FavoriteSessionKeys = _viewModel.GetFavoriteKeys().ToList();
        UiLayoutSettingsStore.Save(_layoutSettings);
    }

    private void LayoutSplitter_DragCompleted(object sender, DragCompletedEventArgs dragCompletedEventArgs)
    {
        if (_isInitializingLayout)
        {
            return;
        }

        CaptureInternalLayoutSettings();
        UiLayoutSettingsStore.Save(_layoutSettings);
    }

    private void CaptureInternalLayoutSettings()
    {
        double workspaceTotalWidth = WorkspaceSessionColumn.ActualWidth + WorkspaceDetailColumn.ActualWidth;
        if (workspaceTotalWidth > 20)
        {
            double ratio = WorkspaceSessionColumn.ActualWidth / workspaceTotalWidth;
            if (IsValidRatio(ratio))
            {
                _layoutSettings.WorkspaceSessionRatio = ratio;
            }
        }

        if (IsValidLength(SessionFilterColumn.ActualWidth) && SessionFilterColumn.ActualWidth >= SessionFilterColumn.MinWidth)
        {
            _layoutSettings.SessionFilterWidth = Clamp(SessionFilterColumn.ActualWidth, SessionFilterColumn.MinWidth, 420);
        }

        if (_detailZoomMode != DetailZoomMode.None)
        {
            return;
        }

        double detailTotalHeight = RequestPanelRow.ActualHeight + ResponsePanelRow.ActualHeight;
        if (detailTotalHeight > 20)
        {
            double ratio = RequestPanelRow.ActualHeight / detailTotalHeight;
            if (IsValidRatio(ratio))
            {
                _layoutSettings.DetailRequestRatio = ratio;
            }
        }
    }

    private static void ApplyColumnRatio(ColumnDefinition firstColumn, ColumnDefinition secondColumn, double ratio, double fallbackRatio)
    {
        double safeRatio = IsValidRatio(ratio) ? ratio : fallbackRatio;
        firstColumn.Width = new GridLength(safeRatio, GridUnitType.Star);
        secondColumn.Width = new GridLength(1 - safeRatio, GridUnitType.Star);
    }

    private void SaveFavoriteSettings()
    {
        _layoutSettings.ShowFavoritesOnly = _viewModel.ShowFavoritesOnly;
        _layoutSettings.FavoriteSessionKeys = _viewModel.GetFavoriteKeys().ToList();
        UiLayoutSettingsStore.Save(_layoutSettings);
    }

    private void SaveColumnSetting(DataGridColumn column, bool isVisible)
    {
        string? key = GetSessionColumnKey(column);
        if (key is null)
        {
            return;
        }

        _layoutSettings.SessionColumns[key] = isVisible;
        UiLayoutSettingsStore.Save(_layoutSettings);
    }

    private void SyncColumnPickerChecks()
    {
        foreach (CheckBox checkBox in FindVisualChildren<CheckBox>(ColumnPickerPanel))
        {
            if (checkBox.Tag is DataGridColumn column)
            {
                checkBox.IsChecked = column.Visibility == Visibility.Visible;
            }
        }
    }

    private Dictionary<string, DataGridColumn> GetSessionColumnMap()
    {
        return new Dictionary<string, DataGridColumn>
        {
            ["Index"] = IndexColumn,
            ["Method"] = MethodColumn,
            ["State"] = StateColumn,
            ["Url"] = UrlColumn,
            ["Length"] = LengthColumn,
            ["Type"] = TypeColumn,
            ["Process"] = ProcessColumn,
            ["Notes"] = NotesColumn
        };
    }

    private string? GetSessionColumnKey(DataGridColumn column)
    {
        foreach ((string key, DataGridColumn value) in GetSessionColumnMap())
        {
            if (ReferenceEquals(column, value))
            {
                return key;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject dependencyObject) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(dependencyObject);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(dependencyObject, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (T nestedChild in FindVisualChildren<T>(child))
            {
                yield return nestedChild;
            }
        }
    }

    private static bool IsValidSize(double width, double height)
    {
        return IsValidLength(width) && width >= 700
            && IsValidLength(height) && height >= 500;
    }

    private static bool IsValidLength(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    private static bool IsValidRatio(double value)
    {
        return IsValidLength(value) && value > 0.08 && value < 0.92;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private static bool IsValidPosition(double left, double top, double width, double height)
    {
        if (double.IsNaN(left) || double.IsInfinity(left) || double.IsNaN(top) || double.IsInfinity(top))
        {
            return false;
        }

        double visibleWidth = Math.Min(width, 240);
        double visibleHeight = Math.Min(height, 160);
        return left + visibleWidth >= SystemParameters.VirtualScreenLeft
            && top + visibleHeight >= SystemParameters.VirtualScreenTop
            && left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - visibleWidth
            && top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - visibleHeight;
    }

    private void ColumnCheckChanged(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is CheckBox { Tag: DataGridColumn column } checkBox)
        {
            column.Visibility = checkBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (_isInitializingLayout)
            {
                return;
            }

            SaveColumnSetting(column, checkBox.IsChecked == true);
        }
    }

    private async void SessionsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs routedEventArgs)
    {
        if (routedEventArgs.Column != NotesColumn)
        {
            return;
        }

        await Dispatcher.InvokeAsync(async () => await _viewModel.UpdateSelectedNotesAsync());
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _viewModel.ClearSessionDecorations();
        SaveFavoriteSettings();
    }

    private void FavoriteSession_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not FrameworkElement { DataContext: CaptureEntry entry })
        {
            return;
        }

        _viewModel.ToggleFavorite(entry);
        SaveFavoriteSettings();
    }

    private void FavoritesOnlyToggleButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SaveFavoriteSettings();
    }

    private void ProcessFilterListBox_SelectionChanged(object sender, SelectionChangedEventArgs routedEventArgs)
    {
        if (_viewModel.IsRefreshingSessionFilters)
        {
            return;
        }

        _viewModel.SetSelectedProcessFilters(ProcessFilterListBox.SelectedItems
            .OfType<SessionFilterItem>()
            .Select(static item => item.Key));
    }

    private async void ProcessFilterListBox_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.Delete)
        {
            return;
        }

        string[] selectedKeys = ProcessFilterListBox.SelectedItems
            .OfType<SessionFilterItem>()
            .Select(static item => item.Key)
            .ToArray();
        if (selectedKeys.Length == 0)
        {
            return;
        }

        keyEventArgs.Handled = true;
        await _viewModel.DeleteSessionsByProcessFiltersAsync(selectedKeys);
    }

    private void DomainFilterListBox_SelectionChanged(object sender, SelectionChangedEventArgs routedEventArgs)
    {
        if (_viewModel.IsRefreshingSessionFilters)
        {
            return;
        }

        _viewModel.SetSelectedDomainFilters(DomainFilterListBox.SelectedItems
            .OfType<SessionFilterItem>()
            .Select(static item => item.Key));
    }

    private async void DomainFilterListBox_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.Delete)
        {
            return;
        }

        string[] selectedKeys = DomainFilterListBox.SelectedItems
            .OfType<SessionFilterItem>()
            .Select(static item => item.Key)
            .ToArray();
        if (selectedKeys.Length == 0)
        {
            return;
        }

        keyEventArgs.Handled = true;
        await _viewModel.DeleteSessionsByDomainFiltersAsync(selectedKeys);
    }

    private void CaptureScopeButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not ToggleButton { Tag: string mode })
        {
            return;
        }

        SetCaptureScopeMode(mode, openSettings: !_isInitializingLayout && string.Equals(mode, "Process", StringComparison.OrdinalIgnoreCase));
    }

    private void SetCaptureScopeMode(string? mode, bool openSettings)
    {
        string normalizedMode = string.Equals(mode, "Process", StringComparison.OrdinalIgnoreCase) ? "Process" : "All";

        bool isAll = string.Equals(normalizedMode, "All", StringComparison.Ordinal);
        _isUpdatingCaptureScope = true;
        try
        {
            CaptureScopeComboBox.SelectedIndex = isAll ? 0 : 1;
        }
        finally
        {
            _isUpdatingCaptureScope = false;
        }

        _layoutSettings.CaptureScopeMode = normalizedMode;

        if (!_isInitializingLayout)
        {
            UiLayoutSettingsStore.Save(_layoutSettings);
        }

        if (openSettings && string.Equals(normalizedMode, "Process", StringComparison.Ordinal))
        {
            ShowProcessSettingsWindow();
        }
    }

    private void CaptureScopeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (_isInitializingLayout || _isUpdatingCaptureScope)
        {
            return;
        }

        string mode = (CaptureScopeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        SetCaptureScopeMode(mode, openSettings: string.Equals(mode, "Process", StringComparison.OrdinalIgnoreCase));
    }

    private void ShowProcessSettingsWindow()
    {
        try
        {
            if (_processSettingsWindow is { IsVisible: true })
            {
                if (_processSettingsWindow.WindowState == WindowState.Minimized)
                {
                    _processSettingsWindow.WindowState = WindowState.Normal;
                }

                _processSettingsWindow.Activate();
                _processSettingsWindow.Focus();
                return;
            }

            _processSettingsWindow = new SettingsWindow(_viewModel, "进程拦截")
            {
                Owner = this
            };
            _processSettingsWindow.Closed += (_, _) => _processSettingsWindow = null;
            _processSettingsWindow.Show();
            _processSettingsWindow.Activate();
        }
        catch (Exception exception)
        {
            ViewModel_NotificationRequested("错误", $"进程设置窗口打开失败：{exception.Message}");
        }
    }

    private async void IeProxy_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ToggleIeProxyAsync();
        UpdateFooterState();
    }

    private async void CaptureToggle_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ToggleCaptureAsync();
        UpdateFooterState();
    }

    private async void BreakpointMode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.CycleBreakpointModeAsync();
        UpdateFooterState();
    }

    private async void Theme_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ToggleThemeAsync();
        UpdateFooterState();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointInt
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointInt PtReserved;
        public PointInt PtMaxSize;
        public PointInt PtMaxPosition;
        public PointInt PtMinTrackSize;
        public PointInt PtMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectInt
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int CbSize;
        public RectInt RcMonitor;
        public RectInt RcWork;
        public int DwFlags;
    }

    private enum DetailZoomMode
    {
        None,
        Request,
        Response
    }
}
