using System.Windows;
using System.Windows.Input;

namespace SunnyNet.Wpf.Windows;

public partial class RepeatCountWindow : Window
{
    public int RepeatCount { get; private set; }

    public RepeatCountWindow()
    {
        InitializeComponent();
        CountTextBox.Focus();
        CountTextBox.SelectAll();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CountTextBox.Text.Trim(), out int count) || count < 1 || count > 9999)
        {
            MessageBox.Show("请输入 1-9999 之间的整数。", "输入无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RepeatCount = count;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
        else if (e.Key == Key.Enter)
        {
            Confirm_Click(sender, e);
        }
    }
}
