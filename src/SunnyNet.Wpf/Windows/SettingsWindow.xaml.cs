using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;
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
        SelectSection(initialSection);
    }

    private void SelectSection(string? initialSection)
    {
        string sectionKey = ResolveSectionKey(initialSection);
        ListBoxItem target = sectionKey switch
        {
            "Ssl" => SslSectionItem,
            "MustTcp" => MustTcpSectionItem,
            "Proxy" => ProxySectionItem,
            "Hosts" => HostsSectionItem,
            "Replace" => ReplaceSectionItem,
            "Intercept" => InterceptSectionItem,
            "Script" => ScriptSectionItem,
            "Process" => ProcessSectionItem,
            "RequestCert" => RequestCertSectionItem,
            _ => BasicSectionItem
        };

        SectionListBox.SelectedItem = target;
        ShowSection(sectionKey);
    }

    private void SectionListBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (SectionListBox.SelectedItem is not ListBoxItem { Tag: string sectionKey })
        {
            return;
        }

        ShowSection(sectionKey);
    }

    private void ShowSection(string sectionKey)
    {
        BasicSectionPanel.Visibility = ToVisibility(sectionKey == "Basic");
        SslSectionPanel.Visibility = ToVisibility(sectionKey == "Ssl");
        MustTcpSectionPanel.Visibility = ToVisibility(sectionKey == "MustTcp");
        ProxySectionPanel.Visibility = ToVisibility(sectionKey == "Proxy");
        HostsSectionPanel.Visibility = ToVisibility(sectionKey == "Hosts");
        ReplaceSectionPanel.Visibility = ToVisibility(sectionKey == "Replace");
        InterceptSectionPanel.Visibility = ToVisibility(sectionKey == "Intercept");
        ScriptSectionPanel.Visibility = ToVisibility(sectionKey == "Script");
        ProcessSectionPanel.Visibility = ToVisibility(sectionKey == "Process");
        RequestCertSectionPanel.Visibility = ToVisibility(sectionKey == "RequestCert");

        (string title, string subtitle) = sectionKey switch
        {
            "Ssl" => ("SSL 证书", "配置默认证书或自定义 CA / KEY 文件。"),
            "MustTcp" => ("强制走 TCP", "通过规则控制指定流量强制转为 TCP。"),
            "Proxy" => ("上游网关", "设置上游代理地址以及命中规则。"),
            "Hosts" => ("HOSTS 设置", "维护域名映射规则，命中后直接重定向。"),
            "Replace" => ("替换规则", "按规则替换请求或响应中的指定内容。"),
            "Intercept" => ("拦截规则", "命中条件后自动进入上行或下行断点编辑。"),
            "Script" => ("脚本编辑", "格式化、恢复默认并保存 Go 核心脚本。"),
            "Process" => ("进程拦截", "加载驱动、指定进程名或按PID精准捕获。"),
            "RequestCert" => ("请求证书", "管理按域名匹配的请求证书并即时载入。"),
            _ => ("常规设置", "配置监听端口、协议禁用项和基础身份验证设置。")
        };

        SectionTitleTextBlock.Text = title;
        SectionSubtitleTextBlock.Text = subtitle;

        if (sectionKey == "Process" && _viewModel.ProcessDriverLoaded)
        {
            _ = RunActionAsync(() => _viewModel.RefreshRunningProcessesAsync());
        }
    }

    private static string ResolveSectionKey(string? initialSection)
    {
        return initialSection switch
        {
            "SSL证书" or "SSL 证书" => "Ssl",
            "强制走TCP" or "强制走 TCP" => "MustTcp",
            "上游网关" => "Proxy",
            "HOSTS设置" or "HOSTS 设置" => "Hosts",
            "替换规则" => "Replace",
            "拦截规则" => "Intercept",
            "脚本编辑" => "Script",
            "进程拦截" => "Process",
            "请求证书" => "RequestCert",
            _ => "Basic"
        };
    }

    private static Visibility ToVisibility(bool visible)
    {
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "SunnyNet 设置异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApplyBasic_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyBasicSettingsAsync());
    }

    private async void ApplyMustTcp_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyMustTcpSettingsAsync());
    }

    private async void EnableProxy_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyProxySettingsAsync(true));
    }

    private async void DisableProxy_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyProxySettingsAsync(false));
    }

    private async void ApplyCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyCertificateSettingsAsync());
    }

    private async void InstallDefaultCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.InstallDefaultCertificateAsync());
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
            await RunActionAsync(() => _viewModel.ExportDefaultCertificateAsync(dialog.FileName));
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

    private void AddHosts_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _viewModel.AddHostsRule();
        HostsRulesGrid.SelectedItem = _viewModel.HostsRuleItems.LastOrDefault();
    }

    private void RemoveHosts_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _viewModel.RemoveHostsRule(HostsRulesGrid.SelectedItem as HostsRuleItem);
    }

    private async void ApplyHosts_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyHostsRulesAsync());
    }

    private void AddReplace_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _viewModel.AddReplaceRule();
        ReplaceRulesGrid.SelectedItem = _viewModel.ReplaceRuleItems.LastOrDefault();
    }

    private void RemoveReplace_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _viewModel.RemoveReplaceRule(ReplaceRulesGrid.SelectedItem as ReplaceRuleItem);
    }

    private void BrowseReplaceFile_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if ((sender as FrameworkElement)?.DataContext is not ReplaceRuleItem item)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = "选择响应替换文件",
            Filter = "所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            item.ReplacementContent = dialog.FileName;
        }
    }

    private async void ApplyReplace_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyReplaceRulesAsync());
    }

    private void AddIntercept_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _viewModel.AddInterceptRule();
        InterceptRulesGrid.SelectedItem = _viewModel.InterceptRuleItems.LastOrDefault();
    }

    private void RemoveIntercept_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _viewModel.RemoveInterceptRule(InterceptRulesGrid.SelectedItem as InterceptRuleItem);
    }

    private async void ApplyIntercept_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyInterceptRulesAsync());
    }

    private async void RestoreDefaultScript_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.RestoreDefaultScriptAsync());
    }

    private async void FormatScript_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.FormatScriptCodeAsync());
    }

    private async void ApplyScript_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ApplyScriptCodeAsync());
    }

    private async void LoadProcessDriver_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.LoadProcessDriverAsync());
    }

    private async void RefreshProcesses_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.RefreshRunningProcessesAsync());
    }

    private async void CaptureAllProcesses_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.SetCaptureAllProcessesAsync(_viewModel.CaptureAllProcesses));
    }

    private async void AddProcessName_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.AddProcessCaptureNameAsync());
    }

    private async void ProcessNameInput_KeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.Enter)
        {
            return;
        }

        keyEventArgs.Handled = true;
        await RunActionAsync(() => _viewModel.AddProcessCaptureNameAsync());
    }

    private async void RemoveProcessName_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.RemoveProcessCaptureNameAsync(ProcessNamesGrid.SelectedItem as ProcessCaptureNameItem));
    }

    private async void AddLdPreset_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        string[] names =
        {
            "dnplayer.exe",
            "LdBoxHeadless.exe",
            "LdVBoxHeadless.exe",
            "Ld9BoxHeadless.exe"
        };

        await RunActionAsync(async () =>
        {
            foreach (string name in names)
            {
                await _viewModel.AddProcessCaptureNameAsync(name);
            }
        });
    }

    private async void SetProcessPid_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RunningProcessItem[] items = RunningProcessesGrid.SelectedItems.OfType<RunningProcessItem>().ToArray();
        if (items.Length == 0 && RunningProcessesGrid.SelectedItem is RunningProcessItem singleItem)
        {
            items = new[] { singleItem };
        }

        await RunActionAsync(() => _viewModel.SetPidCaptureAsync(items));
    }

    private async void ClearProcessPid_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RunningProcessItem[] items = RunningProcessesGrid.SelectedItems.OfType<RunningProcessItem>().ToArray();
        await RunActionAsync(() => items.Length > 0
            ? _viewModel.ClearPidCaptureAsync(items)
            : _viewModel.ClearPidCaptureAsync());
    }

    private async void RunningProcessCaptureCheckBox_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (sender is not CheckBox { DataContext: RunningProcessItem item } checkBox)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            await RunActionAsync(() => _viewModel.SetPidCaptureAsync(item));
            return;
        }

        await RunActionAsync(() => _viewModel.ClearPidCaptureAsync(new[] { item }));
    }

    private async void AddRequestCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.AddRequestCertificateRuleAsync());
        RequestCertGrid.SelectedItem = _viewModel.RequestCertificateItems.LastOrDefault();
    }

    private async void RemoveRequestCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.RemoveRequestCertificateRuleAsync(RequestCertGrid.SelectedItem as RequestCertificateRuleItem));
    }

    private async void ResolveRequestCertHost_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.ResolveRequestCertificateCommonNameAsync(RequestCertGrid.SelectedItem as RequestCertificateRuleItem));
    }

    private void BrowseRequestCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (RequestCertGrid.SelectedItem is not RequestCertificateRuleItem item)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = "选择证书文件",
            Filter = "证书文件 (*.p12;*.pfx;*.pkcs12;*.pem;*.cer)|*.p12;*.pfx;*.pkcs12;*.pem;*.cer|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            item.CertificateFile = dialog.FileName;
        }
    }

    private async void LoadRequestCert_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await RunActionAsync(() => _viewModel.LoadRequestCertificateAsync(RequestCertGrid.SelectedItem as RequestCertificateRuleItem));
    }
}
