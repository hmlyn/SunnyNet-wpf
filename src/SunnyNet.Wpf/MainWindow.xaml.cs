using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Text;
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
    private bool _isApplyingMcpState;
    private CaptureEntry? _contextSessionEntry;
    private DispatcherTimer? _columnWidthSaveTimer;
    private SunnyNetCompatibleMcpServer? _mcpServer;

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
        _viewModel.Mcp.PropertyChanged += Mcp_PropertyChanged;
        _viewModel.Detail.PropertyChanged += Detail_PropertyChanged;
        RegisterSessionColumnWidthListeners();
        UpdateLanIpPopupItems();
        UpdateFooterState();
    }

    private void UpdateLanIpPopupItems()
    {
        string[] addresses = GetLanIPv4Addresses();
        LanIpItemsControl.ItemsSource = addresses.Length == 0 ? new[] { "未检测到" } : addresses;
    }

    private void LanIpIconBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        ShowLanIpPopup();
        mouseButtonEventArgs.Handled = true;
    }

    private void LanIpIconBorder_MouseEnter(object sender, MouseEventArgs mouseEventArgs)
    {
        ShowLanIpPopup();
    }

    private void ShowLanIpPopup()
    {
        UpdateLanIpPopupItems();
        LanIpPopup.IsOpen = true;
    }

    private void LanIpCopyButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is not string ip || ip == "未检测到")
        {
            return;
        }

        try
        {
            Clipboard.SetText(ip);
            _viewModel.StatusRight = $"已复制内网 IP：{ip}";
            LanIpPopup.IsOpen = false;
        }
        catch
        {
        }
    }

    private static string[] GetLanIPv4Addresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(static networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up
                    && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(static networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Where(static address => address.Address.AddressFamily == AddressFamily.InterNetwork && IsLanIPv4(address.Address))
                .Select(static address => address.Address.ToString())
                .Distinct()
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsLanIPv4(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 169 && bytes[1] == 254;
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

        await ApplyMcpServerStateAsync(_viewModel.Mcp.Enabled);
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
            _viewModel.Mcp.PropertyChanged -= Mcp_PropertyChanged;
            if (_mcpServer is not null)
            {
                await _mcpServer.DisposeAsync();
                _mcpServer = null;
            }

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

    private void Mcp_PropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        if (propertyChangedEventArgs.PropertyName is nameof(McpIntegrationState.Enabled))
        {
            if (!_isApplyingMcpState)
            {
                _ = ApplyMcpServerStateAsync(_viewModel.Mcp.Enabled);
            }

            UpdateFooterState();
            return;
        }

        if (propertyChangedEventArgs.PropertyName is nameof(McpIntegrationState.ServerRunning)
            or nameof(McpIntegrationState.BridgeExists)
            or nameof(McpIntegrationState.ServerStatusText)
            or nameof(McpIntegrationState.ToolCount))
        {
            UpdateFooterState();
        }
    }

    private void Detail_PropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        if (propertyChangedEventArgs.PropertyName == nameof(SessionDetail.InlineInterceptMode))
        {
            if (_viewModel.Detail.InlineInterceptMode == 1)
            {
                RequestTabControl.SelectedIndex = 0;
            }
            else if (_viewModel.Detail.InlineInterceptMode == 2)
            {
                ResponseTabControl.SelectedIndex = 0;
            }

            return;
        }

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
            BreakpointLabel.Visibility = Visibility.Visible;
        }
        else if (_viewModel.BreakpointMode == 2)
        {
            BreakpointGlyph.Text = "\uE7BF";
            BreakpointLabel.Text = "拦截下行";
            BreakpointLabel.Visibility = Visibility.Visible;
        }
        else
        {
            BreakpointGlyph.Text = "\uF127";
            BreakpointLabel.Text = string.Empty;
            BreakpointLabel.Visibility = Visibility.Collapsed;
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

    private void StartCompatibleMcpServer()
    {
        if (_mcpServer?.IsRunning == true)
        {
            return;
        }

        try
        {
            _mcpServer ??= new SunnyNetCompatibleMcpServer(_viewModel);
            _mcpServer.Start();
        }
        catch (Exception exception)
        {
            _mcpServer = null;
            _viewModel.Mcp.ServerRunning = false;
            _viewModel.Mcp.ServerStatusText = "启动失败";
            _viewModel.Mcp.LastError = exception.Message;
            _viewModel.StatusRight = $"MCP 启动失败: {exception.Message}";
        }
    }

    private async Task StopCompatibleMcpServerAsync()
    {
        if (_mcpServer is null)
        {
            _viewModel.Mcp.ServerRunning = false;
            _viewModel.Mcp.ServerStatusText = "已关闭";
            return;
        }

        await _mcpServer.DisposeAsync();
        _mcpServer = null;
        _viewModel.Mcp.ServerRunning = false;
        _viewModel.Mcp.ServerStatusText = "已关闭";
    }

    private async Task ApplyMcpServerStateAsync(bool enabled)
    {
        if (_isApplyingMcpState)
        {
            return;
        }

        _isApplyingMcpState = true;
        try
        {
            if (enabled)
            {
                _viewModel.Mcp.LastError = "";
                StartCompatibleMcpServer();
                _viewModel.StatusRight = "MCP 已开启";
            }
            else
            {
                await StopCompatibleMcpServerAsync();
                _viewModel.StatusRight = "MCP 已关闭";
            }

            await _viewModel.RefreshMcpStatusAsync();
        }
        finally
        {
            _isApplyingMcpState = false;
            UpdateFooterState();
        }
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

    private void JsonTool_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        new JsonToolWindow { Owner = this }.Show();
    }

    private void CryptoTool_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        new CryptoToolWindow { Owner = this }.Show();
    }

    private void CertificateGuide_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        new CertificateGuideWindow(_viewModel) { Owner = this }.Show();
    }

    private void Find_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ShowSearchWindow();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key == Key.Delete && IsKeyboardFocusWithinSessionsGrid())
        {
            DeleteSelectedSessionsFromKeyboard();
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && IsKeyboardFocusWithinSessionsGrid())
        {
            SelectAllVisibleSessions();
            keyEventArgs.Handled = true;
            return;
        }

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
        Rect workArea = ResolveTargetWorkArea(
            _layoutSettings.WindowLeft,
            _layoutSettings.WindowTop,
            _layoutSettings.WindowWidth,
            _layoutSettings.WindowHeight);

        (double defaultWidth, double defaultHeight) = GetAdaptiveDefaultWindowSize(workArea);
        bool hasSavedSize = IsValidLength(_layoutSettings.WindowWidth) && IsValidLength(_layoutSettings.WindowHeight);

        double width = hasSavedSize ? _layoutSettings.WindowWidth : defaultWidth;
        double height = hasSavedSize ? _layoutSettings.WindowHeight : defaultHeight;

        bool hasStoredWorkArea = IsValidLength(_layoutSettings.WindowWorkAreaWidth) && IsValidLength(_layoutSettings.WindowWorkAreaHeight);
        if (hasSavedSize && hasStoredWorkArea)
        {
            double scaleX = workArea.Width / _layoutSettings.WindowWorkAreaWidth;
            double scaleY = workArea.Height / _layoutSettings.WindowWorkAreaHeight;
            double scale = Math.Min(1d, Math.Min(scaleX, scaleY));
            if (scale < 0.985)
            {
                width *= scale;
                height *= scale;
            }
        }
        else if (hasSavedSize && !_layoutSettings.IsWindowMaximized
            && (width > workArea.Width * 0.88 || height > workArea.Height * 0.88))
        {
            width = Math.Min(width, defaultWidth);
            height = Math.Min(height, defaultHeight);
        }

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
        Dictionary<string, DataGridColumn> columns = GetSessionColumnMap();
        foreach ((string key, DataGridColumn column) in columns)
        {
            if (_layoutSettings.SessionColumns.TryGetValue(key, out bool isVisible))
            {
                column.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_layoutSettings.SessionColumnWidths.TryGetValue(key, out double width) && IsValidLength(width))
            {
                column.Width = new DataGridLength(Clamp(width, 36, 1200), DataGridLengthUnitType.Pixel);
            }
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
        Rect workArea = ResolveTargetWorkArea(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        if (IsValidSize(bounds.Width, bounds.Height))
        {
            _layoutSettings.WindowWidth = bounds.Width;
            _layoutSettings.WindowHeight = bounds.Height;
        }

        if (IsValidSize(workArea.Width, workArea.Height))
        {
            _layoutSettings.WindowWorkAreaWidth = workArea.Width;
            _layoutSettings.WindowWorkAreaHeight = workArea.Height;
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
            SaveSessionColumnWidth(key, column);
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
        SaveSessionColumnWidth(key, column);
        UiLayoutSettingsStore.Save(_layoutSettings);
    }

    private void SaveSessionColumnWidth(string key, DataGridColumn column)
    {
        double width = column.ActualWidth;
        if (!IsValidLength(width) || width < 20)
        {
            width = column.Width.DisplayValue;
        }

        if (IsValidLength(width) && width >= 20)
        {
            _layoutSettings.SessionColumnWidths[key] = Math.Round(width, 1);
        }
    }

    private void RegisterSessionColumnWidthListeners()
    {
        foreach (DataGridColumn column in GetSessionColumnMap().Values)
        {
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                ?.AddValueChanged(column, SessionColumnWidthChanged);
        }
    }

    private void SessionColumnWidthChanged(object? sender, EventArgs eventArgs)
    {
        if (_isInitializingLayout || sender is not DataGridColumn column)
        {
            return;
        }

        string? key = GetSessionColumnKey(column);
        if (key is null)
        {
            return;
        }

        SaveSessionColumnWidth(key, column);
        _columnWidthSaveTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _columnWidthSaveTimer.Tick -= ColumnWidthSaveTimer_Tick;
        _columnWidthSaveTimer.Tick += ColumnWidthSaveTimer_Tick;
        _columnWidthSaveTimer.Stop();
        _columnWidthSaveTimer.Start();
    }

    private void ColumnWidthSaveTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _columnWidthSaveTimer?.Stop();
        foreach ((string key, DataGridColumn column) in GetSessionColumnMap())
        {
            SaveSessionColumnWidth(key, column);
        }

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
            ["Favorite"] = FavoriteColumn,
            ["Method"] = MethodColumn,
            ["Url"] = UrlColumn,
            ["State"] = StateColumn,
            ["Length"] = LengthColumn,
            ["Type"] = TypeColumn,
            ["Notes"] = NotesColumn,
            ["Process"] = ProcessColumn
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
        if (!IsFiniteCoordinate(left) || !IsFiniteCoordinate(top))
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

    private (double Width, double Height) GetAdaptiveDefaultWindowSize(Rect workArea)
    {
        double preferredWidth = Math.Min(1460, Math.Floor(workArea.Width * 0.8));
        double preferredHeight = Math.Min(820, Math.Floor(workArea.Height * 0.8));
        double width = Clamp(preferredWidth, MinWidth, Math.Max(MinWidth, workArea.Width));
        double height = Clamp(preferredHeight, MinHeight, Math.Max(MinHeight, workArea.Height));
        return (width, height);
    }

    private static bool IsFiniteCoordinate(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static Rect ResolveTargetWorkArea(double left, double top, double width, double height)
    {
        if (!IsValidLength(width) || !IsValidLength(height))
        {
            return SystemParameters.WorkArea;
        }

        RectInt rect = new()
        {
            Left = (int)Math.Round(IsFiniteCoordinate(left) ? left : SystemParameters.WorkArea.Left),
            Top = (int)Math.Round(IsFiniteCoordinate(top) ? top : SystemParameters.WorkArea.Top),
            Right = (int)Math.Round((IsFiniteCoordinate(left) ? left : SystemParameters.WorkArea.Left) + width),
            Bottom = (int)Math.Round((IsFiniteCoordinate(top) ? top : SystemParameters.WorkArea.Top) + height)
        };

        IntPtr monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return SystemParameters.WorkArea;
        }

        MonitorInfo monitorInfo = new()
        {
            CbSize = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return SystemParameters.WorkArea;
        }

        RectInt workArea = monitorInfo.RcWork;
        return new Rect(
            workArea.Left,
            workArea.Top,
            Math.Max(0, workArea.Right - workArea.Left),
            Math.Max(0, workArea.Bottom - workArea.Top));
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

    private void SessionsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (FindVisualParent<DataGridRow>(mouseButtonEventArgs.OriginalSource as DependencyObject) is not { Item: CaptureEntry entry } row)
        {
            return;
        }

        _contextSessionEntry = entry;
        if (!row.IsSelected)
        {
            SessionsGrid.SelectedItems.Clear();
            row.IsSelected = true;
            SessionsGrid.SelectedItem = entry;
        }

        row.Focus();
    }

    private void SessionsGridContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        CaptureEntry[] entries = GetSelectedSessionEntries();
        bool hasSelection = entries.Length > 0;
        bool isHttpSelection = hasSelection && entries.All(IsRequestCodeSupportedSession);
        bool hasConnectedSocket = hasSelection && entries.Any(IsConnectedSocketSession);
        bool hasProcess = entries.Any(static entry => !string.IsNullOrWhiteSpace(entry.Process));
        bool hasDomain = entries.Any(static entry => !string.IsNullOrWhiteSpace(entry.Host));
        bool hasContext = _contextSessionEntry is not null;

        CopySelectedSessionsMenuItem.IsEnabled = hasSelection;
        GenerateCodeSessionsMenuItem.IsEnabled = isHttpSelection;
        FavoriteSelectedSessionsMenuItem.IsEnabled = hasSelection;
        FavoriteSelectedSessionsMenuItem.Header = hasSelection && entries.All(static entry => entry.IsFavorite)
            ? "取消收藏"
            : "标记收藏";
        EditNotesSessionsMenuItem.IsEnabled = hasSelection;
        EditNotesSessionsMenuItem.Header = entries.Length > 1 ? $"编辑备注 ({entries.Length})..." : "编辑备注...";
        SelectSessionsMenuItem.IsEnabled = SessionsGrid.Items.Count > 0;
        SelectParentRequestsMenuItem.IsEnabled = hasContext;
        SelectChildRequestsMenuItem.IsEnabled = hasContext;
        SelectSameValueMenuItem.IsEnabled = hasContext;
        SelectSameValueMenuItem.Header = BuildSameValueMenuHeader(_contextSessionEntry);
        FilterSelectedSessionsMenuItem.IsEnabled = hasSelection;
        FilterByProcessMenuItem.IsEnabled = hasProcess;
        FilterByDomainMenuItem.IsEnabled = hasDomain;
        ClearFiltersFromSessionsMenuItem.IsEnabled = _viewModel.HasActiveSessionFilter;
        ResendSelectedSessionsMenuItem.IsEnabled = isHttpSelection;
        CloseSelectedConnectionsMenuItem.Visibility = hasConnectedSocket ? Visibility.Visible : Visibility.Collapsed;
        CloseSelectedConnectionsMenuItem.IsEnabled = hasConnectedSocket;
        AutoScrollFromSessionsMenuItem.IsChecked = _viewModel.AutoScroll;
        DeleteSelectedSessionsMenuItem.IsEnabled = hasSelection;
        DeleteSelectedSessionsMenuItem.Header = entries.Length > 1 ? $"删除选中 ({entries.Length})" : "删除选中";
    }

    private void SelectAllSessions_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SelectAllVisibleSessions();
    }

    private void InvertSelectedSessions_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        HashSet<CaptureEntry> selected = GetSelectedSessionEntries().ToHashSet();
        SessionsGrid.SelectedItems.Clear();
        foreach (CaptureEntry entry in GetVisibleSessionEntries())
        {
            if (!selected.Contains(entry))
            {
                SessionsGrid.SelectedItems.Add(entry);
            }
        }
    }

    private void SelectParentRequests_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        CaptureEntry? context = _contextSessionEntry ?? _viewModel.SelectedSession;
        if (context is null)
        {
            return;
        }

        SelectSessions(entry => IsParentRequest(entry, context));
    }

    private void SelectChildRequests_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        CaptureEntry? context = _contextSessionEntry ?? _viewModel.SelectedSession;
        if (context is null)
        {
            return;
        }

        SelectSessions(entry => IsChildRequest(entry, context));
    }

    private void SelectSameValueSessions_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        CaptureEntry? context = _contextSessionEntry ?? _viewModel.SelectedSession;
        if (context is null)
        {
            return;
        }

        string matchValue = ResolveMatchValue(context);
        if (string.IsNullOrWhiteSpace(matchValue))
        {
            return;
        }

        SelectSessions(entry => string.Equals(ResolveMatchValue(entry), matchValue, StringComparison.OrdinalIgnoreCase));
    }

    private void CopySelectedSessionUrls_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        string text = string.Join(Environment.NewLine, GetSelectedSessionEntries()
            .Select(static entry => entry.Url)
            .Where(static value => !string.IsNullOrWhiteSpace(value)));

        CopyTextToClipboard(text, "已复制请求地址");
    }

    private void CopySelectedSessionHosts_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        string text = string.Join(Environment.NewLine, GetSelectedSessionEntries()
            .Select(GetSessionHost)
            .Where(static value => !string.IsNullOrWhiteSpace(value)));

        CopyTextToClipboard(text, "已复制 HOST");
    }

    private void CopySelectedSessionSummary_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        CaptureEntry[] entries = GetSelectedSessionEntries();
        if (entries.Length == 0)
        {
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("序号\t方式\t状态\t请求地址\t响应长度\t响应类型\t进程\t备注");
        foreach (CaptureEntry entry in entries)
        {
            builder.Append(entry.Index).Append('\t')
                .Append(entry.DisplayMethod).Append('\t')
                .Append(entry.State).Append('\t')
                .Append(entry.Url).Append('\t')
                .Append(entry.ResponseLength).Append('\t')
                .Append(entry.ResponseType).Append('\t')
                .Append(entry.Process).Append('\t')
                .AppendLine(entry.Notes);
        }

        CopyTextToClipboard(builder.ToString().TrimEnd(), $"已复制 {entries.Length} 条会话摘要");
    }

    private async void GenerateRequestCode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not MenuItem { Tag: string tag })
        {
            return;
        }

        string[] parts = tag.Split('|', 2);
        if (parts.Length != 2)
        {
            return;
        }

        try
        {
            await _viewModel.GenerateRequestCodeAsync(GetSelectedSessionEntries(), parts[0], parts[1]);
        }
        catch (Exception exception)
        {
            ViewModel_NotificationRequested("生成失败", exception.Message);
        }
    }

    private void FavoriteSelectedSessions_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        CaptureEntry[] entries = GetSelectedSessionEntries();
        if (entries.Length == 0)
        {
            return;
        }

        bool shouldFavorite = !entries.All(static entry => entry.IsFavorite);
        foreach (CaptureEntry entry in entries)
        {
            if (entry.IsFavorite != shouldFavorite)
            {
                _viewModel.ToggleFavorite(entry);
            }
        }

        SaveFavoriteSettings();
    }

    private async void EditSelectedSessionNotes_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        CaptureEntry[] entries = GetSelectedSessionEntries();
        if (entries.Length == 0)
        {
            return;
        }

        string initialNotes = entries.Length == 1 || entries.All(entry => string.Equals(entry.Notes, entries[0].Notes, StringComparison.Ordinal))
            ? entries[0].Notes
            : "";

        SessionNotesWindow window = new(initialNotes, entries.Length)
        {
            Owner = this
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _viewModel.UpdateSessionNotesAsync(entries, window.NotesText);
        }
        catch (Exception exception)
        {
            ViewModel_NotificationRequested("备注失败", exception.Message);
        }
    }

    private void FilterSelectedSessionsByProcess_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        string[] keys = GetSelectedSessionEntries()
            .Select(static entry => NormalizeMenuFilterValue(entry.Process, "未知进程"))
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _viewModel.SetSelectedProcessFilters(keys);
    }

    private void FilterSelectedSessionsByDomain_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        string[] keys = GetSelectedSessionEntries()
            .Select(static entry => NormalizeMenuFilterValue(entry.Host, "无域名"))
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _viewModel.SetSelectedDomainFilters(keys);
    }

    private async void ResendSelectedSessions_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not MenuItem menuItem || !int.TryParse(menuItem.Tag?.ToString(), out int mode))
        {
            return;
        }

        try
        {
            CaptureEntry[] entries = GetSelectedSessionEntries();
            if (mode is 1 or 2)
            {
                await _viewModel.ResendSessionEntriesWithInterceptEditorAsync(entries, mode);
            }
            else
            {
                await _viewModel.ResendSessionEntriesAsync(entries, mode);
            }
        }
        catch (Exception exception)
        {
            ViewModel_NotificationRequested("重放失败", exception.Message);
        }
    }

    private async void CloseSelectedConnections_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            await _viewModel.CloseSessionEntriesAsync(GetSelectedSessionEntries().Where(IsConnectedSocketSession));
        }
        catch (Exception exception)
        {
            ViewModel_NotificationRequested("断开失败", exception.Message);
        }
    }

    private void AutoScrollFromSessions_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is MenuItem menuItem)
        {
            SetAutoScrollEnabled(menuItem.IsChecked);
        }
    }

    private void AutoScrollToolbarButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SetAutoScrollEnabled(AutoScrollToolbarButton.IsChecked == true);
    }

    private async void DeleteSelectedSessions_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await DeleteSelectedSessionsAsync();
    }

    private async void DeleteSelectedSessionsFromKeyboard()
    {
        await DeleteSelectedSessionsAsync();
    }

    private async Task DeleteSelectedSessionsAsync()
    {
        try
        {
            await _viewModel.DeleteSessionEntriesAsync(GetSelectedSessionEntries());
        }
        catch (Exception exception)
        {
            ViewModel_NotificationRequested("删除失败", exception.Message);
        }
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

    private CaptureEntry[] GetSelectedSessionEntries()
    {
        CaptureEntry[] entries = SessionsGrid.SelectedItems
            .OfType<CaptureEntry>()
            .OrderBy(static entry => entry.Index)
            .ToArray();

        if (entries.Length > 0)
        {
            return entries;
        }

        return _viewModel.SelectedSession is null
            ? Array.Empty<CaptureEntry>()
            : new[] { _viewModel.SelectedSession };
    }

    private CaptureEntry[] GetVisibleSessionEntries()
    {
        return _viewModel.SessionsView
            .OfType<CaptureEntry>()
            .OrderBy(static entry => entry.Index)
            .ToArray();
    }

    private void SelectAllVisibleSessions()
    {
        SessionsGrid.SelectedItems.Clear();
        foreach (CaptureEntry entry in GetVisibleSessionEntries())
        {
            SessionsGrid.SelectedItems.Add(entry);
        }
    }

    private void SelectSessions(Func<CaptureEntry, bool> predicate)
    {
        CaptureEntry[] entries = GetVisibleSessionEntries()
            .Where(predicate)
            .ToArray();

        SessionsGrid.SelectedItems.Clear();
        foreach (CaptureEntry entry in entries)
        {
            SessionsGrid.SelectedItems.Add(entry);
        }

        if (entries.Length > 0)
        {
            SessionsGrid.ScrollIntoView(entries[0]);
            _viewModel.SelectedSession = entries[0];
        }
    }

    private bool IsKeyboardFocusWithinSessionsGrid()
    {
        if (Keyboard.FocusedElement is TextBoxBase)
        {
            return false;
        }

        return SessionsGrid.IsKeyboardFocusWithin
            || FindVisualParent<DataGrid>(Keyboard.FocusedElement as DependencyObject) == SessionsGrid;
    }

    private static bool IsParentRequest(CaptureEntry entry, CaptureEntry context)
    {
        string contextHost = GetSessionHost(context);
        if (string.IsNullOrWhiteSpace(contextHost))
        {
            return false;
        }

        return entry.Index < context.Index
            && string.Equals(GetSessionHost(entry), contextHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChildRequest(CaptureEntry entry, CaptureEntry context)
    {
        string contextHost = GetSessionHost(context);
        if (string.IsNullOrWhiteSpace(contextHost))
        {
            return false;
        }

        return entry.Index > context.Index
            && string.Equals(GetSessionHost(entry), contextHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMatchValue(CaptureEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Host))
        {
            return entry.Host.Trim();
        }

        if (!string.IsNullOrWhiteSpace(entry.Process))
        {
            return entry.Process.Trim();
        }

        return entry.DisplayMethod.Trim();
    }

    private static string BuildSameValueMenuHeader(CaptureEntry? entry)
    {
        if (entry is null)
        {
            return "匹配值";
        }

        string value = ResolveMatchValue(entry);
        return string.IsNullOrWhiteSpace(value) ? "匹配值" : $"匹配值：{value}";
    }

    private void CopyTextToClipboard(string text, string statusText)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _viewModel.StatusRight = "没有可复制的内容";
            return;
        }

        try
        {
            Clipboard.SetText(text);
            _viewModel.StatusRight = statusText;
        }
        catch (Exception exception)
        {
            ViewModel_NotificationRequested("错误", $"复制失败：{exception.Message}");
        }
    }

    private void SetAutoScrollEnabled(bool enabled)
    {
        _viewModel.AutoScroll = enabled;
        _viewModel.StatusRight = enabled ? "已开启跟随显示" : "已关闭跟随显示";

        if (enabled)
        {
            ScrollToLatestSession();
        }
    }

    private void ScrollToLatestSession()
    {
        CaptureEntry? latestEntry = _viewModel.SessionsView
            .OfType<CaptureEntry>()
            .LastOrDefault();

        if (latestEntry is null)
        {
            return;
        }

        SessionsGrid.ScrollIntoView(latestEntry);
        SessionsGrid.UpdateLayout();
    }

    private static string GetSessionHost(CaptureEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Host))
        {
            return entry.Host.Trim();
        }

        return Uri.TryCreate(entry.Url, UriKind.Absolute, out Uri? uri) ? uri.Host : "";
    }

    private static string NormalizeMenuFilterValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool IsRequestCodeSupportedSession(CaptureEntry entry)
    {
        string method = entry.Method.ToUpperInvariant();
        return !method.Contains("TCP", StringComparison.Ordinal)
            && !method.Contains("UDP", StringComparison.Ordinal)
            && !method.Contains("WEBSOCKET", StringComparison.Ordinal);
    }

    private static bool IsConnectedSocketSession(CaptureEntry entry)
    {
        string method = entry.Method.ToUpperInvariant();
        string state = entry.State.ToUpperInvariant();
        return state.Contains("已连接", StringComparison.Ordinal)
            && (method.Contains("TCP", StringComparison.Ordinal)
                || method.Contains("WEBSOCKET", StringComparison.Ordinal));
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RectInt rect, int flags);

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
