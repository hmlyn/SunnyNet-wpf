using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Windows;

public partial class SessionCompareWindow : Window
{
    private const int MaxRenderCharacters = 600_000;
    private static readonly Brush DiffForegroundBrush = CreateBrush(0xD2, 0x2D, 0x20);
    private static readonly Brush DiffBackgroundBrush = CreateBrush(0xFF, 0xE5, 0xE5);
    private static readonly Brush NormalForegroundBrush = CreateBrush(0x1F, 0x2D, 0x3D);
    private static readonly Brush NormalBackgroundBrush = Brushes.Transparent;
    private readonly MainWindowViewModel _viewModel;
    private int _leftLoadVersion;
    private int _rightLoadVersion;
    private SessionCompareSnapshot? _leftSnapshot;
    private SessionCompareSnapshot? _rightSnapshot;

    public SessionCompareWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        RenderDocument(LeftEditor, "");
        RenderDocument(RightEditor, "");
    }

    public async Task LoadSessionAsync(CaptureEntry entry, CompareSlot slot)
    {
        int version = slot == CompareSlot.Left ? ++_leftLoadVersion : ++_rightLoadVersion;
        SetSlotStatus(slot, $"正在读取 #{entry.Index}...");
        CompareStatusTextBlock.Text = $"正在加载 #{entry.Index} 会话详情...";

        try
        {
            SessionCompareSnapshot? snapshot = await _viewModel.BuildSessionCompareSnapshotAsync(entry);
            if (!IsCurrentLoad(slot, version) || snapshot is null)
            {
                return;
            }

            if (slot == CompareSlot.Left)
            {
                _leftSnapshot = snapshot;
            }
            else
            {
                _rightSnapshot = snapshot;
            }

            UpdateSlot(slot);
            CompareAndRender();
        }
        catch (Exception exception)
        {
            SetSlotStatus(slot, $"加载失败：{exception.Message}");
            CompareStatusTextBlock.Text = $"加载失败：{exception.Message}";
        }
    }

    private bool IsCurrentLoad(CompareSlot slot, int version)
    {
        return slot == CompareSlot.Left
            ? version == _leftLoadVersion
            : version == _rightLoadVersion;
    }

    private void RangeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (!IsLoaded)
        {
            return;
        }

        CompareAndRender();
    }

    private void CompareOption_Changed(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!IsLoaded)
        {
            return;
        }

        CompareAndRender();
    }

    private async void LeftSlot_Drop(object sender, DragEventArgs dragEventArgs)
    {
        await LoadDroppedSessionAsync(dragEventArgs, CompareSlot.Left);
    }

    private async void RightSlot_Drop(object sender, DragEventArgs dragEventArgs)
    {
        await LoadDroppedSessionAsync(dragEventArgs, CompareSlot.Right);
    }

    private void CompareSlot_DragOver(object sender, DragEventArgs dragEventArgs)
    {
        dragEventArgs.Effects = dragEventArgs.Data.GetDataPresent(typeof(CaptureEntry))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        dragEventArgs.Handled = true;
    }

    private async Task LoadDroppedSessionAsync(DragEventArgs dragEventArgs, CompareSlot slot)
    {
        dragEventArgs.Handled = true;
        if (!dragEventArgs.Data.GetDataPresent(typeof(CaptureEntry)) ||
            dragEventArgs.Data.GetData(typeof(CaptureEntry)) is not CaptureEntry entry)
        {
            return;
        }

        await LoadSessionAsync(entry, slot);
    }

    private void Swap_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _leftLoadVersion++;
        _rightLoadVersion++;
        (_leftSnapshot, _rightSnapshot) = (_rightSnapshot, _leftSnapshot);
        UpdateSlot(CompareSlot.Left);
        UpdateSlot(CompareSlot.Right);
        CompareAndRender();
    }

    private void Clear_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _leftLoadVersion++;
        _rightLoadVersion++;
        _leftSnapshot = null;
        _rightSnapshot = null;
        UpdateSlot(CompareSlot.Left);
        UpdateSlot(CompareSlot.Right);
        CompareStatusTextBlock.Text = "等待选择会话";
        SummaryTextBlock.Text = "从会话列表拖拽请求到左右区域，或使用会话右键菜单加入比较。";
        RenderDocument(LeftEditor, "");
        RenderDocument(RightEditor, "");
    }

    private void UpdateSlot(CompareSlot slot)
    {
        SessionCompareSnapshot? snapshot = slot == CompareSlot.Left ? _leftSnapshot : _rightSnapshot;
        TextBlock title = slot == CompareSlot.Left ? LeftTitleTextBlock : RightTitleTextBlock;
        TextBlock meta = slot == CompareSlot.Left ? LeftMetaTextBlock : RightMetaTextBlock;
        TextBlock hash = slot == CompareSlot.Left ? LeftHashTextBlock : RightHashTextBlock;

        if (snapshot is null)
        {
            title.Text = slot == CompareSlot.Left ? "左侧会话：拖入请求" : "右侧会话：拖入请求";
            meta.Text = slot == CompareSlot.Left
                ? "可拖拽会话列表中的任意 HTTP 请求到这里。"
                : "也可以右键会话列表，选择加入比较右侧。";
            hash.Text = "Raw MD5：-";
            return;
        }

        title.Text = snapshot.Summary;
        meta.Text = snapshot.MetaSummary;
        hash.Text = BuildHashText(snapshot);
    }

    private void SetSlotStatus(CompareSlot slot, string text)
    {
        TextBlock title = slot == CompareSlot.Left ? LeftTitleTextBlock : RightTitleTextBlock;
        title.Text = text;
    }

    private void CompareAndRender()
    {
        string range = GetSelectedRange();
        string leftText = ClipForRender(_leftSnapshot?.GetCompareText(range) ?? "");
        string rightText = ClipForRender(_rightSnapshot?.GetCompareText(range) ?? "");

        if (_leftSnapshot is null || _rightSnapshot is null)
        {
            RenderDocument(LeftEditor, leftText);
            RenderDocument(RightEditor, rightText);
            CompareStatusTextBlock.Text = "请把两个会话分别放入左右区域后开始比较";
            return;
        }

        string leftCompare = BuildCompareText(leftText);
        string rightCompare = BuildCompareText(rightText);
        DiffSpan[] leftDiffs = BuildDiffSpans(leftCompare, rightCompare, leftText.Length);
        DiffSpan[] rightDiffs = BuildDiffSpans(rightCompare, leftCompare, rightText.Length);
        RenderDocument(LeftEditor, leftText, leftDiffs);
        RenderDocument(RightEditor, rightText, rightDiffs);

        int diffChars = Math.Max(leftDiffs.Sum(static item => item.Length), rightDiffs.Sum(static item => item.Length));
        string rangeName = (RangeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "请求原文";
        CompareStatusTextBlock.Text = diffChars == 0
            ? $"{rangeName} 一致，左侧 {leftText.Length:N0} 字符，右侧 {rightText.Length:N0} 字符"
            : $"{rangeName} 发现 {diffChars:N0} 个差异字符，左侧 {leftText.Length:N0} 字符，右侧 {rightText.Length:N0} 字符";
        SummaryTextBlock.Text = $"正在比较：左侧 #{_leftSnapshot.Index} 与右侧 #{_rightSnapshot.Index}";
    }

    private string BuildCompareText(string text)
    {
        return IgnoreCaseCheckBox.IsChecked == true
            ? text.ToUpperInvariant()
            : text;
    }

    private string GetSelectedRange()
    {
        return (RangeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "RequestRaw";
    }

    private static string BuildHashText(SessionCompareSnapshot snapshot)
    {
        return string.Join(Environment.NewLine,
            $"请求 Raw   MD5    {snapshot.RequestRawMd5}",
            $"请求 Raw   SHA256 {snapshot.RequestRawSha256}",
            $"请求 Body  MD5    {snapshot.RequestBodyMd5}",
            $"请求 Body  SHA256 {snapshot.RequestBodySha256}",
            $"响应 Raw   MD5    {ValueOrDash(snapshot.ResponseRawMd5)}",
            $"响应 Raw   SHA256 {ValueOrDash(snapshot.ResponseRawSha256)}",
            $"响应 Body  MD5    {ValueOrDash(snapshot.ResponseBodyMd5)}",
            $"响应 Body  SHA256 {ValueOrDash(snapshot.ResponseBodySha256)}");
    }

    private static string ValueOrDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string ClipForRender(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxRenderCharacters)
        {
            return text;
        }

        return text[..MaxRenderCharacters] +
               $"\r\n\r\n比较视图已渲染前 {MaxRenderCharacters:N0} 字符，完整文本 {text.Length:N0} 字符。";
    }

    private static DiffSpan[] BuildDiffSpans(string source, string target, int renderLength)
    {
        List<DiffSpan> spans = new();
        int maxLength = Math.Max(source.Length, target.Length);
        int? start = null;
        for (int index = 0; index < maxLength; index++)
        {
            char? left = index < source.Length ? source[index] : null;
            char? right = index < target.Length ? target[index] : null;
            bool different = left != right && index < renderLength;
            if (different && start is null)
            {
                start = index;
            }
            else if (!different && start is int startIndex)
            {
                spans.Add(new DiffSpan(startIndex, index - startIndex));
                start = null;
            }
        }

        if (start is int tailStart)
        {
            spans.Add(new DiffSpan(tailStart, renderLength - tailStart));
        }

        return spans.ToArray();
    }

    private static void RenderDocument(RichTextBox editor, string text, IReadOnlyList<DiffSpan>? diffs = null)
    {
        FlowDocument document = new()
        {
            PagePadding = new Thickness(10, 8, 10, 8),
            FontFamily = editor.FontFamily,
            FontSize = editor.FontSize,
            LineHeight = 18
        };
        Paragraph paragraph = new() { Margin = new Thickness(0) };

        if (diffs is null || diffs.Count == 0)
        {
            paragraph.Inlines.Add(CreateRun(text, false));
        }
        else
        {
            int offset = 0;
            foreach (DiffSpan diff in diffs)
            {
                if (diff.Start > offset)
                {
                    paragraph.Inlines.Add(CreateRun(text[offset..diff.Start], false));
                }

                paragraph.Inlines.Add(CreateRun(text.Substring(diff.Start, Math.Min(diff.Length, text.Length - diff.Start)), true));
                offset = diff.Start + diff.Length;
            }

            if (offset < text.Length)
            {
                paragraph.Inlines.Add(CreateRun(text[offset..], false));
            }
        }

        document.Blocks.Add(paragraph);
        editor.Document = document;
    }

    private static Run CreateRun(string text, bool isDiff)
    {
        Run run = new(text)
        {
            Foreground = isDiff ? DiffForegroundBrush : NormalForegroundBrush,
            Background = isDiff ? DiffBackgroundBrush : NormalBackgroundBrush
        };
        if (isDiff)
        {
            run.FontWeight = FontWeights.SemiBold;
        }

        return run;
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private readonly record struct DiffSpan(int Start, int Length);
}

public enum CompareSlot
{
    Left,
    Right
}
