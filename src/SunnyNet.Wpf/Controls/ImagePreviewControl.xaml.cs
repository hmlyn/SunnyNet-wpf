using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.Services;

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
        TypeValueTextBlock.Text = GetEffectiveFormatLabel();
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
            ImageSource image = ImagePreviewDecoder.Decode(bytes, PreviewFormat, ImageType ?? "");

            PreviewImage.Source = image;
            SizeValueTextBlock.Text = image is BitmapSource bitmap
                ? $"{bitmap.PixelWidth} × {bitmap.PixelHeight}"
                : $"{image.Width:0.##} × {image.Height:0.##}";
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

        string normalized = type.Split(';', 2)[0].Trim().Replace("image/", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return normalized == "svg+xml" ? "svg" : normalized;
    }

    private string GetEffectiveFormatLabel()
    {
        if (PreviewFormat != ImagePreviewFormat.Auto)
        {
            return PreviewFormat.ToString().ToLowerInvariant();
        }

        string type = NormalizeType(ImageType);
        if (type != "unknown")
        {
            return type;
        }

        byte[] bytes = ImageBytes ?? Array.Empty<byte>();
        if (ImagePreviewDecoder.IsWebp(bytes))
        {
            return "webp";
        }

        return ImagePreviewDecoder.IsSvg(bytes) ? "svg" : type;
    }

    private string GetSaveExtension()
    {
        string extension = GetEffectiveFormatLabel();
        return string.IsNullOrWhiteSpace(extension) ? "bin" : extension;
    }
}
