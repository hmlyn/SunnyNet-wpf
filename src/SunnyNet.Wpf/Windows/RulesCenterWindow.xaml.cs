using System.Windows;
using System.Windows.Input;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Windows;

public partial class RulesCenterWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly string _initialPage;
    private string _currentPage = "请求重写";
    private readonly Dictionary<string, RulePageInfo> _pages = new()
    {
        ["请求屏蔽"] = new RulePageInfo("请求屏蔽", "命中规则后直接返回本地响应，不再访问目标服务器。"),
        ["请求重写"] = new RulePageInfo("请求重写", "命中规则后修改请求或响应的结构化内容。"),
        ["请求映射"] = new RulePageInfo("请求映射", "把命中的请求映射到本地文件、固定内容或新的远程地址。"),
        ["请求解密"] = new RulePageInfo("请求解密", "命中规则后生成请求或响应的明文展示副本，不改原始数据。")
    };

    public RulesCenterWindow(MainWindowViewModel viewModel, string initialPage = "请求重写")
    {
        _viewModel = viewModel;
        _initialPage = _pages.ContainsKey(initialPage) ? initialPage : "请求重写";
        InitializeComponent();
        DataContext = viewModel;
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

        bool blockSelected = key == "请求屏蔽";
        bool rewriteSelected = key == "请求重写";
        bool mappingSelected = key == "请求映射";
        bool decodeSelected = key == "请求解密";
        bool implemented = blockSelected || rewriteSelected || mappingSelected || decodeSelected;

        BlockRulesGrid.Visibility = blockSelected ? Visibility.Visible : Visibility.Collapsed;
        RewriteRulesGrid.Visibility = rewriteSelected ? Visibility.Visible : Visibility.Collapsed;
        MappingRulesGrid.Visibility = mappingSelected ? Visibility.Visible : Visibility.Collapsed;
        DecodeRulesGrid.Visibility = decodeSelected ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderListPanel.Visibility = implemented ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderTextBlock.Text = page.Placeholder;

        AddRuleButton.IsEnabled = implemented;
        RemoveRuleButton.IsEnabled = implemented;
        EditRuleButton.IsEnabled = implemented;

        SelectFirstRuleIfNeeded();
    }

    private void SelectFirstRuleIfNeeded()
    {
        if (_currentPage == "请求屏蔽")
        {
            if (BlockRulesGrid.SelectedItem is null && _viewModel.RequestBlockRules.Count > 0)
            {
                BlockRulesGrid.SelectedIndex = 0;
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

        if (_currentPage == "请求解密"
            && DecodeRulesGrid.SelectedItem is null
            && _viewModel.RequestDecodeRules.Count > 0)
        {
            DecodeRulesGrid.SelectedIndex = 0;
        }
    }

    private int GetRuleCount(string key)
    {
        return key switch
        {
            "请求屏蔽" => _viewModel.RequestBlockRules.Count,
            "请求重写" => _viewModel.RequestRewriteRules.Count,
            "请求映射" => _viewModel.RequestMappingRules.Count,
            "请求解密" => _viewModel.RequestDecodeRules.Count,
            _ => 0
        };
    }

    private void Close_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        Close();
    }

    private async void AddCurrentRule_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        switch (_currentPage)
        {
            case "请求屏蔽":
                await AddBlockRuleAsync();
                break;
            case "请求重写":
                await AddRewriteRuleAsync();
                break;
            case "请求映射":
                await AddMappingRuleAsync();
                break;
            case "请求解密":
                await AddDecodeRuleAsync();
                break;
        }
    }

    private async void EditSelectedRule_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await EditSelectedRuleAsync();
    }

    private async void RulesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        await EditSelectedRuleAsync();
    }

    private async void RemoveSelectedRule_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        switch (_currentPage)
        {
            case "请求屏蔽" when BlockRulesGrid.SelectedItem is RequestBlockRuleItem blockRule:
                _viewModel.RequestBlockRules.Remove(blockRule);
                break;
            case "请求重写" when RewriteRulesGrid.SelectedItem is RequestRewriteRuleItem rewriteRule:
                _viewModel.RequestRewriteRules.Remove(rewriteRule);
                break;
            case "请求映射" when MappingRulesGrid.SelectedItem is RequestMappingRuleItem mappingRule:
                _viewModel.RequestMappingRules.Remove(mappingRule);
                break;
            case "请求解密" when DecodeRulesGrid.SelectedItem is RequestDecodeRuleItem decodeRule:
                _viewModel.RequestDecodeRules.Remove(decodeRule);
                break;
            default:
                return;
        }

        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{GetRuleCount(_currentPage)} 条";
        SelectFirstRuleIfNeeded();
    }

    private async Task AddBlockRuleAsync()
    {
        RequestBlockRuleItem item = new()
        {
            Name = "新请求屏蔽",
            Method = "ANY",
            UrlMatchType = "通配",
            UrlPattern = "",
            Action = "返回空响应",
            StatusCode = 403,
            ResponseValueType = "String(UTF8)",
            State = "未保存"
        };

        if (ShowRuleEditor("请求屏蔽", item) != true)
        {
            return;
        }

        _viewModel.RequestBlockRules.Add(item);
        BlockRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.RequestBlockRules.Count} 条";
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

    private async Task AddDecodeRuleAsync()
    {
        RequestDecodeRuleItem item = new()
        {
            Name = "新请求解密",
            Method = "ANY",
            UrlMatchType = "通配",
            UrlPattern = "",
            Direction = "响应",
            DecoderType = "自动解压",
            Priority = 100,
            State = "未保存"
        };

        if (ShowRuleEditor("请求解密", item) != true)
        {
            return;
        }

        _viewModel.RequestDecodeRules.Add(item);
        DecodeRulesGrid.SelectedItem = item;
        await SaveRulesAsync();
        RuleStatusTextBlock.Text = $"{_viewModel.RequestDecodeRules.Count} 条";
    }

    private async Task EditSelectedRuleAsync()
    {
        switch (_currentPage)
        {
            case "请求屏蔽" when BlockRulesGrid.SelectedItem is RequestBlockRuleItem blockRule:
            {
                RequestBlockRuleItem editing = CloneBlockRule(blockRule);
                if (ShowRuleEditor("请求屏蔽", editing) != true)
                {
                    return;
                }

                ApplyBlockRule(blockRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.RequestBlockRules.Count} 条";
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
            case "请求解密" when DecodeRulesGrid.SelectedItem is RequestDecodeRuleItem decodeRule:
            {
                RequestDecodeRuleItem editing = CloneDecodeRule(decodeRule);
                if (ShowRuleEditor("请求解密", editing) != true)
                {
                    return;
                }

                ApplyDecodeRule(decodeRule, editing);
                await SaveRulesAsync();
                RuleStatusTextBlock.Text = $"{_viewModel.RequestDecodeRules.Count} 条";
                break;
            }
        }
    }

    private bool? ShowRuleEditor(string ruleType, object rule)
    {
        return new RuleEditorWindow(ruleType, rule)
        {
            Owner = this
        }.ShowDialog();
    }

    private async Task SaveRulesAsync()
    {
        try
        {
            await _viewModel.ApplyRuleCenterConfigAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "规则中心", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        target.StatusCode = source.StatusCode;
        target.ResponseValueType = source.ResponseValueType;
        target.ResponseContent = source.ResponseContent;
    }

    private static RequestRewriteRuleItem CloneRewriteRule(RequestRewriteRuleItem source)
    {
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

    private static RequestDecodeRuleItem CloneDecodeRule(RequestDecodeRuleItem source)
    {
        RequestDecodeRuleItem clone = new();
        ApplyDecodeRule(clone, source);
        clone.State = source.State;
        return clone;
    }

    private static void ApplyDecodeRule(RequestDecodeRuleItem target, RequestDecodeRuleItem source)
    {
        ApplyCommonRuleFields(target, source);
        target.Direction = source.Direction;
        target.DecoderType = source.DecoderType;
        target.ScriptCode = source.ScriptCode;
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
