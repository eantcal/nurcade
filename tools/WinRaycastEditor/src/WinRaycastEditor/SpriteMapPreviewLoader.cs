using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

internal static class SpriteMapPreviewLoader
{
    private const int PreviewPixelWidth = 128;

    public static SpriteMapPreview? Load(string metadataPath)
    {
        var result = SpriteMetadataLoader.Load(metadataPath);
        return result.Document is null
            ? null
            : Load(result.Document, metadataPath);
    }

    public static SpriteMapPreview Load(SpriteMetadataDocument document, string metadataPath)
    {
        var metadataDirectory =
            Path.GetDirectoryName(Path.GetFullPath(metadataPath)) ?? Environment.CurrentDirectory;
        var direction = document.Directions.FirstOrDefault(
                item => string.Equals(item.Name, "front", StringComparison.OrdinalIgnoreCase))
            ?? document.Directions.OrderBy(item => item.Angle).FirstOrDefault();
        if (direction is null) {
            return new SpriteMapPreview(document.SpriteSet, null);
        }

        var file = direction.Files.OrderByDescending(item => item.Key).FirstOrDefault();
        if (file.Key == 0) {
            return new SpriteMapPreview(document.SpriteSet, null);
        }

        var imagePath = Path.GetFullPath(Path.Combine(metadataDirectory, file.Value));
        if (!File.Exists(imagePath)) {
            return new SpriteMapPreview(document.SpriteSet, null);
        }

        try {
            return new SpriteMapPreview(
                document.SpriteSet,
                LoadImage(imagePath, document.TransparentColor));
        }
        catch {
            return new SpriteMapPreview(document.SpriteSet, null);
        }
    }

    private static ImageSource? LoadImage(string imagePath, byte[] transparentColor)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.DecodePixelWidth = PreviewPixelWidth;
        bitmap.EndInit();
        bitmap.Freeze();

        return Path.GetExtension(imagePath).Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            ? ApplyColorKeyTransparency(bitmap, transparentColor)
            : bitmap;
    }

    private static BitmapSource ApplyColorKeyTransparency(BitmapSource source, byte[] transparentColor)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var red = transparentColor.Length > 0 ? transparentColor[0] : (byte)0;
        var green = transparentColor.Length > 1 ? transparentColor[1] : (byte)0;
        var blue = transparentColor.Length > 2 ? transparentColor[2] : (byte)0;

        for (var y = 0; y < converted.PixelHeight; ++y) {
            for (var x = 0; x < converted.PixelWidth; ++x) {
                var offset = y * stride + x * 4;
                if (pixels[offset] == blue
                    && pixels[offset + 1] == green
                    && pixels[offset + 2] == red) {
                    pixels[offset + 3] = 0;
                }
            }
        }

        var preview = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        preview.Freeze();
        return preview;
    }
}

internal sealed record SpriteMapPreview(string SpriteSet, ImageSource? Image);
