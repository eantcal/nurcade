using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class TextureAssetViewModel
{
    public TextureAssetViewModel(TextureAsset asset)
    {
        Asset = asset;
        Preview = LoadPreview(asset);
    }

    public TextureAsset Asset { get; }
    public string Key => $"0x{Asset.Key:x2}";
    public string Name => Asset.Name;
    public string Status => Asset.Exists ? "OK" : "Missing";
    public ImageSource? Preview { get; }

    private static ImageSource? LoadPreview(TextureAsset asset)
    {
        if (!asset.Exists) {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(asset.FullPath, UriKind.Absolute);
        bitmap.DecodePixelWidth = 48;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
