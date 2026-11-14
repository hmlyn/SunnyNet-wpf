using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using SharpVectors.Converters;
using SharpVectors.Dom;
using SharpVectors.Dom.Svg;
using SharpVectors.Renderers.Wpf;
using SkiaSharp;
using SunnyNet.Wpf.Models;

namespace SunnyNet.Wpf.Services;

internal static class ImagePreviewDecoder
{
    public static ImageSource Decode(byte[] bytes, ImagePreviewFormat format, string imageType)
    {
        if (format == ImagePreviewFormat.Webp || (format == ImagePreviewFormat.Auto && IsWebp(bytes)))
        {
            return DecodeWebp(bytes);
        }

        if (format == ImagePreviewFormat.Svg || (format == ImagePreviewFormat.Auto &&
            (imageType.Split(';', 2)[0].Trim().Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase) || IsSvg(bytes))))
        {
            return DecodeSvg(bytes);
        }

        BitmapImage image = new();
        using MemoryStream stream = new(bytes, writable: false);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static bool IsWebp(byte[] bytes) =>
        bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);

    public static bool IsSvg(byte[] bytes)
    {
        try
        {
            using MemoryStream stream = new(bytes, writable: false);
            using XmlReader reader = CreateSvgXmlReader(stream);
            return reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "svg" &&
                   (reader.NamespaceURI.Length == 0 || reader.NamespaceURI == "http://www.w3.org/2000/svg");
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static BitmapSource DecodeWebp(byte[] bytes)
    {
        using SKData data = SKData.CreateCopy(bytes);
        using SKCodec codec = SKCodec.Create(data) ?? throw new FormatException("Invalid WebP image.");
        SKImageInfo info = new(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKBitmap bitmap = new(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            throw new FormatException("Unable to decode WebP image.");
        }

        BitmapSource image = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32,
            null, bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
        image.Freeze();
        return image;
    }

    private static DrawingImage DecodeSvg(byte[] bytes)
    {
        WpfDrawingSettings settings = new()
        {
            IncludeRuntime = false,
            TextAsGeometry = true,
            ExternalResourcesAccessMode = ExternalResourcesAccessModes.Ignore
        };
        using FileSvgReader converter = new(settings);
        using MemoryStream stream = new(bytes, writable: false);
        using XmlReader reader = CreateSvgXmlReader(stream);
        DrawingGroup drawing = converter.Read(reader) ?? throw new FormatException("Invalid SVG image.");
        var root = converter.SvgDocument.RootElement;
        double width = root.Width.AnimVal.Value;
        double height = root.Height.AnimVal.Value;
        if (width <= 0 && (!root.HasAttribute("width") || root.Width.AnimVal.UnitType == SvgLengthType.Percentage))
        {
            width = root.ViewBox.AnimVal.Width;
        }

        if (height <= 0 && (!root.HasAttribute("height") || root.Height.AnimVal.UnitType == SvgLengthType.Percentage))
        {
            height = root.ViewBox.AnimVal.Height;
        }

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            throw new FormatException("Invalid SVG dimensions.");
        }

        // DrawingImage otherwise crops the SVG viewport to the visible shapes.
        Rect viewport = new(0, 0, width, height);
        DrawingGroup preview = new() { ClipGeometry = new RectangleGeometry(viewport) };
        preview.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(viewport)));
        preview.Children.Add(drawing);
        DrawingImage image = new(preview);
        image.Freeze();
        return image;
    }

    private static XmlReader CreateSvgXmlReader(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null
    });
}
