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
        bool mapping = ruleType == "请求映射";
        bool block = ruleType == "请求屏蔽";
        bool decode = ruleType == "请求解密";

        RewritePanel.Visibility = mapping || block || decode ? Visibility.Collapsed : Visibility.Visible;
        RewriteOperationPanel.Visibility = mapping || block || decode ? Visibility.Collapsed : Visibility.Visible;
        MappingTypePanel.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
        BlockActionPanel.Visibility = block ? Visibility.Visible : Visibility.Collapsed;
        BlockResponseTypePanel.Visibility = block ? Visibility.Visible : Visibility.Collapsed;
        DecodePanel.Visibility = decode ? Visibility.Visible : Visibility.Collapsed;

        KeyLabelTextBlock.Visibility = mapping || block || decode ? Visibility.Collapsed : Visibility.Visible;
        KeyTextBox.Visibility = mapping || block || decode ? Visibility.Collapsed : Visibility.Visible;
        ValueLabelTextBlock.Visibility = mapping || block || decode ? Visibility.Collapsed : Visibility.Visible;
        ValueTextBox.Visibility = mapping || block || decode ? Visibility.Collapsed : Visibility.Visible;
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

    private void Confirm_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DialogResult = false;
    }
}
