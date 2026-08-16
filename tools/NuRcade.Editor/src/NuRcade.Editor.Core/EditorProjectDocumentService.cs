using System.Text.Json;
using System.Text.Json.Serialization;

namespace NuRcade.Editor.Core;

public sealed class EditorProjectLoadResult
{
    public bool Success => Errors.Count == 0 && Document is not null;
    public EditorProjectDocument? Document { get; set; }
    public List<string> Errors { get; } = [];
}

public static class EditorProjectDocumentService
{
    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static EditorProjectLoadResult Load(string path)
    {
        var result = new EditorProjectLoadResult();
        if (!File.Exists(path)) {
            result.Errors.Add($"Cannot open project file: {path}");
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
                result.Errors.Add("Project root must be a JSON object.");
                return result;
            }

            var document = new EditorProjectDocument {
                SourcePath = Path.GetFullPath(path)
            };

            var root = json.RootElement;
            document.ProjectName = ReadOptionalString(root, "project") ?? string.Empty;
            document.WorldFile = ReadOptionalString(root, "worldFile") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(document.WorldFile)
                && !IsJsonWorldFile(document.WorldFile)) {
                result.Errors.Add(
                    "Project worldFile must point to a JSON world file; legacy INI maps are no longer supported.");
            }

            document.TextureRoot = ReadOptionalString(root, "textureRoot") ?? ".";
            if (root.TryGetProperty("playerStart", out var playerStart)
                && playerStart.ValueKind == JsonValueKind.Object) {
                document.PlayerStart = new WorldPlayerStart {
                    XCell = ReadOptionalDouble(playerStart, "xCell", 1.5),
                    YCell = ReadOptionalDouble(playerStart, "yCell", 1.5),
                    FacingDegrees = ReadOptionalDouble(playerStart, "facingDegrees")
                };
            }

            if (root.TryGetProperty("playerStats", out var playerStats)
                && playerStats.ValueKind == JsonValueKind.Object) {
                document.PlayerStats = new WorldCombatStats {
                    MaxHealth = ReadOptionalDouble(playerStats, "maxHealth", 100.0),
                    Health = ReadOptionalDouble(playerStats, "health", 100.0)
                };
            }

            if (root.TryGetProperty("playerWeapon", out var playerWeapon)) {
                if (playerWeapon.ValueKind == JsonValueKind.String) {
                    document.PlayerWeapon = new WorldPlayerWeapon {
                        File = playerWeapon.GetString() ?? string.Empty,
                        Visible = true,
                        Unlocked = true
                    };
                }
                else if (playerWeapon.ValueKind == JsonValueKind.Object) {
                    document.PlayerWeapon = new WorldPlayerWeapon {
                        File = ReadOptionalString(playerWeapon, "file") ?? string.Empty,
                        Visible = ReadOptionalBool(playerWeapon, "visible", true),
                        Unlocked = ReadOptionalBool(playerWeapon, "unlocked", true),
                        ScreenHeightFraction = ReadOptionalDouble(
                            playerWeapon,
                            "screenHeightFraction")
                    };
                }
            }

            if (root.TryGetProperty("playerWeapons", out var playerWeapons)
                && playerWeapons.ValueKind == JsonValueKind.Array) {
                foreach (var entry in playerWeapons.EnumerateArray()) {
                    var weapon = ReadPlayerWeapon(entry);
                    if (weapon is not null && !string.IsNullOrWhiteSpace(weapon.File)) {
                        document.PlayerWeapons.Add(weapon);
                    }
                }
            }

