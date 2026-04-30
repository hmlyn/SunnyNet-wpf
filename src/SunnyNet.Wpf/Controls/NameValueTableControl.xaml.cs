using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.Services;

namespace SunnyNet.Wpf.Controls;

public partial class NameValueTableControl : UserControl
{
    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.Register(nameof(Rows), typeof(IEnumerable), typeof(NameValueTableControl), new PropertyMetadata(null, OnRowsChanged));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(NameValueTableControl), new PropertyMetadata("无内容", OnEmptyTextChanged));

    public static readonly DependencyProperty ShowExtraColumnProperty =
        DependencyProperty.Register(nameof(ShowExtraColumn), typeof(bool), typeof(NameValueTableControl), new PropertyMetadata(false, OnShowExtraColumnChanged));

    private INotifyCollectionChanged? _notifyCollection;

    public NameValueTableControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshState();
    }

    public IEnumerable? Rows
    {
        get => (IEnumerable?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public bool ShowExtraColumn
    {
        get => (bool)GetValue(ShowExtraColumnProperty);
        set => SetValue(ShowExtraColumnProperty, value);
    }

    private static void OnRowsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not NameValueTableControl control)
        {
            return;
        }

        if (control._notifyCollection is not null)
        {
            control._notifyCollection.CollectionChanged -= control.Rows_CollectionChanged;
        }

        control._notifyCollection = args.NewValue as INotifyCollectionChanged;
        if (control._notifyCollection is not null)
        {
            control._notifyCollection.CollectionChanged += control.Rows_CollectionChanged;
        }

        control.RefreshState();
    }

    private static void OnEmptyTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is NameValueTableControl control && control.EmptyTextBlock is not null)
        {
            control.EmptyTextBlock.Text = args.NewValue?.ToString() ?? "";
        }
    }

    private static void OnShowExtraColumnChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is NameValueTableControl control)
        {
            control.RefreshState();
        }
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RefreshState();
    }

    private void RefreshState()
    {
        if (EmptyPanel is null || ExtraColumn is null || EmptyTextBlock is null)
        {
            return;
        }

        ExtraColumn.Visibility = ShowExtraColumn ? Visibility.Visible : Visibility.Collapsed;
        EmptyTextBlock.Text = EmptyText;
        EmptyPanel.Visibility = CountRows(Rows) == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RowsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (TryGetSelectedRow(out DetailNameValueRow? row) is false)
        {
            return;
        }

        string text = string.IsNullOrWhiteSpace(row.Extra)
            ? $"{row.Name}: {row.Value}"
            : $"{row.Name}: {row.Value}; {row.Extra}";

        try
        {
            ClipboardService.SetText(text);
        }
        catch
        {
        }
    }

    private void RowsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs mouseButtonEventArgs)
    {
        if (FindVisualParent<DataGridRow>(mouseButtonEventArgs.OriginalSource as DependencyObject) is not { Item: DetailNameValueRow row })
        {
            return;
        }

        if (!ReferenceEquals(RowsGrid.SelectedItem, row))
        {
            RowsGrid.SelectedItem = row;
        }
    }

    private void RowsGridContextMenu_Opened(object sender, RoutedEventArgs routedEventArgs)
    {
        bool hasRow = TryGetSelectedRow(out DetailNameValueRow? row);
        CopyRowNameMenuItem.IsEnabled = hasRow && !string.IsNullOrWhiteSpace(row?.Name);
        CopyRowValueMenuItem.IsEnabled = hasRow && !string.IsNullOrWhiteSpace(row?.Value);
    }

    private void CopyRowNameMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (TryGetSelectedRow(out DetailNameValueRow? row) is false)
        {
            return;
        }

        CopyText(row.Name);
    }

    private void CopyRowValueMenuItem_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        if (TryGetSelectedRow(out DetailNameValueRow? row) is false)
        {
            return;
        }

        CopyText(row.Value);
    }

    private bool TryGetSelectedRow(out DetailNameValueRow row)
    {
        row = RowsGrid.SelectedItem as DetailNameValueRow ?? null!;
        return row is not null;
    }

    private static void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
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

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static int CountRows(IEnumerable? rows)
    {
        if (rows is null)
        {
            return 0;
        }

        if (rows is ICollection collection)
        {
            return collection.Count;
        }

        int count = 0;
        IEnumerator enumerator = rows.GetEnumerator();
        try
        {
            while (enumerator.MoveNext())
            {
                count++;
            }
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }

        return count;
    }
}
