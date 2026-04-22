using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SunnyNet.Wpf.Controls;

public partial class HexViewControl : UserControl
{
    public static readonly DependencyProperty BytesProperty =
        DependencyProperty.Register(nameof(Bytes), typeof(byte[]), typeof(HexViewControl), new PropertyMetadata(Array.Empty<byte>(), OnHexDataChanged));

    public static readonly DependencyProperty HeaderLengthProperty =
        DependencyProperty.Register(nameof(HeaderLength), typeof(int), typeof(HexViewControl), new PropertyMetadata(0, OnHexDataChanged));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(HexViewControl), new PropertyMetadata("无十六进制数据", OnEmptyTextChanged));

    private static readonly Brush OffsetBrush = CreateBrush(0x6B, 0x7C, 0x93);
    private static readonly Brush OffsetSelectedBrush = CreateBrush(0x1E, 0x63, 0xD6);
    private static readonly Brush OffsetSelectedBackgroundBrush = CreateBrush(0xE7, 0xF0, 0xFF);
    private static readonly Brush HexHeaderBrush = CreateBrush(0x1D, 0x4E, 0x89);
    private static readonly Brush HexBodyBrush = CreateBrush(0x0F, 0x5E, 0x75);
    private static readonly Brush TextHeaderBrush = CreateBrush(0x5F, 0x6F, 0x82);
    private static readonly Brush TextBodyBrush = CreateBrush(0x19, 0x7A, 0x4D);
    private static readonly Brush SelectedBackgroundBrush = CreateBrush(0x2F, 0x7C, 0xF6);
    private static readonly Brush SelectedForegroundBrush = Brushes.White;
    private static readonly Brush SelectedLineBackgroundBrush = CreateBrush(0xF3, 0xF8, 0xFF);
    private static readonly Brush SelectedLineBorderBrush = CreateBrush(0x8F, 0xB8, 0xF8);
    private static readonly Brush AnchorBrush = CreateBrush(0x8B, 0x5C, 0xD6);
    private static readonly Brush AnchorSoftBrush = CreateBrush(0xF1, 0xEA, 0xFF);
    private static readonly Brush CurrentBrush = CreateBrush(0xF5, 0x9E, 0x0B);
    private static readonly Brush CurrentSoftBrush = CreateBrush(0xFF, 0xF3, 0xD8);
    private const double LineHeight = 22;
    private const double HexPaddingLeft = 12;
    private const double HexPaddingTop = 10;
    private const double AsciiPaddingLeft = 16;
    private const double AsciiPaddingTop = 10;
    private const double AutoScrollThreshold = 28;
    private const double AutoScrollMaxStep = 18;
    private int _lastBytesPerLine;
    private int? _selectionAnchor;
    private int? _selectionStart;
    private int? _selectionEnd;
    private SelectionRegion _activeRegion;
    private SelectionRegion _preferredRegion = SelectionRegion.Hex;
    private readonly DispatcherTimer _autoScrollTimer;
    private readonly DispatcherTimer _copyFeedbackTimer;
    private UIElement? _selectionCaptureElement;
    private string? _copyFeedbackText;

    public HexViewControl()
    {
        InitializeComponent();
        _autoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(24)
        };
        _autoScrollTimer.Tick += AutoScrollTimer_Tick;
        _copyFeedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1600)
        };
        _copyFeedbackTimer.Tick += CopyFeedbackTimer_Tick;
        Loaded += (_, _) => RenderHex();
        Unloaded += (_, _) =>
        {
            StopAutoScroll();
            StopCopyFeedback();
        };
    }

    public byte[] Bytes
    {
        get => (byte[])GetValue(BytesProperty);
        set => SetValue(BytesProperty, value);
    }

    public int HeaderLength
    {
        get => (int)GetValue(HeaderLengthProperty);
        set => SetValue(HeaderLengthProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    private static void OnHexDataChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HexViewControl control)
        {
            control.RenderHex();
        }
    }

    private static void OnEmptyTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HexViewControl control && control.EmptyTextBlock is not null)
        {
            control.EmptyTextBlock.Text = args.NewValue?.ToString() ?? "";
        }
    }

    private void HexViewControl_SizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
    {
        if (!IsLoaded)
        {
            return;
        }

        int bytesPerLine = CalculateBytesPerLine();
        if (bytesPerLine != _lastBytesPerLine)
        {
            RenderHex();
        }
    }

    private void RenderHex()
    {
        if (OffsetRowsPanel is null || HexRowsPanel is null || AsciiRowsPanel is null || SummaryTextBlock is null || SelectionInfoBorder is null || SelectionInfoTextBlock is null)
        {
            return;
        }

        byte[] bytes = Bytes ?? Array.Empty<byte>();
        EmptyTextBlock.Text = EmptyText;
        EmptyPanel.Visibility = bytes.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CoerceSelection(bytes.Length);

        OffsetRowsPanel.Children.Clear();
        HexRowsPanel.Children.Clear();
        AsciiRowsPanel.Children.Clear();

        if (bytes.Length == 0)
        {
            SummaryTextBlock.Text = "HEX 数据";
            SelectionInfoBorder.Visibility = Visibility.Collapsed;
            return;
        }

        int bytesPerLine = CalculateBytesPerLine();
        _lastBytesPerLine = bytesPerLine;
        UpdateColumnWidths(bytesPerLine);
        SummaryTextBlock.Text = BuildSummaryText(bytes.Length, bytesPerLine);
        SelectionInfoBorder.Visibility = HasSelection ? Visibility.Visible : Visibility.Collapsed;
        SelectionInfoTextBlock.Text = BuildSelectionInfoText();

        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            int count = Math.Min(bytesPerLine, bytes.Length - offset);
            int headerCount = Math.Clamp(HeaderLength - offset, 0, count);
            ReadOnlySpan<byte> line = bytes.AsSpan(offset, count);
            bool lineSelected = IsLineSelected(offset, count);
            bool lineStartsSelection = IsSelectionStartLine(offset, count);
            bool lineEndsSelection = IsSelectionEndLine(offset, count);
            bool lineHasAnchor = IsAnchorLine(offset, count);
            bool lineHasCurrent = IsCurrentEdgeLine(offset, count);

            OffsetRowsPanel.Children.Add(CreateOffsetRow(offset, lineSelected, lineStartsSelection, lineEndsSelection, lineHasAnchor, lineHasCurrent));
            HexRowsPanel.Children.Add(CreateContentRow(offset, count, BuildHexTextBlock(line, offset, headerCount), lineSelected, lineStartsSelection, lineEndsSelection, lineHasAnchor, lineHasCurrent, isOffsetColumn: false));
            AsciiRowsPanel.Children.Add(CreateContentRow(offset, count, BuildAsciiTextBlock(line, offset, headerCount), lineSelected, lineStartsSelection, lineEndsSelection, lineHasAnchor, lineHasCurrent, isOffsetColumn: false));
        }
    }

    private void UpdateColumnWidths(int bytesPerLine)
    {
        double charWidth = MeasureCharacterWidth();
        double hexWidth = Math.Max(420, Math.Ceiling(bytesPerLine * charWidth * 3.1) + 36);
        double asciiWidth = Math.Max(220, Math.Ceiling(bytesPerLine * charWidth) + 30);
        HexColumn.Width = new GridLength(hexWidth);
        AsciiColumn.Width = new GridLength(asciiWidth);
    }

    private int CalculateBytesPerLine()
    {
        double actualWidth = ActualWidth;
        if (actualWidth <= 0)
        {
            return 16;
        }

        double charWidth = MeasureCharacterWidth();
        double reserved = 92 + 1 + 1 + 70;
        int bytesPerLine = (int)Math.Floor((actualWidth - reserved) / Math.Max(4.2 * charWidth, 1));
        return Math.Clamp(bytesPerLine, 8, 32);
    }

    private TextBlock BuildHexTextBlock(ReadOnlySpan<byte> line, int lineOffset, int headerCount)
    {
        TextBlock textBlock = new()
        {
            Padding = new Thickness(12, 0, 16, 0),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12,
            Foreground = HexHeaderBrush,
            LineHeight = LineHeight
        };

        foreach (Segment segment in EnumerateSegments(line.Length, lineOffset, headerCount))
        {
            Run run = new(FormatHex(line[segment.Start..segment.End], segment.Start))
            {
                Foreground = segment.IsSelected ? SelectedForegroundBrush : segment.IsHeader ? HexHeaderBrush : HexBodyBrush
            };
            if (segment.IsSelected)
            {
                run.Background = SelectedBackgroundBrush;
            }

            textBlock.Inlines.Add(run);
        }

        return textBlock;
    }

    private TextBlock BuildAsciiTextBlock(ReadOnlySpan<byte> line, int lineOffset, int headerCount)
    {
        TextBlock textBlock = new()
        {
            Padding = new Thickness(16, 0, 12, 0),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12,
            Foreground = TextBodyBrush,
            LineHeight = LineHeight
        };

        foreach (Segment segment in EnumerateSegments(line.Length, lineOffset, headerCount))
        {
            Run run = new(ToVisibleText(line[segment.Start..segment.End]))
            {
                Foreground = segment.IsSelected ? SelectedForegroundBrush : segment.IsHeader ? TextHeaderBrush : TextBodyBrush
            };
            if (segment.IsSelected)
            {
                run.Background = SelectedBackgroundBrush;
            }

            textBlock.Inlines.Add(run);
        }

        return textBlock;
    }

    private void CopyAllHex_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, 8);
        StringBuilder builder = new();
        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            builder.AppendLine(FormatHex(bytes.AsSpan(offset, Math.Min(bytesPerLine, bytes.Length - offset))));
        }

        if (CopyText(builder.ToString().TrimEnd()))
        {
            ShowCopyFeedback($"已复制全部 HEX · {bytes.Length:N0} Bytes");
        }
    }

    private void CopyAllText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, 8);
        StringBuilder builder = new();
        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            builder.AppendLine(ToVisibleText(bytes.AsSpan(offset, Math.Min(bytesPerLine, bytes.Length - offset))));
        }

        if (CopyText(builder.ToString().TrimEnd()))
        {
            ShowCopyFeedback($"已复制全部文本 · {bytes.Length:N0} Bytes");
        }
    }

    private void CopyAllLines_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, 8);
        StringBuilder builder = new();
        for (int offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            ReadOnlySpan<byte> line = bytes.AsSpan(offset, Math.Min(bytesPerLine, bytes.Length - offset));
            builder.Append(offset.ToString("X8"))
                   .Append("  ")
                   .Append(FormatHex(line))
                   .Append("  ")
                   .AppendLine(ToVisibleText(line));
        }

        if (CopyText(builder.ToString().TrimEnd()))
        {
            ShowCopyFeedback($"已复制全部内容 · {bytes.Length:N0} Bytes");
        }
    }

    private void CopySelectedHex_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] selection = GetSelectedBytes();
        if (selection.Length == 0)
        {
            return;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, 8);
        StringBuilder builder = new();
        for (int offset = 0; offset < selection.Length; offset += bytesPerLine)
        {
            builder.AppendLine(FormatHex(selection.AsSpan(offset, Math.Min(bytesPerLine, selection.Length - offset))));
        }

        if (CopyText(builder.ToString().TrimEnd()))
        {
            ShowCopyFeedback($"已复制选中 HEX · {selection.Length:N0} Bytes");
        }
    }

    private void CopySelectedText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] selection = GetSelectedBytes();
        if (selection.Length == 0)
        {
            return;
        }

        if (CopyText(ToVisibleText(selection)))
        {
            ShowCopyFeedback($"已复制选中文本 · {selection.Length:N0} Bytes");
        }
    }

    private void CopySelectedLines_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] selection = GetSelectedBytes();
        if (selection.Length == 0)
        {
            return;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, 8);
        StringBuilder builder = new();
        for (int offset = 0; offset < selection.Length; offset += bytesPerLine)
        {
            ReadOnlySpan<byte> line = selection.AsSpan(offset, Math.Min(bytesPerLine, selection.Length - offset));
            builder.Append(offset.ToString("X8"))
                   .Append("  ")
                   .Append(FormatHex(line))
                   .Append("  ")
                   .AppendLine(ToVisibleText(line));
        }

        if (CopyText(builder.ToString().TrimEnd()))
        {
            ShowCopyFeedback($"已复制选中全部 · {selection.Length:N0} Bytes");
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ClearSelection();
    }

    private void HexContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        bool hasSelection = HasSelection;
        CopySelectedHexMenuItem.IsEnabled = hasSelection;
        CopySelectedTextMenuItem.IsEnabled = hasSelection;
        CopySelectedLinesMenuItem.IsEnabled = hasSelection;
        ClearSelectionMenuItem.IsEnabled = hasSelection;
        if (hasSelection)
        {
            int count = GetSelectedBytes().Length;
            CopySelectedHexMenuItem.Header = $"复制选中 HEX ({count:N0} Bytes)";
            CopySelectedTextMenuItem.Header = $"复制选中文本 ({count:N0} Bytes)";
            CopySelectedLinesMenuItem.Header = $"复制选中全部 ({count:N0} Bytes)";
        }
        else
        {
            CopySelectedHexMenuItem.Header = "复制选中 HEX";
            CopySelectedTextMenuItem.Header = "复制选中文本";
            CopySelectedLinesMenuItem.Header = "复制选中全部";
        }
    }

    private void HexViewControl_PreviewKeyDown(object sender, KeyEventArgs keyEventArgs)
    {
        if (keyEventArgs.Key == Key.Escape)
        {
            ClearSelection();
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectAll();
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && HasSelection)
        {
            CopySelectedHex_Click(sender, new RoutedEventArgs());
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && HasSelection)
        {
            CopySelectedText_Click(sender, new RoutedEventArgs());
            keyEventArgs.Handled = true;
            return;
        }

        if (TryHandleNavigationKey(keyEventArgs))
        {
            keyEventArgs.Handled = true;
            return;
        }

        if (keyEventArgs.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && HasSelection)
        {
            CopySelectedLines_Click(sender, new RoutedEventArgs());
            keyEventArgs.Handled = true;
        }
    }

    private Border CreateOffsetRow(int offset, bool isSelected, bool isSelectionStartLine, bool isSelectionEndLine, bool hasAnchor, bool hasCurrent)
    {
        TextBlock textBlock = new()
        {
            Text = offset.ToString("X8"),
            Padding = new Thickness(10, 0, 10, 0),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
            FontSize = 12,
            Foreground = isSelected ? OffsetSelectedBrush : OffsetBrush,
            LineHeight = LineHeight,
            VerticalAlignment = VerticalAlignment.Center
        };

        Border border = CreateLineBorder(isSelected, isSelectionStartLine, isSelectionEndLine, hasAnchor, hasCurrent, true);
        border.Child = CreateMarkerHost(textBlock, hasAnchor, hasCurrent);
        return border;
    }

    private Border CreateContentRow(int offset, int count, TextBlock textBlock, bool isSelected, bool isSelectionStartLine, bool isSelectionEndLine, bool hasAnchor, bool hasCurrent, bool isOffsetColumn)
    {
        Border border = CreateLineBorder(isSelected, isSelectionStartLine, isSelectionEndLine, hasAnchor, hasCurrent, isOffsetColumn);
        border.Tag = (offset, count);
        border.Child = CreateMarkerHost(textBlock, hasAnchor, hasCurrent);
        return border;
    }

    private Border CreateLineBorder(bool isSelected, bool isSelectionStartLine, bool isSelectionEndLine, bool hasAnchor, bool hasCurrent, bool isOffsetColumn)
    {
        Thickness borderThickness = new(
            isSelected ? 1 : 0,
            isSelectionStartLine ? 2 : 0,
            isSelected ? 1 : 0,
            isSelectionEndLine ? 2 : 0);

        return new Border
        {
            Height = LineHeight,
            Background = isSelected
                ? isOffsetColumn ? OffsetSelectedBackgroundBrush : SelectedLineBackgroundBrush
                : Brushes.Transparent,
            BorderBrush = isSelected
                ? hasAnchor ? AnchorBrush
                : hasCurrent ? CurrentBrush
                : SelectedLineBorderBrush
                : Brushes.Transparent,
            BorderThickness = borderThickness,
            CornerRadius = isSelectionStartLine || isSelectionEndLine ? new CornerRadius(4) : new CornerRadius(0),
            SnapsToDevicePixels = true
        };
    }

    private Grid CreateMarkerHost(UIElement content, bool hasAnchor, bool hasCurrent)
    {
        Grid grid = new()
        {
            ClipToBounds = true
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = hasAnchor ? new GridLength(4) : new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = hasCurrent ? new GridLength(4) : new GridLength(0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (hasAnchor)
        {
            Border anchorMarker = new()
            {
                Background = AnchorBrush
            };
            Grid.SetColumn(anchorMarker, 0);
            grid.Children.Add(anchorMarker);
        }

        if (hasCurrent)
        {
            Border currentMarker = new()
            {
                Background = CurrentBrush
            };
            Grid.SetColumn(currentMarker, 1);
            grid.Children.Add(currentMarker);
        }

        Grid.SetColumn(content, 2);
        grid.Children.Add(content);
        return grid;
    }

    private void HexRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        BeginSelection((IInputElement)sender, mouseButtonEventArgs, SelectionRegion.Hex);
    }

    private void HexRegion_MouseMove(object sender, MouseEventArgs mouseEventArgs)
    {
        UpdateSelection((IInputElement)sender, mouseEventArgs, SelectionRegion.Hex);
    }

    private void HexRegion_MouseLeftButtonUp(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        EndSelection((IInputElement)sender, mouseButtonEventArgs, SelectionRegion.Hex);
    }

    private void HexRegion_LostMouseCapture(object sender, MouseEventArgs mouseEventArgs)
    {
        EndSelection();
    }

    private void AsciiRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        BeginSelection((IInputElement)sender, mouseButtonEventArgs, SelectionRegion.Ascii);
    }

    private void AsciiRegion_MouseMove(object sender, MouseEventArgs mouseEventArgs)
    {
        UpdateSelection((IInputElement)sender, mouseEventArgs, SelectionRegion.Ascii);
    }

    private void AsciiRegion_MouseLeftButtonUp(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        EndSelection((IInputElement)sender, mouseButtonEventArgs, SelectionRegion.Ascii);
    }

    private void AsciiRegion_LostMouseCapture(object sender, MouseEventArgs mouseEventArgs)
    {
        EndSelection();
    }

    private void BeginSelection(IInputElement inputElement, MouseButtonEventArgs mouseButtonEventArgs, SelectionRegion region)
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0 || inputElement is not UIElement element)
        {
            return;
        }

        element.Focus();
        int index = GetByteIndex(inputElement, mouseButtonEventArgs, region);
        bool extendSelection = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && HasSelection;
        _preferredRegion = region;
        if (extendSelection)
        {
            int anchor = _selectionAnchor ?? _selectionStart ?? index;
            _selectionAnchor = anchor;
            _selectionStart = Math.Min(anchor, index);
            _selectionEnd = Math.Max(anchor, index);
        }
        else
        {
            _selectionAnchor = index;
            _selectionStart = index;
            _selectionEnd = index;
        }

        _activeRegion = region;
        _selectionCaptureElement = element;
        element.CaptureMouse();
        StartAutoScroll();
        RenderHex();
    }

    private void UpdateSelection(IInputElement inputElement, MouseEventArgs mouseEventArgs, SelectionRegion region)
    {
        if (_selectionAnchor is null || _activeRegion != region || inputElement is not UIElement element || !element.IsMouseCaptured)
        {
            return;
        }

        int index = GetByteIndex(inputElement, mouseEventArgs, region);
        int newStart = Math.Min(_selectionAnchor.Value, index);
        int newEnd = Math.Max(_selectionAnchor.Value, index);
        if (_selectionStart == newStart && _selectionEnd == newEnd)
        {
            return;
        }

        _selectionStart = newStart;
        _selectionEnd = newEnd;
        RenderHex();
    }

    private void EndSelection(IInputElement inputElement, MouseButtonEventArgs mouseButtonEventArgs, SelectionRegion region)
    {
        if (_selectionAnchor is null || _activeRegion != region || inputElement is not UIElement element)
        {
            return;
        }

        int index = GetByteIndex(inputElement, mouseButtonEventArgs, region);
        _selectionStart = Math.Min(_selectionAnchor.Value, index);
        _selectionEnd = Math.Max(_selectionAnchor.Value, index);
        element.ReleaseMouseCapture();
        _activeRegion = SelectionRegion.None;
        _selectionCaptureElement = null;
        StopAutoScroll();
        RenderHex();
    }

    private void EndSelection()
    {
        _activeRegion = SelectionRegion.None;
        _selectionCaptureElement = null;
        StopAutoScroll();
    }

    private int GetByteIndex(IInputElement inputElement, MouseEventArgs mouseEventArgs, SelectionRegion region)
    {
        return GetByteIndex(inputElement, mouseEventArgs.GetPosition(inputElement), region);
    }

    private int GetByteIndex(IInputElement inputElement, Point point, SelectionRegion region)
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return 0;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, CalculateBytesPerLine());
        int lineCount = (int)Math.Ceiling(bytes.Length / (double)bytesPerLine);

        double topPadding = region == SelectionRegion.Hex ? HexPaddingTop : AsciiPaddingTop;
        double leftPadding = region == SelectionRegion.Hex ? HexPaddingLeft : AsciiPaddingLeft;
        double step = region == SelectionRegion.Hex ? MeasureCharacterWidth() * 3 : MeasureCharacterWidth();
        int line = Math.Clamp((int)Math.Floor((point.Y - topPadding) / LineHeight), 0, Math.Max(lineCount - 1, 0));
        int lineStart = line * bytesPerLine;
        int bytesInLine = Math.Min(bytesPerLine, bytes.Length - lineStart);

        int column = Math.Clamp((int)Math.Floor((point.X - leftPadding + (step * 0.35)) / Math.Max(step, 1)), 0, Math.Max(bytesInLine - 1, 0));
        return Math.Clamp(lineStart + column, 0, bytes.Length - 1);
    }

    private IEnumerable<Segment> EnumerateSegments(int lineLength, int lineOffset, int headerCount)
    {
        List<int> boundaries = new() { 0, lineLength, headerCount };
        if (_selectionStart is int selectionStart && _selectionEnd is int selectionEnd)
        {
            int selectedStart = Math.Max(selectionStart - lineOffset, 0);
            int selectedEnd = Math.Min(selectionEnd - lineOffset + 1, lineLength);
            if (selectedStart > 0 && selectedStart < lineLength)
            {
                boundaries.Add(selectedStart);
            }

            if (selectedEnd > 0 && selectedEnd < lineLength)
            {
                boundaries.Add(selectedEnd);
            }
        }

        boundaries = boundaries.Distinct().Where(static value => value >= 0).OrderBy(static value => value).ToList();
        for (int index = 0; index < boundaries.Count - 1; index++)
        {
            int start = boundaries[index];
            int end = boundaries[index + 1];
            if (end <= start)
            {
                continue;
            }

            bool isHeader = start < headerCount;
            bool isSelected = _selectionStart is int currentSelectionStart &&
                              _selectionEnd is int currentSelectionEnd &&
                              lineOffset + start <= currentSelectionEnd &&
                              lineOffset + end - 1 >= currentSelectionStart;
            yield return new Segment(start, end, isHeader, isSelected);
        }
    }

    private bool TryHandleNavigationKey(KeyEventArgs keyEventArgs)
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return false;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, CalculateBytesPerLine());
        int current = GetKeyboardCurrentIndex();
        int target = current;

        switch (keyEventArgs.Key)
        {
            case Key.Left:
                target = Math.Max(0, current - 1);
                break;
            case Key.Right:
                target = Math.Min(bytes.Length - 1, current + 1);
                break;
            case Key.Up:
                target = Math.Max(0, current - bytesPerLine);
                break;
            case Key.Down:
                target = Math.Min(bytes.Length - 1, current + bytesPerLine);
                break;
            case Key.Home:
                target = (current / bytesPerLine) * bytesPerLine;
                break;
            case Key.End:
            {
                int lineStart = (current / bytesPerLine) * bytesPerLine;
                target = Math.Min(bytes.Length - 1, lineStart + bytesPerLine - 1);
                break;
            }
            default:
                return false;
        }

        ApplyKeyboardSelection(target, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        return true;
    }

    private void ApplyKeyboardSelection(int target, bool extendSelection)
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return;
        }

        int clampedTarget = Math.Clamp(target, 0, bytes.Length - 1);
        if (extendSelection)
        {
            int anchor = _selectionAnchor ?? _selectionStart ?? clampedTarget;
            _selectionAnchor = anchor;
            _selectionStart = Math.Min(anchor, clampedTarget);
            _selectionEnd = Math.Max(anchor, clampedTarget);
        }
        else
        {
            _selectionAnchor = clampedTarget;
            _selectionStart = clampedTarget;
            _selectionEnd = clampedTarget;
        }

        _activeRegion = SelectionRegion.None;
        _selectionCaptureElement = null;
        StopAutoScroll();
        RenderHex();
        EnsureByteVisible(clampedTarget, _preferredRegion);
        Focus();
    }

    private int GetKeyboardCurrentIndex()
    {
        if (HasSelection)
        {
            return GetCurrentEdgeIndex();
        }

        if (_selectionAnchor is int anchor)
        {
            return anchor;
        }

        return 0;
    }

    private string BuildSummaryText(int length, int bytesPerLine)
    {
        if (!HasSelection)
        {
            return $"{length:N0} Bytes · 每行 {bytesPerLine} Bytes";
        }

        return $"{length:N0} Bytes · 每行 {bytesPerLine} Bytes · 已选中 {GetSelectedBytes().Length:N0} Bytes";
    }

    private string BuildSelectionInfoText()
    {
        if (!HasSelection)
        {
            return "";
        }

        int start = _selectionStart!.Value;
        int end = _selectionEnd!.Value;
        int anchor = _selectionAnchor ?? start;
        int current = GetCurrentEdgeIndex();
        return $"锚点 {anchor:X8} · 当前 {current:X8} · 选区 {start:X8}-{end:X8} · 长度 {GetSelectedBytes().Length:N0} Bytes · {GetSelectionScopeText(start, end)}"
            + (_copyFeedbackText is null ? "" : $" · {_copyFeedbackText}");
    }

    private byte[] GetSelectedBytes()
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (!HasSelection)
        {
            return Array.Empty<byte>();
        }

        int start = _selectionStart!.Value;
        int end = _selectionEnd!.Value;
        int count = end - start + 1;
        byte[] result = new byte[count];
        Buffer.BlockCopy(bytes, start, result, 0, count);
        return result;
    }

    private bool HasSelection => _selectionStart is int start && _selectionEnd is int end && end >= start;

    private bool IsLineSelected(int offset, int count)
    {
        return _selectionStart is int start &&
               _selectionEnd is int end &&
               offset <= end &&
               offset + count - 1 >= start;
    }

    private bool IsSelectionStartLine(int offset, int count)
    {
        return _selectionStart is int start &&
               start >= offset &&
               start < offset + count;
    }

    private bool IsSelectionEndLine(int offset, int count)
    {
        return _selectionEnd is int end &&
               end >= offset &&
               end < offset + count;
    }

    private bool IsAnchorLine(int offset, int count)
    {
        return _selectionAnchor is int anchor &&
               anchor >= offset &&
               anchor < offset + count;
    }

    private bool IsCurrentEdgeLine(int offset, int count)
    {
        int current = GetCurrentEdgeIndex();
        return current >= offset &&
               current < offset + count;
    }

    private void ClearSelection()
    {
        if (!HasSelection)
        {
            return;
        }

        _selectionAnchor = null;
        _selectionStart = null;
        _selectionEnd = null;
        _activeRegion = SelectionRegion.None;
        _selectionCaptureElement = null;
        StopAutoScroll();
        RenderHex();
    }

    private void SelectAll()
    {
        byte[] bytes = Bytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return;
        }

        _selectionAnchor = 0;
        _selectionStart = 0;
        _selectionEnd = bytes.Length - 1;
        _activeRegion = SelectionRegion.None;
        _selectionCaptureElement = null;
        StopAutoScroll();
        Focus();
        RenderHex();
    }

    private string GetSelectionScopeText(int start, int end)
    {
        if (end < HeaderLength)
        {
            return "范围：请求头";
        }

        if (start >= HeaderLength)
        {
            return "范围：正文";
        }

        return "范围：头体混合";
    }

    private int GetCurrentEdgeIndex()
    {
        if (!HasSelection)
        {
            return 0;
        }

        if (_selectionAnchor is not int anchor)
        {
            return _selectionEnd ?? 0;
        }

        if (_selectionStart == anchor)
        {
            return _selectionEnd ?? anchor;
        }

        return _selectionStart ?? anchor;
    }

    private void EnsureByteVisible(int index, SelectionRegion region)
    {
        if (BodyScrollViewer is null)
        {
            return;
        }

        int bytesPerLine = Math.Max(_lastBytesPerLine, CalculateBytesPerLine());
        int lineIndex = index / bytesPerLine;
        int columnIndex = index % bytesPerLine;

        double lineTop = 10 + (lineIndex * LineHeight);
        double lineBottom = lineTop + LineHeight;
        if (lineTop < BodyScrollViewer.VerticalOffset)
        {
            BodyScrollViewer.ScrollToVerticalOffset(lineTop);
        }
        else if (lineBottom > BodyScrollViewer.VerticalOffset + BodyScrollViewer.ViewportHeight)
        {
            BodyScrollViewer.ScrollToVerticalOffset(Math.Max(0, lineBottom - BodyScrollViewer.ViewportHeight));
        }

        double charWidth = MeasureCharacterWidth();
        double offsetWidth = OffsetColumn.ActualWidth > 0 ? OffsetColumn.ActualWidth : OffsetColumn.Width.Value;
        double hexWidth = HexColumn.ActualWidth > 0 ? HexColumn.ActualWidth : HexColumn.Width.Value;
        double byteLeft;
        double byteWidth;

        if (region == SelectionRegion.Ascii)
        {
            byteLeft = offsetWidth + 1 + hexWidth + 1 + AsciiPaddingLeft + (columnIndex * charWidth);
            byteWidth = Math.Max(charWidth, 12);
        }
        else
        {
            byteLeft = offsetWidth + 1 + HexPaddingLeft + (columnIndex * charWidth * 3);
            byteWidth = Math.Max(charWidth * 3, 18);
        }

        if (byteLeft < BodyScrollViewer.HorizontalOffset)
        {
            BodyScrollViewer.ScrollToHorizontalOffset(Math.Max(0, byteLeft - 16));
        }
        else if (byteLeft + byteWidth > BodyScrollViewer.HorizontalOffset + BodyScrollViewer.ViewportWidth)
        {
            BodyScrollViewer.ScrollToHorizontalOffset(Math.Max(0, byteLeft + byteWidth - BodyScrollViewer.ViewportWidth + 16));
        }
    }

    private void AutoScrollTimer_Tick(object? sender, EventArgs eventArgs)
    {
        if (_activeRegion == SelectionRegion.None || _selectionCaptureElement is null || BodyScrollViewer is null)
        {
            StopAutoScroll();
            return;
        }

        Point point = Mouse.GetPosition(BodyScrollViewer);
        double verticalDelta = CalculateScrollDelta(point.Y, BodyScrollViewer.ViewportHeight, BodyScrollViewer.ScrollableHeight);
        double horizontalDelta = CalculateScrollDelta(point.X, BodyScrollViewer.ViewportWidth, BodyScrollViewer.ScrollableWidth);

        bool scrolled = false;
        if (verticalDelta != 0)
        {
            double nextVertical = Math.Clamp(BodyScrollViewer.VerticalOffset + verticalDelta, 0, BodyScrollViewer.ScrollableHeight);
            if (!nextVertical.Equals(BodyScrollViewer.VerticalOffset))
            {
                BodyScrollViewer.ScrollToVerticalOffset(nextVertical);
                scrolled = true;
            }
        }

        if (horizontalDelta != 0)
        {
            double nextHorizontal = Math.Clamp(BodyScrollViewer.HorizontalOffset + horizontalDelta, 0, BodyScrollViewer.ScrollableWidth);
            if (!nextHorizontal.Equals(BodyScrollViewer.HorizontalOffset))
            {
                BodyScrollViewer.ScrollToHorizontalOffset(nextHorizontal);
                scrolled = true;
            }
        }

        if (!scrolled)
        {
            return;
        }

        UpdateSelectionFromMouse();
    }

    private void StartAutoScroll()
    {
        if (_autoScrollTimer.IsEnabled)
        {
            return;
        }

        _autoScrollTimer.Start();
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTimer.IsEnabled)
        {
            _autoScrollTimer.Stop();
        }
    }

    private void UpdateSelectionFromMouse()
    {
        if (_selectionAnchor is null || _selectionCaptureElement is null)
        {
            return;
        }

        int index = GetByteIndexFromCurrentMouse(_selectionCaptureElement, _activeRegion);
        int newStart = Math.Min(_selectionAnchor.Value, index);
        int newEnd = Math.Max(_selectionAnchor.Value, index);
        if (_selectionStart == newStart && _selectionEnd == newEnd)
        {
            return;
        }

        _selectionStart = newStart;
        _selectionEnd = newEnd;
        RenderHex();
    }

    private int GetByteIndexFromCurrentMouse(IInputElement inputElement, SelectionRegion region)
    {
        return GetByteIndex(inputElement, Mouse.GetPosition(inputElement), region);
    }

    private double CalculateScrollDelta(double position, double viewport, double scrollable)
    {
        if (scrollable <= 0 || viewport <= 0)
        {
            return 0;
        }

        if (position < AutoScrollThreshold)
        {
            double ratio = (AutoScrollThreshold - Math.Max(position, 0)) / AutoScrollThreshold;
            return -Math.Ceiling(AutoScrollMaxStep * Math.Clamp(ratio, 0, 1));
        }

        double distanceToEnd = viewport - position;
        if (distanceToEnd < AutoScrollThreshold)
        {
            double ratio = (AutoScrollThreshold - Math.Max(distanceToEnd, 0)) / AutoScrollThreshold;
            return Math.Ceiling(AutoScrollMaxStep * Math.Clamp(ratio, 0, 1));
        }

        return 0;
    }

    private void CoerceSelection(int length)
    {
        if (length <= 0)
        {
            _selectionAnchor = null;
            _selectionStart = null;
            _selectionEnd = null;
            _activeRegion = SelectionRegion.None;
            return;
        }

        if (_selectionAnchor is int anchor)
        {
            _selectionAnchor = Math.Clamp(anchor, 0, length - 1);
        }

        if (_selectionStart is int selectionStart)
        {
            _selectionStart = Math.Clamp(selectionStart, 0, length - 1);
        }

        if (_selectionEnd is int selectionEnd)
        {
            _selectionEnd = Math.Clamp(selectionEnd, 0, length - 1);
        }

        if (_selectionStart is int coercedStart && _selectionEnd is int coercedEnd && coercedEnd < coercedStart)
        {
            (_selectionStart, _selectionEnd) = (coercedEnd, coercedStart);
        }
    }

    private double MeasureCharacterWidth()
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Typeface typeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText text = new("0",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            12,
            Brushes.Black,
            pixelsPerDip);
        return text.WidthIncludingTrailingWhitespace;
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes, int startIndex = 0)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        StringBuilder builder = new(bytes.Length * 3);
        for (int index = 0; index < bytes.Length; index++)
        {
            if (startIndex + index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[index].ToString("X2"));
        }

        return builder.ToString();
    }

    private static string ToVisibleText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        StringBuilder builder = new(bytes.Length);
        for (int index = 0; index < bytes.Length; index++)
        {
            byte current = bytes[index];
            builder.Append(current is >= 32 and <= 126 ? (char)current : '.');
        }

        return builder.ToString();
    }

    private bool CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ShowCopyFeedback(string text)
    {
        _copyFeedbackText = text;
        _copyFeedbackTimer.Stop();
        _copyFeedbackTimer.Start();
        RenderHex();
    }

    private void StopCopyFeedback()
    {
        _copyFeedbackTimer.Stop();
        _copyFeedbackText = null;
    }

    private void CopyFeedbackTimer_Tick(object? sender, EventArgs eventArgs)
    {
        StopCopyFeedback();
        RenderHex();
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private readonly record struct Segment(int Start, int End, bool IsHeader, bool IsSelected);

    private enum SelectionRegion
    {
        None,
        Hex,
        Ascii
    }
}
