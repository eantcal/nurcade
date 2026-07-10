using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinRaycastEditor.Core;

public sealed class WorldJsonLoadResult
{
    public bool Success => Errors.Count == 0 && Document is not null;
    public WorldDocument? Document { get; set; }
    public List<string> Errors { get; } = [];
}

public static class WorldJsonDocumentService
{
    public const int SchemaVersion = 2;
    public const string EmptyBlockId = "00";

    private static readonly JsonSerializerOptions ReadOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static WorldJsonLoadResult Load(string path)
    {
        var result = new WorldJsonLoadResult();
        if (!File.Exists(path)) {
            result.Errors.Add($"Cannot open world JSON file: {path}");
            return result;
        }

        TryParseAndValidate(File.ReadAllText(path), out var document, out var errors);
        result.Errors.AddRange(errors);
        result.Document = document;
        return result;
    }

    /// <summary>
    /// Parses <paramref name="json"/> into a <see cref="WorldDocument"/> and validates it,
    /// collecting both JSON syntax errors and semantic validation errors. Returns true only
    /// when parsing and validation both succeed; <paramref name="document"/> is non-null
    /// whenever the JSON parsed (even if validation reported problems).
    /// </summary>
    public static bool TryParseAndValidate(
        string json,
        out WorldDocument? document,
        out List<string> errors)
    {
        errors = [];
        document = null;

        WorldDocument? parsed;
        try {
            parsed = JsonSerializer.Deserialize<WorldDocument>(json, ReadOptions);
        }
        catch (JsonException error) {
            errors.Add($"Invalid world JSON: {error.Message}");
            return false;
        }

        if (parsed is null) {
            errors.Add("World JSON is empty.");
            return false;
        }

        Validate(parsed, errors);
        document = parsed;
        return errors.Count == 0;
    }

    public static void Save(WorldDocument document, string path)
    {
        File.WriteAllText(path, Serialize(document));
    }

    public static string Serialize(WorldDocument document)
    {
        return JsonSerializer.Serialize(document, WriteOptions);
    }

    public static void Validate(WorldDocument document, List<string> errors)
    {
        if (!string.Equals(document.Format, "winraycast.world", StringComparison.Ordinal)) {
            errors.Add($"Unsupported world format: {document.Format}");
        }

        if (document.Version != SchemaVersion) {
            errors.Add($"Unsupported world version: {document.Version} (expected {SchemaVersion}).");
        }

        if (document.Grid.Columns <= 0 || document.Grid.Rows <= 0) {
            errors.Add("World grid rows and columns must be positive.");
        }

        if (document.Grid.CellWidth <= 0 || document.Grid.CellDepth <= 0) {
            errors.Add("World grid cell dimensions must be positive.");
        }

        if (document.Grid.DefaultWallHeight <= 0) {
            errors.Add("World grid default wall height must be positive.");
        }

        if (!document.Blocks.ContainsKey(EmptyBlockId)) {
            errors.Add($"World blocks must include an empty block with id '{EmptyBlockId}'.");
        }

        if (document.PlayerStats.MaxHealth <= 0.0) {
            errors.Add("Player max health must be positive.");
        }

        if (document.PlayerStats.Health < 0.0
            || document.PlayerStats.Health > document.PlayerStats.MaxHealth) {
            errors.Add("Player health must stay between zero and max health.");
        }

        if (document.Brightness < 0.05 || document.Brightness > 2.0) {
            errors.Add("World brightness must be between 0.05 and 2.0.");
        }
        if (document.DepthShading < 1.0 || document.DepthShading > 1000.0) {
            errors.Add("World depth shading must be between 1 and 1000.");
        }

        ValidateBackgroundMusic(document.BackgroundMusic, "World", errors);

        foreach (var entry in document.Blocks) {
            ValidateBlock(document, entry.Key, entry.Value, errors);
        }

        if (document.Layers.Count == 0 && document.Cells.Count != document.Grid.Rows) {
            errors.Add("World cell row count does not match grid.rows.");
        }

        ValidateCellMatrix(
            document,
            document.Cells,
            document.Grid,
            "World",
            errors);

        var layerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in document.Layers) {
            if (string.IsNullOrWhiteSpace(layer.Id)) {
                errors.Add("World layer has no id.");
                continue;
            }

            if (!layerIds.Add(layer.Id)) {
                errors.Add($"World layer id '{layer.Id}' is declared more than once.");
            }

            ValidateBackgroundMusic(layer.BackgroundMusic, $"Layer '{layer.Id}'", errors);

            if (layer.Brightness is < 0.05 or > 2.0) {
                errors.Add($"Layer '{layer.Id}' brightness must be between 0.05 and 2.0.");
            }
            if (layer.DepthShading is < 1.0 or > 1000.0) {
                errors.Add($"Layer '{layer.Id}' depth shading must be between 1 and 1000.");
            }

            ValidateCellMatrix(
                document,
                layer.Cells,
                layer.Grid ?? document.Grid,
                $"Layer '{layer.Id}'",
                errors);
        }

