using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using SunnyNet.Wpf.Services;

namespace SunnyNet.Wpf.Windows;

public partial class OpenSourceWindow : Window
{
    private const string ProjectUrl = "https://github.com/hmlyn/SunnyNet-wpf";
    private const string CoreUrl = "https://github.com/qtgolang/SunnyNet";

    public OpenSourceWindow()
    {
        InitializeComponent();
        Loaded += OpenSourceWindow_Loaded;
    }

    private void OpenSourceWindow_Loaded(object sender, RoutedEventArgs routedEventArgs)
    {
        try
        {
            SponsorImage.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/SponsorWeChat.png", UriKind.Absolute));
            SponsorImage.Visibility = Visibility.Visible;
            SponsorMissingPanel.Visibility = Visibility.Collapsed;
        }
        catch
        {
            SponsorImageHintTextBlock.Text = "未找到内嵌赞赏二维码资源：Resources\\SponsorWeChat.png";
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenUrl(ProjectUrl);
    }

    private void CopyProject_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        ClipboardService.SetText(ProjectUrl);
    }

    private void OpenCore_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        OpenUrl(CoreUrl);
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
