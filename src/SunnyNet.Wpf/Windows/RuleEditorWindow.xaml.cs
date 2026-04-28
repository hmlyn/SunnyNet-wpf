using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Windows;

public partial class RuleEditorWindow : Window
{
    private readonly DispatcherTimer _alertTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private readonly Func<object, string?>? _validateRule;

    public RuleEditorWindow(string ruleType, object rule, Func<object, string?>? validateRule = null)
    {
        _validateRule = validateRule;
        InitializeComponent();
        Title = $"{ruleType}设置";
        EditorTitleTextBlock.Text = ruleType;
        DataContext = rule;
        _alertTimer.Tick += AlertTimer_Tick;
        ApplyRuleType(ruleType);
        UpdateUrlMatchHint();
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void ApplyRuleType(string ruleType)
    {
        bool rewrite = ruleType == "请求重写";
        bool mapping = ruleType == "请求映射";
        bool webSocketBlock = ruleType == "WebSocket屏蔽";
        bool tcpBlock = ruleType == "TCP屏蔽";
        bool udpBlock = ruleType == "UDP屏蔽";
        bool block = (ruleType is "HTTP屏蔽" or "请求屏蔽") || webSocketBlock || tcpBlock || udpBlock;
        bool decode = ruleType == "请求解密";
        ConfigureRuleLayout(block, rewrite, mapping, decode, webSocketBlock || udpBlock);

        if (rewrite && DataContext is RequestRewriteRuleItem rewriteRule)
        {
            rewriteRule.EnsureOperations();
            if (rewriteRule.Operations.Count > 0)
            {
                RewriteOperationsGrid.SelectedIndex = 0;
            }
        }

        RewritePanel.Visibility = mapping || block || decode ? Visibility.Collapsed : Visibility.Visible;
        RewriteOperationPanel.Visibility = Visibility.Collapsed;
        RewriteOperationsPanel.Visibility = rewrite ? Visibility.Visible : Visibility.Collapsed;
        MappingTypePanel.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
        BlockActionPanel.Visibility = block ? Visibility.Visible : Visibility.Collapsed;
        DecodePanel.Visibility = decode ? Visibility.Visible : Visibility.Collapsed;
        BlockActionComboBox.ItemsSource = (System.Collections.IEnumerable)FindResource(GetBlockActionResourceKey(ruleType));
        MethodComboBox.ItemsSource = (System.Collections.IEnumerable)FindResource(tcpBlock ? "TcpProtocolItems" : "RuleMethodItems");
        MethodLabelTextBlock.Text = tcpBlock ? "协议" : "请求方法";
        UrlLabelTextBlock.Text = tcpBlock || udpBlock ? "地址" : "Url";
        MethodLabelTextBlock.Visibility = udpBlock || webSocketBlock ? Visibility.Collapsed : Visibility.Visible;
        MethodComboBox.Visibility = udpBlock || webSocketBlock ? Visibility.Collapsed : Visibility.Visible;
        if (webSocketBlock && DataContext is WebSocketBlockRuleItem webSocketBlockRule)
        {
            webSocketBlockRule.Method = "ANY";
        }
        if (udpBlock && DataContext is UdpBlockRuleItem udpBlockRule)
        {
            udpBlockRule.Method = "UDP";
        }

        KeyLabelTextBlock.Visibility = Visibility.Collapsed;
        KeyTextBox.Visibility = Visibility.Collapsed;
        ValueLabelTextBlock.Visibility = Visibility.Collapsed;
        ValueTextBox.Visibility = Visibility.Collapsed;
        MappingTargetLabelTextBlock.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
        MappingTargetPanel.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
        DecodeScriptLabelTextBlock.Visibility = decode ? Visibility.Visible : Visibility.Collapsed;
        DecodeScriptTextBox.Visibility = decode ? Visibility.Visible : Visibility.Collapsed;

        Grid.SetRow(NoteLabelTextBlock, block ? 6 : 9);
        Grid.SetRow(NoteTextBox, block ? 6 : 9);
    }

    private static string GetBlockActionResourceKey(string ruleType)
    {
        return ruleType switch
        {
            "WebSocket屏蔽" => "WebSocketBlockActionItems",
            "TCP屏蔽" => "TcpBlockActionItems",
            "UDP屏蔽" => "UdpBlockActionItems",
            _ => "BlockActionItems"
        };
    }

    private void ConfigureRuleLayout(bool block, bool rewrite, bool mapping, bool decode, bool hideMethod)
    {
        SetEditorRows(42, 42, 42, 42, 50, 42, 0, 0, 0, 84, 0, 0, 0);
        EditorScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Height = 660;
        MinHeight = 520;

        if (block)
        {
            SetEditorRows(42, 42, 42, hideMethod ? 0 : 42, 50, 42, 84, 0, 0, 0, 0, 0, 0);
            EditorScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Height = hideMethod ? 468 : 500;
            MinHeight = hideMethod ? 468 : 500;
            return;
        }

        if (rewrite)
        {
            SetEditorRows(42, 42, 42, 42, 50, 42, 0, 170, 0, 84, 0, 0, 0);
            return;
        }

        if (mapping)
        {
            SetEditorRows(42, 42, 42, 42, 50, 42, 0, 42, 0, 84, 0, 0, 0);
            return;
        }

        if (decode)
        {
            SetEditorRows(42, 42, 42, 42, 50, 42, 0, 84, 0, 84, 0, 0, 0);
        }
    }

    private void SetEditorRows(params double[] heights)
    {
        RowDefinition[] rows =
        {
            EditorRow0,
            EditorRow1,
            EditorRow2,
            EditorRow3,
            EditorRow4,
            EditorRow5,
            EditorRow6,
            EditorRow7,
            EditorRow8,
            EditorRow9,
            EditorRow10,
            EditorRow11,
            EditorRow12
        };

        for (int index = 0; index < rows.Length && index < heights.Length; index++)
        {
            rows[index].Height = new GridLength(heights[index]);
        }
    }

    private void UrlMatchTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        UpdateUrlMatchHint();
    }

