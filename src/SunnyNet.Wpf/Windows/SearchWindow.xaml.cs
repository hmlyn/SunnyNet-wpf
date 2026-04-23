using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Windows;

public partial class SearchWindow : Window
{
    private static readonly Dictionary<string, List<string>> SearchHistory = new(StringComparer.Ordinal)
    {
        ["UTF8"] = new List<string>(),
        ["GBK"] = new List<string>(),
        ["Hex"] = new List<string>(),
        ["Base64"] = new List<string>(),
        ["pb"] = new List<string>(),
        ["整数4"] = new List<string>(),
        ["整数8"] = new List<string>(),
        ["浮点数4"] = new List<string>(),
        ["浮点数8"] = new List<string>()
    };

    private readonly MainWindowViewModel _viewModel;
    private bool _isSearching;

    public SearchWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        PopulateOptions();
        Loaded += (_, _) => FocusSearchText();
    }

    public void FocusSearchInput()
    {
        FocusSearchText();
    }

    private void PopulateOptions()
    {
        FindTypeComboBox.ItemsSource = new[]
        {
            new SelectOption("UTF8", "字符串(UTF8)"),
            new SelectOption("GBK", "字符串(GBK)"),
            new SelectOption("Hex", "十六进制"),
            new SelectOption("Base64", "Base64"),
            new SelectOption("pb", "ProtoBuf"),
            new SelectOption("整数4", "4字节整数"),
            new SelectOption("整数8", "8字节整数"),
            new SelectOption("浮点数4", "4字节浮点数"),
            new SelectOption("浮点数8", "8字节浮点数")
        };
        FindTypeComboBox.SelectedIndex = 0;

        FindRangeComboBox.ItemsSource = new[]
        {
            new SelectOption("全部", "全部"),
            new SelectOption("HTTP请求", "HTTP/HTTPS 请求"),
            new SelectOption("HTTP响应", "HTTP/HTTPS 响应"),
            new SelectOption("socketSend", "TCP/UDP/WebSocket 发送"),
            new SelectOption("socketRec", "TCP/UDP/WebSocket 接收"),
            new SelectOption("socketAll", "TCP/UDP/WebSocket 发送/接收")
        };
        FindRangeComboBox.SelectedIndex = 0;

        FindColorComboBox.ItemsSource = new[]
        {
            new SelectOption("#ffe100", "黄色"),
            new SelectOption("#005ffd", "蓝色"),
            new SelectOption("#fa0000", "红色"),
            new SelectOption("#029802", "绿色"),
            new SelectOption("#6905fa", "紫色")
        };
        FindColorComboBox.SelectedIndex = 0;
        UpdateTextHistory();
        UpdateTypeSensitiveOptions();
        UpdateColorPreview();
    }

    private async void FindButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await StartSearchAsync();
    }

    private async void ClearSearchMark_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        await _viewModel.CancelSearchHighlightAsync();
        SearchStateTextBlock.Text = "搜索颜色标记已清除。";
    }

    private void Close_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        Close();
    }

    private async void FindTextComboBox_KeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.Enter)
        {
            return;
        }

        keyEventArgs.Handled = true;
        await StartSearchAsync();
    }

    private void SearchWindow_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key == Key.Escape)
        {
            Close();
        }
    }

    private void FindTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (!IsLoaded && FindTextComboBox is null)
        {
            return;
        }

        UpdateTextHistory();
        UpdateTypeSensitiveOptions();
    }

    private void FindColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        UpdateColorPreview();
    }

    private async Task StartSearchAsync()
    {
        if (_isSearching)
        {
            return;
        }

        string value = FindTextComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(this, "请输入要搜索的内容。", "查找失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusSearchText();
            return;
        }

        string type = GetSelectedValue(FindTypeComboBox, "UTF8");
        int protoSkip = 0;
        if (type == "pb" && !int.TryParse(ProtoSkipTextBox.Text.Trim(), out protoSkip))
        {
            MessageBox.Show(this, "ProtoBuf 忽略字节数必须是整数。", "查找失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            ProtoSkipTextBox.Focus();
            ProtoSkipTextBox.SelectAll();
            return;
        }

        AddHistory(type, value);
        SetSearchingState(true);
        try
        {
            SearchRequest request = new(
                value,
                type,
                GetSelectedValue(FindRangeComboBox, "全部"),
                GetSelectedValue(FindColorComboBox, "#ffe100"),
                IgnoreCaseCheckBox.Visibility == Visibility.Visible && IgnoreCaseCheckBox.IsChecked == true,
                RemoveSpacesCheckBox.IsChecked == true,
                ClearPreviousCheckBox.IsChecked == true,
                Math.Max(protoSkip, 0));

            SearchExecutionResult result = await _viewModel.SearchAsync(request);
            if (result.MatchCount <= 0)
            {
                SearchStateTextBlock.Text = "没有搜索结果。";
                MessageBox.Show(this, "没有搜索结果。", "查找", MessageBoxButton.OK, MessageBoxImage.Information);
                FocusSearchText();
                return;
            }

            Close();
        }
        finally
        {
            SetSearchingState(false);
        }
    }

    private void SetSearchingState(bool isSearching)
    {
        _isSearching = isSearching;
        FindButton.IsEnabled = !isSearching;
        SearchProgressBar.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        SearchStateTextBlock.Text = isSearching ? "正在查找，请稍候..." : "Enter 开始查找，Esc 关闭窗口。";
    }

    private void UpdateTextHistory()
    {
        if (FindTextComboBox is null)
        {
            return;
        }

        string type = GetSelectedValue(FindTypeComboBox, "UTF8");
        string current = FindTextComboBox.Text;
        FindTextComboBox.ItemsSource = SearchHistory.TryGetValue(type, out List<string>? history)
            ? history
            : Array.Empty<string>();
        FindTextComboBox.Text = current;
    }

    private void UpdateTypeSensitiveOptions()
    {
        string type = GetSelectedValue(FindTypeComboBox, "UTF8");
        bool textSearch = type is "UTF8" or "GBK";
        IgnoreCaseCheckBox.Visibility = textSearch ? Visibility.Visible : Visibility.Collapsed;
        if (!textSearch)
        {
            IgnoreCaseCheckBox.IsChecked = false;
        }
        else if (IgnoreCaseCheckBox.IsChecked is null)
        {
            IgnoreCaseCheckBox.IsChecked = true;
        }

        ProtoSkipPanel.Visibility = type == "pb" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateColorPreview()
    {
        if (ColorPreviewBorder is null)
        {
            return;
        }

        string color = GetSelectedValue(FindColorComboBox, "#ffe100");
        try
        {
            ColorPreviewBorder.Background = (Brush)new BrushConverter().ConvertFromString(color)!;
        }
        catch
        {
            ColorPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xE1, 0x00));
        }
    }

    private static void AddHistory(string type, string value)
    {
        if (!SearchHistory.TryGetValue(type, out List<string>? history))
        {
            history = new List<string>();
            SearchHistory[type] = history;
        }

        int existing = history.FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            history.RemoveAt(existing);
        }

        history.Insert(0, value);
        if (history.Count > 12)
        {
            history.RemoveRange(12, history.Count - 12);
        }
    }

    private void FocusSearchText()
    {
        FindTextComboBox.ApplyTemplate();
        FindTextComboBox.Focus();
        if (FindTextComboBox.Template.FindName("PART_EditableTextBox", FindTextComboBox) is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private static string GetSelectedValue(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is SelectOption option ? option.Value : fallback;
    }

    private sealed record SelectOption(string Value, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
