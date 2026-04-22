using System.Windows;
using Microsoft.Win32;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Windows;

public partial class SettingsWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public SettingsWindow(MainWindowViewModel viewModel, string? initialSection = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        ExpandSection(initialSection);
    }

    private void ExpandSection(string? initialSection)
    {
        if (string.IsNullOrWhiteSpace(initialSection))
        {
            return;
        }

        BasicExpander.IsExpanded = initialSection == "常规设置";
        SslExpander.IsExpanded = initialSection == "SSL证书";
        MustTcpExpander.IsExpanded = initialSection == "强制走TCP";
        ProxyExpander.IsExpanded = initialSection == "上游网关";
        HostsExpander.IsExpanded = initialSection == "HOSTS设置";
        ReplaceExpander.IsExpanded = initialSection == "替换规则";
        ScriptExpander.IsExpanded = initialSection == "脚本编辑";
        ProcessExpander.IsExpanded = initialSection == "进程拦截";
        RequestCertExpander.IsExpanded = initialSection == "请求证书";
        ColorExpander.IsExpanded = initialSection == "列表配色";
    }

    private async void ApplyBasic_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ApplyBasicSettingsAsync();
    }

    private async void ApplyMustTcp_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ApplyMustTcpSettingsAsync();
    }

    private async void EnableProxy_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ApplyProxySettingsAsync(true);
    }

    private async void DisableProxy_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ApplyProxySettingsAsync(false);
    }

    private async void ApplyCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.ApplyCertificateSettingsAsync();
    }

    private async void InstallDefaultCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.InstallDefaultCertificateAsync();
    }

    private async void ExportDefaultCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        SaveFileDialog dialog = new()
        {
            Title = "导出默认证书",
            Filter = "证书文件 (*.cer)|*.cer",
            DefaultExt = ".cer",
            FileName = "SunnyNet.cer"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.ExportDefaultCertificateAsync(dialog.FileName);
        }
    }

    private void BrowseCa_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择 CA 文件",
            Filter = "证书文件 (*.cer;*.crt)|*.cer;*.crt|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Settings.CaFilePath = dialog.FileName;
        }
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择 KEY 文件",
            Filter = "密钥文件 (*.key)|*.key|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Settings.KeyFilePath = dialog.FileName;
        }
    }
}
