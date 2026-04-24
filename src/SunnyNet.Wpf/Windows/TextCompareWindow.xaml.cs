using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SunnyNet.Wpf.Windows;

public partial class TextCompareWindow : Window
{
    private static readonly Brush DiffForegroundBrush = new SolidColorBrush(Color.FromRgb(210, 45, 32));
    private static readonly Brush DiffBackgroundBrush = new SolidColorBrush(Color.FromRgb(255, 229, 229));
    private static readonly Brush NormalForegroundBrush = new SolidColorBrush(Color.FromRgb(31, 45, 61));
    private static readonly Brush NormalBackgroundBrush = Brushes.Transparent;
    private readonly DispatcherTimer _compareTimer;
    private bool _isRendering;

    public TextCompareWindow()
    {
        InitializeComponent();
        _compareTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _compareTimer.Tick += CompareTimer_Tick;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
    {
        if (_isRendering)
        {
            return;
        }

        _compareTimer.Stop();
        _compareTimer.Start();
    }

    private void CompareTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _compareTimer.Stop();
        CompareAndHighlight();
    }

    private void Clear_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _isRendering = true;
        try
        {
            LeftEditor.Document.Blocks.Clear();
            RightEditor.Document.Blocks.Clear();
            LeftTitleTextBlock.Text = "左侧文本";
            RightTitleTextBlock.Text = "右侧文本";
            CompareSummaryTextBlock.Text = "输入或粘贴文本后自动对比，差异字符会以红色高亮。";
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void CompareAndHighlight()
    {
        string leftText = NormalizeEditorText(GetEditorText(LeftEditor));
        string rightText = NormalizeEditorText(GetEditorText(RightEditor));
        DiffSpan[] leftDiffs = BuildDiffSpans(leftText, rightText);
        DiffSpan[] rightDiffs = BuildDiffSpans(rightText, leftText);

        _isRendering = true;
        try
        {
            RenderDocument(LeftEditor, leftText, leftDiffs);
            RenderDocument(RightEditor, rightText, rightDiffs);
        }
        finally
        {
            _isRendering = false;
        }

        int diffChars = Math.Max(leftDiffs.Sum(static span => span.Length), rightDiffs.Sum(static span => span.Length));
        if (diffChars == 0)
        {
            LeftTitleTextBlock.Text = "左侧文本 · 一致";
            RightTitleTextBlock.Text = "右侧文本 · 一致";
            CompareSummaryTextBlock.Text = leftText.Length == 0 && rightText.Length == 0
                ? "输入或粘贴文本后自动对比，差异字符会以红色高亮。"
                : $"文本一致，共 {leftText.Length:N0} 个字符。";
            return;
        }

        LeftTitleTextBlock.Text = $"左侧文本 · 差异 {leftDiffs.Length} 段";
        RightTitleTextBlock.Text = $"右侧文本 · 差异 {rightDiffs.Length} 段";
        CompareSummaryTextBlock.Text = $"发现 {diffChars:N0} 个差异字符，左侧 {leftText.Length:N0} 字符，右侧 {rightText.Length:N0} 字符。";
    }

    private static DiffSpan[] BuildDiffSpans(string source, string target)
    {
        List<DiffSpan> spans = new();
        int maxLength = Math.Max(source.Length, target.Length);
        int? start = null;

        for (int index = 0; index < maxLength; index++)
        {
            char? left = index < source.Length ? source[index] : null;
            char? right = index < target.Length ? target[index] : null;
            bool different = left != right && index < source.Length;
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
            spans.Add(new DiffSpan(tailStart, source.Length - tailStart));
        }

        return spans.ToArray();
    }

    private static void RenderDocument(RichTextBox editor, string text, IReadOnlyList<DiffSpan> diffs)
    {
        int caretOffset = GetCaretOffset(editor);
        FlowDocument document = new()
        {
            PagePadding = new Thickness(10, 8, 10, 8),
            FontFamily = editor.FontFamily,
            FontSize = editor.FontSize,
            LineHeight = 18
        };
        Paragraph paragraph = new() { Margin = new Thickness(0) };

        int offset = 0;
        foreach (DiffSpan diff in diffs)
        {
            if (diff.Start > offset)
            {
                paragraph.Inlines.Add(CreateRun(text[offset..diff.Start], isDiff: false));
            }

            paragraph.Inlines.Add(CreateRun(text.Substring(diff.Start, diff.Length), isDiff: true));
            offset = diff.Start + diff.Length;
        }

        if (offset < text.Length)
        {
            paragraph.Inlines.Add(CreateRun(text[offset..], isDiff: false));
        }

        document.Blocks.Add(paragraph);
        editor.Document = document;
        RestoreCaretOffset(editor, Math.Min(caretOffset, text.Length));
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

    private static string GetEditorText(RichTextBox editor)
    {
        TextRange range = new(editor.Document.ContentStart, editor.Document.ContentEnd);
        return range.Text;
    }

    private static string NormalizeEditorText(string text)
    {
        string normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        return normalized.EndsWith('\n') ? normalized[..^1] : normalized;
    }

    private static int GetCaretOffset(RichTextBox editor)
    {
        return new TextRange(editor.Document.ContentStart, editor.CaretPosition).Text.Length;
    }

    private static void RestoreCaretOffset(RichTextBox editor, int offset)
    {
        TextPointer pointer = editor.Document.ContentStart;
        int remaining = offset;
        while (pointer is not null)
        {
            TextPointerContext context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                string text = pointer.GetTextInRun(LogicalDirection.Forward);
                if (remaining <= text.Length)
                {
                    editor.CaretPosition = pointer.GetPositionAtOffset(remaining, LogicalDirection.Forward) ?? pointer;
                    return;
                }

                remaining -= text.Length;
            }

            TextPointer? next = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (next is null)
            {
                break;
            }

            pointer = next;
        }

        editor.CaretPosition = editor.Document.ContentEnd;
    }

    private readonly record struct DiffSpan(int Start, int Length);
}
