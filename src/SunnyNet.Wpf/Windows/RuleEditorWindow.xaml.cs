using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Windows;

public partial class RuleEditorWindow : Window
{
    public static readonly DependencyProperty SelectedRewriteOperationProperty = DependencyProperty.Register(
        nameof(SelectedRewriteOperation),
        typeof(RequestRewriteOperationItem),
        typeof(RuleEditorWindow),
        new PropertyMetadata(null));

    private readonly DispatcherTimer _alertTimer = new() { Interval = TimeSpan.FromSeconds(2.8) };
    private readonly Func<object, string?>? _validateRule;
    private RequestMappingRuleItem? _mappingRule;

    public RequestRewriteOperationItem? SelectedRewriteOperation
    {
        get => (RequestRewriteOperationItem?)GetValue(SelectedRewriteOperationProperty);
        set => SetValue(SelectedRewriteOperationProperty, value);
    }

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
            if (RewriteWorkbenchPanel.Visibility == Visibility.Visible)
            {
                RewriteNameTextBox.Focus();
                RewriteNameTextBox.SelectAll();
                return;
            }

            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
        Closed += (_, _) => DetachMappingRule();
    }

    private void ApplyRuleType(string ruleType)
    {
        bool rewrite = ruleType == "请求重写";
        bool mapping = ruleType == "请求映射";
        bool webSocketBlock = ruleType == "WebSocket屏蔽";
        bool tcpBlock = ruleType == "TCP屏蔽";
        bool udpBlock = ruleType == "UDP屏蔽";
        bool block = (ruleType is "HTTP屏蔽" or "请求屏蔽") || webSocketBlock || tcpBlock || udpBlock;
        ConfigureRuleLayout(block, rewrite, mapping, webSocketBlock || udpBlock);

        if (rewrite && DataContext is RequestRewriteRuleItem rewriteRule)
        {
            rewriteRule.EnsureOperations();
            SelectedRewriteOperation = rewriteRule.Operations.Count > 0 ? rewriteRule.Operations[0] : null;
        }

        RewriteWorkbenchPanel.Visibility = rewrite ? Visibility.Visible : Visibility.Collapsed;
        EditorScrollViewer.Visibility = rewrite ? Visibility.Collapsed : Visibility.Visible;

        RewritePanel.Visibility = rewrite || mapping || block ? Visibility.Collapsed : Visibility.Visible;
        RewriteOperationPanel.Visibility = Visibility.Collapsed;
        RewriteOperationsPanel.Visibility = Visibility.Collapsed;
        MappingTypePanel.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
        BlockActionPanel.Visibility = block ? Visibility.Visible : Visibility.Collapsed;
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
        if (mapping && DataContext is RequestMappingRuleItem mappingRule)
        {
            AttachMappingRule(mappingRule);
            UpdateMappingEditorState();
        }
        else
        {
            DetachMappingRule();
        }

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

    private void ConfigureRuleLayout(bool block, bool rewrite, bool mapping, bool hideMethod)
    {
        SetEditorRows(42, 42, 42, 42, 50, 42, 0, 0, 0, 84, 0, 0, 0);
        EditorScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Width = 760;
        MinWidth = 640;
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
            Width = 1040;
            MinWidth = 900;
            Height = 700;
            MinHeight = 620;
            EditorScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            return;
        }

        if (mapping)
        {
            SetEditorRows(42, 42, 42, 42, 50, 42, 0, 42, 0, 84, 0, 0, 0);
            return;
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

    private void MappingTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        UpdateMappingEditorState();
    }

    private void AttachMappingRule(RequestMappingRuleItem mappingRule)
    {
        if (ReferenceEquals(_mappingRule, mappingRule))
        {
            return;
        }

        DetachMappingRule();
        _mappingRule = mappingRule;
        _mappingRule.PropertyChanged += MappingRule_PropertyChanged;
    }

    private void DetachMappingRule()
    {
        if (_mappingRule is null)
        {
            return;
        }

        _mappingRule.PropertyChanged -= MappingRule_PropertyChanged;
        _mappingRule = null;
    }

    private void MappingRule_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RequestMappingRuleItem.MappingType))
        {
            UpdateMappingEditorState();
        }
    }

    private void UpdateMappingEditorState()
    {
        if (MappingTargetLabelTextBlock is null)
        {
            return;
        }

        string mappingType = MappingTypeComboBox?.SelectedItem?.ToString()
            ?? (DataContext as RequestMappingRuleItem)?.MappingType
            ?? "";
        bool localFile = string.Equals(mappingType, "本地文件", StringComparison.Ordinal);
        bool fixedResponse = string.Equals(mappingType, "固定响应", StringComparison.Ordinal);

        MappingValueTypeLabelTextBlock.Visibility = fixedResponse ? Visibility.Visible : Visibility.Collapsed;
        MappingValueTypeComboBox.Visibility = fixedResponse ? Visibility.Visible : Visibility.Collapsed;
        BrowseMappingFileButton.Visibility = localFile ? Visibility.Visible : Visibility.Collapsed;

        MappingTargetLabelTextBlock.Text = mappingType switch
        {
            "固定响应" => "响应内容",
            "远程地址" => "目标URL",
            _ => "响应文件"
        };

        MappingTargetTextBox.ToolTip = mappingType switch
        {
            "固定响应" => "命中后直接把这里填写的内容作为响应体返回。",
            "远程地址" => "命中后把请求转发到这个 URL，支持绝对 URL 或相对地址。",
            _ => "命中后读取这个本地文件作为响应体返回。"
        };

        MappingTargetTextBox.AcceptsReturn = fixedResponse;
        MappingTargetTextBox.TextWrapping = fixedResponse ? TextWrapping.Wrap : TextWrapping.NoWrap;
        MappingTargetTextBox.VerticalScrollBarVisibility = fixedResponse ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        MappingTargetTextBox.VerticalContentAlignment = fixedResponse ? VerticalAlignment.Top : VerticalAlignment.Center;
        MappingTargetTextBox.Height = fixedResponse ? 72 : 32;
        EditorRow7.Height = new GridLength(fixedResponse ? 84 : 42);
    }

    private void UpdateUrlMatchHint()
    {
        if (UrlMatchHintTextBlock is null)
        {
            return;
        }

        string matchType = (RewriteWorkbenchPanel?.Visibility == Visibility.Visible
                ? RewriteUrlMatchTypeComboBox?.SelectedItem?.ToString()
                : UrlMatchTypeComboBox?.SelectedItem?.ToString())
            ?? "";
        string hint = matchType switch
        {
            "包含" => "示例：/api/login",
            "等于" => "示例：https://example.com/api/login",
            "正则" => "示例：^https://.*\\.example\\.com/api/",
            _ => "示例：https://*.example.com/api/*"
        };
        UrlMatchHintTextBlock.Text = hint;
        if (RewriteUrlMatchHintTextBlock is not null)
        {
            RewriteUrlMatchHintTextBlock.Text = hint;
        }
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

        RequestRewriteOperationItem operation = CreateRewriteOperation("协议头");
        RequestRewriteOperationItem? anchor = GetRewriteOperationFromSender(sender);
        int anchorIndex = anchor is null ? -1 : item.Operations.IndexOf(anchor);
        int insertIndex = anchorIndex >= 0 ? anchorIndex + 1 : item.Operations.Count;
        item.Operations.Insert(insertIndex, operation);
        SelectedRewriteOperation = operation;
        item.State = "未保存";
    }

    private void AddRewriteOperationFromPalette_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not RequestRewriteRuleItem item || sender is not FrameworkElement element)
        {
            return;
        }

        string target = element.Tag?.ToString() ?? "协议头";
        if (!CanAddRewriteTarget(item, target))
        {
            ShowRuleAlert($"{target} 不能继续添加，可能与当前方向或已有动作冲突。");
            return;
        }

        RequestRewriteOperationItem operation = CreateRewriteOperation(target);
        RequestRewriteOperationItem? anchor = SelectedRewriteOperation;
        int anchorIndex = anchor is null ? -1 : item.Operations.IndexOf(anchor);
        int insertIndex = anchorIndex >= 0 ? anchorIndex + 1 : item.Operations.Count;
        item.Operations.Insert(insertIndex, operation);
        SelectedRewriteOperation = operation;
        item.State = "未保存";
    }

    private void RemoveRewriteOperation_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is not RequestRewriteRuleItem item || GetRewriteOperationFromSender(sender) is not RequestRewriteOperationItem operation)
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

        SelectedRewriteOperation = item.Operations[Math.Min(index, item.Operations.Count - 1)];
        item.State = "未保存";
    }

    private void MoveRewriteOperationUp_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRewriteOperation(sender, -1);
    }

    private void MoveRewriteOperationDown_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        MoveSelectedRewriteOperation(sender, 1);
    }

    private void MoveSelectedRewriteOperation(object? sender, int offset)
    {
        if (DataContext is not RequestRewriteRuleItem item || GetRewriteOperationFromSender(sender) is not RequestRewriteOperationItem operation)
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
        SelectedRewriteOperation = operation;
        item.State = "未保存";
    }

    private RequestRewriteOperationItem? GetRewriteOperationFromSender(object? sender)
    {
        return sender is FrameworkElement element && element.DataContext is RequestRewriteOperationItem operation
            ? operation
            : SelectedRewriteOperation;
    }

    private static RequestRewriteOperationItem CreateRewriteOperation(string target)
    {
        string normalizedTarget = string.IsNullOrWhiteSpace(target) ? "协议头" : target.Trim();
        return new RequestRewriteOperationItem
        {
            Target = normalizedTarget,
            Operation = normalizedTarget == "协议头" || normalizedTarget == "参数" ? "添加" : "设置",
            ValueType = normalizedTarget == "Body" ? "String(UTF8)" : "String(UTF8)"
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (DataContext is RequestRewriteRuleItem rewriteRule)
        {
            rewriteRule.EnsureOperations();
            rewriteRule.RefreshRewriteEditorState();
            string? rewriteValidationMessage = ValidateRewriteRule(rewriteRule);
            if (!string.IsNullOrWhiteSpace(rewriteValidationMessage))
            {
                ShowRuleAlert(rewriteValidationMessage);
                return;
            }

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

    private static bool CanAddRewriteTarget(RequestRewriteRuleItem rule, string target)
    {
        return target.Trim() switch
        {
            "请求方法" => rule.CanAddRequestMethod,
            "URL" => rule.CanAddUrl,
            "Path" => rule.CanAddPath,
            "参数" => rule.CanAddParameter,
            "协议头" => rule.CanAddHeader,
            "Body" => rule.CanAddBody,
            "状态码" => rule.CanAddStatusCode,
            _ => true
        };
    }

    private static string? ValidateRewriteRule(RequestRewriteRuleItem rule)
    {
        if (rule.Operations.Count == 0)
        {
            return "请求重写至少需要一个动作。";
        }

        foreach (RequestRewriteOperationItem operation in rule.Operations)
        {
            if (operation.HasValidationWarning)
            {
                return $"动作 {operation.DisplayIndexText}: {operation.ValidationMessage}";
            }

            if (!operation.AvailableOperations.Contains(operation.Operation))
            {
                return $"动作 {operation.DisplayIndexText}: {operation.Target} 不支持“{operation.Operation}”。";
            }

            if (IsRewriteKeyTarget(operation.Target) && string.IsNullOrWhiteSpace(operation.Key))
            {
                return $"动作 {operation.DisplayIndexText}: 请填写{operation.KeyLabel}。";
            }

            if (NeedsRewriteRequiredValue(operation.Target, operation.Operation) && string.IsNullOrWhiteSpace(operation.Value))
            {
                return $"动作 {operation.DisplayIndexText}: 请填写{operation.ValueLabel}。";
            }

            if (IsRewriteStatusCodeTarget(operation.Target) && !ValidateStatusCode(operation.Value))
            {
                return $"动作 {operation.DisplayIndexText}: 状态码必须是 100-599。";
            }

            if (IsRewriteBodyTarget(operation.Target)
                && operation.ShowsValue
                && !string.IsNullOrWhiteSpace(operation.Value))
            {
                string? bodyFormatMessage = ValidateRewriteBodyFormat(operation.Value, operation.ValueType);
                if (!string.IsNullOrWhiteSpace(bodyFormatMessage))
                {
                    return $"动作 {operation.DisplayIndexText}: {bodyFormatMessage}";
                }
            }
        }

        var duplicateKeyOperation = rule.Operations
            .Where(static operation => IsRewriteKeyTarget(operation.Target)
                && !string.Equals(operation.Operation?.Trim(), "添加", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(operation.Key))
            .GroupBy(static operation => $"{NormalizeRewriteTarget(operation.Target)}|{operation.Operation.Trim()}|{operation.Key.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateKeyOperation is not null)
        {
            RequestRewriteOperationItem operation = duplicateKeyOperation.First();
            return $"动作 {operation.DisplayIndexText}: {operation.Target} 的“{operation.Operation} {operation.Key}”重复。";
        }

        return null;
    }

    private static bool NeedsRewriteRequiredValue(string target, string operation)
    {
        if (string.Equals(operation?.Trim(), "删除", StringComparison.Ordinal))
        {
            return false;
        }

        return target?.Trim() is "请求方法" or "Method" or "URL" or "完整URL" or "状态码" or "StatusCode";
    }

    private static bool ValidateStatusCode(string value)
    {
        return int.TryParse(value?.Trim(), out int statusCode) && statusCode is >= 100 and <= 599;
    }

    private static string? ValidateRewriteBodyFormat(string value, string valueType)
    {
        string normalizedValueType = valueType?.Trim() ?? "";
        if (string.Equals(normalizedValueType, "HEX", StringComparison.Ordinal))
        {
            string hex = value.Trim();
            if (hex.Length % 2 != 0 || hex.Any(static ch => !Uri.IsHexDigit(ch)))
            {
                return "HEX 内容必须是偶数长度的十六进制字符，不能包含空格。";
            }
        }

        if (string.Equals(normalizedValueType, "Base64", StringComparison.Ordinal))
        {
            try
            {
                _ = Convert.FromBase64String(value.Trim());
            }
            catch (FormatException)
            {
                return "Base64 内容格式不正确。";
            }
        }

        return null;
    }

    private static bool IsRewriteKeyTarget(string target)
    {
        return target?.Trim() is "参数" or "URL参数" or "Query" or "协议头" or "请求头" or "响应头" or "Header";
    }

    private static bool IsRewriteBodyTarget(string target)
    {
        return target?.Trim() is "Body" or "请求体" or "响应体";
    }

    private static bool IsRewriteStatusCodeTarget(string target)
    {
        return target?.Trim() is "状态码" or "StatusCode";
    }

    private static string NormalizeRewriteTarget(string target)
    {
        return target?.Trim() switch
        {
            "URL参数" or "Query" => "参数",
            "请求头" or "响应头" or "Header" => "协议头",
            _ => target?.Trim() ?? ""
        };
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
