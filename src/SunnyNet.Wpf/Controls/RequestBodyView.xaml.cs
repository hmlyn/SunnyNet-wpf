using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace SunnyNet.Wpf.Controls;

public partial class RequestBodyView : UserControl
{
    public static readonly DependencyProperty RawTextProperty =
        DependencyProperty.Register(nameof(RawText), typeof(string), typeof(RequestBodyView), new PropertyMetadata(""));

    public static readonly DependencyProperty UrlEncodedRowsProperty =
        DependencyProperty.Register(nameof(UrlEncodedRows), typeof(IEnumerable), typeof(RequestBodyView), new PropertyMetadata(null));

    public static readonly DependencyProperty HasUrlEncodedRowsProperty =
        DependencyProperty.Register(nameof(HasUrlEncodedRows), typeof(bool), typeof(RequestBodyView), new PropertyMetadata(false, OnHasUrlEncodedRowsChanged));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(RequestBodyView), new PropertyMetadata(""));

    public static readonly DependencyProperty SearchIgnoreCaseProperty =
        DependencyProperty.Register(nameof(SearchIgnoreCase), typeof(bool), typeof(RequestBodyView), new PropertyMetadata(true));

    public RequestBodyView()
    {
        InitializeComponent();
        ApplyMode(false);
    }

    public string RawText
    {
        get => (string)GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    public IEnumerable? UrlEncodedRows
    {
        get => (IEnumerable?)GetValue(UrlEncodedRowsProperty);
        set => SetValue(UrlEncodedRowsProperty, value);
    }

    public bool HasUrlEncodedRows
    {
        get => (bool)GetValue(HasUrlEncodedRowsProperty);
        set => SetValue(HasUrlEncodedRowsProperty, value);
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

    public bool MoveToNextMatch()
    {
        return RawViewer.Visibility == Visibility.Visible && RawViewer.MoveToNextMatch();
    }

    private static void OnHasUrlEncodedRowsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is RequestBodyView view)
        {
            view.UrlEncodedButton.IsEnabled = (bool)args.NewValue;
            view.ApplyMode((bool)args.NewValue);
        }
    }

    private void RawButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ApplyMode(false);
    }

    private void UrlEncodedButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ApplyMode(HasUrlEncodedRows);
    }

    private void ApplyMode(bool showUrlEncoded)
    {
        if (!HasUrlEncodedRows)
        {
            showUrlEncoded = false;
        }

        RawButton.IsChecked = !showUrlEncoded;
        UrlEncodedButton.IsChecked = showUrlEncoded;
        UrlEncodedButton.IsEnabled = HasUrlEncodedRows;
        RawViewer.Visibility = showUrlEncoded ? Visibility.Collapsed : Visibility.Visible;
        UrlEncodedGrid.Visibility = showUrlEncoded ? Visibility.Visible : Visibility.Collapsed;
    }
}
