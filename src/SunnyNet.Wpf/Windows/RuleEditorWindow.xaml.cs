using System.Windows;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Windows;

public partial class RuleEditorWindow : Window
{
    public RuleEditorWindow(string ruleType, object rule)
    {
        InitializeComponent();
        Title = $"{ruleType}设置";
        EditorTitleTextBlock.Text = ruleType;
        DataContext = rule;
        ApplyRuleType(ruleType);
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
        bool block = ruleType == "请求屏蔽";
        bool decode = ruleType == "请求解密";

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
        BlockResponseTypePanel.Visibility = block ? Visibility.Visible : Visibility.Collapsed;
        DecodePanel.Visibility = decode ? Visibility.Visible : Visibility.Collapsed;

        KeyLabelTextBlock.Visibility = Visibility.Collapsed;
        KeyTextBox.Visibility = Visibility.Collapsed;
        ValueLabelTextBlock.Visibility = Visibility.Collapsed;
        ValueTextBox.Visibility = Visibility.Collapsed;
        MappingTargetLabelTextBlock.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
        MappingTargetPanel.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
        BlockResponseLabelTextBlock.Visibility = block ? Visibility.Visible : Visibility.Collapsed;
        BlockResponseTextBox.Visibility = block ? Visibility.Visible : Visibility.Collapsed;
        DecodeScriptLabelTextBlock.Visibility = decode ? Visibility.Visible : Visibility.Collapsed;
        DecodeScriptTextBox.Visibility = decode ? Visibility.Visible : Visibility.Collapsed;
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

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DialogResult = false;
    }
}
