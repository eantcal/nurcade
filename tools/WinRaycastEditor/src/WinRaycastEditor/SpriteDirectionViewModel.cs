using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class SpriteDirectionViewModel : INotifyPropertyChanged
{
    private readonly SpriteDirectionMetadata m_direction;
    private readonly string m_metadataDirectory;
    private readonly byte[] m_transparentColor;
    private int m_previewSize = 56;

    public SpriteDirectionViewModel(
        SpriteDirectionMetadata direction,
        string metadataDirectory,
        byte[] transparentColor)
    {
        m_direction = direction;
        m_metadataDirectory = metadataDirectory;
        m_transparentColor = transparentColor;
        Name = direction.Name;
        Angle = $"{direction.Angle} deg";
        Resolutions = string.Join(", ", direction.Files.Keys.OrderBy(value => value));
        var preview = direction.Files.OrderByDescending(item => item.Key).FirstOrDefault();
        PreviewResolution = preview.Key == 0 ? string.Empty : $"{preview.Key}px";
        if (preview.Key != 0) {
            UpdatePreview(preview.Key);
        }
    }

    public string Name { get; }
    public string Angle { get; }
    public string Resolutions { get; }
    public string PreviewResolution { get; }
    public string SelectedResolution { get; private set; } = string.Empty;
    public ImageSource? Preview { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetPreviewSize(int previewSize)
    {
        if (m_previewSize == previewSize) {
            return;
        }

        m_previewSize = previewSize;
        var selected = SpriteResolutionSelector.SelectClosestAvailableResolution(
            m_direction,
            ExtractResolution(SelectedResolution));
        UpdatePreview(selected);
    }

    public void UpdateSelectedResolution(int preferredResolution)
    {
        var selected = SpriteResolutionSelector.SelectClosestAvailableResolution(
            m_direction,
            preferredResolution);
        SelectedResolution = selected == preferredResolution
            ? $"{selected}px"
            : $"{selected}px (fallback from {preferredResolution}px)";
        UpdatePreview(selected);
        OnPropertyChanged(nameof(SelectedResolution));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static ImageSource? LoadPreview(
        string metadataDirectory,
        string relativePath,
        byte[] transparentColor,
        int previewSize)
    {
        var path = Path.GetFullPath(Path.Combine(metadataDirectory, relativePath));
        if (!File.Exists(path)) {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new System.Uri(path, System.UriKind.Absolute);
        bitmap.DecodePixelWidth = previewSize;
        bitmap.EndInit();
        bitmap.Freeze();
        return ApplyTransparencyCheckerboard(bitmap, transparentColor);
    }

    private static BitmapSource ApplyTransparencyCheckerboard(BitmapSource source, byte[] transparentColor)
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
                var darkSquare = ((x / 8) + (y / 8)) % 2 == 0;
                var checker = darkSquare ? (byte)196 : (byte)232;
                if (pixels[offset] != blue
                    || pixels[offset + 1] != green
                    || pixels[offset + 2] != red) {
                    var alpha = pixels[offset + 3];
                    if (alpha == 255) {
                        continue;
                    }

                    pixels[offset] = (byte)((pixels[offset] * alpha + checker * (255 - alpha)) / 255);
                    pixels[offset + 1] = (byte)((pixels[offset + 1] * alpha + checker * (255 - alpha)) / 255);
                    pixels[offset + 2] = (byte)((pixels[offset + 2] * alpha + checker * (255 - alpha)) / 255);
                    pixels[offset + 3] = 255;
                    continue;
                }

                pixels[offset] = checker;
                pixels[offset + 1] = checker;
                pixels[offset + 2] = checker;
                pixels[offset + 3] = 255;
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

    private void UpdatePreview(int resolution)
    {
        if (!m_direction.Files.TryGetValue(resolution, out var relativePath)) {
            return;
        }

        Preview = LoadPreview(
            m_metadataDirectory,
            relativePath,
            m_transparentColor,
            m_previewSize);
        OnPropertyChanged(nameof(Preview));
    }

    private static int ExtractResolution(string text)
    {
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var resolution) ? resolution : 0;
    }
}
