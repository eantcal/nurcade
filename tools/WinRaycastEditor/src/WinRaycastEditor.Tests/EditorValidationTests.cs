using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class EditorValidationTests
{
    [TestMethod]
    public void ValidateReportsMissingTextureImageFiles()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-editor-validation-");
        var worldPath = Path.Combine(directory.FullName, "world.world.json");

        try {
            var document = new EditorMapDocument { SourcePath = worldPath };
            document.TextureMap[0x01] = "missing_wall";
            document.Rows.Add([new EditorMapCell(0, 0, 0x01)]);

            var messages = EditorValidation.Validate(document, worldPath);

            Assert.IsTrue(messages.Any(message => message.Contains("missing image file")));
            Assert.IsTrue(messages.Any(message => message.Contains("missing_wall.bmp")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ValidateAcceptsExistingTextureImageFiles()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-editor-validation-");
        var worldPath = Path.Combine(directory.FullName, "world.world.json");
        File.WriteAllBytes(Path.Combine(directory.FullName, "wall.bmp"), []);

        try {
            var document = new EditorMapDocument { SourcePath = worldPath };
            document.TextureMap[0x01] = "wall";
            document.Rows.Add([new EditorMapCell(0, 0, 0x01)]);

            var messages = EditorValidation.Validate(document, worldPath);

            Assert.IsFalse(messages.Any(message => message.Contains("missing image file")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ValidateAcceptsTexturePngFiles()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-editor-validation-");
        var worldPath = Path.Combine(directory.FullName, "world.world.json");
        File.WriteAllBytes(Path.Combine(directory.FullName, "wall.png"), []);

        try {
            var document = new EditorMapDocument { SourcePath = worldPath };
            document.TextureMap[0x01] = "wall.png";
            document.Rows.Add([new EditorMapCell(0, 0, 0x01)]);

            var messages = EditorValidation.Validate(document, worldPath);

            Assert.IsFalse(messages.Any(message => message.Contains("missing image file")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void ValidateReportsDuplicateSpriteNames()
    {
        var document = OneCellDocument(0);
        document.SpriteInstances.Add(new EditorSpriteInstance { Name = "guard", XCell = 0.5, YCell = 0.5 });
        document.SpriteInstances.Add(new EditorSpriteInstance { Name = "guard", XCell = 0.5, YCell = 0.5 });

        var messages = EditorValidation.Validate(document);

        Assert.IsTrue(messages.Any(message => message.Contains("Duplicate sprite instance name")));
    }

    [TestMethod]
    public void ValidateReportsSpriteInsideSolidWallUnlessPassThrough()
    {
        var blocked = OneCellDocument(0x01);
        blocked.SpriteInstances.Add(new EditorSpriteInstance { Name = "guard", XCell = 0.5, YCell = 0.5 });
        var passThrough = OneCellDocument(0x01);
        passThrough.SpriteInstances.Add(new EditorSpriteInstance {
            Name = "ghost",
            XCell = 0.5,
            YCell = 0.5,
            PassThroughWalls = true
        });

        var blockedMessages = EditorValidation.Validate(blocked);
        var passThroughMessages = EditorValidation.Validate(passThrough);

        Assert.IsTrue(blockedMessages.Any(message => message.Contains("inside a solid wall")));
        Assert.IsFalse(passThroughMessages.Any(message => message.Contains("inside a solid wall")));
    }

    [TestMethod]
    public void ValidateReportsInvalidSpriteScaleAndCollisionRadius()
    {
        var document = OneCellDocument(0);
        document.SpriteInstances.Add(new EditorSpriteInstance {
            Name = "guard",
            XCell = 0.5,
            YCell = 0.5,
            ScaleCells = 0.0,
            CollisionRadiusCells = -1.0
        });

        var messages = EditorValidation.Validate(document);

        Assert.IsTrue(messages.Any(message => message.Contains("scale must be positive")));
        Assert.IsTrue(messages.Any(message => message.Contains("collision radius cannot be negative")));
    }

    [TestMethod]
    public void ValidateReportsInvalidPlayerStartPlacement()
    {
        var blocked = OneCellDocument(0x01);
        blocked.PlayerStart.XCell = 0.5;
        blocked.PlayerStart.YCell = 0.5;

        var blockedMessages = EditorValidation.Validate(blocked);

        Assert.IsTrue(blockedMessages.Any(message => message.Contains("Player start is inside a solid wall cell")));

        var outOfBounds = OneCellDocument(0);
        outOfBounds.PlayerStart.XCell = 2.0;
        outOfBounds.PlayerStart.YCell = 0.5;

        var outOfBoundsMessages = EditorValidation.Validate(outOfBounds);

        Assert.IsTrue(outOfBoundsMessages.Any(message => message.Contains("Player start is outside the map bounds")));
    }

    private static EditorMapDocument OneCellDocument(ulong packedCell)
    {
        var document = new EditorMapDocument();
        document.Rows.Add([new EditorMapCell(0, 0, packedCell)]);
        return document;
    }
}
