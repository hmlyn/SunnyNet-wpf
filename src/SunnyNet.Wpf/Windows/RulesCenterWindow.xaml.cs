using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Windows;

public partial class RulesCenterWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly string _initialPage;
    private readonly DispatcherTimer _alertTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private string _currentPage = "请求重写";
    private readonly Dictionary<string, RulePageInfo> _pages = new()
    {
        ["HTTP屏蔽"] = new RulePageInfo("HTTP屏蔽", "仅作用于 HTTP/HTTPS：命中后可断开请求或断开响应。"),
        ["WebSocket屏蔽"] = new RulePageInfo("WebSocket屏蔽", "仅作用于 WebSocket：命中后可断开连接或按方向丢弃帧。"),
        ["TCP屏蔽"] = new RulePageInfo("TCP屏蔽", "仅作用于 TCP/TLS-TCP：命中后可断开连接或按方向丢弃数据包。"),
        ["UDP屏蔽"] = new RulePageInfo("UDP屏蔽", "仅作用于 UDP：命中后可按方向丢弃数据包。"),
        ["请求重写"] = new RulePageInfo("请求重写", "命中规则后修改请求或响应的结构化内容。"),
        ["请求映射"] = new RulePageInfo("请求映射", "把命中的请求映射到本地文件、固定内容或新的远程地址。")
    };

    public RulesCenterWindow(MainWindowViewModel viewModel, string initialPage = "请求重写")
    {
        _viewModel = viewModel;
        initialPage = NormalizeRulePageKey(initialPage);
        _initialPage = _pages.ContainsKey(initialPage) ? initialPage : "请求重写";
        InitializeComponent();
        DataContext = viewModel;
        _alertTimer.Tick += AlertTimer_Tick;
        Loaded += (_, _) => ApplyPage(_initialPage);
    }

    private void ApplyPage(string key)
    {
        if (!_pages.TryGetValue(key, out RulePageInfo? page))
        {
            return;
        }

        _currentPage = key;
        Title = page.Title;
        RuleStatusTextBlock.Text = $"{GetRuleCount(key)} 条";

        bool blockSelected = key == "HTTP屏蔽";
        bool webSocketBlockSelected = key == "WebSocket屏蔽";
        bool tcpBlockSelected = key == "TCP屏蔽";
        bool udpBlockSelected = key == "UDP屏蔽";
        bool rewriteSelected = key == "请求重写";
        bool mappingSelected = key == "请求映射";
        bool implemented = blockSelected || webSocketBlockSelected || tcpBlockSelected || udpBlockSelected || rewriteSelected || mappingSelected;

        BlockRulesGrid.Visibility = blockSelected ? Visibility.Visible : Visibility.Collapsed;
        WebSocketBlockRulesGrid.Visibility = webSocketBlockSelected ? Visibility.Visible : Visibility.Collapsed;
        TcpBlockRulesGrid.Visibility = tcpBlockSelected ? Visibility.Visible : Visibility.Collapsed;
        UdpBlockRulesGrid.Visibility = udpBlockSelected ? Visibility.Visible : Visibility.Collapsed;
        RewriteRulesGrid.Visibility = rewriteSelected ? Visibility.Visible : Visibility.Collapsed;
        MappingRulesGrid.Visibility = mappingSelected ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderListPanel.Visibility = implemented ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderTextBlock.Text = page.Placeholder;

        SelectFirstRuleIfNeeded();
        AddRuleButton.IsEnabled = implemented;
        UpdateRuleActionButtons();
    }

    private void SelectFirstRuleIfNeeded()
    {
        if (_currentPage == "HTTP屏蔽")
        {
            if (BlockRulesGrid.SelectedItem is null && _viewModel.RequestBlockRules.Count > 0)
            {
                BlockRulesGrid.SelectedIndex = 0;
            }
            return;
        }

        if (_currentPage == "WebSocket屏蔽")
        {
            if (WebSocketBlockRulesGrid.SelectedItem is null && _viewModel.WebSocketBlockRules.Count > 0)
            {
                WebSocketBlockRulesGrid.SelectedIndex = 0;
            }
            return;
        }

        if (_currentPage == "TCP屏蔽")
        {
            if (TcpBlockRulesGrid.SelectedItem is null && _viewModel.TcpBlockRules.Count > 0)
            {
                TcpBlockRulesGrid.SelectedIndex = 0;
            }
            return;
        }

        if (_currentPage == "UDP屏蔽")
        {
            if (UdpBlockRulesGrid.SelectedItem is null && _viewModel.UdpBlockRules.Count > 0)
            {
                UdpBlockRulesGrid.SelectedIndex = 0;
            }
            return;
        }

        if (_currentPage == "请求重写")
        {
            if (RewriteRulesGrid.SelectedItem is null && _viewModel.RequestRewriteRules.Count > 0)
            {
                RewriteRulesGrid.SelectedIndex = 0;
            }
            return;
        }

        if (_currentPage == "请求映射"
            && MappingRulesGrid.SelectedItem is null
            && _viewModel.RequestMappingRules.Count > 0)
        {
            MappingRulesGrid.SelectedIndex = 0;
            return;
        }

    }

    private int GetRuleCount(string key)
    {
        return key switch
        {
            "HTTP屏蔽" => _viewModel.RequestBlockRules.Count,
            "WebSocket屏蔽" => _viewModel.WebSocketBlockRules.Count,
            "TCP屏蔽" => _viewModel.TcpBlockRules.Count,
            "UDP屏蔽" => _viewModel.UdpBlockRules.Count,
            "请求重写" => _viewModel.RequestRewriteRules.Count,
            "请求映射" => _viewModel.RequestMappingRules.Count,
            _ => 0
        };
    }

    private async void AddCurrentRule_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        switch (_currentPage)
        {
            case "HTTP屏蔽":
                await AddBlockRuleAsync();
                break;
            case "WebSocket屏蔽":
                await AddWebSocketBlockRuleAsync();
                break;
            case "TCP屏蔽":
                await AddTcpBlockRuleAsync();
                break;
            case "UDP屏蔽":
                await AddUdpBlockRuleAsync();
                break;
            case "请求重写":
                await AddRewriteRuleAsync();
                break;
            case "请求映射":
                await AddMappingRuleAsync();
                break;
        }
    }

    private async void EditSelectedRule_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (GetSelectedRule() is null)
        {
            return;
        }

        await EditSelectedRuleAsync();
    }

    private async void RulesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (GetSelectedRule() is null)
        {
            return;
        }

        await EditSelectedRuleAsync();
    }

    private void RulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        UpdateRuleActionButtons();
    }

    private async void RemoveSelectedRule_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (GetSelectedRule() is null)
        {
            return;
        }

        switch (_currentPage)
        {
            case "HTTP屏蔽" when BlockRulesGrid.SelectedItem is RequestBlockRuleItem blockRule:
                _viewModel.RequestBlockRules.Remove(blockRule);
                break;
            case "WebSocket屏蔽" when WebSocketBlockRulesGrid.SelectedItem is WebSocketBlockRuleItem webSocketBlockRule:
                _viewModel.WebSocketBlockRules.Remove(webSocketBlockRule);
                break;
            case "TCP屏蔽" when TcpBlockRulesGrid.SelectedItem is TcpBlockRuleItem tcpBlockRule:
                _viewModel.TcpBlockRules.Remove(tcpBlockRule);
                break;
            case "UDP屏蔽" when UdpBlockRulesGrid.SelectedItem is UdpBlockRuleItem udpBlockRule:
                _viewModel.UdpBlockRules.Remove(udpBlockRule);
                break;
            case "请求重写" when RewriteRulesGrid.SelectedItem is RequestRewriteRuleItem rewriteRule:
                _viewModel.RequestRewriteRules.Remove(rewriteRule);
                break;
            case "请求映射" when MappingRulesGrid.SelectedItem is RequestMappingRuleItem mappingRule:
                _viewModel.RequestMappingRules.Remove(mappingRule);
                break;
            default:
                return;
        }

        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{GetRuleCount(_currentPage)} 条";
        SelectFirstRuleIfNeeded();
        UpdateRuleActionButtons();
    }

    private async void RuleEnableCheckBox_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await SaveRulesAsync(showNotification: false);
    }

    private async Task AddBlockRuleAsync()
    {
        RequestBlockRuleItem item = new()
        {
            Name = "新HTTP屏蔽",
            Method = "ANY",
            UrlMatchType = "通配",
            UrlPattern = "",
            Action = "断开请求",
            State = "未保存"
        };

        if (ShowRuleEditor("HTTP屏蔽", item) != true)
        {
            return;
        }

        _viewModel.RequestBlockRules.Add(item);
        BlockRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.RequestBlockRules.Count} 条";
    }

    private async Task AddWebSocketBlockRuleAsync()
    {
        WebSocketBlockRuleItem item = new()
        {
            Name = "新WebSocket屏蔽",
            Method = "ANY",
            UrlMatchType = "通配",
            UrlPattern = "",
            Action = "断开连接",
            State = "未保存"
        };

        if (ShowRuleEditor("WebSocket屏蔽", item) != true)
        {
            return;
        }

        _viewModel.WebSocketBlockRules.Add(item);
        WebSocketBlockRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.WebSocketBlockRules.Count} 条";
    }

    private async Task AddTcpBlockRuleAsync()
    {
        TcpBlockRuleItem item = new()
        {
            Name = "新TCP屏蔽",
            Method = "ANY",
            UrlMatchType = "通配",
            UrlPattern = "",
            Action = "断开连接",
            State = "未保存"
        };

        if (ShowRuleEditor("TCP屏蔽", item) != true)
        {
            return;
        }

        _viewModel.TcpBlockRules.Add(item);
        TcpBlockRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.TcpBlockRules.Count} 条";
    }

    private async Task AddUdpBlockRuleAsync()
    {
        UdpBlockRuleItem item = new()
        {
            Name = "新UDP屏蔽",
            Method = "UDP",
            UrlMatchType = "通配",
            UrlPattern = "",
            Action = "丢弃上行包",
            State = "未保存"
        };

        if (ShowRuleEditor("UDP屏蔽", item) != true)
        {
            return;
        }

        _viewModel.UdpBlockRules.Add(item);
        UdpBlockRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.UdpBlockRules.Count} 条";
    }

    private async Task AddRewriteRuleAsync()
    {
        RequestRewriteRuleItem item = new()
        {
            Name = "新请求重写",
            Method = "ANY",
            UrlMatchType = "通配",
            UrlPattern = "",
            Direction = "请求",
            Target = "协议头",
            Operation = "设置",
            ValueType = "String(UTF8)",
            Priority = 100,
            State = "未保存"
        };

        if (ShowRuleEditor("请求重写", item) != true)
        {
            return;
        }

        _viewModel.RequestRewriteRules.Add(item);
        RewriteRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.RequestRewriteRules.Count} 条";
    }

    private async Task AddMappingRuleAsync()
    {
        RequestMappingRuleItem item = new()
        {
            Name = "新请求映射",
            Method = "ANY",
            UrlMatchType = "通配",
            UrlPattern = "",
            MappingType = "本地文件",
            ValueType = "String(UTF8)",
            Priority = 100,
            State = "未保存"
        };

        if (ShowRuleEditor("请求映射", item) != true)
        {
            return;
        }

        _viewModel.RequestMappingRules.Add(item);
        MappingRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.RequestMappingRules.Count} 条";
    }

    private async Task EditSelectedRuleAsync()
    {
        switch (_currentPage)
        {
            case "HTTP屏蔽" when BlockRulesGrid.SelectedItem is RequestBlockRuleItem blockRule:
            {
                RequestBlockRuleItem editing = CloneBlockRule(blockRule);
                if (ShowRuleEditor("HTTP屏蔽", editing) != true)
                {
                    return;
                }

                ApplyBlockRule(blockRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.RequestBlockRules.Count} 条";
                break;
            }
            case "WebSocket屏蔽" when WebSocketBlockRulesGrid.SelectedItem is WebSocketBlockRuleItem webSocketBlockRule:
            {
                WebSocketBlockRuleItem editing = CloneWebSocketBlockRule(webSocketBlockRule);
                if (ShowRuleEditor("WebSocket屏蔽", editing) != true)
                {
                    return;
                }

                ApplyWebSocketBlockRule(webSocketBlockRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.WebSocketBlockRules.Count} 条";
                break;
            }
            case "TCP屏蔽" when TcpBlockRulesGrid.SelectedItem is TcpBlockRuleItem tcpBlockRule:
            {
                TcpBlockRuleItem editing = CloneTcpBlockRule(tcpBlockRule);
                if (ShowRuleEditor("TCP屏蔽", editing) != true)
                {
                    return;
                }

                ApplyTcpBlockRule(tcpBlockRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.TcpBlockRules.Count} 条";
                break;
            }
            case "UDP屏蔽" when UdpBlockRulesGrid.SelectedItem is UdpBlockRuleItem udpBlockRule:
            {
                UdpBlockRuleItem editing = CloneUdpBlockRule(udpBlockRule);
                if (ShowRuleEditor("UDP屏蔽", editing) != true)
                {
                    return;
                }

                ApplyUdpBlockRule(udpBlockRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.UdpBlockRules.Count} 条";
                break;
            }
            case "请求重写" when RewriteRulesGrid.SelectedItem is RequestRewriteRuleItem rewriteRule:
            {
                RequestRewriteRuleItem editing = CloneRewriteRule(rewriteRule);
                if (ShowRuleEditor("请求重写", editing) != true)
                {
                    return;
                }

                ApplyRewriteRule(rewriteRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.RequestRewriteRules.Count} 条";
                break;
            }
            case "请求映射" when MappingRulesGrid.SelectedItem is RequestMappingRuleItem mappingRule:
            {
                RequestMappingRuleItem editing = CloneMappingRule(mappingRule);
                if (ShowRuleEditor("请求映射", editing) != true)
                {
                    return;
                }

                ApplyMappingRule(mappingRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.RequestMappingRules.Count} 条";
                break;
            }
        }
    }

    private TrafficRuleItemBase? GetSelectedRule()
    {
        return _currentPage switch
        {
            "HTTP屏蔽" => BlockRulesGrid.SelectedItem as TrafficRuleItemBase,
            "WebSocket屏蔽" => WebSocketBlockRulesGrid.SelectedItem as TrafficRuleItemBase,
            "TCP屏蔽" => TcpBlockRulesGrid.SelectedItem as TrafficRuleItemBase,
            "UDP屏蔽" => UdpBlockRulesGrid.SelectedItem as TrafficRuleItemBase,
            "请求重写" => RewriteRulesGrid.SelectedItem as TrafficRuleItemBase,
            "请求映射" => MappingRulesGrid.SelectedItem as TrafficRuleItemBase,
            _ => null
        };
    }

    private void UpdateRuleActionButtons()
    {
        bool hasSelection = GetSelectedRule() is not null;
        RemoveRuleButton.IsEnabled = hasSelection;
        EditRuleButton.IsEnabled = hasSelection;
    }

    private bool? ShowRuleEditor(string ruleType, object rule)
    {
        return new RuleEditorWindow(ruleType, rule, ValidateRuleBeforeSave)
        {
            Owner = this
        }.ShowDialog();
    }

    private string? ValidateRuleBeforeSave(object rule)
    {
        if (rule is RequestBlockRuleItem blockRule)
        {
            return ValidateUniqueBlockRule(
                blockRule,
                _viewModel.RequestBlockRules,
                "HTTP屏蔽");
        }

        if (rule is WebSocketBlockRuleItem webSocketBlockRule)
        {
            webSocketBlockRule.Method = "ANY";
            return ValidateUniqueBlockRule(
                webSocketBlockRule,
                _viewModel.WebSocketBlockRules,
                "WebSocket屏蔽");
        }

        if (rule is TcpBlockRuleItem tcpBlockRule)
        {
            return ValidateUniqueBlockRule(
                tcpBlockRule,
                _viewModel.TcpBlockRules,
                "TCP屏蔽");
        }

        if (rule is UdpBlockRuleItem udpBlockRule)
        {
            udpBlockRule.Method = "UDP";
            return ValidateUniqueBlockRule(
                udpBlockRule,
                _viewModel.UdpBlockRules,
                "UDP屏蔽");
        }

        return null;
    }

    private static string? ValidateUniqueBlockRule<T>(T rule, IEnumerable<T> rules, string ruleType)
        where T : TrafficRuleItemBase
    {
        if (rule is null)
        {
            return null;
        }

        string url = NormalizeRuleText(rule.UrlPattern);
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Url 不能为空。";
        }

        string method = NormalizeRuleText(rule.Method);
        string matchType = NormalizeRuleText(rule.UrlMatchType);
        T? duplicate = rules.FirstOrDefault(existing =>
            !string.Equals(existing.Hash, rule.Hash, StringComparison.Ordinal)
            && string.Equals(NormalizeRuleText(existing.UrlPattern), url, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeRuleText(existing.Method), method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeRuleText(existing.UrlMatchType), matchType, StringComparison.Ordinal));

        if (duplicate is not null)
        {
            return $"已存在相同 Url 的 {ruleType}规则：{duplicate.Name}";
        }

        return null;
    }

    private static string NormalizeRuleText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private static string NormalizeRulePageKey(string? value)
    {
        return value == "请求屏蔽" ? "HTTP屏蔽" : value ?? "";
    }

    private async Task SaveRulesAsync(bool showNotification = false)
    {
        try
        {
            bool saved = await _viewModel.ApplyRuleCenterConfigAsync(showNotification);
            if (!saved)
            {
                ShowRuleAlert("规则中心配置保存失败。");
            }
        }
        catch (Exception exception)
        {
            ShowRuleAlert(exception.Message);
        }
    }

    private void ShowRuleAlert(string message)
    {
        RuleAlertTextBlock.Text = message;
        RuleAlertBorder.Visibility = Visibility.Visible;
        _alertTimer.Stop();
        _alertTimer.Start();
    }

    private void HideRuleAlert()
    {
        _alertTimer.Stop();
        RuleAlertBorder.Visibility = Visibility.Collapsed;
    }

    private void AlertTimer_Tick(object? sender, EventArgs eventArgs)
    {
        HideRuleAlert();
    }

    private static RequestBlockRuleItem CloneBlockRule(RequestBlockRuleItem source)
    {
        RequestBlockRuleItem clone = new();
        ApplyBlockRule(clone, source);
        clone.State = source.State;
        return clone;
    }

    private static void ApplyBlockRule(RequestBlockRuleItem target, RequestBlockRuleItem source)
    {
        ApplyCommonRuleFields(target, source);
        target.Action = source.Action;
    }

    private static WebSocketBlockRuleItem CloneWebSocketBlockRule(WebSocketBlockRuleItem source)
    {
        WebSocketBlockRuleItem clone = new();
        ApplyWebSocketBlockRule(clone, source);
        clone.State = source.State;
        return clone;
    }

    private static void ApplyWebSocketBlockRule(WebSocketBlockRuleItem target, WebSocketBlockRuleItem source)
    {
        ApplyCommonRuleFields(target, source);
        target.Method = "ANY";
        target.Action = source.Action;
    }

    private static TcpBlockRuleItem CloneTcpBlockRule(TcpBlockRuleItem source)
    {
        TcpBlockRuleItem clone = new();
        ApplyTcpBlockRule(clone, source);
        clone.State = source.State;
        return clone;
    }

    private static void ApplyTcpBlockRule(TcpBlockRuleItem target, TcpBlockRuleItem source)
    {
        ApplyCommonRuleFields(target, source);
        target.Action = source.Action;
    }

    private static UdpBlockRuleItem CloneUdpBlockRule(UdpBlockRuleItem source)
    {
        UdpBlockRuleItem clone = new();
        ApplyUdpBlockRule(clone, source);
        clone.State = source.State;
        return clone;
    }

    private static void ApplyUdpBlockRule(UdpBlockRuleItem target, UdpBlockRuleItem source)
    {
        ApplyCommonRuleFields(target, source);
        target.Method = "UDP";
        target.Action = source.Action;
    }

    private static RequestRewriteRuleItem CloneRewriteRule(RequestRewriteRuleItem source)
    {
        source.SyncOperationsJson();
        RequestRewriteRuleItem clone = new();
        ApplyRewriteRule(clone, source);
        clone.State = source.State;
        return clone;
    }

    private static void ApplyRewriteRule(RequestRewriteRuleItem target, RequestRewriteRuleItem source)
    {
        ApplyCommonRuleFields(target, source);
        target.Direction = source.Direction;
        target.Target = source.Target;
        target.Operation = source.Operation;
        target.Key = source.Key;
        target.Value = source.Value;
        target.ValueType = source.ValueType;
        target.OperationsJson = source.OperationsJson;
        target.LoadOperationsFromJson();
    }

    private static RequestMappingRuleItem CloneMappingRule(RequestMappingRuleItem source)
    {
        RequestMappingRuleItem clone = new();
        ApplyMappingRule(clone, source);
        clone.State = source.State;
        return clone;
    }

    private static void ApplyMappingRule(RequestMappingRuleItem target, RequestMappingRuleItem source)
    {
        ApplyCommonRuleFields(target, source);
        target.MappingType = source.MappingType;
        target.SourceContent = source.SourceContent;
        target.TargetContent = source.TargetContent;
        target.ValueType = source.ValueType;
        target.LegacyReplaceRule = source.LegacyReplaceRule;
    }

    private static void ApplyCommonRuleFields(TrafficRuleItemBase target, TrafficRuleItemBase source)
    {
        target.Hash = source.Hash;
        target.Enabled = source.Enabled;
        target.Name = source.Name;
        target.Method = source.Method;
        target.UrlMatchType = source.UrlMatchType;
        target.UrlPattern = source.UrlPattern;
        target.Priority = source.Priority;
        target.Note = source.Note;
    }

    private sealed record RulePageInfo(string Title, string Placeholder);
}
