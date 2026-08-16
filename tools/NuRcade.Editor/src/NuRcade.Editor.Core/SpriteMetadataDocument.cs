namespace NuRcade.Editor.Core;

public sealed class SpriteMetadataDocument
{
    public string SpriteSet { get; set; } = string.Empty;
    public string Format { get; set; } = "BMP";
    public byte[] TransparentColor { get; set; } = [0, 0, 0];
    public List<int> SupportedResolutions { get; set; } = [];
    public int DefaultResolution { get; set; }
    public int MaxResolution { get; set; }
    public List<SpriteDirectionMetadata> Directions { get; set; } = [];
    public List<SpriteAnimationMetadata> Animations { get; set; } = [];
    public List<SpriteLodMetadata> Lod { get; set; } = [];
}

public sealed class SpriteDirectionMetadata
{
    public string Name { get; set; } = string.Empty;
    public int Angle { get; set; }
    public Dictionary<int, string> Files { get; set; } = [];
}

public sealed class SpriteLodMetadata
{
    public double MaxDistance { get; set; }
    public int Resolution { get; set; }
}

public sealed class SpriteAnimationMetadata
{
    public string Name { get; set; } = string.Empty;
    public double FrameDurationMs { get; set; }
    public bool Loop { get; set; } = true;
    public List<SpriteDirectionMetadata> Directions { get; set; } = [];
    public List<SpriteAnimationFrameMetadata> Frames { get; set; } = [];
}

public sealed class SpriteAnimationFrameMetadata
{
    public List<SpriteDirectionMetadata> Directions { get; set; } = [];
}
