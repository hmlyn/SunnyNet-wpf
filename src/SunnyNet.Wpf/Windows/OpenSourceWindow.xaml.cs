using System.Diagnostics;
using System.Windows;

namespace SunnyNet.Wpf.Windows;

public partial class OpenSourceWindow : Window
{
    public OpenSourceWindow()
    {
        InitializeComponent();
    }

    private void OpenTools_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        Process.Start(new ProcessStartInfo("https://github.com/qtgolang/SunnyNetTools") { UseShellExecute = true });
    }

    private void OpenCore_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        Process.Start(new ProcessStartInfo("https://github.com/qtgolang/SunnyNet") { UseShellExecute = true });
    }
}
