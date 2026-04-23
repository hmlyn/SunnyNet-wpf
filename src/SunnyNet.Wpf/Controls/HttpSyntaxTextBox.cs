using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SunnyNet.Wpf.Controls;

public sealed class HttpSyntaxTextBox : RichTextBox
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(nameof(SourceText), typeof(string), typeof(HttpSyntaxTextBox), new PropertyMetadata("", OnSourceChanged));

    public static readonly DependencyProperty HighlightModeProperty =
        DependencyProperty.Register(nameof(HighlightMode), typeof(string), typeof(HttpSyntaxTextBox), new PropertyMetadata("Body", OnSourceChanged));

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

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HttpSyntaxTextBox viewer)
        {
            viewer.RenderDocument();
        }
    }

    private void RenderDocument()
    {
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

        string text = (SourceText ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
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
                paragraph.Inlines.Add(new Run(""));
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
                paragraph.Inlines.Add(CreateRun(line, TextBrush));
            }

            if (index < lines.Length - 1)
            {
                paragraph.Inlines.Add(new LineBreak());
            }
        }

        document.Blocks.Add(paragraph);
        Document = document;
    }

    private static void AppendRequestLine(Paragraph paragraph, string line)
    {
        string[] parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            paragraph.Inlines.Add(CreateRun(line, TextBrush));
            return;
        }

        paragraph.Inlines.Add(CreateRun(parts[0], MethodBrush(parts[0]), FontWeights.SemiBold));
        if (parts.Length > 1)
        {
            paragraph.Inlines.Add(CreateRun(" ", TextBrush));
            paragraph.Inlines.Add(CreateRun(parts[1], UrlBrush));
        }

        if (parts.Length > 2)
        {
            paragraph.Inlines.Add(CreateRun(" ", TextBrush));
            paragraph.Inlines.Add(CreateRun(parts[2], MutedBrush));
        }
    }

    private static void AppendResponseLine(Paragraph paragraph, string line)
    {
        string[] parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            paragraph.Inlines.Add(CreateRun(line, TextBrush));
            return;
        }

        paragraph.Inlines.Add(CreateRun(parts[0], MutedBrush));
        if (parts.Length > 1)
        {
            paragraph.Inlines.Add(CreateRun(" ", TextBrush));
            paragraph.Inlines.Add(CreateRun(parts[1], StatusBrush(parts[1]), FontWeights.SemiBold));
        }

        if (parts.Length > 2)
        {
            paragraph.Inlines.Add(CreateRun(" ", TextBrush));
            paragraph.Inlines.Add(CreateRun(parts[2], TextBrush));
        }
    }

    private static void AppendHeaderLine(Paragraph paragraph, string line)
    {
        int colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
        {
            paragraph.Inlines.Add(CreateRun(line, TextBrush));
            return;
        }

        string name = line[..colonIndex];
        string value = line[(colonIndex + 1)..].TrimStart();
        paragraph.Inlines.Add(CreateRun(name, HeaderNameBrush, FontWeights.SemiBold));
        paragraph.Inlines.Add(CreateRun(": ", SeparatorBrush));
        paragraph.Inlines.Add(CreateRun(value, HeaderValueBrush));
    }

    private static void AppendJsonLine(Paragraph paragraph, string line)
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
                paragraph.Inlines.Add(CreateRun(token, brush));
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

                paragraph.Inlines.Add(CreateRun(line[start..index], JsonNumberBrush));
            }
            else if (StartsWithKeyword(line, index, "true", out int trueEnd) ||
                     StartsWithKeyword(line, index, "false", out trueEnd))
            {
                paragraph.Inlines.Add(CreateRun(line[index..trueEnd], JsonBoolBrush, FontWeights.SemiBold));
                index = trueEnd;
            }
            else if (StartsWithKeyword(line, index, "null", out trueEnd))
            {
                paragraph.Inlines.Add(CreateRun(line[index..trueEnd], JsonNullBrush, FontWeights.SemiBold));
                index = trueEnd;
            }
            else
            {
                paragraph.Inlines.Add(CreateRun(
                    current.ToString(),
                    current is '{' or '}' or '[' or ']' or ':' or ','
                        ? JsonSeparatorBrush
                        : TextBrush));
                index++;
            }
        }
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
