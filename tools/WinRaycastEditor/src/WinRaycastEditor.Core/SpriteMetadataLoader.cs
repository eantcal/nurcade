using System.Text.Json;

namespace WinRaycastEditor.Core;

public sealed class SpriteMetadataLoadResult
{
    public bool Success => Errors.Count == 0 && Document is not null;
    public SpriteMetadataDocument? Document { get; set; }
    public List<string> Errors { get; } = [];
}

public static class SpriteMetadataLoader
{
    private static readonly IReadOnlyDictionary<string, int> ExpectedDirections =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["front"] = 0,
            ["front_right"] = 45,
            ["right"] = 90,
            ["back_right"] = 135,
            ["back"] = 180,
            ["back_left"] = 225,
            ["left"] = 270,
            ["front_left"] = 315
        };
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "BMP",
        "PNG"
    };
    private const int MaxSupportedSpriteResolution = 1024;

    public static SpriteMetadataLoadResult Load(string path)
    {
        var result = new SpriteMetadataLoadResult();
        if (!File.Exists(path)) {
            result.Errors.Add($"Cannot open sprite metadata file: {path}");
            return result;
        }

        JsonDocument json;
        try {
            json = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException error) {
            result.Errors.Add($"Invalid JSON: {error.Message}");
            return result;
        }

        using (json) {
            if (json.RootElement.ValueKind != JsonValueKind.Object) {
                result.Errors.Add("Sprite metadata root must be a JSON object.");
                return result;
            }

            var document = new SpriteMetadataDocument();
            var metadataDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;
            var supportedResolutions = new HashSet<int>();

            ReadHeader(json.RootElement, document, supportedResolutions, result.Errors);
            ReadDirectionsOrAnimations(json.RootElement, document, supportedResolutions, metadataDirectory, result.Errors);
            ReadLod(json.RootElement, document, supportedResolutions, result.Errors);

            result.Document = document;
            return result;
        }
    }

    private static void ReadHeader(
        JsonElement root,
        SpriteMetadataDocument document,
        HashSet<int> supportedResolutions,
        List<string> errors)
    {
        document.SpriteSet = ReadString(root, "spriteSet", errors) ?? string.Empty;
        document.Format = ReadString(root, "format", errors) ?? string.Empty;
        if (!SupportedFormats.Contains(document.Format)) {
            errors.Add($"Unsupported sprite format: {document.Format}");
        }

        if (!root.TryGetProperty("transparentColor", out var color)
            || color.ValueKind != JsonValueKind.Array
            || color.GetArrayLength() != 3) {
            errors.Add("transparentColor must be an RGB array.");
        }
        else {
            var values = color.EnumerateArray().Select(item => ReadByte(item, errors, "transparentColor")).ToArray();
            if (values.Length == 3) {
                document.TransparentColor = values;
            }
        }

        if (!root.TryGetProperty("supportedResolutions", out var resolutions)
            || resolutions.ValueKind != JsonValueKind.Array) {
            errors.Add("supportedResolutions must be an array.");
        }
        else {
            foreach (var resolution in resolutions.EnumerateArray()) {
                if (!resolution.TryGetInt32(out var value) || value <= 0) {
                    errors.Add("supportedResolutions must contain positive integers.");
                    continue;
                }

                if (value > MaxSupportedSpriteResolution) {
                    errors.Add($"Supported sprite resolution out of range: {value}");
                    continue;
                }

                supportedResolutions.Add(value);
                document.SupportedResolutions.Add(value);
            }
        }

        document.DefaultResolution = ReadPositiveInt(root, "defaultResolution", errors);
        document.MaxResolution = ReadPositiveInt(root, "maxResolution", errors);
        if (document.MaxResolution > MaxSupportedSpriteResolution) {
            errors.Add($"maxResolution exceeds current engine limit of {MaxSupportedSpriteResolution}.");
        }
    }

    private static void ReadDirectionsOrAnimations(
        JsonElement root,
        SpriteMetadataDocument document,
        HashSet<int> supportedResolutions,
        string metadataDirectory,
        List<string> errors)
    {
        if (root.TryGetProperty("animations", out var animations)) {
            ReadAnimations(animations, document, supportedResolutions, metadataDirectory, errors);
            return;
        }

        var directions = ReadDirections(root, "Sprite metadata", supportedResolutions, metadataDirectory, errors);
        document.Directions.AddRange(directions);
        if (directions.Count > 0) {
            var idle = new SpriteAnimationMetadata {
                Name = "idle",
                Loop = true
            };
            idle.Directions.AddRange(CloneDirections(directions));
            idle.Frames.Add(new SpriteAnimationFrameMetadata {
                Directions = CloneDirections(directions)
            });
            document.Animations.Add(idle);
        }
    }

    private static void ReadAnimations(
        JsonElement animationsJson,
        SpriteMetadataDocument document,
        HashSet<int> supportedResolutions,
        string metadataDirectory,
        List<string> errors)
    {
        if (animationsJson.ValueKind != JsonValueKind.Object) {
            errors.Add("animations must be an object.");
            return;
        }

        foreach (var animationItem in animationsJson.EnumerateObject()) {
            if (animationItem.Value.ValueKind != JsonValueKind.Object) {
                errors.Add($"Animation {animationItem.Name} must be an object.");
                continue;
            }

            var animation = new SpriteAnimationMetadata { Name = animationItem.Name };
            if (animationItem.Value.TryGetProperty("frameDurationMs", out var duration)
                && duration.TryGetDouble(out var frameDurationMs)) {
                animation.FrameDurationMs = frameDurationMs;
            }

            if (animationItem.Value.TryGetProperty("loop", out var loop)
                && (loop.ValueKind == JsonValueKind.True || loop.ValueKind == JsonValueKind.False)) {
                animation.Loop = loop.GetBoolean();
            }

            if (animationItem.Value.TryGetProperty("frames", out var framesJson)) {
                if (framesJson.ValueKind != JsonValueKind.Array) {
                    errors.Add($"Animation {animation.Name} frames must be an array.");
                    continue;
                }

                foreach (var frameJson in framesJson.EnumerateArray()) {
                    if (frameJson.ValueKind != JsonValueKind.Object
                        || !frameJson.TryGetProperty("directions", out var frameDirectionsJson)) {
                        errors.Add($"Each frame in animation {animation.Name} must contain directions.");
                        continue;
                    }

                    var frameDirections = ReadDirections(
                        frameDirectionsJson,
                        $"Animation {animation.Name} frame",
                        supportedResolutions,
                        metadataDirectory,
                        errors);
                    if (frameDirections.Count > 0) {
                        animation.Frames.Add(new SpriteAnimationFrameMetadata {
                            Directions = frameDirections
                        });
                    }
                }

                if (animation.Frames.Count > 0) {
                    animation.Directions.AddRange(CloneDirections(animation.Frames[0].Directions));
                }
            }
            else if (animationItem.Value.TryGetProperty("directions", out var directionsJson)) {
                var directions = ReadDirections(
                    directionsJson,
                    $"Animation {animation.Name}",
                    supportedResolutions,
                    metadataDirectory,
                    errors);
                animation.Directions.AddRange(directions);
                if (directions.Count > 0) {
                    animation.Frames.Add(new SpriteAnimationFrameMetadata {
                        Directions = CloneDirections(directions)
                    });
                }
            }
            else {
                errors.Add($"Animation {animation.Name} is missing directions.");
                continue;
            }

            if (animation.Directions.Count > 0) {
                document.Animations.Add(animation);
            }
        }

        var idle = document.Animations.FirstOrDefault(item => item.Name == "idle");
        if (idle is null) {
            errors.Add("animations must define an idle clip.");
        }
        else {
            document.Directions.AddRange(CloneDirections(idle.Directions));
        }
    }

    private static List<SpriteDirectionMetadata> ReadDirections(
        JsonElement root,
        string ownerName,
        HashSet<int> supportedResolutions,
        string metadataDirectory,
        List<string> errors)
    {
        var directions = root;
        if (root.ValueKind == JsonValueKind.Object) {
            if (!root.TryGetProperty("directions", out directions)) {
                errors.Add("directions must be an array.");
                return [];
            }
        }

        if (directions.ValueKind != JsonValueKind.Array) {
            errors.Add($"{ownerName} directions must be an array.");
            return [];
        }

        var result = new List<SpriteDirectionMetadata>();
        var directionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directionJson in directions.EnumerateArray()) {
            if (directionJson.ValueKind != JsonValueKind.Object) {
                errors.Add("Each direction must be an object.");
                continue;
            }

            var name = ReadString(directionJson, "name", errors);
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            if (!ExpectedDirections.TryGetValue(name, out var expectedAngle)) {
                errors.Add($"Unsupported direction name: {name}");
                continue;
            }

            if (!directionNames.Add(name)) {
                errors.Add($"Duplicate direction name: {name}");
                continue;
            }

            var angle = ReadInt(directionJson, "angle", errors);
            if (angle != expectedAngle) {
                errors.Add($"Direction {name} has an invalid angle.");
                continue;
            }

            var direction = new SpriteDirectionMetadata { Name = name, Angle = angle };
            ReadDirectionFiles(directionJson, direction, supportedResolutions, metadataDirectory, errors);

            if (direction.Files.Count == 0) {
                errors.Add($"Direction {name} has no valid files.");
                continue;
            }

            result.Add(direction);
        }

        if (directionNames.Count != ExpectedDirections.Count) {
            errors.Add($"{ownerName} must define all 8 supported directions.");
        }

        return result;
    }

    private static void ReadDirectionFiles(
        JsonElement directionJson,
        SpriteDirectionMetadata direction,
        HashSet<int> supportedResolutions,
        string metadataDirectory,
        List<string> errors)
    {
        if (!directionJson.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Object) {
            errors.Add($"Direction {direction.Name} is missing files.");
            return;
        }

        foreach (var fileItem in files.EnumerateObject()) {
            if (!int.TryParse(fileItem.Name, out var resolution)) {
                errors.Add($"Invalid resolution key in direction {direction.Name}");
                continue;
            }

            if (!supportedResolutions.Contains(resolution)) {
                errors.Add($"Direction {direction.Name} references unsupported resolution {resolution}");
                continue;
            }

            if (fileItem.Value.ValueKind != JsonValueKind.String) {
                errors.Add($"File entry must be a string in direction {direction.Name}");
                continue;
            }

            var filePath = fileItem.Value.GetString() ?? string.Empty;
            var fullPath = Path.GetFullPath(Path.Combine(metadataDirectory, filePath));
            if (!File.Exists(fullPath)) {
                errors.Add($"Missing sprite image file: {fullPath}");
                continue;
            }

            direction.Files[resolution] = filePath;
        }
    }

    private static List<SpriteDirectionMetadata> CloneDirections(IEnumerable<SpriteDirectionMetadata> directions)
    {
        return directions
            .Select(direction => new SpriteDirectionMetadata {
                Name = direction.Name,
                Angle = direction.Angle,
                Files = direction.Files.ToDictionary(item => item.Key, item => item.Value)
            })
            .ToList();
    }

    private static void ReadLod(
        JsonElement root,
        SpriteMetadataDocument document,
        HashSet<int> supportedResolutions,
        List<string> errors)
    {
        if (!root.TryGetProperty("lod", out var lod)
            || lod.ValueKind != JsonValueKind.Array) {
            errors.Add("lod must be an array.");
            return;
        }

        foreach (var lodJson in lod.EnumerateArray()) {
            if (lodJson.ValueKind != JsonValueKind.Object
                || !lodJson.TryGetProperty("maxDistance", out var maxDistanceJson)
                || !lodJson.TryGetProperty("resolution", out var resolutionJson)
                || !maxDistanceJson.TryGetDouble(out var maxDistance)
                || !resolutionJson.TryGetInt32(out var resolution)) {
                errors.Add("Each lod entry must contain maxDistance and resolution.");
                continue;
            }

            if (maxDistance <= 0.0) {
                errors.Add("LOD maxDistance must be positive.");
                continue;
            }

            if (!supportedResolutions.Contains(resolution)) {
                errors.Add($"LOD references unsupported resolution {resolution}");
                continue;
            }

            document.Lod.Add(new SpriteLodMetadata {
                MaxDistance = maxDistance,
                Resolution = resolution
            });
        }

        if (document.Lod.Count == 0) {
            errors.Add("At least one valid LOD rule is required.");
        }
    }

    private static string? ReadString(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) {
            errors.Add($"Missing or invalid string field: {name}.");
            return null;
        }

        return value.GetString();
    }

    private static int ReadInt(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetInt32(out var number)) {
            errors.Add($"Missing or invalid integer field: {name}.");
            return 0;
        }

        return number;
    }

    private static int ReadPositiveInt(JsonElement root, string name, List<string> errors)
    {
        var number = ReadInt(root, name, errors);
        if (number <= 0) {
            errors.Add($"{name} must be positive.");
        }

        return number;
    }

    private static byte ReadByte(JsonElement value, List<string> errors, string fieldName)
    {
        if (!value.TryGetInt32(out var number)) {
            errors.Add($"{fieldName} values must be integers.");
            return 0;
        }

        if (number < byte.MinValue || number > byte.MaxValue) {
            errors.Add($"{fieldName} values must be in 0..255.");
            return 0;
        }

        return (byte)number;
    }
}
