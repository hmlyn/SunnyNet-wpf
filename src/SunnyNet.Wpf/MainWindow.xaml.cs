using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;
using SunnyNet.Wpf.Windows;

namespace SunnyNet.Wpf;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private DetailZoomMode _detailZoomMode;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        _viewModel.NotificationRequested += ViewModel_NotificationRequested;
        _viewModel.ScrollToEntryRequested += ViewModel_ScrollToEntryRequested;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateFooterState();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.InitializeAsync();
        UpdateFooterState();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs cancelEventArgs)
    {
        await _viewModel.DisposeAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs propertyChangedEventArgs)
    {
        if (propertyChangedEventArgs.PropertyName is nameof(MainWindowViewModel.BreakpointMode) or nameof(MainWindowViewModel.IeProxyEnabled) or nameof(MainWindowViewModel.IsCapturing) or nameof(MainWindowViewModel.Settings))
        {
            UpdateFooterState();
        }
    }

    private void UpdateFooterState()
    {
        ThemeGlyph.Text = _viewModel.Settings.IsDarkTheme ? "\uE706" : "\uE708";
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
        SettingsWindow window = new(_viewModel, "进程拦截") { Owner = this };
        window.Show();
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

        RequestPanelRow.Height = responseExpanded ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        DetailSplitterRow.Height = isZoomed ? new GridLength(0) : new GridLength(10);
        ResponsePanelRow.Height = requestExpanded ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        RequestZoomGlyph.Text = requestExpanded ? "\uE923" : "\uE740";
        ResponseZoomGlyph.Text = responseExpanded ? "\uE923" : "\uE740";
    }

    private void ColumnCheckChanged(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is CheckBox { Tag: DataGridColumn column } checkBox)
        {
            column.Visibility = checkBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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

    private enum DetailZoomMode
    {
        None,
        Request,
        Response
    }
}
