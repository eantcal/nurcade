using System.Text.Json.Serialization;

namespace WinRaycastEditor.Core;

public sealed class WorldDocument
{
    public string Format { get; set; } = "winraycast.world";
    public int Version { get; set; } = 2;
    public string Name { get; set; } = string.Empty;
    public WorldGridDefinition Grid { get; set; } = new();
    public WorldPlayerStart PlayerStart { get; set; } = new();
    public WorldCombatStats PlayerStats { get; set; } = new();
    public WorldPlayerTurn PlayerTurn { get; set; } = new();
    public double Brightness { get; set; } = 1.0;
    public double DepthShading { get; set; } = 100.0;
    public string TextureRoot { get; set; } = ".";
    public string? DefaultHorizonImage { get; set; }
    public WorldPlayerWeapon? PlayerWeapon { get; set; }
    public List<WorldPlayerWeapon> PlayerWeapons { get; set; } = [];
    public WorldBackgroundMusic? BackgroundMusic { get; set; }
    public string? ActiveLayer { get; set; }
    public string? StartLayer { get; set; }
    public Dictionary<string, WorldTextureDefinition> Textures { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WorldBlockDefinition> Blocks { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<List<string>> Cells { get; set; } = [];
    public List<WorldLayerDefinition> Layers { get; set; } = [];
    public List<WorldLayerTransition> LayerTransitions { get; set; } = [];
    public WorldGameGoal? GameGoal { get; set; }
    public List<string> SpriteSets { get; set; } = [];
    public List<EditorSpriteInstance> SpriteInstances { get; set; } = [];
}

public sealed class WorldGridDefinition
{
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int CellWidth { get; set; } = 512;
    public int CellDepth { get; set; } = 512;
    public int DefaultWallHeight { get; set; } = 512;
}

public sealed class WorldPlayerStart
{
    public double XCell { get; set; } = 1.5;
    public double YCell { get; set; } = 1.5;
    public double FacingDegrees { get; set; }
}

public sealed class WorldLayerDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Brightness { get; set; }
    public double? DepthShading { get; set; }
    public WorldGridDefinition? Grid { get; set; }
    public WorldPlayerStart? PlayerStart { get; set; }
    public string? DefaultHorizonImage { get; set; }
    public WorldBackgroundMusic? BackgroundMusic { get; set; }
    public List<List<string>> Cells { get; set; } = [];
    public List<EditorSpriteInstance> SpriteInstances { get; set; } = [];
}

public sealed class WorldLayerTransition
{
    public string FromLayer { get; set; } = string.Empty;
    public string ToLayer { get; set; } = string.Empty;
    public string? RequiredKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerBlockId { get; set; }

    public WorldLayerTransitionTrigger? Trigger { get; set; }
    public double WaitSeconds { get; set; } = 1.5;
    public WorldPlayerStart? TargetPlayerStart { get; set; }

    [JsonIgnore]
    public string EffectiveTriggerBlockId =>
        !string.IsNullOrWhiteSpace(TriggerBlockId)
            ? TriggerBlockId!
            : Trigger?.BlockId ?? string.Empty;
}

public sealed class WorldGameGoal
{
    public string Layer { get; set; } = string.Empty;
    public int Row { get; set; }
    public int Column { get; set; }
    public string? RequiredKey { get; set; }
}

public sealed class WorldLayerTransitionTrigger
{
    public string BlockId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Row { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Column { get; set; }
}

public sealed class WorldCombatStats
{
    public double MaxHealth { get; set; } = 100.0;
    public double Health { get; set; } = 100.0;
}

/// <summary>
/// Progressive player-turn feel (degrees/second). Consumed by the engine's WorldJsonLoader
/// (<c>playerTurn</c> object); keep the defaults in sync with WorldMap / WinRayCast.cpp.
/// </summary>
public sealed class WorldPlayerTurn
{
    public double BaseDegreesPerSecond { get; set; } = 90.0;
    public double MaxDegreesPerSecond { get; set; } = 300.0;
    public double AccelerationDegreesPerSecondSquared { get; set; } = 360.0;
}

public sealed class WorldPlayerWeapon
{
    public string File { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public bool Unlocked { get; set; } = true;
    public double ScreenHeightFraction { get; set; }
}

public sealed class WorldBackgroundMusic
{
    public string File { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool Loop { get; set; } = true;
    public int VolumePercent { get; set; } = 80;
}

public sealed class WorldTextureDefinition
{
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

public sealed class WorldBlockDefinition
{
    public string Name { get; set; } = string.Empty;
    public WorldSurface? Floor { get; set; }
    public WorldSurface? Ceiling { get; set; }
    public List<WorldWallSpan> Walls { get; set; } = [];
    public WorldDoorDefinition? Door { get; set; }
    public List<WorldBlockAnimationDefinition>? Animations { get; set; }
    public string? HorizonImage { get; set; }
}

public sealed class WorldDoorDefinition
{
    public bool Enabled { get; set; } = true;
    public bool BlocksWhenClosed { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequiredKey { get; set; }

    public double TriggerDistanceCells { get; set; } = 1.25;
    public double OpenTimeSeconds { get; set; } = 0.45;
    public double CloseDelaySeconds { get; set; } = 1.0;
    public string? OpenSound { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int OpenSoundVolumePercent { get; set; }

    public List<string> Frames { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LockedOverlays { get; set; }
}

public sealed class WorldBlockAnimationDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = "wall";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WallIndex { get; set; }

    public string Face { get; set; } = "all";
    public double FrameDurationMs { get; set; } = 120.0;
    public bool Loop { get; set; } = true;
    public List<string> Frames { get; set; } = [];
}

public sealed class WorldSurface
{
    public string Texture { get; set; } = string.Empty;
    public int Height { get; set; }
}

public sealed class WorldWallSpan
{
    public string Kind { get; set; } = "solid";
    public string Texture { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? FaceTextures { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, bool>? FacesEnabled { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InteriorTexture { get; set; }

    public int Bottom { get; set; }
    public int Top { get; set; } = 512;

    [JsonPropertyOrder(50)]
    public bool Collision { get; set; } = true;

    [JsonPropertyOrder(51)]
    public bool Passable
    {
        get => !Collision;
        set => Collision = !value;
    }
}
