namespace WinRaycastEditor.Core;

public static class TexturePaletteBuilder
{
    public static IReadOnlyList<TextureAsset> Build(EditorMapDocument document, string worldFilePath)
    {
        var worldDirectory = Path.GetDirectoryName(Path.GetFullPath(worldFilePath)) ?? Environment.CurrentDirectory;
        return document.TextureMap
            .OrderBy(item => item.Key)
            .Select(item => CreateAsset(worldDirectory, item.Key, item.Value))
            .ToList();
    }

    private static TextureAsset CreateAsset(string worldDirectory, byte key, string name)
    {
        var relativePath = ResolveRelativePath(worldDirectory, name);
        var fullPath = Path.GetFullPath(Path.Combine(worldDirectory, relativePath));

        return new TextureAsset {
            Key = key,
            Name = name,
            RelativePath = relativePath,
            FullPath = fullPath,
            Exists = File.Exists(fullPath)
        };
    }

    private static bool HasSupportedImageExtension(string path)
    {
        return path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRelativePath(string worldDirectory, string name)
    {
        if (HasSupportedImageExtension(name)) {
            return name;
        }

        var pngPath = $"{name}.png";
        if (File.Exists(Path.GetFullPath(Path.Combine(worldDirectory, pngPath)))) {
            return pngPath;
        }

        return $"{name}.bmp";
    }
}