    private void UpdateUrlMatchHint()
    {
        if (UrlMatchHintTextBlock is null)
        {
            return;
        }

        string matchType = UrlMatchTypeComboBox?.SelectedItem?.ToString() ?? "";
        UrlMatchHintTextBlock.Text = matchType switch
        {
            "包含" => "示例：/api/login",
            "等于" => "示例：https://example.com/api/login",
            "正则" => "示例：^https://.*\\.example\\.com/api/",
            _ => "示例：https://*.example.com/api/*"
        };
    }

    private void BrowseMappingFile_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not RequestMappingRuleItem item)
        {
            return;
        }

        OpenFileDialog dialog = new()
        {
            Title = "选择映射文件",
            Filter = "所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            item.MappingType = "本地文件";
            item.TargetContent = dialog.FileName;
        }
    }

    private void AddRewriteOperation_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not RequestRewriteRuleItem item)
        {
            return;
        }

        RequestRewriteOperationItem operation = new()
        {
            Target = "协议头",
            Operation = "设置",
            ValueType = "String(UTF8)"
        };
        int insertIndex = RewriteOperationsGrid.SelectedIndex >= 0
            ? RewriteOperationsGrid.SelectedIndex + 1
            : item.Operations.Count;
        item.Operations.Insert(insertIndex, operation);
        RewriteOperationsGrid.SelectedItem = operation;
        item.State = "未保存";
    }

    private void RemoveRewriteOperation_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not RequestRewriteRuleItem item || RewriteOperationsGrid.SelectedItem is not RequestRewriteOperationItem operation)
        {
            return;
        }

        int index = item.Operations.IndexOf(operation);
        item.Operations.Remove(operation);
        if (item.Operations.Count == 0)
        {
            AddRewriteOperation_Click(sender, routedEventArgs);
            return;
        }

        RewriteOperationsGrid.SelectedIndex = Math.Min(index, item.Operations.Count - 1);
        item.State = "未保存";
    }

    private void MoveRewriteOperationUp_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRewriteOperation(-1);
    }

    private void MoveRewriteOperationDown_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRewriteOperation(1);
    }

    private void MoveSelectedRewriteOperation(int offset)
    {
        if (DataContext is not RequestRewriteRuleItem item || RewriteOperationsGrid.SelectedItem is not RequestRewriteOperationItem operation)
        {
            return;
        }

        int index = item.Operations.IndexOf(operation);
        int targetIndex = index + offset;
        if (index < 0 || targetIndex < 0 || targetIndex >= item.Operations.Count)
        {
            return;
        }

        item.Operations.Move(index, targetIndex);
        RewriteOperationsGrid.SelectedItem = operation;
        item.State = "未保存";
    }

    private void Confirm_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        RewriteOperationsGrid.CommitEdit();
        RewriteOperationsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        if (DataContext is RequestRewriteRuleItem rewriteRule)
        {
            rewriteRule.EnsureOperations();
            rewriteRule.SyncOperationsJson();
        }

        if (DataContext is RequestBlockRuleItem blockRule)
        {
            NormalizeBlockRule(blockRule);
        }

        if (DataContext is WebSocketBlockRuleItem webSocketBlockRule)
        {
            NormalizeWebSocketBlockRule(webSocketBlockRule);
        }

        if (DataContext is TcpBlockRuleItem tcpBlockRule)
        {
            NormalizeTcpBlockRule(tcpBlockRule);
        }

        if (DataContext is UdpBlockRuleItem udpBlockRule)
        {
            NormalizeUdpBlockRule(udpBlockRule);
        }

        string? validationMessage = _validateRule?.Invoke(DataContext);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            ShowRuleAlert(validationMessage);
            return;
        }

        DialogResult = true;
    }

    private static void NormalizeBlockRule(RequestBlockRuleItem rule)
    {
        rule.UrlPattern = rule.UrlPattern.Trim();
        rule.Action = rule.Action?.Trim() == "断开响应" ? "断开响应" : "断开请求";
    }

    private static void NormalizeWebSocketBlockRule(WebSocketBlockRuleItem rule)
    {
        rule.Method = "ANY";
        rule.UrlPattern = rule.UrlPattern.Trim();
        rule.Action = rule.Action?.Trim() switch
        {
            "丢弃上行帧" => "丢弃上行帧",
            "丢弃下行帧" => "丢弃下行帧",
            _ => "断开连接"
        };
    }

    private static void NormalizeTcpBlockRule(TcpBlockRuleItem rule)
    {
        rule.UrlPattern = rule.UrlPattern.Trim();
        rule.Method = string.IsNullOrWhiteSpace(rule.Method) ? "ANY" : rule.Method.Trim();
        rule.Action = rule.Action?.Trim() switch
        {
            "丢弃上行包" => "丢弃上行包",
            "丢弃下行包" => "丢弃下行包",
            _ => "断开连接"
        };
    }

    private static void NormalizeUdpBlockRule(UdpBlockRuleItem rule)
    {
        rule.Method = "UDP";
        rule.UrlPattern = rule.UrlPattern.Trim();
        rule.Action = rule.Action?.Trim() == "丢弃下行包" ? "丢弃下行包" : "丢弃上行包";
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

    private void Cancel_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DialogResult = false;
    }
}
