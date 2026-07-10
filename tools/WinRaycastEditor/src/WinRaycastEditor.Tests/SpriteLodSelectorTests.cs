using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class SpriteLodSelectorTests
{
    [TestMethod]
    public void SelectResolutionUsesNearestMatchingDistanceRule()
    {
        var document = new SpriteMetadataDocument {
            DefaultResolution = 128,
            Lod =
            {
                new SpriteLodMetadata { MaxDistance = 10.0, Resolution = 128 },
                new SpriteLodMetadata { MaxDistance = 2.0, Resolution = 512 },
                new SpriteLodMetadata { MaxDistance = 5.0, Resolution = 256 }
            }
        };

        Assert.AreEqual(512, SpriteLodSelector.SelectResolution(document, 1.0));
        Assert.AreEqual(256, SpriteLodSelector.SelectResolution(document, 4.0));
        Assert.AreEqual(128, SpriteLodSelector.SelectResolution(document, 12.0));
    }

    [TestMethod]
    public void SelectResolutionFallsBackToDefaultWhenNoLodRulesExist()
    {
        var document = new SpriteMetadataDocument { DefaultResolution = 64 };

        Assert.AreEqual(64, SpriteLodSelector.SelectResolution(document, 3.0));
    }
}
