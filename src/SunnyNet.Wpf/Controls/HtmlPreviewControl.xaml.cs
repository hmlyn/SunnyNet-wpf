using System.Windows;
using System.Windows.Controls;

namespace SunnyNet.Wpf.Controls;

public partial class HtmlPreviewControl : UserControl
{
    public static readonly DependencyProperty HtmlTextProperty =
        DependencyProperty.Register(nameof(HtmlText), typeof(string), typeof(HtmlPreviewControl), new PropertyMetadata("", OnHtmlChanged));

    public HtmlPreviewControl()
    {
        InitializeComponent();
        Loaded += (_, _) => NavigateHtml();
    }

    public string HtmlText
    {
        get => (string)GetValue(HtmlTextProperty);
        set => SetValue(HtmlTextProperty, value);
    }

    private static void OnHtmlChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HtmlPreviewControl control)
        {
            control.NavigateHtml();
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ApplyMode(true);
    }

    private void SourceButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ApplyMode(false);
    }

    private void ApplyMode(bool preview)
    {
        PreviewButton.IsChecked = preview;
        SourceButton.IsChecked = !preview;
        PreviewPanel.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        SourceViewer.Visibility = preview ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Refresh_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        NavigateHtml();
    }

    private void CopySource_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (string.IsNullOrWhiteSpace(HtmlText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(HtmlText);
        }
        catch
        {
        }
    }

    private void NavigateHtml()
    {
        if (!IsLoaded)
        {
            return;
        }

        SummaryTextBlock.Text = string.IsNullOrWhiteSpace(HtmlText)
            ? "无 HTML 内容"
            : $"HTML 文档 · {HtmlText.Length:N0} 字符";

        string html = string.IsNullOrWhiteSpace(HtmlText)
            ? "<html><body style=\"font-family:Segoe UI,Microsoft YaHei UI;color:#6B7C93;padding:24px\">暂无 HTML 内容</body></html>"
            : HtmlText;
        HtmlBrowser.NavigateToString(html);
    }
}
