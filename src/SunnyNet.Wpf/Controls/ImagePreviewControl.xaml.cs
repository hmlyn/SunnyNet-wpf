using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Controls;

public partial class ImagePreviewControl : UserControl
{
    public static readonly DependencyProperty ImageBytesProperty =
        DependencyProperty.Register(nameof(ImageBytes), typeof(byte[]), typeof(ImagePreviewControl), new PropertyMetadata(Array.Empty<byte>(), OnImageChanged));

    public static readonly DependencyProperty ImageTypeProperty =
        DependencyProperty.Register(nameof(ImageType), typeof(string), typeof(ImagePreviewControl), new PropertyMetadata("", OnImageChanged));

    public static readonly DependencyProperty EmptyTextProperty =
        DependencyProperty.Register(nameof(EmptyText), typeof(string), typeof(ImagePreviewControl), new PropertyMetadata("暂无图片内容", OnEmptyTextChanged));

    public static readonly DependencyProperty PreviewFormatProperty =
        DependencyProperty.Register(nameof(PreviewFormat), typeof(ImagePreviewFormat), typeof(ImagePreviewControl), new PropertyMetadata(ImagePreviewFormat.Auto, OnPreviewFormatChanged));

    public ImagePreviewControl()
    {
        InitializeComponent();
        RenderImage();
    }

    public byte[] ImageBytes
    {
        get => (byte[])GetValue(ImageBytesProperty);
        set => SetValue(ImageBytesProperty, value);
    }

    public string ImageType
    {
        get => (string)GetValue(ImageTypeProperty);
        set => SetValue(ImageTypeProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public ImagePreviewFormat PreviewFormat
    {
        get => (ImagePreviewFormat)GetValue(PreviewFormatProperty);
        set => SetValue(PreviewFormatProperty, value);
    }

    private static void OnImageChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is ImagePreviewControl control)
        {
            control.PreviewFormat = ImagePreviewFormat.Auto;
            control.RenderImage();
        }
    }