        if (document.Layers.Count > 0) {
            var selectedLayer = document.ActiveLayer ?? document.StartLayer;
            if (!string.IsNullOrWhiteSpace(selectedLayer)
                && !layerIds.Contains(selectedLayer)) {
                errors.Add($"Selected world layer '{selectedLayer}' does not exist.");
            }

            foreach (var transition in document.LayerTransitions) {
                ValidateLayerTransition(transition, layerIds, errors);
            }

            if (document.GameGoal is not null) {
                var goalLayer = document.Layers.FirstOrDefault(layer =>
                    string.Equals(layer.Id, document.GameGoal.Layer, StringComparison.OrdinalIgnoreCase));
                if (goalLayer is null) {
                    errors.Add($"Game goal references unknown layer '{document.GameGoal.Layer}'.");
                }
                else {
                    var goalGrid = goalLayer.Grid ?? document.Grid;
                    if (document.GameGoal.Row < 0
                        || document.GameGoal.Column < 0
                        || document.GameGoal.Row >= goalGrid.Rows
                        || document.GameGoal.Column >= goalGrid.Columns) {
                        errors.Add(
                            $"Game goal cell ({document.GameGoal.Row},{document.GameGoal.Column}) "
                            + $"is outside layer '{goalLayer.Id}'.");
                    }
                }
            }
        }
        else if (document.GameGoal is not null) {
            errors.Add("Game goal requires a layered world.");
        }
    }

    private static void ValidateCellMatrix(
        WorldDocument document,
        List<List<string>> cells,
        WorldGridDefinition grid,
        string label,
        List<string> errors)
    {
        if (cells.Count == 0) {
            return;
        }

        if (cells.Count != grid.Rows) {
            errors.Add($"{label} cell row count does not match grid.rows.");
        }

        for (var row = 0; row < cells.Count; ++row) {
            if (cells[row].Count != grid.Columns) {
                errors.Add($"{label} cell row {row} does not match grid.columns.");
            }

            for (var column = 0; column < cells[row].Count; ++column) {
                var blockId = cells[row][column];
                if (string.IsNullOrWhiteSpace(blockId)) {
                    errors.Add($"{label} cell ({row},{column}) has no block id.");
                    continue;
                }

                if (!document.Blocks.ContainsKey(blockId)) {
                    errors.Add($"{label} cell ({row},{column}) references unknown block id '{blockId}'.");
                }
            }
        }
    }

    private static void ValidateLayerTransition(
        WorldLayerTransition transition,
        HashSet<string> layerIds,
        List<string> errors)
    {
        if (!layerIds.Contains(transition.FromLayer)) {
            errors.Add($"Layer transition references unknown source layer '{transition.FromLayer}'.");
        }

        if (!layerIds.Contains(transition.ToLayer)) {
            errors.Add($"Layer transition references unknown target layer '{transition.ToLayer}'.");
        }

        if (string.IsNullOrWhiteSpace(transition.EffectiveTriggerBlockId)) {
            errors.Add("Layer transition has no trigger block id.");
        }

        if (transition.Trigger is not null
            && ((transition.Trigger.Row is null) != (transition.Trigger.Column is null))) {
            errors.Add("Layer transition trigger row and column must be specified together.");
        }

        if (transition.WaitSeconds < 0.0) {
            errors.Add("Layer transition wait seconds must not be negative.");
        }
    }

    private static void ValidateBackgroundMusic(
        WorldBackgroundMusic? music,
        string label,
        List<string> errors)
    {
        if (music is null || !music.Enabled) {
            return;
        }

        if (music.VolumePercent < 0 || music.VolumePercent > 100) {
            errors.Add($"{label} background music volume must be between 0 and 100.");
        }

        if (string.IsNullOrWhiteSpace(music.File)) {
            errors.Add($"{label} background music is enabled but has no file.");
        }
    }

    private static void ValidateBlock(
        WorldDocument document,
        string blockId,
        WorldBlockDefinition block,
        List<string> errors)
    {
        ValidateSurface(document, block.Floor, blockId, "floor", errors);
        ValidateSurface(document, block.Ceiling, blockId, "ceiling", errors);
        ValidateDoor(document, block.Door, blockId, errors);
        ValidateBlockAnimations(document, block, blockId, errors);

        foreach (var wall in block.Walls) {
            if (string.IsNullOrWhiteSpace(wall.Texture)
                && (wall.FaceTextures is null || wall.FaceTextures.Count == 0)) {
                errors.Add($"Block '{blockId}' has a wall with no texture.");
            }
            else if (!string.IsNullOrWhiteSpace(wall.Texture)
                && !document.Textures.ContainsKey(wall.Texture)) {
                errors.Add($"Block '{blockId}' references unknown wall texture '{wall.Texture}'.");
            }

            if (wall.FaceTextures is not null) {
                foreach (var faceTexture in wall.FaceTextures) {
                    if (!IsKnownFace(faceTexture.Key)) {
                        errors.Add($"Block '{blockId}' references unknown wall face '{faceTexture.Key}'.");
                    }
                    else if (string.IsNullOrWhiteSpace(faceTexture.Value)) {
                        errors.Add($"Block '{blockId}' has an empty {faceTexture.Key} wall texture.");
                    }
                    else if (!document.Textures.ContainsKey(faceTexture.Value)) {
                        errors.Add(
                            $"Block '{blockId}' references unknown {faceTexture.Key} wall texture '{faceTexture.Value}'.");
                    }
                }
            }

            if (wall.Top <= wall.Bottom) {
                errors.Add($"Block '{blockId}' has an invalid wall span.");
            }
        }
    }

    private static void ValidateBlockAnimations(
        WorldDocument document,
        WorldBlockDefinition block,
        string blockId,
        List<string> errors)
    {
        if (block.Animations is null) {
            return;
        }

        for (var index = 0; index < block.Animations.Count; ++index) {
            var animation = block.Animations[index];
            var label = string.IsNullOrWhiteSpace(animation.Name)
                ? $"#{index}"
                : $"'{animation.Name}'";

            if (string.IsNullOrWhiteSpace(animation.Name)) {
                errors.Add($"Block '{blockId}' has an unnamed animation at index {index}.");
            }

            if (!IsKnownAnimationTarget(animation.Target)) {
                errors.Add($"Block '{blockId}' animation {label} has unknown target '{animation.Target}'.");
            }

            if (animation.FrameDurationMs <= 0.0) {
                errors.Add($"Block '{blockId}' animation {label} has a non-positive frame duration.");
            }

            if (animation.Frames.Count == 0) {
                errors.Add($"Block '{blockId}' animation {label} has no frames.");
            }

            if (AnimationTargetsWall(animation.Target)) {
                if (animation.WallIndex is null
                    || animation.WallIndex.Value < 0
                    || animation.WallIndex.Value >= block.Walls.Count) {
                    errors.Add($"Block '{blockId}' animation {label} references an invalid wall index.");
                }

                if (!IsKnownAnimationFace(animation.Face)) {
                    errors.Add($"Block '{blockId}' animation {label} references unknown wall face '{animation.Face}'.");
                }
            }

            foreach (var frame in animation.Frames) {
                if (string.IsNullOrWhiteSpace(frame)) {
                    errors.Add($"Block '{blockId}' animation {label} has an empty frame texture.");
                }
                else if (!document.Textures.ContainsKey(frame)) {
                    errors.Add($"Block '{blockId}' animation {label} references unknown frame texture '{frame}'.");
                }
            }
        }
    }

    private static void ValidateDoor(
        WorldDocument document,
        WorldDoorDefinition? door,
        string blockId,
        List<string> errors)
    {
        if (door is null) {
            return;
        }

        if (door.TriggerDistanceCells < 0.0) {
            errors.Add($"Block '{blockId}' has a negative door trigger distance.");
        }

        if (door.OpenTimeSeconds <= 0.0) {
            errors.Add($"Block '{blockId}' has a non-positive door open time.");
        }

        if (door.CloseDelaySeconds < 0.0) {
            errors.Add($"Block '{blockId}' has a negative door close delay.");
        }

        foreach (var frame in door.Frames) {
            if (string.IsNullOrWhiteSpace(frame)) {
                errors.Add($"Block '{blockId}' has an empty door frame texture.");
            }
            else if (!document.Textures.ContainsKey(frame)) {
                errors.Add($"Block '{blockId}' references unknown door frame texture '{frame}'.");
            }
        }

        if (door.LockedOverlays is not null) {
            foreach (var overlay in door.LockedOverlays) {
                if (string.IsNullOrWhiteSpace(overlay.Key)) {
                    errors.Add($"Block '{blockId}' has an empty door locked overlay key.");
                }
                else if (string.IsNullOrWhiteSpace(overlay.Value)) {
                    errors.Add($"Block '{blockId}' has an empty locked overlay texture for key '{overlay.Key}'.");
                }
                else if (!document.Textures.ContainsKey(overlay.Value)) {
                    errors.Add(
                        $"Block '{blockId}' references unknown locked overlay texture '{overlay.Value}' for key '{overlay.Key}'.");
                }
            }
        }
    }

    private static bool IsKnownFace(string face)
    {
        return string.Equals(face, "north", StringComparison.OrdinalIgnoreCase)
            || string.Equals(face, "east", StringComparison.OrdinalIgnoreCase)
            || string.Equals(face, "south", StringComparison.OrdinalIgnoreCase)
            || string.Equals(face, "west", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownAnimationTarget(string target)
    {
        return string.Equals(target, "block", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "floor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "ceiling", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "wall", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "wallOverlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "overlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "door", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AnimationTargetsWall(string target)
    {
        return string.Equals(target, "wall", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "wallOverlay", StringComparison.OrdinalIgnoreCase)
            || string.Equals(target, "overlay", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownAnimationFace(string face)
    {
        return string.Equals(face, "all", StringComparison.OrdinalIgnoreCase)
            || IsKnownFace(face);
    }

    private static void ValidateSurface(
        WorldDocument document,
        WorldSurface? surface,
        string blockId,
        string surfaceName,
        List<string> errors)
    {
        if (surface is null || string.IsNullOrWhiteSpace(surface.Texture)) {
            return;
        }

        if (!document.Textures.ContainsKey(surface.Texture)) {
            errors.Add($"Block '{blockId}' references unknown {surfaceName} texture '{surface.Texture}'.");
        }
    }
}
