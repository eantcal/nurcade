using System.Text.Json;

namespace NuRcade.Editor.Core;

public static class SpriteMetadataWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true
    };

    public static void Save(SpriteMetadataDocument document, string path)
    {
        File.WriteAllText(path, Serialize(document));
    }

    public static string Serialize(SpriteMetadataDocument document)
    {
        var payload = new Dictionary<string, object?> {
            ["spriteSet"] = document.SpriteSet,
            ["format"] = document.Format,
            ["transparentColor"] = document.TransparentColor.Select(value => (int)value).ToArray(),
            ["supportedResolutions"] = document.SupportedResolutions.ToArray(),
            ["defaultResolution"] = document.DefaultResolution,
            ["maxResolution"] = document.MaxResolution
        };

        if (document.Animations.Count > 0) {
            payload["animations"] = document.Animations.ToDictionary(
                animation => animation.Name,
                animation => new {
                    frameDurationMs = animation.FrameDurationMs,
                    loop = animation.Loop,
                    frames = animation.Frames.Select(frame => new {
                        directions = SerializeDirections(frame.Directions)
                    }).ToArray()
                });
        }
        else {
            payload["directions"] = SerializeDirections(document.Directions);
        }

        payload["lod"] = document.Lod
            .OrderBy(rule => rule.MaxDistance)
            .Select(rule => new {
                maxDistance = rule.MaxDistance,
                resolution = rule.Resolution
            })
            .ToArray();

        return JsonSerializer.Serialize(payload, WriteOptions);
    }

    private static object[] SerializeDirections(IEnumerable<SpriteDirectionMetadata> directions)
    {
        return directions.Select(direction => new {
            name = direction.Name,
            angle = direction.Angle,
            files = direction.Files
                .OrderBy(item => item.Key)
                .ToDictionary(
                    item => item.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item => item.Value)
        }).ToArray<object>();
    }
}
