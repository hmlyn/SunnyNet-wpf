using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SunnyNet.Wpf.Controls;

public sealed class HttpSyntaxTextBox : RichTextBox
{
    private const int MaxImmediateRenderCharacters = 48 * 1024;
    private const int MaxSearchTextLength = 256;

    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(HttpSyntaxTextBox), new PropertyMetadata("", OnSourceChanged));

    public static readonly DependencyProperty HighlightModeProperty =
        DependencyProperty.Register(nameof(HighlightMode), typeof(string), typeof(HttpSyntaxTextBox), new PropertyMetadata("Body", OnSourceChanged));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(HttpSyntaxTextBox), new PropertyMetadata("", OnSearchChanged));

    public static readonly DependencyProperty SearchIgnoreCaseProperty =
        DependencyProperty.Register(nameof(SearchIgnoreCase), typeof(bool), typeof(HttpSyntaxTextBox), new PropertyMetadata(true, OnSearchChanged));

    private static readonly Brush TextBrush = CreateBrush(0x1F, 0x2D, 0x3D);
    private static readonly Brush MutedBrush = CreateBrush(0x6B, 0x7C, 0x93);
    private static readonly Brush SeparatorBrush = CreateBrush(0xA7, 0xB2, 0xC3);
    private static readonly Brush JsonSeparatorBrush = CreateBrush(0xC1, 0xCB, 0xD8);
    private static readonly Brush HeaderNameBrush = CreateBrush(0x1E, 0x50, 0xC8);
    private static readonly Brush HeaderValueBrush = TextBrush;
    private static readonly Brush UrlBrush = CreateBrush(0x1D, 0x4E, 0x89);
    private static readonly Brush GetBrush = CreateBrush(0x19, 0x7A, 0x4D);
    private static readonly Brush PostBrush = CreateBrush(0x2F, 0x7C, 0xF6);
    private static readonly Brush PutBrush = CreateBrush(0xB4, 0x6B, 0x00);
    private static readonly Brush DeleteBrush = CreateBrush(0xD9, 0x2D, 0x20);
    private static readonly Brush PatchBrush = CreateBrush(0x8B, 0x5C, 0xD6);
    private static readonly Brush JsonKeyBrush = CreateBrush(0x00, 0x00, 0x00);
    private static readonly Brush JsonStringBrush = CreateBrush(0x0F, 0x7B, 0x63);
    private static readonly Brush JsonNumberBrush = CreateBrush(0xD9, 0x2D, 0x20);
    private static readonly Brush JsonBoolBrush = CreateBrush(0x1E, 0x50, 0xC8);
    private static readonly Brush JsonNullBrush = CreateBrush(0x8C, 0x8C, 0x8C);
    private static readonly Brush SearchMatchBrush = CreateBrush(0xFF, 0xF3, 0xB0);
    private static readonly Brush SearchActiveMatchBrush = CreateBrush(0xFF, 0xB0, 0x20);

    private readonly List<Run> _searchMatchRuns = new();
    private int _activeSearchMatchIndex;

    public HttpSyntaxTextBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Foreground = TextBrush;
        FontFamily = new FontFamily("Consolas, Microsoft YaHei UI");
        FontSize = 12;
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        PreviewKeyDown += HttpSyntaxTextBox_PreviewKeyDown;
        RenderDocument();
    }

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string HighlightMode
    {
        get => (string)GetValue(HighlightModeProperty);
        set => SetValue(HighlightModeProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public bool SearchIgnoreCase
    {
        get => (bool)GetValue(SearchIgnoreCaseProperty);
        set => SetValue(SearchIgnoreCaseProperty, value);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HttpSyntaxTextBox viewer)
        {
            viewer._activeSearchMatchIndex = 0;
            viewer.RenderDocument();
        }
    }

    private static void OnSearchChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HttpSyntaxTextBox viewer)
        {
            viewer._activeSearchMatchIndex = 0;
            viewer.RenderDocument();
        }
    }

    public bool MoveToNextMatch()
    {
        if (_searchMatchRuns.Count == 0)
        {
            return false;
        }

        _activeSearchMatchIndex = (_activeSearchMatchIndex + 1) % _searchMatchRuns.Count;
        ApplySearchMatchVisuals(scrollToActiveMatch: true);
        Focus();
        return true;
    }

    public bool MoveToFirstMatch()
    {
        if (_searchMatchRuns.Count == 0)
        {
            return false;
        }

        _activeSearchMatchIndex = 0;
        ApplySearchMatchVisuals(scrollToActiveMatch: true);
        return true;
    }

    private void HttpSyntaxTextBox_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key != Key.Enter || string.IsNullOrEmpty(GetEffectiveSearchText()))
        {
            return;
        }

        keyEventArgs.Handled = MoveToNextMatch();
    }

    private void RenderDocument()
    {
        _searchMatchRuns.Clear();
        FlowDocument document = new()
        {
            PagePadding = new Thickness(14, 12, 14, 12),
            FontFamily = FontFamily,
            FontSize = FontSize,
            LineHeight = 18
        };

        Paragraph paragraph = new()
        {
            Margin = new Thickness(0)
        };

        string sourceText = SourceText ?? "";
        string text = BuildDisplayText(sourceText)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        string[] lines = text.Split('\n');
        bool inHeaders = HighlightMode.Contains("Raw", StringComparison.OrdinalIgnoreCase);
        bool looksJson = LooksJson(text) || HighlightMode.Contains("Json", StringComparison.OrdinalIgnoreCase);

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (index == 0 && HighlightMode.Contains("Request", StringComparison.OrdinalIgnoreCase))
            {
                AppendRequestLine(paragraph, line);
            }
            else if (index == 0 && HighlightMode.Contains("Response", StringComparison.OrdinalIgnoreCase))
            {
                AppendResponseLine(paragraph, line);
            }
            else if (inHeaders && string.IsNullOrWhiteSpace(line))
            {
                AppendRun(paragraph, "", TextBrush);
                inHeaders = false;
            }
            else if (inHeaders && IsHeaderLine(line))
            {
                AppendHeaderLine(paragraph, line);
            }
            else if (looksJson)
            {
                AppendJsonLine(paragraph, line);
            }
            else
            {
                AppendRun(paragraph, line, TextBrush);
            }

            if (index < lines.Length - 1)
            {
                paragraph.Inlines.Add(new LineBreak());
            }
        }

        document.Blocks.Add(paragraph);
        Document = document;
        ApplySearchMatchVisuals(scrollToActiveMatch: HasSearchText());
    }

    private void AppendRequestLine(Paragraph paragraph, string line)
    {
        string[] parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            AppendRun(paragraph, line, TextBrush);
            return;
        }

        AppendRun(paragraph, parts[0], MethodBrush(parts[0]), FontWeights.SemiBold);
        if (parts.Length > 1)
        {
            AppendRun(paragraph, " ", TextBrush);
            AppendRun(paragraph, parts[1], UrlBrush);
        }

        if (parts.Length > 2)
        {
            AppendRun(paragraph, " ", TextBrush);
            AppendRun(paragraph, parts[2], MutedBrush);
        }
    }

    private void AppendResponseLine(Paragraph paragraph, string line)
    {
        string[] parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            AppendRun(paragraph, line, TextBrush);
            return;
        }

        AppendRun(paragraph, parts[0], MutedBrush);
        if (parts.Length > 1)
        {
            AppendRun(paragraph, " ", TextBrush);
            AppendRun(paragraph, parts[1], StatusBrush(parts[1]), FontWeights.SemiBold);
        }

        if (parts.Length > 2)
        {
            AppendRun(paragraph, " ", TextBrush);
            AppendRun(paragraph, parts[2], TextBrush);
        }
    }

    private void AppendHeaderLine(Paragraph paragraph, string line)
    {
        int colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
        {
            AppendRun(paragraph, line, TextBrush);
            return;
        }

        string name = line[..colonIndex];
        string value = line[(colonIndex + 1)..].TrimStart();
        AppendRun(paragraph, name, HeaderNameBrush, FontWeights.SemiBold);
        AppendRun(paragraph, ": ", SeparatorBrush);
        AppendRun(paragraph, value, HeaderValueBrush);
    }

    private void AppendJsonLine(Paragraph paragraph, string line)
    {
        int index = 0;
        while (index < line.Length)
        {
            char current = line[index];
            if (current == '"')
            {
                int end = FindStringEnd(line, index + 1);
                string token = line[index..end];
                int probe = end;
                while (probe < line.Length && char.IsWhiteSpace(line[probe]))
                {
                    probe++;
                }

                Brush brush = probe < line.Length && line[probe] == ':' ? JsonKeyBrush : JsonStringBrush;
                AppendRun(paragraph, token, brush);
                index = end;
            }
            else if (char.IsDigit(current) || current == '-')
            {
                int start = index;
                index++;
                while (index < line.Length && (char.IsDigit(line[index]) || line[index] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    index++;
                }

                AppendRun(paragraph, line[start..index], JsonNumberBrush);
            }
            else if (StartsWithKeyword(line, index, "true", out int trueEnd) ||
                     StartsWithKeyword(line, index, "false", out trueEnd))
            {
                AppendRun(paragraph, line[index..trueEnd], JsonBoolBrush, FontWeights.SemiBold);
                index = trueEnd;
            }
            else if (StartsWithKeyword(line, index, "null", out trueEnd))
            {
                AppendRun(paragraph, line[index..trueEnd], JsonNullBrush, FontWeights.SemiBold);
                index = trueEnd;
            }
            else
            {
                AppendRun(
                    paragraph,
                    current.ToString(),
                    current is '{' or '}' or '[' or ']' or ':' or ','
                        ? JsonSeparatorBrush
                        : TextBrush);
                index++;
            }
        }
    }

    private void AppendRun(Paragraph paragraph, string text, Brush brush, FontWeight? fontWeight = null)
    {
        string searchText = GetEffectiveSearchText();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
        {
            paragraph.Inlines.Add(CreateRun(text, brush, fontWeight));
            return;
        }

        StringComparison comparison = SearchIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int cursor = 0;
        while (cursor < text.Length)
        {
            int matchIndex = text.IndexOf(searchText, cursor, comparison);
            if (matchIndex < 0)
            {
                paragraph.Inlines.Add(CreateRun(text[cursor..], brush, fontWeight));
                return;
            }

            if (matchIndex > cursor)
            {
                paragraph.Inlines.Add(CreateRun(text[cursor..matchIndex], brush, fontWeight));
            }

            Run matchRun = CreateRun(text.Substring(matchIndex, searchText.Length), brush, fontWeight);
            _searchMatchRuns.Add(matchRun);
            paragraph.Inlines.Add(matchRun);
            cursor = matchIndex + searchText.Length;
        }
    }

    private void ApplySearchMatchVisuals(bool scrollToActiveMatch)
    {
        if (_searchMatchRuns.Count == 0)
        {
            return;
        }

        if (_activeSearchMatchIndex < 0 || _activeSearchMatchIndex >= _searchMatchRuns.Count)
        {
            _activeSearchMatchIndex = 0;
        }

        for (int index = 0; index < _searchMatchRuns.Count; index++)
        {
            _searchMatchRuns[index].Background = index == _activeSearchMatchIndex
                ? SearchActiveMatchBrush
                : SearchMatchBrush;
        }

        if (!scrollToActiveMatch)
        {
            return;
        }

        Run activeRun = _searchMatchRuns[_activeSearchMatchIndex];
        Dispatcher.BeginInvoke(new Action(() => activeRun.BringIntoView()), System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool HasSearchText()
    {
        return !string.IsNullOrEmpty(GetEffectiveSearchText());
    }

    private string BuildDisplayText(string sourceText)
    {
        if (sourceText.Length <= MaxImmediateRenderCharacters)
        {
            return sourceText;
        }

        string searchText = GetEffectiveSearchText();
        if (!string.IsNullOrEmpty(searchText))
        {
            StringComparison comparison = SearchIgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            int matchIndex = sourceText.IndexOf(searchText, comparison);
            if (matchIndex >= 0)
            {
                int start = Math.Max(0, matchIndex - MaxImmediateRenderCharacters / 2);
                int length = Math.Min(MaxImmediateRenderCharacters, sourceText.Length - start);
                string prefix = start > 0
                    ? $"…… 已跳过前 {start:N0} 字符，以下为搜索命中附近预览。\r\n\r\n"
                    : "";
                string suffix = start + length < sourceText.Length
                    ? $"\r\n\r\n…… 后续还有 {sourceText.Length - start - length:N0} 字符，完整内容请切换 HEX 视图或使用复制功能。"
                    : "";

                return prefix + sourceText.Substring(start, length) + suffix;
            }
        }

        return sourceText[..MaxImmediateRenderCharacters] + $"\r\n\r\n…… 已预览前 {MaxImmediateRenderCharacters:N0} 字符，完整内容请切换 HEX 视图或使用复制功能。";
    }

    private string GetEffectiveSearchText()
    {
        string text = SearchText ?? "";
        return text.Length > MaxSearchTextLength ? text[..MaxSearchTextLength] : text;
    }

    private static bool StartsWithKeyword(string line, int index, string keyword, out int end)
    {
        end = index + keyword.Length;
        return end <= line.Length && string.Compare(line, index, keyword, 0, keyword.Length, StringComparison.Ordinal) == 0;
    }

    private static int FindStringEnd(string line, int start)
    {
        bool escaped = false;
        for (int index = start; index < line.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (line[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (line[index] == '"')
            {
                return index + 1;
            }
        }

        return line.Length;
    }

    private static bool IsHeaderLine(string line)
    {
        int colonIndex = line.IndexOf(':');
        return colonIndex > 0 && colonIndex < line.Length - 1;
    }

    private static bool LooksJson(string text)
    {
        string trimmed = text.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static Brush MethodBrush(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => GetBrush,
            "POST" => PostBrush,
            "PUT" => PutBrush,
            "DELETE" => DeleteBrush,
            "PATCH" => PatchBrush,
            _ => TextBrush
        };
    }

    private static Brush StatusBrush(string status)
    {
        if (!int.TryParse(status, out int code))
        {
            return TextBrush;
        }

        return code switch
        {
            >= 200 and < 300 => GetBrush,
            >= 300 and < 400 => PostBrush,
            >= 400 and < 500 => PutBrush,
            >= 500 => DeleteBrush,
            _ => TextBrush
        };
    }

    private static Run CreateRun(string text, Brush brush, FontWeight? fontWeight = null)
    {
        Run run = new(text)
        {
            Foreground = brush
        };

        if (fontWeight.HasValue)
        {
            run.FontWeight = fontWeight.Value;
        }

        return run;
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
