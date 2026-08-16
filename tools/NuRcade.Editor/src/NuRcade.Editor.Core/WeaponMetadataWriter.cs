using System.Text.Json;

namespace NuRcade.Editor.Core;

public static class WeaponMetadataWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true
    };

    public static void Save(WeaponMetadataDocument document, string path)
    {
        File.WriteAllText(path, Serialize(document));
    }

    public static string Serialize(WeaponMetadataDocument document)
    {
        var payload = new Dictionary<string, object?> {
            ["weapon"] = document.Weapon,
            ["format"] = document.Format,
            ["frameWidth"] = document.FrameWidth,
            ["frameHeight"] = document.FrameHeight,
            ["screenHeightFraction"] = document.ScreenHeightFraction,
            ["damage"] = document.Damage,
            ["rangeCells"] = document.RangeCells,
            ["anchor"] = new {
                x = document.Anchor.X,
                y = document.Anchor.Y
            },
            ["baseOffset"] = new {
                x = document.BaseOffset.X,
                y = document.BaseOffset.Y
            },
            ["bob"] = new {
                enabled = document.Bob.Enabled,
                amount = document.Bob.Amount,
                amplitudeX = document.Bob.AmplitudeX,
                amplitudeY = document.Bob.AmplitudeY,
                frequencyHz = document.Bob.FrequencyHz
            },
            ["animations"] = document.Animations.ToDictionary(
                animation => animation.Name,
                animation => new {
                    frameDurationMs = animation.FrameDurationMs,
                    loop = animation.Loop,
                    files = animation.Files.ToArray()
                })
        };

        if (document.Sounds is not null) {
            payload["sounds"] = new {
                fire = document.Sounds.Fire
            };
        }

        if (document.FireBehavior is not null) {
            payload["fireBehavior"] = new {
                automatic = document.FireBehavior.Automatic,
                intervalMs = document.FireBehavior.IntervalMs,
                soundIntervalMs = document.FireBehavior.SoundIntervalMs
            };
        }

        if (document.Ammo is not null) {
            payload["ammo"] = new {
                magazineSize = document.Ammo.MagazineSize,
                maxAmmo = document.Ammo.MaxAmmo,
                initialAmmo = document.Ammo.InitialAmmo
            };
        }

        return JsonSerializer.Serialize(payload, WriteOptions);
    }
}
