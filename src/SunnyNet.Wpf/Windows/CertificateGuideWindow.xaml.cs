using System.Windows;
using SunnyNet.Wpf.ViewModels;

namespace SunnyNet.Wpf.Windows;

public partial class CertificateGuideWindow : Window
{
    public CertificateGuideWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
