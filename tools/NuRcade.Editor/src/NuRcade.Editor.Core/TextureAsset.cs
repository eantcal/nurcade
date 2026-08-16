namespace NuRcade.Editor.Core;

public sealed class TextureAsset
{
    public byte Key { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool Exists { get; init; }
}
