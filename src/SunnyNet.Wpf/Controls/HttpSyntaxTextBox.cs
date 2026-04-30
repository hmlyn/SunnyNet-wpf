using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SunnyNet.Wpf.Services;

namespace SunnyNet.Wpf.Controls;

public sealed class HttpSyntaxTextBox : RichTextBox
{
    private const int ProgressiveThresholdCharacters = 128 * 1024;
    private const int InitialRenderCharacters = 64 * 1024;
    private const int ProgressiveChunkCharacters = 64 * 1024;
    private const int MaxChunkLineLookAhead = 8 * 1024;
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
    private int _renderVersion;
    private bool _renderPending;
    private bool _isProgressiveRendering;
    private bool _progressiveInHeaders;
    private bool _progressiveLooksJson;
    private bool _searchAutoScrolled;
    private bool _logicalSelectAll;
    private string _progressiveText = "";
    private int _progressiveIndex;
    private Paragraph? _paragraph;
    private Span? _progressStatusSpan;
    private ScrollViewer? _scrollViewer;
    private ContextMenu? _defaultContextMenu;

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
        Loaded += HttpSyntaxTextBox_Loaded;
        Unloaded += HttpSyntaxTextBox_Unloaded;
        IsVisibleChanged += HttpSyntaxTextBox_IsVisibleChanged;
        PreviewKeyDown += HttpSyntaxTextBox_PreviewKeyDown;
        PreviewMouseLeftButtonDown += HttpSyntaxTextBox_PreviewMouseLeftButtonDown;
        ContextMenuOpening += HttpSyntaxTextBox_ContextMenuOpening;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopyCommand_Executed, CopyCommand_CanExecute));
        _defaultContextMenu = CreateDefaultContextMenu();
        ContextMenu = _defaultContextMenu;
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
            viewer._logicalSelectAll = false;
            viewer.QueueRender();
        }
    }

    private static void OnSearchChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HttpSyntaxTextBox viewer)
        {
            viewer._activeSearchMatchIndex = 0;
            viewer.QueueRender();
        }
    }

    private void HttpSyntaxTextBox_Loaded(object sender, RoutedEventArgs routedEventArgs)
    {
        AttachScrollViewer();
        if (_renderPending)
        {
            QueueRender();
        }
    }

    private void HttpSyntaxTextBox_Unloaded(object sender, RoutedEventArgs routedEventArgs)
    {
        _renderVersion++;
        _isProgressiveRendering = false;
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer = null;
        }
    }

    private void HttpSyntaxTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsReadyToRender() && _renderPending)
        {
            QueueRender();
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
        if (keyEventArgs.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            MarkLogicalSelectAll();
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopyCurrentSelection();
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key != Key.Enter || string.IsNullOrEmpty(GetEffectiveSearchText()))
        {
            return;
        }

        keyEventArgs.Handled = MoveToNextMatch();
    }

    private void HttpSyntaxTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        _logicalSelectAll = false;
    }

    private void HttpSyntaxTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs contextMenuEventArgs)
    {
        if (ContextMenu is null)
        {
            _defaultContextMenu ??= CreateDefaultContextMenu();
            ContextMenu = _defaultContextMenu;
        }

        UpdateDefaultContextMenu();
    }

    private ContextMenu CreateDefaultContextMenu()
    {
        ContextMenu menu = new();
        MenuItem copyMenuItem = new()
        {
            Header = "复制",
            InputGestureText = "Ctrl+C",
            Command = ApplicationCommands.Copy,
            CommandTarget = this
        };
        MenuItem copyAllMenuItem = new()
        {
            Header = "复制全部",
            InputGestureText = "Ctrl+A, Ctrl+C",
            Tag = "CopyAll"
        };
        MenuItem openInNotepadMenuItem = new()
        {
            Header = "记事本打开",
            Tag = "OpenInNotepad"
        };
        copyAllMenuItem.Click += CopyAllMenuItem_Click;
        openInNotepadMenuItem.Click += OpenInNotepadMenuItem_Click;
        menu.Items.Add(copyMenuItem);
        menu.Items.Add(copyAllMenuItem);
        menu.Items.Add(openInNotepadMenuItem);
        menu.Opened += (_, _) => UpdateDefaultContextMenu();
        return menu;
    }

    private void UpdateDefaultContextMenu()
    {
        if (ContextMenu is null || ContextMenu != _defaultContextMenu || ContextMenu.Items.Count == 0)
        {
            return;
        }

        bool hasText = !string.IsNullOrEmpty(SourceText);
        bool hasSelection = _logicalSelectAll || !Selection.IsEmpty;
        foreach (object item in ContextMenu.Items)
        {
            if (item is not MenuItem menuItem)
            {
                continue;
            }

            if (menuItem.Command == ApplicationCommands.Copy)
            {
                menuItem.IsEnabled = hasSelection;
                menuItem.Header = _logicalSelectAll
                    ? $"复制全部文本 ({(SourceText?.Length ?? 0):N0} 字符)"
                    : "复制";
            }
            else if (Equals(menuItem.Tag, "CopyAll"))
            {
                menuItem.IsEnabled = hasText;
                menuItem.Header = hasText
                    ? $"复制全部 ({(SourceText?.Length ?? 0):N0} 字符)"
                    : "复制全部";
            }
            else if (Equals(menuItem.Tag, "OpenInNotepad"))
            {
                menuItem.IsEnabled = hasText;
            }
        }
    }

    private void CopyCommand_CanExecute(object sender, CanExecuteRoutedEventArgs canExecuteRoutedEventArgs)
    {
        canExecuteRoutedEventArgs.CanExecute = _logicalSelectAll || !Selection.IsEmpty;
        canExecuteRoutedEventArgs.Handled = true;
    }

    private void CopyCommand_Executed(object sender, ExecutedRoutedEventArgs executedRoutedEventArgs)
    {
        CopyCurrentSelection();
        executedRoutedEventArgs.Handled = true;
    }

    private void CopyAllMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        CopyText(SourceText ?? "");
    }

    private void OpenInNotepadMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenTextInNotepad(SourceText ?? "");
    }

    private void MarkLogicalSelectAll()
    {
        _logicalSelectAll = true;
        try
        {
            Selection.Select(Document.ContentStart, Document.ContentStart);
        }
        catch
        {
        }
    }

    private void CopyCurrentSelection()
    {
        if (_logicalSelectAll)
        {
            CopyText(SourceText ?? "");
            return;
        }

        if (!Selection.IsEmpty)
        {
            CopyText(Selection.Text);
        }
    }

    private static void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            ClipboardService.SetText(text);
        }
        catch
        {
        }
    }

    private static void OpenTextInNotepad(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            string directory = Path.Combine(Path.GetTempPath(), "SunnyNetWpf");
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, $"sunnynet-view-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(filePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            ProcessStartInfo startInfo = new("notepad.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(filePath);
            Process.Start(startInfo);
        }
        catch
        {
        }
    }

    private void QueueRender()
    {
        _renderVersion++;
        _isProgressiveRendering = false;
        _searchAutoScrolled = false;
        _logicalSelectAll = false;
        if (!IsReadyToRender())
        {
            _renderPending = true;
            return;
        }

        _renderPending = false;
        RenderDocument(_renderVersion);
    }

    private bool IsReadyToRender()
    {
        return IsLoaded && IsVisible;
    }

    private void RenderDocument(int version)
    {
        _searchMatchRuns.Clear();
        _paragraph = new Paragraph
        {
            Margin = new Thickness(0)
        };

        FlowDocument document = new()
        {
            PagePadding = new Thickness(14, 12, 14, 12),
            FontFamily = FontFamily,
            FontSize = FontSize,
            LineHeight = 18
        };
        document.Blocks.Add(_paragraph);
        Document = document;

        _progressiveText = NormalizeText(SourceText ?? "");
        _progressiveIndex = 0;
        _progressiveInHeaders = HighlightMode.Contains("Raw", StringComparison.OrdinalIgnoreCase);
        _progressiveLooksJson = LooksJson(_progressiveText) || HighlightMode.Contains("Json", StringComparison.OrdinalIgnoreCase);

        if (_progressiveText.Length == 0)
        {
            return;
        }

        bool progressive = _progressiveText.Length > ProgressiveThresholdCharacters;
        int initialLength = progressive ? InitialRenderCharacters : _progressiveText.Length;
        int firstEnd = FindChunkEnd(_progressiveText, 0, initialLength);
        AppendTextRange(_paragraph, _progressiveText, 0, firstEnd, ref _progressiveInHeaders, _progressiveLooksJson);
        _progressiveIndex = firstEnd;

        if (!progressive || _progressiveIndex >= _progressiveText.Length)
        {
            ApplySearchMatchVisuals(scrollToActiveMatch: HasSearchText());
            return;
        }

        _isProgressiveRendering = true;
        AddProgressStatus();
        ApplySearchMatchVisuals(scrollToActiveMatch: HasSearchText());
        ScheduleProgressiveAppend(version);
    }

    private void ScheduleProgressiveAppend(int version)
    {
        Dispatcher.BeginInvoke(new Action(() => AppendProgressiveChunk(version)), DispatcherPriority.Background);
    }

    private void AppendProgressiveChunk(int version)
    {
        if (version != _renderVersion)
        {
            return;
        }

        if (!IsReadyToRender())
        {
            _renderPending = true;
            _isProgressiveRendering = false;
            return;
        }

        if (_paragraph is null || _progressiveIndex >= _progressiveText.Length)
        {
            _isProgressiveRendering = false;
            RemoveProgressStatus();
            return;
        }

        RemoveProgressStatus();
        int chunkEnd = FindChunkEnd(_progressiveText, _progressiveIndex, ProgressiveChunkCharacters);
        AppendTextRange(_paragraph, _progressiveText, _progressiveIndex, chunkEnd, ref _progressiveInHeaders, _progressiveLooksJson);
        _progressiveIndex = chunkEnd;

        if (_progressiveIndex < _progressiveText.Length)
        {
            AddProgressStatus();
            ApplySearchMatchVisuals(scrollToActiveMatch: HasSearchText() && !_searchAutoScrolled);
            ScheduleProgressiveAppend(version);
            return;
        }

        _isProgressiveRendering = false;
        ApplySearchMatchVisuals(scrollToActiveMatch: HasSearchText() && !_searchAutoScrolled);
    }

    private void AddProgressStatus()
    {
        if (_paragraph is null || !_isProgressiveRendering)
        {
            return;
        }

        _progressStatusSpan = new Span();
        _progressStatusSpan.Inlines.Add(new LineBreak());
        _progressStatusSpan.Inlines.Add(new LineBreak());
        _progressStatusSpan.Inlines.Add(CreateRun(
            $"…… 正在后台加载完整内容：{_progressiveIndex:N0}/{_progressiveText.Length:N0} 字符",
            MutedBrush));
        _paragraph.Inlines.Add(_progressStatusSpan);
    }

    private void RemoveProgressStatus()
    {
        if (_paragraph is not null && _progressStatusSpan is not null)
        {
            _paragraph.Inlines.Remove(_progressStatusSpan);
        }

        _progressStatusSpan = null;
    }

    private void AppendTextRange(Paragraph paragraph, string text, int start, int end, ref bool inHeaders, bool looksJson)
    {
        int cursor = start;
        bool firstLine = start == 0;
        while (cursor < end)
        {
            int lineEnd = text.IndexOf('\n', cursor);
            if (lineEnd < 0 || lineEnd >= end)
            {
                lineEnd = end;
            }

            string line = text[cursor..lineEnd];
            AppendStyledLine(paragraph, line, firstLine, ref inHeaders, looksJson);
            firstLine = false;

            if (lineEnd < end)
            {
                paragraph.Inlines.Add(new LineBreak());
                cursor = lineEnd + 1;
            }
            else
            {
                cursor = end;
            }
        }
    }

    private void AppendStyledLine(Paragraph paragraph, string line, bool firstLine, ref bool inHeaders, bool looksJson)
    {
        if (firstLine && HighlightMode.Contains("Request", StringComparison.OrdinalIgnoreCase))
        {
            AppendRequestLine(paragraph, line);
        }
        else if (firstLine && HighlightMode.Contains("Response", StringComparison.OrdinalIgnoreCase))
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
        _searchAutoScrolled = true;
        Dispatcher.BeginInvoke(new Action(() => activeRun.BringIntoView()), DispatcherPriority.Background);
    }

    private bool HasSearchText()
    {
        return !string.IsNullOrEmpty(GetEffectiveSearchText());
    }

    private string GetEffectiveSearchText()
    {
        string text = SearchText ?? "";
        return text.Length > MaxSearchTextLength ? text[..MaxSearchTextLength] : text;
    }

    private static string NormalizeText(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static int FindChunkEnd(string text, int start, int preferredLength)
    {
        int preferredEnd = Math.Min(text.Length, start + preferredLength);
        if (preferredEnd >= text.Length)
        {
            return text.Length;
        }

        int nextLineBreak = text.IndexOf('\n', preferredEnd);
        if (nextLineBreak >= 0 && nextLineBreak - preferredEnd <= MaxChunkLineLookAhead)
        {
            return nextLineBreak + 1;
        }

        int searchLength = Math.Max(0, preferredEnd - start);
        int previousLineBreak = searchLength > 0 ? text.LastIndexOf('\n', preferredEnd - 1, searchLength) : -1;
        return previousLineBreak > start ? previousLineBreak + 1 : preferredEnd;
    }

    private void AttachScrollViewer()
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        _scrollViewer = FindVisualChild<ScrollViewer>(this);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs scrollChangedEventArgs)
    {
        if (!_isProgressiveRendering || scrollChangedEventArgs.VerticalChange == 0)
        {
            return;
        }

        if (scrollChangedEventArgs.VerticalChange > 0 &&
            scrollChangedEventArgs.VerticalOffset >= scrollChangedEventArgs.ExtentHeight - scrollChangedEventArgs.ViewportHeight - 2)
        {
            ScheduleProgressiveAppend(_renderVersion);
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

    private static T? FindVisualChild<T>(DependencyObject element) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(element, index);
            if (child is T target)
            {
                return target;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
