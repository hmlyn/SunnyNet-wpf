using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SunnyNet.Wpf.Models;

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
        if (RowsGrid.SelectedItem is not DetailNameValueRow row)
        {
            return;
        }

        string text = string.IsNullOrWhiteSpace(row.Extra)
            ? $"{row.Name}: {row.Value}"
            : $"{row.Name}: {row.Value}; {row.Extra}";

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
        }
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
