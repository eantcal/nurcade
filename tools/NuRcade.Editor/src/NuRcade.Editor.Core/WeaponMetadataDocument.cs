namespace NuRcade.Editor.Core;

public sealed class WeaponMetadataDocument
{
    public string Weapon { get; set; } = string.Empty;
    public string Format { get; set; } = "PNG";
    public int FrameWidth { get; set; } = 320;
    public int FrameHeight { get; set; } = 220;
    public double ScreenHeightFraction { get; set; } = 0.45;
    public double Damage { get; set; }
    public double RangeCells { get; set; } = 8.0;
    public WeaponFireBehaviorMetadata? FireBehavior { get; set; }
    public WeaponSoundMetadata? Sounds { get; set; }
    public WeaponAmmoMetadata? Ammo { get; set; }
    public WeaponPointMetadata Anchor { get; set; } = new() { X = 0.5, Y = 1.0 };
    public WeaponPointMetadata BaseOffset { get; set; } = new();
    public WeaponBobMetadata Bob { get; set; } = new();
    public List<WeaponAnimationMetadata> Animations { get; set; } = [];
}

public sealed class WeaponSoundMetadata
{
    public string Fire { get; set; } = string.Empty;
}

public sealed class WeaponFireBehaviorMetadata
{
    public bool Automatic { get; set; }
    public double IntervalMs { get; set; }
    public double SoundIntervalMs { get; set; }
}

public sealed class WeaponAmmoMetadata
{
    public int MagazineSize { get; set; }
    public int MaxAmmo { get; set; }
    public int InitialAmmo { get; set; } = -1;
}

public sealed class WeaponPointMetadata
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class WeaponBobMetadata
{
    public bool Enabled { get; set; } = true;
    public double Amount { get; set; } = 1.0;
    public double AmplitudeX { get; set; } = 6.0;
    public double AmplitudeY { get; set; } = 4.0;
    public double FrequencyHz { get; set; } = 3.0;
}

public sealed class WeaponAnimationMetadata
{
    public string Name { get; set; } = string.Empty;
    public double FrameDurationMs { get; set; } = 100.0;
    public bool Loop { get; set; } = true;
    public List<string> Files { get; set; } = [];
}
