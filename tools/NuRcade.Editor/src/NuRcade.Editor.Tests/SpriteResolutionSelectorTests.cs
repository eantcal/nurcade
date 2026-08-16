using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class SpriteResolutionSelectorTests
{
    [TestMethod]
    public void SelectClosestAvailableResolutionUsesExactMatch()
    {
        var direction = DirectionWithResolutions(64, 128, 256);

        Assert.AreEqual(128, SpriteResolutionSelector.SelectClosestAvailableResolution(direction, 128));
    }

    [TestMethod]
    public void SelectClosestAvailableResolutionFallsBackToLowerThenHigher()
    {
        var direction = DirectionWithResolutions(64, 256);

        Assert.AreEqual(64, SpriteResolutionSelector.SelectClosestAvailableResolution(direction, 128));
        Assert.AreEqual(64, SpriteResolutionSelector.SelectClosestAvailableResolution(direction, 32));
    }

    [TestMethod]
    public void SelectClosestAvailableResolutionReturnsZeroForEmptyDirection()
    {
        var direction = new SpriteDirectionMetadata { Name = "front", Angle = 0 };

        Assert.AreEqual(0, SpriteResolutionSelector.SelectClosestAvailableResolution(direction, 128));
    }

    private static SpriteDirectionMetadata DirectionWithResolutions(params int[] resolutions)
    {
        var direction = new SpriteDirectionMetadata { Name = "front", Angle = 0 };
        foreach (var resolution in resolutions) {
            direction.Files[resolution] = $"{resolution}.bmp";
        }

        return direction;
    }
}
