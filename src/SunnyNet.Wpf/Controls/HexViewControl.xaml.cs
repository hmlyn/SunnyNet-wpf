using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Controls;

public partial class HexViewControl : UserControl
{
    public static readonly DependencyProperty BytesProperty =
        DependencyProperty.Register(nameof(Bytes), typeof(byte[]), typeof(HexViewControl), new PropertyMetadata(Array.Empty<byte>(), OnHexDataChanged));

    public static readonly DependencyProperty HeaderLengthProperty =
        DependencyProperty.Register(nameof(HeaderLength), typeof(int), typeof(HexViewControl), new PropertyMetadata(0, OnHexDataChanged));

    public static readonly DependencyProperty VirtualSourceProperty =
        DependencyProperty.Register(nameof(VirtualSource), typeof(HexVirtualDataSource), typeof(HexViewControl), new PropertyMetadata(null, OnVirtualSourceChanged));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(HexViewControl), new PropertyMetadata("无十六进制数据", OnEmptyTextChanged));

    public static readonly DependencyProperty EnableBodyCopyActionsProperty =
        DependencyProperty.Register(nameof(EnableBodyCopyActions), typeof(bool), typeof(HexViewControl), new PropertyMetadata(false));

    private const double LineHeight = 22;
    private const double TopPadding = 10;
    private const double BottomPadding = 10;
    private const double OffsetWidth = 92;
    private const double DividerWidth = 1;
    private const double OffsetTextLeft = 10;
    private const double HexPaddingLeft = 12;
    private const double AsciiPaddingLeft = 16;
    private const int MinBytesPerLine = 8;
    private const int MaxBytesPerLine = 32;
    private const int VirtualPageSize = 64 * 1024;
    private const int CopyReadLimit = 16 * 1024 * 1024;

    private static readonly Brush OffsetBrush = CreateBrush(0x6B, 0x7C, 0x93);
    private static readonly Brush OffsetSelectedBrush = CreateBrush(0x1E, 0x63, 0xD6);
    private static readonly Brush OffsetBackgroundBrush = CreateBrush(0xF6, 0xF8, 0xFC);
    private static readonly Brush DividerBrush = CreateBrush(0xE7, 0xEC, 0xF3);
    private static readonly Brush HexHeaderBrush = CreateBrush(0x1D, 0x4E, 0x89);
    private static readonly Brush HexBodyBrush = CreateBrush(0x0F, 0x5E, 0x75);
    private static readonly Brush TextHeaderBrush = CreateBrush(0x5F, 0x6F, 0x82);
    private static readonly Brush TextBodyBrush = CreateBrush(0x19, 0x7A, 0x4D);
    private static readonly Brush SelectedBackgroundBrush = CreateBrush(0x2F, 0x7C, 0xF6);
    private static readonly Brush SelectedForegroundBrush = Brushes.White;
    private static readonly Brush SelectedLineBackgroundBrush = CreateBrush(0xF3, 0xF8, 0xFF);
    private static readonly Brush SelectedLineBorderBrush = CreateBrush(0x8F, 0xB8, 0xF8);
    private static readonly Brush AnchorBrush = CreateBrush(0x8B, 0x5C, 0xD6);
    private static readonly Brush CurrentBrush = CreateBrush(0xF5, 0x9E, 0x0B);
    private static readonly Typeface MonoTypeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private int _bytesPerLine = 16;
    private double _charWidth = 7.2;
    private double _hexByteWidth = 21.6;
    private double _hexColumnWidth = 420;
    private double _asciiColumnWidth = 220;
    private int? _selectionAnchor;
    private int? _selectionStart;
    private int? _selectionEnd;
    private SelectionRegion _activeRegion;
    private SelectionRegion _preferredRegion = SelectionRegion.Hex;
    private string? _copyFeedbackText;
    private int _virtualVersion;
    private readonly Dictionary<int, byte[]> _virtualPages = new();
    private readonly HashSet<int> _loadingVirtualPages = new();
    private readonly System.Windows.Threading.DispatcherTimer _copyFeedbackTimer;

    public HexViewControl()
    {
        InitializeComponent();
        RenderSurface.Owner = this;
        RenderSurface.MouseLeftButtonDown += RenderSurface_MouseLeftButtonDown;
        RenderSurface.MouseMove += RenderSurface_MouseMove;
        RenderSurface.MouseLeftButtonUp += RenderSurface_MouseLeftButtonUp;
        RenderSurface.LostMouseCapture += RenderSurface_LostMouseCapture;
        _copyFeedbackTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1600)
        };
        _copyFeedbackTimer.Tick += CopyFeedbackTimer_Tick;
        Loaded += (_, _) => RefreshLayout();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshLayout();
            }
        };
        Unloaded += (_, _) => StopCopyFeedback();
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

    public HexVirtualDataSource? VirtualSource
    {
        get => (HexVirtualDataSource?)GetValue(VirtualSourceProperty);
        set => SetValue(VirtualSourceProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public bool EnableBodyCopyActions
    {
        get => (bool)GetValue(EnableBodyCopyActionsProperty);
        set => SetValue(EnableBodyCopyActionsProperty, value);
    }

    internal byte[] RenderBytes => Bytes ?? Array.Empty<byte>();

    internal int RenderLength => ActiveVirtualSource?.TotalLength ?? RenderBytes.Length;

    internal int RenderHeaderLength => ActiveVirtualSource?.HeaderLength ?? HeaderLength;

    internal int BytesPerLine => _bytesPerLine;

    internal double RenderLineHeight => LineHeight;

    internal double RenderTopPadding => TopPadding;

    internal double RenderOffsetWidth => OffsetWidth;

    internal double RenderHexColumnWidth => _hexColumnWidth;

    internal double RenderAsciiColumnWidth => _asciiColumnWidth;

    internal double RenderHexByteWidth => _hexByteWidth;

    internal double RenderCharWidth => _charWidth;

    internal ScrollViewer ScrollHost => BodyScrollViewer;

    private HexVirtualDataSource? ActiveVirtualSource => VirtualSource?.TotalLength > 0 ? VirtualSource : null;

    private static void OnHexDataChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HexViewControl control)
        {
            control.CoerceSelection();
            control.RefreshLayout();
        }
    }

    private static void OnVirtualSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is HexViewControl control)
        {
            control.ResetVirtualCache();
            control.CoerceSelection();
            control.RefreshLayout();
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
        RefreshLayout();
    }

    private void BodyScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs scrollChangedEventArgs)
    {
        EnsureVisibleVirtualRangeLoaded();
        RenderSurface.InvalidateVisual();
    }

    private void RefreshLayout()
    {
        if (!IsLoaded || RenderSurface is null || SummaryTextBlock is null)
        {
            return;
        }

        int length = RenderLength;
        EmptyTextBlock.Text = EmptyText;
        EmptyPanel.Visibility = length == 0 ? Visibility.Visible : Visibility.Collapsed;

        _charWidth = MeasureCharacterWidth();
        _hexByteWidth = Math.Ceiling(_charWidth * 3);
        _bytesPerLine = CalculateBytesPerLine();
        _hexColumnWidth = Math.Max(420, Math.Ceiling(_bytesPerLine * _hexByteWidth) + HexPaddingLeft + 18);
        _asciiColumnWidth = Math.Max(220, Math.Ceiling(_bytesPerLine * _charWidth) + AsciiPaddingLeft + 18);

        int lineCount = GetLineCount(length);
        RenderSurface.Width = Math.Max(BodyScrollViewer.ViewportWidth, OffsetWidth + DividerWidth + _hexColumnWidth + DividerWidth + _asciiColumnWidth);
        RenderSurface.Height = Math.Max(BodyScrollViewer.ViewportHeight, TopPadding + BottomPadding + lineCount * LineHeight);
        SummaryTextBlock.Text = BuildSummaryText(length);
        SelectionInfoBorder.Visibility = HasSelection ? Visibility.Visible : Visibility.Collapsed;
        SelectionInfoTextBlock.Text = BuildSelectionInfoText();
        EnsureVisibleVirtualRangeLoaded();
        RenderSurface.InvalidateVisual();
    }

    private int CalculateBytesPerLine()
    {
        double width = BodyScrollViewer?.ViewportWidth > 0 ? BodyScrollViewer.ViewportWidth : ActualWidth;
        if (width <= 0)
        {
            return 16;
        }

        double reserved = OffsetWidth + DividerWidth + DividerWidth + HexPaddingLeft + AsciiPaddingLeft + 70;
        double perByte = _hexByteWidth + _charWidth;
        int bytesPerLine = (int)Math.Floor((width - reserved) / Math.Max(perByte, 1));
        return Math.Clamp(bytesPerLine, MinBytesPerLine, MaxBytesPerLine);
    }

    private int GetLineCount(int length)
    {
        return length == 0 ? 0 : (int)Math.Ceiling(length / (double)Math.Max(_bytesPerLine, 1));
    }

    internal void DrawHex(DrawingContext context)
    {
        int length = RenderLength;
        double width = Math.Max(RenderSurface.ActualWidth, RenderSurface.Width);
        double height = Math.Max(RenderSurface.ActualHeight, RenderSurface.Height);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        context.DrawRectangle(OffsetBackgroundBrush, null, new Rect(0, 0, OffsetWidth, height));
        context.DrawRectangle(DividerBrush, null, new Rect(OffsetWidth, 0, DividerWidth, height));
        context.DrawRectangle(DividerBrush, null, new Rect(GetAsciiStartX() - DividerWidth, 0, DividerWidth, height));

        if (length == 0)
        {
            return;
        }

        int lineCount = GetLineCount(length);
        int firstLine = Math.Clamp((int)Math.Floor((BodyScrollViewer.VerticalOffset - TopPadding) / LineHeight), 0, Math.Max(0, lineCount - 1));
        int lastLine = Math.Clamp((int)Math.Ceiling((BodyScrollViewer.VerticalOffset + BodyScrollViewer.ViewportHeight - TopPadding) / LineHeight), firstLine, Math.Max(0, lineCount - 1));
        EnsureVirtualRangeLoaded(firstLine * _bytesPerLine, Math.Min(length - firstLine * _bytesPerLine, (lastLine - firstLine + 1) * _bytesPerLine));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
        {
            int offset = lineIndex * _bytesPerLine;
            int count = Math.Min(_bytesPerLine, length - offset);
            double y = TopPadding + lineIndex * LineHeight;
            bool lineSelected = IsLineSelected(offset, count);
            bool lineHasAnchor = IsAnchorLine(offset, count);
            bool lineHasCurrent = IsCurrentEdgeLine(offset, count);

            if (lineSelected)
            {
                context.DrawRoundedRectangle(SelectedLineBackgroundBrush, new Pen(SelectedLineBorderBrush, 1), new Rect(OffsetWidth + DividerWidth + 2, y + 1, Math.Max(0, width - OffsetWidth - 4), LineHeight - 2), 4, 4);
            }

            if (lineHasAnchor)
            {
                context.DrawRectangle(AnchorBrush, null, new Rect(OffsetWidth + DividerWidth + 2, y + 3, 3, LineHeight - 6));
            }

            if (lineHasCurrent)
            {
                context.DrawRectangle(CurrentBrush, null, new Rect(OffsetWidth + DividerWidth + 6, y + 3, 3, LineHeight - 6));
            }

            DrawText(context, offset.ToString("X8"), OffsetTextLeft, y + 3, lineSelected ? OffsetSelectedBrush : OffsetBrush, pixelsPerDip, FontWeights.Normal);
            for (int column = 0; column < count; column++)
            {
                int index = offset + column;
                bool selected = IsByteSelected(index);
                bool header = index < RenderHeaderLength;
                double hexX = GetHexStartX() + column * _hexByteWidth;
                double asciiX = GetAsciiStartX() + AsciiPaddingLeft + column * _charWidth;
                if (selected)
                {
                    context.DrawRoundedRectangle(SelectedBackgroundBrush, null, new Rect(hexX - 1, y + 2, Math.Max(_hexByteWidth - 2, 12), LineHeight - 4), 2, 2);
                    context.DrawRoundedRectangle(SelectedBackgroundBrush, null, new Rect(asciiX - 1, y + 2, Math.Max(_charWidth, 8), LineHeight - 4), 2, 2);
                }

                Brush hexBrush = selected ? SelectedForegroundBrush : header ? HexHeaderBrush : HexBodyBrush;
                Brush asciiBrush = selected ? SelectedForegroundBrush : header ? TextHeaderBrush : TextBodyBrush;
                if (TryGetRenderByte(index, out byte value))
                {
                    DrawText(context, value.ToString("X2"), hexX, y + 3, hexBrush, pixelsPerDip, FontWeights.Normal);
                    DrawText(context, ToVisibleChar(value).ToString(), asciiX, y + 3, asciiBrush, pixelsPerDip, FontWeights.Normal);
                }
                else
                {
                    DrawText(context, "??", hexX, y + 3, OffsetBrush, pixelsPerDip, FontWeights.Normal);
                    DrawText(context, "·", asciiX, y + 3, OffsetBrush, pixelsPerDip, FontWeights.Normal);
                }
            }
        }
    }

    private void RenderSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (RenderLength == 0)
        {
            return;
        }

        Focus();
        RenderSurface.Focus();
        SelectionRegion region = GetRegion(mouseButtonEventArgs.GetPosition(RenderSurface));
        if (region == SelectionRegion.None)
        {
            return;
        }

        int index = GetByteIndex(mouseButtonEventArgs.GetPosition(RenderSurface), region);
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
        RenderSurface.CaptureMouse();
        RefreshLayout();
        mouseButtonEventArgs.Handled = true;
    }

    private void RenderSurface_MouseMove(object sender, MouseEventArgs mouseEventArgs)
    {
        if (_activeRegion == SelectionRegion.None || _selectionAnchor is null || !RenderSurface.IsMouseCaptured)
        {
            return;
        }

        int index = GetByteIndex(mouseEventArgs.GetPosition(RenderSurface), _activeRegion);
        int start = Math.Min(_selectionAnchor.Value, index);
        int end = Math.Max(_selectionAnchor.Value, index);
        if (_selectionStart == start && _selectionEnd == end)
        {
            return;
        }

        _selectionStart = start;
        _selectionEnd = end;
        RefreshLayout();
    }

    private void RenderSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (_activeRegion == SelectionRegion.None || _selectionAnchor is null)
        {
            return;
        }

        int index = GetByteIndex(mouseButtonEventArgs.GetPosition(RenderSurface), _activeRegion);
        _selectionStart = Math.Min(_selectionAnchor.Value, index);
        _selectionEnd = Math.Max(_selectionAnchor.Value, index);
        _activeRegion = SelectionRegion.None;
        RenderSurface.ReleaseMouseCapture();
        RefreshLayout();
    }

    private void RenderSurface_LostMouseCapture(object sender, MouseEventArgs mouseEventArgs)
    {
        _activeRegion = SelectionRegion.None;
    }

    private SelectionRegion GetRegion(Point point)
    {
        double hexStart = GetHexStartX();
        double asciiStart = GetAsciiStartX() + AsciiPaddingLeft;
        if (point.X >= hexStart && point.X < hexStart + _bytesPerLine * _hexByteWidth)
        {
            return SelectionRegion.Hex;
        }

        if (point.X >= asciiStart && point.X < asciiStart + _bytesPerLine * _charWidth)
        {
            return SelectionRegion.Ascii;
        }

        return SelectionRegion.None;
    }

    private int GetByteIndex(Point point, SelectionRegion region)
    {
        int length = RenderLength;
        if (length == 0)
        {
            return 0;
        }

        int lineCount = GetLineCount(length);
        int line = Math.Clamp((int)Math.Floor((point.Y - TopPadding) / LineHeight), 0, Math.Max(lineCount - 1, 0));
        int lineStart = line * _bytesPerLine;
        int count = Math.Min(_bytesPerLine, length - lineStart);
        double xStart = region == SelectionRegion.Hex
            ? GetHexStartX()
            : GetAsciiStartX() + AsciiPaddingLeft;
        double xStep = region == SelectionRegion.Hex ? _hexByteWidth : _charWidth;
        int column = Math.Clamp((int)Math.Floor((point.X - xStart + xStep * 0.35) / Math.Max(xStep, 1)), 0, Math.Max(count - 1, 0));
        return Math.Clamp(lineStart + column, 0, length - 1);
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

    private bool TryHandleNavigationKey(KeyEventArgs keyEventArgs)
    {
        int length = RenderLength;
        if (length == 0)
        {
            return false;
        }

        int current = GetKeyboardCurrentIndex();
        int target = current;
        switch (keyEventArgs.Key)
        {
            case Key.Left:
                target = Math.Max(0, current - 1);
                break;
            case Key.Right:
                target = Math.Min(length - 1, current + 1);
                break;
            case Key.Up:
                target = Math.Max(0, current - _bytesPerLine);
                break;
            case Key.Down:
                target = Math.Min(length - 1, current + _bytesPerLine);
                break;
            case Key.Home:
                target = (current / _bytesPerLine) * _bytesPerLine;
                break;
            case Key.End:
                target = Math.Min(length - 1, (current / _bytesPerLine) * _bytesPerLine + _bytesPerLine - 1);
                break;
            default:
                return false;
        }

        ApplyKeyboardSelection(target, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        return true;
    }

    private void ApplyKeyboardSelection(int target, bool extendSelection)
    {
        int length = RenderLength;
        if (length == 0)
        {
            return;
        }

        int clampedTarget = Math.Clamp(target, 0, length - 1);
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

        EnsureByteVisible(clampedTarget, _preferredRegion);
        RefreshLayout();
        Focus();
    }

    private void EnsureByteVisible(int index, SelectionRegion region)
    {
        int line = index / Math.Max(_bytesPerLine, 1);
        double lineTop = TopPadding + line * LineHeight;
        double lineBottom = lineTop + LineHeight;
        if (lineTop < BodyScrollViewer.VerticalOffset)
        {
            BodyScrollViewer.ScrollToVerticalOffset(lineTop);
        }
        else if (lineBottom > BodyScrollViewer.VerticalOffset + BodyScrollViewer.ViewportHeight)
        {
            BodyScrollViewer.ScrollToVerticalOffset(Math.Max(0, lineBottom - BodyScrollViewer.ViewportHeight));
        }

        int column = index % Math.Max(_bytesPerLine, 1);
        double byteLeft = region == SelectionRegion.Ascii
            ? GetAsciiStartX() + AsciiPaddingLeft + column * _charWidth
            : GetHexStartX() + column * _hexByteWidth;
        double byteWidth = region == SelectionRegion.Ascii ? _charWidth : _hexByteWidth;
        if (byteLeft < BodyScrollViewer.HorizontalOffset)
        {
            BodyScrollViewer.ScrollToHorizontalOffset(Math.Max(0, byteLeft - 16));
        }
        else if (byteLeft + byteWidth > BodyScrollViewer.HorizontalOffset + BodyScrollViewer.ViewportWidth)
        {
            BodyScrollViewer.ScrollToHorizontalOffset(Math.Max(0, byteLeft + byteWidth - BodyScrollViewer.ViewportWidth + 16));
        }
    }

    private void HexContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        bool hasSelection = HasSelection;
        CopySelectedHexMenuItem.IsEnabled = hasSelection;
        CopySelectedTextMenuItem.IsEnabled = hasSelection;
        CopySelectedLinesMenuItem.IsEnabled = hasSelection;
        ClearSelectionMenuItem.IsEnabled = hasSelection;
        UpdateBodyCopyMenuItems();
        if (hasSelection)
        {
            int count = GetSelectionLength();
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

    private void UpdateBodyCopyMenuItems()
    {
        int bodyLength = GetBodyLength();
        bool visible = EnableBodyCopyActions && bodyLength > 0;
        bool enabled = visible;
        Visibility visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BodyCopySeparator.Visibility = visibility;
        CopyBodyTextMenuItem.Visibility = visibility;
        CopyBodyHexMenuItem.Visibility = visibility;
        CopyBodyBase64MenuItem.Visibility = visibility;
        CopyBodyTextMenuItem.IsEnabled = enabled;
        CopyBodyHexMenuItem.IsEnabled = enabled;
        CopyBodyBase64MenuItem.IsEnabled = enabled;
        CopyBodyTextMenuItem.Header = enabled ? $"复制 Body 文本 ({bodyLength:N0} Bytes)" : "复制 Body 文本";
        CopyBodyHexMenuItem.Header = enabled ? $"复制 Body HEX ({bodyLength:N0} Bytes)" : "复制 Body HEX";
        CopyBodyBase64MenuItem.Header = enabled ? $"复制 Body Base64 ({bodyLength:N0} Bytes)" : "复制 Body Base64";
    }

    private async void CopyAllHex_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        int length = RenderLength;
        if (length == 0)
        {
            return;
        }

        byte[] bytes = await ReadBytesForCopyAsync(0, length);
        if (bytes.Length > 0 && CopyText(BuildHexLines(bytes, 0, bytes.Length, includeOffset: false, includeAscii: false)))
        {
            ShowCopyFeedback($"已复制全部 HEX · {length:N0} Bytes");
        }
    }

    private async void CopyAllText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        int length = RenderLength;
        if (length == 0)
        {
            return;
        }

        byte[] bytes = await ReadBytesForCopyAsync(0, length);
        if (bytes.Length > 0 && CopyText(ToVisibleText(bytes)))
        {
            ShowCopyFeedback($"已复制全部文本 · {length:N0} Bytes");
        }
    }

    private async void CopyAllLines_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        int length = RenderLength;
        if (length == 0)
        {
            return;
        }

        byte[] bytes = await ReadBytesForCopyAsync(0, length);
        if (bytes.Length > 0 && CopyText(BuildHexLines(bytes, 0, bytes.Length, includeOffset: true, includeAscii: true)))
        {
            ShowCopyFeedback($"已复制全部内容 · {length:N0} Bytes");
        }
    }

    private async void CopyBodyText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] body = await ReadBodyBytesForCopyAsync();
        if (body.Length == 0)
        {
            return;
        }

        if (CopyText(DecodeBodyText(body)))
        {
            ShowCopyFeedback($"已复制 Body 文本 · {body.Length:N0} Bytes");
        }
    }

    private async void CopyBodyHex_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] body = await ReadBodyBytesForCopyAsync();
        if (body.Length == 0)
        {
            return;
        }

        if (CopyText(BuildHexLines(body, 0, body.Length, includeOffset: false, includeAscii: false)))
        {
            ShowCopyFeedback($"已复制 Body HEX · {body.Length:N0} Bytes");
        }
    }

    private async void CopyBodyBase64_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] body = await ReadBodyBytesForCopyAsync();
        if (body.Length == 0)
        {
            return;
        }

        if (CopyText(Convert.ToBase64String(body)))
        {
            ShowCopyFeedback($"已复制 Body Base64 · {body.Length:N0} Bytes");
        }
    }

    private async void CopySelectedHex_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        (int start, int count) = GetSelectionRange();
        if (count <= 0)
        {
            return;
        }

        byte[] selection = await ReadBytesForCopyAsync(start, count);
        if (selection.Length > 0 && CopyText(BuildHexLines(selection, 0, selection.Length, includeOffset: false, includeAscii: false, displayOffset: start)))
        {
            ShowCopyFeedback($"已复制选中 HEX · {count:N0} Bytes");
        }
    }

    private async void CopySelectedText_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        (int start, int count) = GetSelectionRange();
        byte[] selection = await ReadBytesForCopyAsync(start, count);
        if (selection.Length == 0)
        {
            return;
        }

        if (CopyText(ToVisibleText(selection)))
        {
            ShowCopyFeedback($"已复制选中文本 · {selection.Length:N0} Bytes");
        }
    }

    private async void CopySelectedLines_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        (int start, int count) = GetSelectionRange();
        if (count <= 0)
        {
            return;
        }

        byte[] selection = await ReadBytesForCopyAsync(start, count);
        if (selection.Length > 0 && CopyText(BuildHexLines(selection, 0, selection.Length, includeOffset: true, includeAscii: true, displayOffset: start)))
        {
            ShowCopyFeedback($"已复制选中全部 · {count:N0} Bytes");
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ClearSelection();
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
        RefreshLayout();
    }

    private void SelectAll()
    {
        int length = RenderLength;
        if (length == 0)
        {
            return;
        }

        _selectionAnchor = 0;
        _selectionStart = 0;
        _selectionEnd = length - 1;
        RefreshLayout();
    }

    private void CoerceSelection()
    {
        int length = RenderLength;
        if (length == 0)
        {
            _selectionAnchor = null;
            _selectionStart = null;
            _selectionEnd = null;
            return;
        }

        if (_selectionAnchor is int anchor)
        {
            _selectionAnchor = Math.Clamp(anchor, 0, length - 1);
        }

        if (_selectionStart is int start)
        {
            _selectionStart = Math.Clamp(start, 0, length - 1);
        }

        if (_selectionEnd is int end)
        {
            _selectionEnd = Math.Clamp(end, 0, length - 1);
        }
    }

    private bool HasSelection => _selectionStart is int start && _selectionEnd is int end && end >= start;

    private bool IsByteSelected(int index)
    {
        return _selectionStart is int start && _selectionEnd is int end && index >= start && index <= end;
    }

    private bool IsLineSelected(int offset, int count)
    {
        return _selectionStart is int start && _selectionEnd is int end && offset <= end && offset + count - 1 >= start;
    }

    private bool IsAnchorLine(int offset, int count)
    {
        return _selectionAnchor is int anchor && anchor >= offset && anchor < offset + count;
    }

    private bool IsCurrentEdgeLine(int offset, int count)
    {
        int current = GetCurrentEdgeIndex();
        return current >= offset && current < offset + count;
    }

    private void ResetVirtualCache()
    {
        _virtualVersion++;
        _virtualPages.Clear();
        _loadingVirtualPages.Clear();
    }

    private bool TryGetRenderByte(int index, out byte value)
    {
        HexVirtualDataSource? source = ActiveVirtualSource;
        if (source is null)
        {
            byte[] bytes = RenderBytes;
            if (index >= 0 && index < bytes.Length)
            {
                value = bytes[index];
                return true;
            }

            value = 0;
            return false;
        }

        int pageIndex = index / VirtualPageSize;
        int pageOffset = pageIndex * VirtualPageSize;
        if (_virtualPages.TryGetValue(pageIndex, out byte[]? page))
        {
            int relative = index - pageOffset;
            if (relative >= 0 && relative < page.Length)
            {
                value = page[relative];
                return true;
            }
        }

        EnsureVirtualPageLoaded(source, pageIndex);
        value = 0;
        return false;
    }

    private void EnsureVisibleVirtualRangeLoaded()
    {
        if (ActiveVirtualSource is null || BodyScrollViewer is null || _bytesPerLine <= 0)
        {
            return;
        }

        int length = RenderLength;
        if (length <= 0)
        {
            return;
        }

        int firstLine = Math.Clamp((int)Math.Floor((BodyScrollViewer.VerticalOffset - TopPadding) / LineHeight), 0, Math.Max(0, GetLineCount(length) - 1));
        int visibleLines = Math.Max(1, (int)Math.Ceiling(BodyScrollViewer.ViewportHeight / LineHeight) + 4);
        EnsureVirtualRangeLoaded(firstLine * _bytesPerLine, Math.Min(length - firstLine * _bytesPerLine, visibleLines * _bytesPerLine));
    }

    private void EnsureVirtualRangeLoaded(int start, int count)
    {
        HexVirtualDataSource? source = ActiveVirtualSource;
        if (source is null || count <= 0 || start < 0)
        {
            return;
        }

        int end = Math.Min(source.TotalLength, start + count);
        int firstPage = start / VirtualPageSize;
        int lastPage = Math.Max(firstPage, (end - 1) / VirtualPageSize);
        for (int pageIndex = firstPage; pageIndex <= lastPage; pageIndex++)
        {
            EnsureVirtualPageLoaded(source, pageIndex);
        }
    }

    private void EnsureVirtualPageLoaded(HexVirtualDataSource source, int pageIndex)
    {
        if (_virtualPages.ContainsKey(pageIndex) || _loadingVirtualPages.Contains(pageIndex))
        {
            return;
        }

        int offset = pageIndex * VirtualPageSize;
        if (offset >= source.TotalLength)
        {
            return;
        }

        int count = Math.Min(VirtualPageSize, source.TotalLength - offset);
        int version = _virtualVersion;
        _loadingVirtualPages.Add(pageIndex);
        _ = LoadVirtualPageAsync(source, pageIndex, offset, count, version);
    }

    private async Task LoadVirtualPageAsync(HexVirtualDataSource source, int pageIndex, int offset, int count, int version)
    {
        byte[] data;
        try
        {
            data = await source.ReadRangeAsync(offset, count, CancellationToken.None);
        }
        catch
        {
            data = Array.Empty<byte>();
        }

        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(() => ApplyVirtualPage(source, pageIndex, data, version));
            return;
        }

        ApplyVirtualPage(source, pageIndex, data, version);
    }

    private void ApplyVirtualPage(HexVirtualDataSource source, int pageIndex, byte[] data, int version)
    {
        _loadingVirtualPages.Remove(pageIndex);
        if (version != _virtualVersion || !ReferenceEquals(source, ActiveVirtualSource))
        {
            return;
        }

        _virtualPages[pageIndex] = data;
        SummaryTextBlock.Text = BuildSummaryText(RenderLength);
        RenderSurface.InvalidateVisual();
    }

    private int GetKeyboardCurrentIndex()
    {
        if (HasSelection)
        {
            return GetCurrentEdgeIndex();
        }

        return _selectionAnchor ?? 0;
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

        return _selectionStart == anchor ? _selectionEnd ?? anchor : _selectionStart ?? anchor;
    }

    private int GetSelectionLength()
    {
        (int _, int count) = GetSelectionRange();
        return count;
    }

    private (int Start, int Count) GetSelectionRange()
    {
        if (!HasSelection)
        {
            return (0, 0);
        }

        int start = _selectionStart!.Value;
        int end = _selectionEnd!.Value;
        return (start, end - start + 1);
    }

    private byte[] GetSelectedBytes()
    {
        byte[] bytes = RenderBytes;
        (int start, int count) = GetSelectionRange();
        if (count <= 0)
        {
            return Array.Empty<byte>();
        }

        byte[] result = new byte[count];
        Buffer.BlockCopy(bytes, start, result, 0, count);
        return result;
    }

    private async Task<byte[]> ReadBytesForCopyAsync(int start, int count)
    {
        if (count <= 0 || start < 0)
        {
            return Array.Empty<byte>();
        }

        if (count > CopyReadLimit)
        {
            ShowCopyFeedback($"数据过大，请缩小选区后复制 · {count:N0} Bytes");
            return Array.Empty<byte>();
        }

        HexVirtualDataSource? source = ActiveVirtualSource;
        if (source is not null)
        {
            ShowCopyFeedback("正在读取选区...");
            return await source.ReadRangeAsync(start, count, CancellationToken.None);
        }

        byte[] bytes = RenderBytes;
        if (start >= bytes.Length)
        {
            return Array.Empty<byte>();
        }

        int safeCount = Math.Min(count, bytes.Length - start);
        byte[] result = new byte[safeCount];
        Buffer.BlockCopy(bytes, start, result, 0, safeCount);
        return result;
    }

    private int GetBodyStart()
    {
        return Math.Clamp(RenderHeaderLength, 0, RenderLength);
    }

    private int GetBodyLength()
    {
        int bodyStart = GetBodyStart();
        return Math.Max(0, RenderLength - bodyStart);
    }

    private byte[] GetBodyBytes()
    {
        byte[] bytes = RenderBytes;
        int bodyStart = GetBodyStart();
        int bodyLength = Math.Max(0, bytes.Length - bodyStart);
        if (bodyLength <= 0)
        {
            return Array.Empty<byte>();
        }

        byte[] result = new byte[bodyLength];
        Buffer.BlockCopy(bytes, bodyStart, result, 0, bodyLength);
        return result;
    }

    private Task<byte[]> ReadBodyBytesForCopyAsync()
    {
        int bodyStart = GetBodyStart();
        int bodyLength = GetBodyLength();
        return ReadBytesForCopyAsync(bodyStart, bodyLength);
    }

    private string BuildSummaryText(int length)
    {
        string text = length == 0
            ? "HEX 数据"
            : ActiveVirtualSource is null
                ? $"{length:N0} Bytes · 每行 {_bytesPerLine} Bytes"
                : $"{length:N0} Bytes · 每行 {_bytesPerLine} Bytes · 分页加载";
        if (HasSelection)
        {
            text += $" · 已选中 {GetSelectionLength():N0} Bytes";
        }

        return _copyFeedbackText is null ? text : $"{text} · {_copyFeedbackText}";
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
        return $"锚点 {anchor:X8} · 当前 {current:X8} · 选区 {start:X8}-{end:X8} · 长度 {GetSelectionLength():N0} Bytes · {GetSelectionScopeText(start, end)}";
    }

    private string GetSelectionScopeText(int start, int end)
    {
        if (end < RenderHeaderLength)
        {
            return "范围：请求头";
        }

        if (start >= RenderHeaderLength)
        {
            return "范围：正文";
        }

        return "范围：头体混合";
    }

    private string BuildHexLines(byte[] bytes, int start, int count, bool includeOffset, bool includeAscii, int displayOffset = 0)
    {
        if (bytes.Length == 0 || count <= 0)
        {
            return "";
        }

        int end = Math.Min(bytes.Length, start + count);
        int bytesPerLine = Math.Max(_bytesPerLine, 16);
        StringBuilder builder = new();
        for (int offset = start; offset < end; offset += bytesPerLine)
        {
            int lineCount = Math.Min(bytesPerLine, end - offset);
            ReadOnlySpan<byte> line = bytes.AsSpan(offset, lineCount);
            if (includeOffset)
            {
                builder.Append((displayOffset + offset).ToString("X8")).Append("  ");
            }

            builder.Append(FormatHex(line));
            if (includeAscii)
            {
                builder.Append("  ").Append(ToVisibleText(line));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private bool CopyText(string text)
    {
        if (string.IsNullOrEmpty(text))
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
        RefreshLayout();
    }

    private void StopCopyFeedback()
    {
        _copyFeedbackTimer.Stop();
        _copyFeedbackText = null;
    }

    private void CopyFeedbackTimer_Tick(object? sender, EventArgs eventArgs)
    {
        StopCopyFeedback();
        RefreshLayout();
    }

    private double GetHexStartX()
    {
        return OffsetWidth + DividerWidth + HexPaddingLeft;
    }

    private double GetAsciiStartX()
    {
        return OffsetWidth + DividerWidth + _hexColumnWidth + DividerWidth;
    }

    private double MeasureCharacterWidth()
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText text = new("0",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            12,
            Brushes.Black,
            pixelsPerDip);
        return Math.Ceiling(text.WidthIncludingTrailingWhitespace);
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, Brush brush, double pixelsPerDip, FontWeight weight)
    {
        FormattedText formatted = new(text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            weight == FontWeights.Normal ? MonoTypeface : new Typeface(MonoTypeface.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            12,
            brush,
            pixelsPerDip);
        context.DrawText(formatted, new Point(x, y));
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        StringBuilder builder = new(bytes.Length * 3);
        for (int index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
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
        foreach (byte current in bytes)
        {
            builder.Append(ToVisibleChar(current));
        }

        return builder.ToString();
    }

    private static string DecodeBodyText(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static char ToVisibleChar(byte value)
    {
        return value is >= 32 and <= 126 ? (char)value : '.';
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private enum SelectionRegion
    {
        None,
        Hex,
        Ascii
    }
}

public sealed class HexRenderSurface : FrameworkElement
{
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(HexRenderSurface), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    internal HexViewControl? Owner { get; set; }

    public Brush Background
    {
        get => (Brush)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        Owner?.DrawHex(drawingContext);
    }
}
