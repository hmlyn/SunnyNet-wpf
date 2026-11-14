using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;
using SunnyNet.Wpf.Controls;
using SunnyNet.Wpf.Models;
using SunnyNet.Wpf.ViewModels;
using Xunit;

namespace SunnyNet.Wpf.Tests;

public sealed class ImagePreviewTests(WpfTestHost host) : IClassFixture<WpfTestHost>
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"24\" viewBox=\"0 0 32 24\"><rect x=\"8\" y=\"6\" width=\"16\" height=\"12\" fill=\"#ff0000\"/></svg>";

    [Theory]
    [InlineData("image/webp", "Auto", false)]
    [InlineData("", "Auto", false)]
    [InlineData("image/webp", "Webp", false)]
    [InlineData("image/webp", "Auto", true)]
    [InlineData("", "Auto", true)]
    [InlineData("image/webp", "Webp", true)]
    public void WebpRendersWithTransparency(string mime, string format, bool lossless)
    {
        using SKBitmap bitmap = new(new SKImageInfo(32, 24, SKColorType.Bgra8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using SKCanvas canvas = new(bitmap);
        using SKPaint paint = new() { Color = SKColors.Red };
        canvas.DrawRect(8, 6, 16, 12, paint);
        using SKPixmap pixels = bitmap.PeekPixels();
        using SKData data = pixels.Encode(new SKWebpEncoderOptions(
            lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, 100))
            ?? throw new InvalidOperationException("Unable to create WebP fixture.");
        byte[] bytes = data.ToArray();

        host.Invoke(() => AssertPreview(bytes, mime, format));
    }

    [Theory]
    [InlineData("image/svg+xml", "Auto")]
    [InlineData("", "Auto")]
    [InlineData("image/svg+xml", "Svg")]
    public void SvgRendersWithItsTransparentViewport(string mime, string format)
    {
        host.Invoke(() => AssertPreview(Encoding.UTF8.GetBytes(Svg), mime, format));
    }

    [Fact]
    public void SvgSupportsXmlDeclarationDoctypeAndUtf16()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"utf-16\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" + Svg;
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray();

        host.Invoke(() => AssertPreview(bytes, "image/svg+xml", "Auto"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("width=\"100%\" height=\"100%\" ")]
    public void SvgSupportsViewBoxWithoutExplicitDimensions(string dimensions)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Svg.Replace("width=\"32\" height=\"24\" ", dimensions));

        host.Invoke(() => AssertPreview(bytes, "image/svg+xml", "Auto"));
    }

    [Fact]
    public void SvgUsesItsDeclaredSizeWhenDifferentFromViewBox()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Svg.Replace("width=\"32\" height=\"24\"", "width=\"64\" height=\"48\""));

        host.Invoke(() => AssertPreview(bytes, "image/svg+xml", "Auto", 64, 48));
    }

    [Fact]
    public void ExistingPngPreviewStillWorks()
    {
        host.Invoke(() =>
        {
            using MemoryStream stream = new();
            PngBitmapEncoder encoder = new();
            byte[] pixels = new byte[32 * 24 * 4];
            for (int y = 6; y < 18; y++)
            {
                for (int x = 8; x < 24; x++)
                {
                    pixels[(y * 32 + x) * 4 + 2] = 255;
                    pixels[(y * 32 + x) * 4 + 3] = 255;
                }
            }

            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32, 24, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4)));
            encoder.Save(stream);
            AssertPreview(stream.ToArray(), "image/png", "Auto");
        });
    }

    [Theory]
    [InlineData("image/webp", "RIFF0000WEBPbroken")]
    [InlineData("image/svg+xml", "<svg><broken>")]
    public void InvalidImageKeepsTheOriginalBytesAvailableToSave(string mime, string body)
    {
        host.Invoke(() =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ImagePreviewControl control = new() { ImageType = mime, ImageBytes = bytes };
            Assert.Null(Find<Image>(control, "PreviewImage").Source);
            Assert.Equal(Visibility.Visible, Find<TextBlock>(control, "EmptyTextBlock").Visibility);
            Assert.Equal(Visibility.Collapsed, Find<ScrollViewer>(control, "ImageScrollViewer").Visibility);
            Assert.True(Find<Button>(control, "SaveButton").IsEnabled);
            Assert.Same(bytes, control.ImageBytes);
            Assert.Equal(bytes, Encoding.UTF8.GetBytes(body));
        });
    }

    [Theory]
    [InlineData("image/svg+xml", "svg")]
    [InlineData("image/svg+xml; charset=utf-8", "svg")]
    [InlineData("image/webp", "webp")]
    public void SavingUsesTheCorrectExtension(string mime, string expected)
    {
        host.Invoke(() =>
        {
            ImagePreviewControl control = new() { ImageType = mime };
            MethodInfo method = typeof(ImagePreviewControl).GetMethod("GetSaveExtension", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(expected, method.Invoke(control, null));
        });
    }

    [Fact]
    public void ResponseWithoutImageMimeStillRecognizesSvg()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Svg);
        MethodInfo method = typeof(MainWindowViewModel).GetMethod("TryGetImageBytes", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Same(bytes, method.Invoke(null, ["application/octet-stream", bytes]));
        Assert.Empty((byte[])method.Invoke(null, ["application/json", Encoding.UTF8.GetBytes("{\"text\":\"<svg>\"}")])!);
    }

    private static void AssertPreview(byte[] bytes, string mime, string format, int width = 32, int height = 24)
    {
        byte[] original = bytes.ToArray();
        ImagePreviewControl control = new() { ImageType = mime, ImageBytes = bytes };
        control.PreviewFormat = Enum.Parse<ImagePreviewFormat>(format);
        ImageSource source = Find<Image>(control, "PreviewImage").Source;
        Assert.NotNull(source);
        Assert.Equal(Visibility.Collapsed, Find<TextBlock>(control, "EmptyTextBlock").Visibility);
        Assert.Equal(Visibility.Visible, Find<ScrollViewer>(control, "ImageScrollViewer").Visibility);
        Assert.True(Find<Button>(control, "SaveButton").IsEnabled);
        Assert.Equal($"{width} \u00d7 {height}", Find<TextBlock>(control, "SizeValueTextBlock").Text);
        if (source is BitmapSource bitmap)
        {
            Assert.Equal(width, bitmap.PixelWidth);
            Assert.Equal(height, bitmap.PixelHeight);
        }
        else
        {
            Assert.Equal(width, source.Width, 5);
            Assert.Equal(height, source.Height, 5);
        }

        DrawingVisual visual = new();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawImage(source, new Rect(0, 0, width, height));
        }

        RenderTargetBitmap rendered = new(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        byte[] pixels = new byte[width * height * 4];
        rendered.CopyPixels(pixels, width * 4, 0);
        int center = ((height / 2) * width + width / 2) * 4;
        Assert.InRange(pixels[center], 0, 12);
        Assert.InRange(pixels[center + 1], 0, 12);
        Assert.InRange(pixels[center + 2], 240, 255);
        Assert.Equal(255, pixels[center + 3]);
        Assert.Equal(0, pixels[3]);
        Assert.Same(bytes, control.ImageBytes);
        Assert.Equal(original, bytes);

        control.ImageBytes = [];
        Assert.Null(Find<Image>(control, "PreviewImage").Source);
        Assert.False(Find<Button>(control, "SaveButton").IsEnabled);
    }

    private static T Find<T>(ImagePreviewControl control, string name) where T : FrameworkElement =>
        (T)control.FindName(name);
}

public sealed class WpfTestHost : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public WpfTestHost()
    {
        TaskCompletionSource<Dispatcher> ready = new();
        _thread = new Thread(() =>
        {
            try
            {
                Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/SunnyNet;component/Themes/FluentLight.xaml")
                });
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
            }
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public void Invoke(Action action) => _dispatcher.Invoke(action);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join();
    }
}
