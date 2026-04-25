using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
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
        _compareTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _compareTimer.Tick += CompareTimer_Tick;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
    {
        if (_isRendering) return;
        _compareTimer.Stop();
        _compareTimer.Start();
    }

    private void CompareTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _compareTimer.Stop();
        CompareAndHighlight();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        _isRendering = true;
        try
        {
            LeftEditor.Document.Blocks.Clear();
            RightEditor.Document.Blocks.Clear();
            CodecInputTextBox.Clear();
            CodecOutputTextBox.Clear();
            CompressInputTextBox.Clear();
            CompressOutputTextBox.Clear();
            DistinctLeftTextBox.Clear();
            DistinctOutputTextBox.Clear();
            CaseInputTextBox.Clear();
            CaseOutputTextBox.Clear();
            TimestampInputTextBox.Clear();
            TimestampOutputTextBox.Clear();
            LeftTitleTextBlock.Text = "左侧文本";
            RightTitleTextBlock.Text = "右侧文本";
            ToolSummaryTextBlock.Text = "支持编码解码、压缩解压和文本处理。";
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
            ToolSummaryTextBlock.Text = leftText.Length == 0 && rightText.Length == 0
                ? "输入或粘贴文本后自动对比，差异字符会以红色高亮。"
                : $"文本一致，共 {leftText.Length:N0} 个字符。";
            return;
        }

        LeftTitleTextBlock.Text = $"左侧文本 · 差异 {leftDiffs.Length} 段";
        RightTitleTextBlock.Text = $"右侧文本 · 差异 {rightDiffs.Length} 段";
        ToolSummaryTextBlock.Text = $"发现 {diffChars:N0} 个差异字符，左侧 {leftText.Length:N0} 字符，右侧 {rightText.Length:N0} 字符。";
    }

    private void Encode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            byte[] inputBytes = GetCodecInputBytes();
            string type = GetComboText(CodecComboBox);
            CodecOutputTextBox.Text = type switch
            {
                "Base64" => Convert.ToBase64String(inputBytes),
                "URL" => WebUtility.UrlEncode(Encoding.UTF8.GetString(inputBytes)),
                "UCS2" => CodecHexCheckBox.IsChecked == true ? ToHex(Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(inputBytes))) : Encoding.Unicode.GetString(Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(inputBytes))),
                _ => Encoding.UTF8.GetString(inputBytes)
            };
            ToolSummaryTextBlock.Text = $"{type} 编码完成。";
        }
        catch (Exception exception) { ToolSummaryTextBlock.Text = $"编码失败：{exception.Message}"; }
    }

    private void Decode_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            string type = GetComboText(CodecComboBox);
            string input = CodecInputTextBox.Text ?? "";
            CodecOutputTextBox.Text = type switch
            {
                "Base64" => Encoding.UTF8.GetString(Convert.FromBase64String(RemoveWhitespace(input))),
                "URL" => WebUtility.UrlDecode(input),
                "UCS2" => Encoding.UTF8.GetString(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, CodecHexCheckBox.IsChecked == true ? FromHex(input) : Encoding.Unicode.GetBytes(input))),
                _ => input
            };
            ToolSummaryTextBlock.Text = $"{type} 解码完成。";
        }
        catch (Exception exception) { ToolSummaryTextBlock.Text = $"解码失败：{exception.Message}"; }
    }

    private void Compress_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            byte[] inputBytes = CompressHexCheckBox.IsChecked == true ? FromHex(CompressInputTextBox.Text) : Encoding.UTF8.GetBytes(CompressInputTextBox.Text ?? "");
            byte[] outputBytes = TransformCompression(inputBytes, GetComboText(CompressComboBox), compress: true);
            CompressOutputTextBox.Text = CompressHexCheckBox.IsChecked == true ? ToHex(outputBytes) : Convert.ToBase64String(outputBytes);
            ToolSummaryTextBlock.Text = $"压缩完成：{inputBytes.Length:N0} → {outputBytes.Length:N0} Bytes。";
        }
        catch (Exception exception) { ToolSummaryTextBlock.Text = $"压缩失败：{exception.Message}"; }
    }

    private void Decompress_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            byte[] inputBytes = CompressHexCheckBox.IsChecked == true ? FromHex(CompressInputTextBox.Text) : Convert.FromBase64String(RemoveWhitespace(CompressInputTextBox.Text));
            byte[] outputBytes = TransformCompression(inputBytes, GetComboText(CompressComboBox), compress: false);
            CompressOutputTextBox.Text = CompressHexCheckBox.IsChecked == true ? ToHex(outputBytes) : Encoding.UTF8.GetString(outputBytes);
            ToolSummaryTextBlock.Text = $"解压完成：{inputBytes.Length:N0} → {outputBytes.Length:N0} Bytes。";
        }
        catch (Exception exception) { ToolSummaryTextBlock.Text = $"解压失败：{exception.Message}"; }
    }

    private void RemoveDuplicateLines_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        string inputText = DistinctLeftTextBox.Text ?? string.Empty;
        string[] lines = inputText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> result = new();
        foreach (string line in lines)
        {
            if (seen.Add(line)) result.Add(line);
        }
        DistinctOutputTextBox.Text = string.Join(Environment.NewLine, result);
        ToolSummaryTextBlock.Text = $"文本去重完成：{lines.Length:N0} 行 → {result.Count:N0} 行。";
    }

    private void UseCurrentTime_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        DateTimeInputTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        DateTimeToTimestamp_Click(sender, routedEventArgs);
    }

    private void DateTimeToTimestamp_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!DateTime.TryParse(DateTimeInputTextBox.Text, out DateTime dateTime))
        {
            ToolSummaryTextBlock.Text = "时间转换失败：请输入有效时间，例如 2026-04-25 12:00:00。";
            return;
        }
        DateTimeOffset dateTimeOffset = new(dateTime.ToLocalTime());
        TimestampInputTextBox.Text = dateTimeOffset.ToUnixTimeMilliseconds().ToString();
        TimestampOutputTextBox.Text = FormatTimestampResult(dateTimeOffset);
        ToolSummaryTextBlock.Text = "时间已转换为 Unix 时间戳。";
    }

    private void TimestampToDateTime_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!long.TryParse(TimestampInputTextBox.Text?.Trim(), out long timestamp))
        {
            ToolSummaryTextBlock.Text = "时间戳转换失败：请输入 Unix 秒或毫秒时间戳。";
            return;
        }
        DateTimeOffset dateTimeOffset = timestamp > 9999999999 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp) : DateTimeOffset.FromUnixTimeSeconds(timestamp);
        DateTimeInputTextBox.Text = dateTimeOffset.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
        TimestampOutputTextBox.Text = FormatTimestampResult(dateTimeOffset);
        ToolSummaryTextBlock.Text = "时间戳已转换为时间。";
    }

    private void UpperCase_Click(object sender, RoutedEventArgs routedEventArgs) { CaseOutputTextBox.Text = (CaseInputTextBox.Text ?? "").ToUpperInvariant(); ToolSummaryTextBlock.Text = "大小写转换完成：大写。"; }
    private void LowerCase_Click(object sender, RoutedEventArgs routedEventArgs) { CaseOutputTextBox.Text = (CaseInputTextBox.Text ?? "").ToLowerInvariant(); ToolSummaryTextBlock.Text = "大小写转换完成：小写。"; }
    private void TitleCase_Click(object sender, RoutedEventArgs routedEventArgs) { CaseOutputTextBox.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase((CaseInputTextBox.Text ?? "").ToLower()); ToolSummaryTextBlock.Text = "大小写转换完成：首字母大写。"; }

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
            if (different && start is null) start = index;
            else if (!different && start is int startIndex) { spans.Add(new DiffSpan(startIndex, index - startIndex)); start = null; }
        }
        if (start is int tailStart) spans.Add(new DiffSpan(tailStart, source.Length - tailStart));
        return spans.ToArray();
    }

    private static void RenderDocument(RichTextBox editor, string text, IReadOnlyList<DiffSpan> diffs)
    {
        int caretOffset = GetCaretOffset(editor);
        FlowDocument document = new() { PagePadding = new Thickness(10, 8, 10, 8), FontFamily = editor.FontFamily, FontSize = editor.FontSize, LineHeight = 18 };
        Paragraph paragraph = new() { Margin = new Thickness(0) };
        int offset = 0;
        foreach (DiffSpan diff in diffs)
        {
            if (diff.Start > offset) paragraph.Inlines.Add(CreateRun(text[offset..diff.Start], false));
            paragraph.Inlines.Add(CreateRun(text.Substring(diff.Start, diff.Length), true));
            offset = diff.Start + diff.Length;
        }
        if (offset < text.Length) paragraph.Inlines.Add(CreateRun(text[offset..], false));
        document.Blocks.Add(paragraph);
        editor.Document = document;
        RestoreCaretOffset(editor, Math.Min(caretOffset, text.Length));
    }

    private static Run CreateRun(string text, bool isDiff)
    {
        Run run = new(text) { Foreground = isDiff ? DiffForegroundBrush : NormalForegroundBrush, Background = isDiff ? DiffBackgroundBrush : NormalBackgroundBrush };
        if (isDiff) run.FontWeight = FontWeights.SemiBold;
        return run;
    }

    private static string GetEditorText(RichTextBox editor) => new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
    private static string NormalizeEditorText(string text) { string normalized = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n'); return normalized.EndsWith('\n') ? normalized[..^1] : normalized; }
    private static int GetCaretOffset(RichTextBox editor) => new TextRange(editor.Document.ContentStart, editor.CaretPosition).Text.Length;

    private static void RestoreCaretOffset(RichTextBox editor, int offset)
    {
        TextPointer pointer = editor.Document.ContentStart;
        int remaining = offset;
        while (pointer is not null)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string text = pointer.GetTextInRun(LogicalDirection.Forward);
                if (remaining <= text.Length) { editor.CaretPosition = pointer.GetPositionAtOffset(remaining, LogicalDirection.Forward) ?? pointer; return; }
                remaining -= text.Length;
            }
            TextPointer? next = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (next is null) break;
            pointer = next;
        }
        editor.CaretPosition = editor.Document.ContentEnd;
    }

    private byte[] GetCodecInputBytes() => CodecHexCheckBox.IsChecked == true ? FromHex(CodecInputTextBox.Text) : Encoding.UTF8.GetBytes(CodecInputTextBox.Text ?? "");
    private static byte[] TransformCompression(byte[] inputBytes, string type, bool compress)
    {
        using MemoryStream input = new(inputBytes);
        using MemoryStream output = new();
        if (compress) { using Stream stream = CreateCompressionStream(output, type, CompressionMode.Compress); input.CopyTo(stream); stream.Close(); return output.ToArray(); }
        using (Stream stream = CreateCompressionStream(input, type, CompressionMode.Decompress)) { stream.CopyTo(output); }
        return output.ToArray();
    }
    private static Stream CreateCompressionStream(Stream stream, string type, CompressionMode mode) => type switch { "GZIP" => new GZipStream(stream, mode, true), "ZLIB" => new ZLibStream(stream, mode, true), "Brotli" => new BrotliStream(stream, mode, true), _ => new GZipStream(stream, mode, true) };
    private static string GetComboText(ComboBox comboBox) => (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private static string FormatTimestampResult(DateTimeOffset value) => $"本地时间：{value.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff}\r\nUTC时间：{value.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff}\r\nUnix秒：{value.ToUnixTimeSeconds()}\r\nUnix毫秒：{value.ToUnixTimeMilliseconds()}\r\nISO 8601：{value:O}";
    private static string RemoveWhitespace(string? text) => new((text ?? "").Where(static c => !char.IsWhiteSpace(c)).ToArray());
    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);
    private static byte[] FromHex(string? text) { string hex = RemoveWhitespace(text).Replace("0x", "", StringComparison.OrdinalIgnoreCase); if (hex.Length % 2 != 0) hex = "0" + hex; return Convert.FromHexString(hex); }
    private readonly record struct DiffSpan(int Start, int Length);
}
