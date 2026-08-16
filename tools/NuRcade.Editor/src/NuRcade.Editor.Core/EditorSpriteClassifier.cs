namespace NuRcade.Editor.Core;

/// <summary>
/// Classifies sprite sets as collectible items versus actors (enemies/monsters).
/// Items live under a "sprites/items/" folder or use the "item_" naming convention.
/// They are placed as static, pass-through sprites with no combat stats, whereas
/// actors carry health, attack damage and AI behaviour.
/// </summary>
public static class EditorSpriteClassifier
{
    public const string ItemNamePrefix = "item_";

    /// <summary>
    /// Returns <c>true</c> when the sprite set represents a collectible item.
    /// The decision is based on the sprite set name (an <c>item_</c> prefix) or,
    /// when provided, the sprite set file living under an <c>items</c> folder.
    /// </summary>
    public static bool IsItemSpriteSet(string? spriteSetName, string? spriteSetFile = null)
    {
        if (!string.IsNullOrWhiteSpace(spriteSetName)
            && spriteSetName.StartsWith(ItemNamePrefix, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.IsNullOrWhiteSpace(spriteSetFile)) {
            return false;
        }

        var normalized = spriteSetFile.Replace('\\', '/');
        return normalized.Contains("/items/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("items/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies the default placement properties for an item or an actor sprite.
    /// Items are static (no AI), pass through walls, have no collision footprint
    /// and carry no combat stats; actors get the standard enemy defaults.
    /// </summary>
    public static void ApplyPlacementDefaults(EditorSpriteInstance sprite, bool isItem)
    {
        if (isItem) {
            sprite.FacingDegrees = 180.0;
            sprite.ScaleCells = 0.5;
            sprite.CollisionRadiusCells = 0.0;
            sprite.PassThroughWalls = true;
            sprite.ChasePlayer = false;
            sprite.PatrolCircuit = false;
            sprite.SpeedCellsPerSecond = 0.0;
            sprite.DetectionRadiusCells = 0.0;
            sprite.PatrolRadiusCells = 0.0;
            sprite.StoppingDistanceCells = 0.0;
            sprite.MaxHealth = 0.0;
            sprite.Health = 0.0;
            sprite.AttackDamage = 0.0;
            return;
        }

        sprite.FacingDegrees = 0.0;
        sprite.ScaleCells = 1.0;
        sprite.CollisionRadiusCells = 0.2;
        sprite.PassThroughWalls = false;
        sprite.MaxHealth = 100.0;
        sprite.Health = 100.0;
        sprite.AttackDamage = 8.0;
    }
}