            if (root.TryGetProperty("spriteSets", out var spriteSets)
                && spriteSets.ValueKind == JsonValueKind.Array) {
                foreach (var entry in spriteSets.EnumerateArray()) {
                    if (entry.ValueKind == JsonValueKind.String) {
                        var value = entry.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) {
                            document.SpriteSets.Add(value);
                        }
                    }
                }
            }

            if (root.TryGetProperty("spriteInstances", out var spriteInstances)
                && spriteInstances.ValueKind == JsonValueKind.Array) {
                foreach (var entry in spriteInstances.EnumerateArray()) {
                    if (entry.ValueKind != JsonValueKind.Object) {
                        result.Errors.Add("Each sprite instance must be a JSON object.");
                        continue;
                    }

                    var sprite = new EditorSpriteInstance {
                        Name = ReadOptionalString(entry, "name") ?? string.Empty,
                        SpriteSet = ReadOptionalString(entry, "spriteSet") ?? string.Empty,
                        XCell = ReadOptionalDouble(entry, "xCell"),
                        YCell = ReadOptionalDouble(entry, "yCell"),
                        FacingDegrees = ReadOptionalDouble(entry, "facingDegrees"),
                        ScaleCells = ReadOptionalDouble(entry, "scaleCells", 1.0),
                        VerticalOffsetCells = ReadOptionalDouble(entry, "verticalOffsetCells"),
                        CollisionRadiusCells = ReadOptionalDouble(entry, "collisionRadiusCells", 0.2),
                        Visible = ReadOptionalBool(entry, "visible", true),
                        PassThroughWalls = ReadOptionalBool(entry, "passThroughWalls", false),
                        ChasePlayer = ReadOptionalBool(entry, "chasePlayer", false),
                        SpeedCellsPerSecond = ReadOptionalDouble(entry, "speedCellsPerSecond"),
                        DetectionRadiusCells = ReadOptionalDouble(entry, "detectionRadiusCells"),
                        PatrolRadiusCells = ReadOptionalDouble(entry, "patrolRadiusCells"),
                        EngagementHysteresisCells = ReadOptionalDouble(entry, "engagementHysteresisCells", 0.5),
                        PatrolCircuit = ReadOptionalBool(entry, "patrolCircuit", false),
                        StoppingDistanceCells = ReadOptionalDouble(entry, "stoppingDistanceCells"),
                        MaxHealth = ReadOptionalDouble(entry, "maxHealth"),
                        Health = ReadOptionalDouble(entry, "health"),
                        AttackDamage = ReadOptionalDouble(entry, "attackDamage"),
                        RangedAttack = ReadOptionalBool(entry, "rangedAttack", false),
                        AttackRangeCells = ReadOptionalDouble(entry, "attackRangeCells"),
                        AttackCooldownSeconds = ReadOptionalDouble(entry, "attackCooldownSeconds", 1.0),
                        AttackFovDegrees = ReadOptionalDouble(entry, "attackFovDegrees", 70.0),
                        AttackBurstShots = Math.Max(1, (int)ReadOptionalDouble(entry, "attackBurstShots", 3.0)),
                        AttackBurstPauseSeconds = ReadOptionalDouble(entry, "attackBurstPauseSeconds", 1.2),
                        PickupHealth = ReadOptionalDouble(entry, "pickupHealth"),
                        UnlocksMap = ReadOptionalBool(entry, "unlocksMap", false),
                        SavePoint = ReadOptionalBool(entry, "savePoint", false),
                        PickupWeapon = ReadOptionalString(entry, "pickupWeapon"),
                        Explosive = ReadOptionalBool(entry, "explosive", false),
                        ExplosiveHitPoints = ReadOptionalDouble(entry, "explosiveHitPoints", 45.0),
                        ExplosionRadiusCells = ReadOptionalDouble(entry, "explosionRadiusCells"),
                        ExplosionDamage = ReadOptionalDouble(entry, "explosionDamage"),
                        ExplosionScaleCells = ReadOptionalDouble(entry, "explosionScaleCells", 1.5),
                        ExplosionSpriteSet = ReadOptionalString(entry, "explosionSpriteSet"),
                        DestroyedSpriteSet = ReadOptionalString(entry, "destroyedSpriteSet"),
                        DestroyedScaleCells = ReadOptionalDouble(entry, "destroyedScaleCells", 0.55),
                        DamageResponse = ReadDamageResponse(entry)
                    };
                    document.SpriteInstances.Add(sprite);
                }
            }

            result.Document = document;
            return result;
        }
    }

    public static void Save(EditorProjectDocument document, string path)
    {
        var payload = new {
            project = document.ProjectName,
            worldFile = document.WorldFile,
            textureRoot = document.TextureRoot,
            playerStart = document.PlayerStart is null
                ? null
                : new {
                    xCell = document.PlayerStart.XCell,
                    yCell = document.PlayerStart.YCell,
                    facingDegrees = document.PlayerStart.FacingDegrees
                },
            playerStats = new {
                maxHealth = document.PlayerStats.MaxHealth,
                health = document.PlayerStats.Health
            },
            playerWeapon = document.PlayerWeapon is null
                ? null
                : new {
                    file = document.PlayerWeapon.File,
                    visible = document.PlayerWeapon.Visible,
                    unlocked = document.PlayerWeapon.Unlocked,
                    screenHeightFraction = document.PlayerWeapon.ScreenHeightFraction
                },
            playerWeapons = document.PlayerWeapons.Select(weapon => new {
                file = weapon.File,
                visible = weapon.Visible,
                unlocked = weapon.Unlocked,
                screenHeightFraction = weapon.ScreenHeightFraction
            }).ToArray(),
            spriteSets = document.SpriteSets.ToArray(),
            spriteInstances = document.SpriteInstances.Select(sprite => new {
                name = sprite.Name,
                spriteSet = sprite.SpriteSet,
                xCell = sprite.XCell,
                yCell = sprite.YCell,
                facingDegrees = sprite.FacingDegrees,
                scaleCells = sprite.ScaleCells,
                verticalOffsetCells = sprite.VerticalOffsetCells,
                collisionRadiusCells = sprite.CollisionRadiusCells,
                visible = sprite.Visible,
                passThroughWalls = sprite.PassThroughWalls,
                chasePlayer = sprite.ChasePlayer,
                speedCellsPerSecond = sprite.SpeedCellsPerSecond,
                detectionRadiusCells = sprite.DetectionRadiusCells,
                patrolRadiusCells = sprite.PatrolRadiusCells,
                engagementHysteresisCells = sprite.EngagementHysteresisCells,
                patrolCircuit = sprite.PatrolCircuit,
                stoppingDistanceCells = sprite.StoppingDistanceCells,
                maxHealth = sprite.MaxHealth,
                health = sprite.Health,
                attackDamage = sprite.AttackDamage,
                rangedAttack = sprite.RangedAttack,
                attackRangeCells = sprite.AttackRangeCells,
                attackCooldownSeconds = sprite.AttackCooldownSeconds,
                attackFovDegrees = sprite.AttackFovDegrees,
                attackBurstShots = sprite.AttackBurstShots,
                attackBurstPauseSeconds = sprite.AttackBurstPauseSeconds,
                pickupHealth = sprite.PickupHealth,
                unlocksMap = sprite.UnlocksMap,
                savePoint = sprite.SavePoint,
                pickupWeapon = string.IsNullOrWhiteSpace(sprite.PickupWeapon)
                    ? null
                    : sprite.PickupWeapon,
                explosive = sprite.Explosive,
                explosiveHitPoints = sprite.ExplosiveHitPoints,
                explosionRadiusCells = sprite.ExplosionRadiusCells,
                explosionDamage = sprite.ExplosionDamage,
                explosionScaleCells = sprite.ExplosionScaleCells,
                explosionSpriteSet = string.IsNullOrWhiteSpace(sprite.ExplosionSpriteSet)
                    ? null
                    : sprite.ExplosionSpriteSet,
                destroyedSpriteSet = string.IsNullOrWhiteSpace(sprite.DestroyedSpriteSet)
                    ? null
                    : sprite.DestroyedSpriteSet,
                destroyedScaleCells = sprite.DestroyedScaleCells,
                damageResponse = CreateDamageResponsePayload(sprite.DamageResponse)
            }).ToArray()
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, WriteOptions));
        document.SourcePath = Path.GetFullPath(path);
    }

    public static EditorProjectDocument FromMapDocument(EditorMapDocument map, string worldFileRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(worldFileRelativePath)
            && !IsJsonWorldFile(worldFileRelativePath)) {
            throw new ArgumentException(
                "Project world file must be JSON; legacy INI maps are no longer supported.",
                nameof(worldFileRelativePath));
        }

        var project = new EditorProjectDocument {
            WorldFile = worldFileRelativePath,
            PlayerStart = new WorldPlayerStart {
                XCell = map.PlayerStart.XCell,
                YCell = map.PlayerStart.YCell,
                FacingDegrees = map.PlayerStart.FacingDegrees
            },
            PlayerStats = CloneCombatStats(map.PlayerStats),
            PlayerWeapon = ClonePlayerWeapon(map.PlayerWeapon)
        };
        CopyPlayerWeapons(map.PlayerWeapons, project.PlayerWeapons);

        foreach (var spriteSet in map.SpriteSetFiles) {
            project.SpriteSets.Add(spriteSet);
        }

        foreach (var sprite in map.SpriteInstances) {
            project.SpriteInstances.Add(sprite);
        }

        return project;
    }

    private static bool IsJsonWorldFile(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static string? ReadOptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) {
            return null;
        }

        return value.GetString();
    }

    private static EditorSpriteDamageResponse? ReadDamageResponse(JsonElement root)
    {
        if (!root.TryGetProperty("damageResponse", out var response)
            || response.ValueKind != JsonValueKind.Object) {
            return null;
        }

        return new EditorSpriteDamageResponse {
            Type = ReadOptionalString(response, "type") ?? string.Empty,
            HitPoints = ReadOptionalDouble(response, "hitPoints", 45.0),
            EffectSpriteSet = ReadOptionalString(response, "effectSpriteSet"),
            EffectAnimation = ReadOptionalString(response, "effectAnimation"),
            EffectScaleCells = ReadOptionalDouble(response, "effectScaleCells", 1.5),
            DestroyedSpriteSet = ReadOptionalString(response, "destroyedSpriteSet"),
            DestroyedScaleCells = ReadOptionalDouble(response, "destroyedScaleCells", 0.55),
            Sound = ReadOptionalString(response, "sound"),
            RadiusCells = ReadOptionalDouble(response, "radiusCells"),
            Damage = ReadOptionalDouble(response, "damage")
        };
    }

    private static object? CreateDamageResponsePayload(EditorSpriteDamageResponse? response)
    {
        if (response is null) {
            return null;
        }

        return new {
            type = string.IsNullOrWhiteSpace(response.Type) ? null : response.Type,
            hitPoints = response.HitPoints,
            effectSpriteSet = string.IsNullOrWhiteSpace(response.EffectSpriteSet)
                ? null
                : response.EffectSpriteSet,
            effectAnimation = string.IsNullOrWhiteSpace(response.EffectAnimation)
                ? null
                : response.EffectAnimation,
            effectScaleCells = response.EffectScaleCells,
            destroyedSpriteSet = string.IsNullOrWhiteSpace(response.DestroyedSpriteSet)
                ? null
                : response.DestroyedSpriteSet,
            destroyedScaleCells = response.DestroyedScaleCells,
            sound = string.IsNullOrWhiteSpace(response.Sound) ? null : response.Sound,
            radiusCells = response.RadiusCells,
            damage = response.Damage
        };
    }

    private static double ReadOptionalDouble(JsonElement root, string name, double fallback = 0.0)
    {
        if (!root.TryGetProperty(name, out var value) || !value.TryGetDouble(out var number)) {
            return fallback;
        }

        return number;
    }

    private static bool ReadOptionalBool(JsonElement root, string name, bool fallback)
    {
        if (!root.TryGetProperty(name, out var value)) {
            return fallback;
        }

        return value.ValueKind switch {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static WorldPlayerWeapon? ClonePlayerWeapon(WorldPlayerWeapon? weapon)
    {
        if (weapon is null) {
            return null;
        }

        return new WorldPlayerWeapon {
            File = weapon.File,
            Visible = weapon.Visible,
            Unlocked = weapon.Unlocked,
            ScreenHeightFraction = weapon.ScreenHeightFraction
        };
    }

    private static WorldPlayerWeapon? ReadPlayerWeapon(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) {
            return new WorldPlayerWeapon {
                File = value.GetString() ?? string.Empty,
                Visible = true,
                Unlocked = true
            };
        }

        if (value.ValueKind != JsonValueKind.Object) {
            return null;
        }

        return new WorldPlayerWeapon {
            File = ReadOptionalString(value, "file") ?? string.Empty,
            Visible = ReadOptionalBool(value, "visible", true),
            Unlocked = ReadOptionalBool(value, "unlocked", true),
            ScreenHeightFraction = ReadOptionalDouble(
                value,
                "screenHeightFraction")
        };
    }

    private static void CopyPlayerWeapons(
        IEnumerable<WorldPlayerWeapon> source,
        ICollection<WorldPlayerWeapon> target)
    {
        foreach (var weapon in source) {
            var clone = ClonePlayerWeapon(weapon);
            if (clone is not null) {
                target.Add(clone);
            }
        }
    }

    private static WorldCombatStats CloneCombatStats(WorldCombatStats stats)
    {
        return new WorldCombatStats {
            MaxHealth = stats.MaxHealth,
            Health = stats.Health
        };
    }
}
