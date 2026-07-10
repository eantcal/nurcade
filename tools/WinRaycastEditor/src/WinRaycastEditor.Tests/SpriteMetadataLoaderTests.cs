using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class SpriteMetadataLoaderTests
{
    private static readonly (string Name, int Angle)[] Directions =
    [
        ("front", 0),
        ("front_right", 45),
        ("right", 90),
        ("back_right", 135),
        ("back", 180),
        ("back_left", 225),
        ("left", 270),
        ("front_left", 315)
    ];

    [TestMethod]
    public void LoadAcceptsValidSpriteMetadata()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-sprite-metadata-");

        try {
            var path = WriteMetadata(directory.FullName);

            var result = SpriteMetadataLoader.Load(path);

            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.IsNotNull(result.Document);
            Assert.AreEqual("doom_style_monster", result.Document.SpriteSet);
            Assert.AreEqual("BMP", result.Document.Format);
            Assert.HasCount(8, result.Document.Directions);
            Assert.HasCount(1, result.Document.Lod);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadAcceptsPngSpriteMetadata()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-sprite-metadata-");

        try {
            var path = WriteMetadata(directory.FullName, format: "PNG");

            var result = SpriteMetadataLoader.Load(path);

            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.IsNotNull(result.Document);
            Assert.AreEqual("PNG", result.Document.Format);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadReportsUnsupportedFormat()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-sprite-metadata-");

        try {
            var path = WriteMetadata(directory.FullName, format: "GIF");

            var result = SpriteMetadataLoader.Load(path);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("Unsupported sprite format")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadReportsMissingSpriteBitmapFile()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-sprite-metadata-");

        try {
            var path = WriteMetadata(directory.FullName, createBitmapFiles: false);

            var result = SpriteMetadataLoader.Load(path);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("Missing sprite image file")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadAcceptsResolutionUpTo1024()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-sprite-metadata-");

        try {
            var path = WriteMetadata(directory.FullName, format: "PNG", maxResolution: 1024);

            var result = SpriteMetadataLoader.Load(path);

            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.AreEqual(1024, result.Document!.MaxResolution);
            Assert.IsTrue(result.Document.SupportedResolutions.Contains(1024));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadRejectsMissingDirections()
    {
        var directory = Directory.CreateTempSubdirectory("winraycast-sprite-metadata-");

        try {
            var path = WriteMetadata(directory.FullName, directionCount: 7);

            var result = SpriteMetadataLoader.Load(path);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("all 8 supported directions")));
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadAcceptsRepositorySpriteTestMetadata()
    {
        var result = SpriteMetadataLoader.Load(
            FindRepoFile("res", "examples", "sprite_test", "sprite_test.sprite.json"));

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("sprite_test", result.Document.SpriteSet);
        Assert.HasCount(8, result.Document.Directions);
    }

    [TestMethod]
    public void LoadAcceptsRepositorySheetBruteAnimationMetadata()
    {
        var result = SpriteMetadataLoader.Load(
            FindRepoFile("res", "worlds", "demo_embedded", "sprites", "sheet_brute", "sheet_brute.sprite.json"));

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("sheet_brute", result.Document.SpriteSet);
        Assert.HasCount(8, result.Document.Directions);

        var walk = result.Document.Animations.Single(animation => animation.Name == "walk");
        Assert.IsTrue(walk.Loop);
        Assert.AreEqual(130.0, walk.FrameDurationMs, 1e-9);
        Assert.HasCount(4, walk.Frames);
        Assert.HasCount(8, walk.Frames[0].Directions);
        Assert.HasCount(8, walk.Frames[1].Directions);
    }

    [TestMethod]
    public void LoadAcceptsRepositorySoldierAnimationMetadata()
    {
        var result = SpriteMetadataLoader.Load(
            FindRepoFile("res", "worlds", "demo_embedded", "sprites", "soldier", "soldier.sprite.json"));

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("soldier", result.Document.SpriteSet);

        var walk = result.Document.Animations.Single(animation => animation.Name == "walk");
        Assert.IsTrue(walk.Loop);
        Assert.AreEqual(125.0, walk.FrameDurationMs, 1e-9);
        Assert.HasCount(4, walk.Frames);
        Assert.HasCount(8, walk.Frames[0].Directions);
        Assert.HasCount(8, walk.Frames[1].Directions);
    }

    private static string WriteMetadata(
        string directory,
        string format = "BMP",
        bool createBitmapFiles = true,
        int directionCount = 8,
        int maxResolution = 64)
    {
        var directionJson = new List<string>();
        foreach (var direction in Directions.Take(directionCount)) {
            var extension = format.Equals("PNG", StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : ".bmp";
            var fileName = $"{direction.Name}_64{extension}";
            if (createBitmapFiles) {
                File.WriteAllBytes(Path.Combine(directory, fileName), []);
            }

            directionJson.Add($$"""
                {
                  "name": "{{direction.Name}}",
                  "angle": {{direction.Angle}},
                  "files": {
                    "64": "{{fileName}}"
                  }
                }
                """);
        }

        var json = $$"""
            {
              "spriteSet": "doom_style_monster",
              "format": "{{format}}",
              "transparentColor": [0, 0, 0],
              "supportedResolutions": [64{{(maxResolution == 64 ? string.Empty : $", {maxResolution}")}}],
              "defaultResolution": 64,
              "maxResolution": {{maxResolution}},
              "directions": [
            {{string.Join(",\n", directionJson)}}
              ],
              "lod": [
                {
                  "maxDistance": 9999.0,
                  "resolution": 64
                }
              ]
            }
            """;

        var path = Path.Combine(directory, "monster.sprite.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string FindRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find repo file.", Path.Combine(parts));
    }
}
