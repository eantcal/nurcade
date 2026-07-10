using System.IO;

namespace WinRaycastEditor;

public enum TextureImportStatus
{
    Added,
    ReusedExisting,
    Skipped,
    Failed
}

/// <summary>
/// Result of copying a candidate image into the world's texture folder. Produced
/// off the UI thread; it carries no model state so the view model can turn it into
/// a palette entry afterwards.
/// </summary>
public sealed record TextureCopyOutcome(
    string SourcePath,
    string FileName,
    bool Success,
    string? RelativePath,
    string? DestinationPath,
    string? Message);

/// <summary>
/// Per-file outcome of a texture import, shown in the import progress dialog.
/// </summary>
public sealed class TextureImportResult
{
    public required string FileName { get; init; }
    public TextureImportStatus Status { get; init; }
    public byte? Key { get; init; }
    public string? RelativePath { get; init; }
    public string? DestinationPath { get; init; }
    public string? Message { get; init; }

    public string KeyText => Key is byte key ? $"0x{key:x2}" : "—";

    public string StatusText => Status switch
    {
        TextureImportStatus.Added => "Added",
        TextureImportStatus.ReusedExisting => "Already in library",
        TextureImportStatus.Skipped => "Skipped",
        _ => "Failed"
    };

    public string Detail => Status switch
    {
        TextureImportStatus.Added or TextureImportStatus.ReusedExisting =>
            $"{KeyText}  →  {RelativePath}",
        _ => Message ?? StatusText
    };
}

/// <summary>
/// Filesystem side of importing textures: validates an image and copies it into the
/// world's <c>textures</c> folder, alongside the existing texture resources.
/// </summary>
public static class TextureImporter
{
    public static bool IsSupportedTextureFile(string path)
    {
        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    public static TextureCopyOutcome CopyToWorld(string sourcePath, string worldDirectory)
    {
        var fileName = Path.GetFileName(sourcePath);
        try {
            if (!IsSupportedTextureFile(sourcePath) || !File.Exists(sourcePath)) {
                return new TextureCopyOutcome(
                    sourcePath, fileName, false, null, null, "Unsupported or missing image.");
            }

            var texturesDirectory = Path.Combine(worldDirectory, "textures");
            Directory.CreateDirectory(texturesDirectory);

            var fullSource = Path.GetFullPath(sourcePath);
            var name = fileName;
            var fullTarget = Path.GetFullPath(Path.Combine(texturesDirectory, name));

            if (string.Equals(fullTarget, fullSource, StringComparison.OrdinalIgnoreCase)) {
                return new TextureCopyOutcome(
                    sourcePath, name, true, $"textures/{name}", fullTarget, "Already in folder.");
            }

            if (File.Exists(fullTarget)) {
                if (new FileInfo(fullTarget).Length == new FileInfo(fullSource).Length) {
                    return new TextureCopyOutcome(
                        sourcePath, name, true, $"textures/{name}", fullTarget, "Already in folder.");
                }

                var baseName = Path.GetFileNameWithoutExtension(name);
                var extension = Path.GetExtension(name);
                var counter = 1;
                do {
                    name = $"{baseName}_{counter}{extension}";
                    fullTarget = Path.GetFullPath(Path.Combine(texturesDirectory, name));
                    ++counter;
                } while (File.Exists(fullTarget));
            }

            File.Copy(fullSource, fullTarget);
            return new TextureCopyOutcome(
                sourcePath, name, true, $"textures/{name}", fullTarget, "Copied.");
        }
        catch (Exception error) {
            return new TextureCopyOutcome(sourcePath, fileName, false, null, null, error.Message);
        }
    }
}
