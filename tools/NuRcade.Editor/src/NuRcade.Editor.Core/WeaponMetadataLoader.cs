using System.Text.Json;

namespace NuRcade.Editor.Core;

public sealed class WeaponMetadataLoadResult
{
    public bool Success => Errors.Count == 0 && Document is not null;
    public WeaponMetadataDocument? Document { get; set; }
    public List<string> Errors { get; } = [];
}

public static class WeaponMetadataLoader
{
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "BMP",
        "PNG"
    };

    public static WeaponMetadataLoadResult Load(string path)
    {
        var result = new WeaponMetadataLoadResult();
        if (!File.Exists(path)) {
            result.Errors.Add($"Cannot open weapon metadata file: {path}");
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
                result.Errors.Add("Weapon metadata root must be a JSON object.");
                return result;
            }

            var metadataDirectory =
                Path.GetDirectoryName(Path.GetFullPath(path)) ?? Environment.CurrentDirectory;
            var document = new WeaponMetadataDocument {
                Weapon = ReadOptionalString(json.RootElement, "weapon", "view_weapon"),
                Format = ReadOptionalString(json.RootElement, "format", "PNG"),
                FrameWidth = ReadPositiveInt(json.RootElement, "frameWidth", 320, result.Errors),
                FrameHeight = ReadPositiveInt(json.RootElement, "frameHeight", 220, result.Errors),
                ScreenHeightFraction = ReadOptionalDouble(json.RootElement, "screenHeightFraction", 0.45),
                Damage = ReadOptionalDouble(json.RootElement, "damage", 0.0),
                RangeCells = ReadOptionalDouble(json.RootElement, "rangeCells", 8.0)
            };

            if (!SupportedFormats.Contains(document.Format)) {
                result.Errors.Add($"Unsupported weapon format: {document.Format}");
            }

            if (document.ScreenHeightFraction <= 0.0) {
                result.Errors.Add("screenHeightFraction must be positive.");
            }

            if (document.Damage < 0.0) {
                result.Errors.Add("damage must not be negative.");
            }

            if (document.RangeCells < 0.0) {
                result.Errors.Add("rangeCells must not be negative.");
            }

            document.Anchor = ReadPoint(json.RootElement, "anchor", new WeaponPointMetadata { X = 0.5, Y = 1.0 });
            document.BaseOffset = ReadPoint(json.RootElement, "baseOffset", new WeaponPointMetadata());
            document.Bob = ReadBob(json.RootElement);
            document.FireBehavior = ReadFireBehavior(json.RootElement);
            document.Sounds = ReadSounds(json.RootElement, metadataDirectory, result.Errors);
            document.Ammo = ReadAmmo(json.RootElement, result.Errors);

            ReadAnimations(json.RootElement, document, metadataDirectory, result.Errors);
            result.Document = document;
            return result;
        }
    }

    private static void ReadAnimations(
        JsonElement root,
        WeaponMetadataDocument document,
        string metadataDirectory,
        List<string> errors)
    {
        if (!root.TryGetProperty("animations", out var animationsJson)
            || animationsJson.ValueKind != JsonValueKind.Object) {
            errors.Add("animations must be an object.");
            return;
        }

        foreach (var animationItem in animationsJson.EnumerateObject()) {
            if (animationItem.Value.ValueKind != JsonValueKind.Object) {
                errors.Add($"Animation {animationItem.Name} must be an object.");
                continue;
            }

            var animation = new WeaponAnimationMetadata {
                Name = animationItem.Name,
                FrameDurationMs = ReadOptionalDouble(animationItem.Value, "frameDurationMs", 100.0),
                Loop = ReadOptionalBool(animationItem.Value, "loop", true)
            };

            if (!animationItem.Value.TryGetProperty("files", out var filesJson)
                || filesJson.ValueKind != JsonValueKind.Array) {
                errors.Add($"Animation {animation.Name} files must be an array.");
                continue;
            }

            foreach (var fileJson in filesJson.EnumerateArray()) {
                if (fileJson.ValueKind != JsonValueKind.String) {
                    errors.Add($"Animation {animation.Name} files must contain strings.");
                    continue;
                }

                var relativePath = fileJson.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(relativePath)) {
                    errors.Add($"Animation {animation.Name} contains an empty file path.");
                    continue;
                }

                var absolutePath = Path.GetFullPath(Path.Combine(metadataDirectory, relativePath));
                if (!File.Exists(absolutePath)) {
                    errors.Add($"Missing weapon image file: {absolutePath}");
                }

                animation.Files.Add(relativePath);
            }

            if (animation.Files.Count == 0) {
                errors.Add($"Animation {animation.Name} has no valid files.");
                continue;
            }

            document.Animations.Add(animation);
        }

        if (document.Animations.Count == 0) {
            errors.Add("At least one weapon animation is required.");
        }

        if (!document.Animations.Any(animation =>
                string.Equals(animation.Name, "idle", StringComparison.OrdinalIgnoreCase))) {
            errors.Add("animations must define an idle clip.");
        }
    }

    private static WeaponPointMetadata ReadPoint(JsonElement root, string name, WeaponPointMetadata fallback)
    {
        if (!root.TryGetProperty(name, out var pointJson)
            || pointJson.ValueKind != JsonValueKind.Object) {
            return fallback;
        }

        return new WeaponPointMetadata {
            X = ReadOptionalDouble(pointJson, "x", fallback.X),
            Y = ReadOptionalDouble(pointJson, "y", fallback.Y)
        };
    }

    private static WeaponBobMetadata ReadBob(JsonElement root)
    {
        var fallback = new WeaponBobMetadata();
        if (!root.TryGetProperty("bob", out var bobJson)
            || bobJson.ValueKind != JsonValueKind.Object) {
            return fallback;
        }

        return new WeaponBobMetadata {
            Enabled = ReadOptionalBool(bobJson, "enabled", fallback.Enabled),
            Amount = ReadOptionalDouble(bobJson, "amount", fallback.Amount),
            AmplitudeX = ReadOptionalDouble(bobJson, "amplitudeX", fallback.AmplitudeX),
            AmplitudeY = ReadOptionalDouble(bobJson, "amplitudeY", fallback.AmplitudeY),
            FrequencyHz = ReadOptionalDouble(bobJson, "frequencyHz", fallback.FrequencyHz)
        };
    }

    private static WeaponFireBehaviorMetadata? ReadFireBehavior(JsonElement root)
    {
        if (root.TryGetProperty("fireBehavior", out var fireBehaviorJson)
            && fireBehaviorJson.ValueKind == JsonValueKind.Object) {
            return new WeaponFireBehaviorMetadata {
                Automatic = ReadOptionalBool(fireBehaviorJson, "automatic", false),
                IntervalMs = ReadOptionalDouble(fireBehaviorJson, "intervalMs", 0.0),
                SoundIntervalMs = ReadOptionalDouble(fireBehaviorJson, "soundIntervalMs", 0.0)
            };
        }

        var legacyAutomatic = ReadOptionalBool(root, "automaticFire", false);
        var legacyIntervalMs = ReadOptionalDouble(root, "fireIntervalMs", 0.0);
        return legacyAutomatic || legacyIntervalMs > 0.0
            ? new WeaponFireBehaviorMetadata {
                Automatic = legacyAutomatic,
                IntervalMs = legacyIntervalMs
            }
            : null;
    }

    private static WeaponSoundMetadata? ReadSounds(
        JsonElement root,
        string metadataDirectory,
        List<string> errors)
    {
        if (!root.TryGetProperty("sounds", out var soundsJson)
            || soundsJson.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var sounds = new WeaponSoundMetadata {
            Fire = ReadOptionalString(soundsJson, "fire", string.Empty)
        };

        if (!string.IsNullOrWhiteSpace(sounds.Fire)) {
            var absolutePath = Path.GetFullPath(Path.Combine(metadataDirectory, sounds.Fire));
            if (!File.Exists(absolutePath)) {
                errors.Add($"Missing weapon fire sound file: {absolutePath}");
            }
        }

        return sounds;
    }

    private static WeaponAmmoMetadata? ReadAmmo(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("ammo", out var ammoJson)
            || ammoJson.ValueKind != JsonValueKind.Object) {
            return null;
        }

        var ammo = new WeaponAmmoMetadata {
            MagazineSize = ReadOptionalInt(ammoJson, "magazineSize", 0),
            MaxAmmo = ReadOptionalInt(ammoJson, "maxAmmo", 0),
            InitialAmmo = ReadOptionalInt(ammoJson, "initialAmmo", -1)
        };

        if (ammo.MagazineSize <= 0) {
            errors.Add("ammo.magazineSize must be positive.");
        }

        if (ammo.MaxAmmo <= 0) {
            errors.Add("ammo.maxAmmo must be positive.");
        }

        if (ammo.InitialAmmo > ammo.MaxAmmo) {
            errors.Add("ammo.initialAmmo must not exceed ammo.maxAmmo.");
        }

        return ammo;
    }

    private static string ReadOptionalString(JsonElement root, string name, string fallback)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static double ReadOptionalDouble(JsonElement root, string name, double fallback)
    {
        return root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : fallback;
    }

    private static bool ReadOptionalBool(JsonElement root, string name, bool fallback)
    {
        return root.TryGetProperty(name, out var value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;
    }

    private static int ReadOptionalInt(JsonElement root, string name, int fallback)
    {
        return root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : fallback;
    }

    private static int ReadPositiveInt(JsonElement root, string name, int fallback, List<string> errors)
    {
        if (!root.TryGetProperty(name, out var value)) {
            return fallback;
        }

        if (!value.TryGetInt32(out var number) || number <= 0) {
            errors.Add($"{name} must be a positive integer.");
            return fallback;
        }

        return number;
    }
}