    private static void OnEmptyTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is ImagePreviewControl control)
        {
            control.EmptyTextBlock.Text = args.NewValue?.ToString() ?? "";
        }
    }

    private static void OnPreviewFormatChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is ImagePreviewControl control)
        {
            control.SelectFormatItem();
            control.RenderImage();
        }
    }

    private void RenderImage()
    {
        byte[] bytes = ImageBytes ?? Array.Empty<byte>();
        EmptyTextBlock.Text = EmptyText;
        TypeValueTextBlock.Text = NormalizeType(GetEffectiveFormatLabel());
        LengthValueTextBlock.Text = $"{bytes.Length:N0} Bytes";
        SizeValueTextBlock.Text = "-";
        SaveButton.IsEnabled = bytes.Length > 0;
        if (bytes.Length == 0)
        {
            PreviewImage.Source = null;
            InfoTextBlock.Text = "暂无图片";
            EmptyTextBlock.Visibility = Visibility.Visible;
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            byte[] imageBytes = ResolveImageBytes(bytes, PreviewFormat, ImageType);
            BitmapImage image = new();
            using MemoryStream stream = new(imageBytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            PreviewImage.Source = image;
            SizeValueTextBlock.Text = $"{image.PixelWidth} × {image.PixelHeight}";
            InfoTextBlock.Text = "已加载图片预览，可切换显示方式或保存原图。";
            EmptyTextBlock.Visibility = Visibility.Collapsed;
            ImageScrollViewer.Visibility = Visibility.Visible;
        }
        catch
        {
            PreviewImage.Source = null;
            InfoTextBlock.Text = "当前格式无法直接预览，但仍可保存原始图片。";
            EmptyTextBlock.Text = "当前图片格式无法直接预览";
            EmptyTextBlock.Visibility = Visibility.Visible;
            ImageScrollViewer.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectFormatItem()
    {
        if (FormatBox is null)
        {
            return;
        }

        foreach (object item in FormatBox.Items)
        {
            if (item is ComboBoxItem { Tag: string tag } && string.Equals(tag, PreviewFormat.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                FormatBox.SelectedItem = item;
                return;
            }
        }
    }

    private void StretchModeBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (PreviewImage is null || StretchModeBox is null)
        {
            return;
        }

        if (StretchModeBox.SelectedItem is not ComboBoxItem { Tag: string mode })
        {
            return;
        }

        PreviewImage.Stretch = mode switch
        {
            "Fill" => Stretch.Fill,
            "None" => Stretch.None,
            _ => Stretch.Uniform
        };
    }

    private void FormatBox_SelectionChanged(object sender, SelectionChangedEventArgs selectionChangedEventArgs)
    {
        if (FormatBox is null || FormatBox.SelectedItem is not ComboBoxItem { Tag: string mode })
        {
            return;
        }

        if (Enum.TryParse(mode, true, out ImagePreviewFormat previewFormat))
        {
            PreviewFormat = previewFormat;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs routedEventArgs)
    {
        byte[] bytes = ImageBytes ?? Array.Empty<byte>();
        if (bytes.Length == 0)
        {
            return;
        }

        string extension = GetSaveExtension();
        if (string.IsNullOrWhiteSpace(extension) || extension.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            extension = "bin";
        }

        SaveFileDialog dialog = new()
        {
            Title = "保存图片",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            FileName = $"SunnyNet_Image.{extension}",
            Filter = $"图片文件 (*.{extension})|*.{extension}|所有文件 (*.*)|*.*",
            DefaultExt = "." + extension
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, bytes);
            InfoTextBlock.Text = $"图片已保存到：{dialog.FileName}";
        }
        catch (Exception exception)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "保存图片失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "unknown";
        }

        return type.Replace("image/", "", StringComparison.OrdinalIgnoreCase);
    }

    private string GetEffectiveFormatLabel()
    {
        return PreviewFormat == ImagePreviewFormat.Auto
            ? NormalizeType(ImageType)
            : PreviewFormat.ToString().ToLowerInvariant();
    }

    private static byte[] ResolveImageBytes(byte[] bytes, ImagePreviewFormat previewFormat, string imageType)
    {
        if (bytes.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (previewFormat == ImagePreviewFormat.Auto)
        {
            if (TryDecodeFromKnownMagic(bytes, imageType, out byte[] decoded))
            {
                return decoded;
            }

            return bytes;
        }

        if (TryDecodeByFormat(bytes, previewFormat, out byte[] resolved))
        {
            return resolved;
        }

        return bytes;
    }

    private static bool TryDecodeByFormat(byte[] bytes, ImagePreviewFormat previewFormat, out byte[] resolved)
    {
        resolved = bytes;
        string header = Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 12)));
        return previewFormat switch
        {
            ImagePreviewFormat.Jpeg => header.StartsWith("FFD8FF", StringComparison.OrdinalIgnoreCase),
            ImagePreviewFormat.Png => header.StartsWith("89504E470D0A1A0A", StringComparison.OrdinalIgnoreCase),
            ImagePreviewFormat.Gif => header.StartsWith("47494638", StringComparison.OrdinalIgnoreCase),
            ImagePreviewFormat.Bmp => header.StartsWith("424D", StringComparison.OrdinalIgnoreCase),
            ImagePreviewFormat.Webp => header.StartsWith("52494646", StringComparison.OrdinalIgnoreCase) && bytes.Length >= 12,
            ImagePreviewFormat.Ico => header.StartsWith("00000100", StringComparison.OrdinalIgnoreCase),
            ImagePreviewFormat.Tiff => header.StartsWith("49492A00", StringComparison.OrdinalIgnoreCase) || header.StartsWith("4D4D002A", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool TryDecodeFromKnownMagic(byte[] bytes, string imageType, out byte[] resolved)
    {
        resolved = bytes;
        if (!string.IsNullOrWhiteSpace(imageType) && imageType.Contains("jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }

        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return true;
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        if (bytes.Length >= 4 && ((bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) ||
                                  (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x02 && bytes[3] == 0x00)))
        {
            return true;
        }

        if (bytes.Length >= 4 && ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                                  (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
        {
            return true;
        }

        return false;
    }

    private string GetSaveExtension()
    {
        if (PreviewFormat != ImagePreviewFormat.Auto)
        {
            return PreviewFormat.ToString().ToLowerInvariant();
        }

        string extension = NormalizeType(ImageType);
        return string.IsNullOrWhiteSpace(extension) ? "bin" : extension;
    }
}
