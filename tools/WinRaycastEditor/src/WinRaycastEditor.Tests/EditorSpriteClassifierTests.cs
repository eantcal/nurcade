using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class EditorSpriteClassifierTests
{
    [TestMethod]
    public void ItemNamePrefixIsClassifiedAsItem()
    {
        Assert.IsTrue(EditorSpriteClassifier.IsItemSpriteSet("item_medikit"));
        Assert.IsTrue(EditorSpriteClassifier.IsItemSpriteSet("ITEM_Ammo_Box"));
    }

    [TestMethod]
    public void ItemsFolderPathIsClassifiedAsItem()
    {
        Assert.IsTrue(EditorSpriteClassifier.IsItemSpriteSet(
            "crate", "sprites/items/crate/crate.sprite.json"));
        Assert.IsTrue(EditorSpriteClassifier.IsItemSpriteSet(
            "crate", "sprites\\items\\crate\\crate.sprite.json"));
    }

    [TestMethod]
    public void ActorSpriteSetIsNotClassifiedAsItem()
    {
        Assert.IsFalse(EditorSpriteClassifier.IsItemSpriteSet("soldier"));
        Assert.IsFalse(EditorSpriteClassifier.IsItemSpriteSet(
            "soldier", "sprites/soldier/soldier.sprite.json"));
        Assert.IsFalse(EditorSpriteClassifier.IsItemSpriteSet(null));
    }

    [TestMethod]
    public void ApplyPlacementDefaultsForItemProducesStaticPassThroughSprite()
    {
        var sprite = new EditorSpriteInstance();

        EditorSpriteClassifier.ApplyPlacementDefaults(sprite, isItem: true);

        Assert.IsTrue(sprite.PassThroughWalls);
        Assert.AreEqual(0.0, sprite.CollisionRadiusCells);
        Assert.AreEqual(0.0, sprite.MaxHealth);
        Assert.AreEqual(0.0, sprite.Health);
        Assert.AreEqual(0.0, sprite.AttackDamage);
        Assert.IsFalse(sprite.ChasePlayer);
        Assert.IsFalse(sprite.PatrolCircuit);
        Assert.AreEqual(0.0, sprite.SpeedCellsPerSecond);
    }

    [TestMethod]
    public void ApplyPlacementDefaultsForActorProducesCombatSprite()
    {
        var sprite = new EditorSpriteInstance();

        EditorSpriteClassifier.ApplyPlacementDefaults(sprite, isItem: false);

        Assert.IsFalse(sprite.PassThroughWalls);
        Assert.AreEqual(0.2, sprite.CollisionRadiusCells);
        Assert.AreEqual(100.0, sprite.MaxHealth);
        Assert.AreEqual(100.0, sprite.Health);
        Assert.AreEqual(8.0, sprite.AttackDamage);
    }
}
