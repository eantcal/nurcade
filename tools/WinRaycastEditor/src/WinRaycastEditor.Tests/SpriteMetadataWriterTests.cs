using WinRaycastEditor.Core;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class SpriteMetadataWriterTests
{
    [TestMethod]
    public void SaveAndLoadRoundTripPreservesEverything()
    {
        var source = LocateRepoSpriteMetadata();
        var loaded = SpriteMetadataLoader.Load(source);
        Assert.IsTrue(loaded.Success, string.Join(Environment.NewLine, loaded.Errors));
        Assert.IsNotNull(loaded.Document);

        var directory = Directory.CreateTempSubdirectory("winraycast-sprite-writer-");
        try {
            CopyBitmapFiles(loaded.Document!, source, directory.FullName);

            var written = Path.Combine(directory.FullName, "sprite_test.sprite.json");
            SpriteMetadataWriter.Save(loaded.Document, written);

            var reloaded = SpriteMetadataLoader.Load(written);

            Assert.IsTrue(reloaded.Success, string.Join(Environment.NewLine, reloaded.Errors));
            Assert.IsNotNull(reloaded.Document);
            Assert.AreEqual(loaded.Document.SpriteSet, reloaded.Document.SpriteSet);
            Assert.AreEqual(loaded.Document.Format, reloaded.Document.Format);
            CollectionAssert.AreEqual(loaded.Document.TransparentColor, reloaded.Document.TransparentColor);
            CollectionAssert.AreEqual(loaded.Document.SupportedResolutions, reloaded.Document.SupportedResolutions);
            Assert.AreEqual(loaded.Document.DefaultResolution, reloaded.Document.DefaultResolution);
            Assert.AreEqual(loaded.Document.MaxResolution, reloaded.Document.MaxResolution);
            Assert.HasCount(loaded.Document.Directions.Count, reloaded.Document.Directions);
            Assert.HasCount(loaded.Document.Animations.Count, reloaded.Document.Animations);

            for (var index = 0; index < loaded.Document.Directions.Count; ++index) {
                var expected = loaded.Document.Directions[index];
                var actual = reloaded.Document.Directions[index];
                Assert.AreEqual(expected.Name, actual.Name);
                Assert.AreEqual(expected.Angle, actual.Angle);
                CollectionAssert.AreEquivalent(expected.Files.Keys, actual.Files.Keys);
            }

            Assert.HasCount(loaded.Document.Lod.Count, reloaded.Document.Lod);
            for (var index = 0; index < loaded.Document.Lod.Count; ++index) {
                Assert.AreEqual(loaded.Document.Lod[index].MaxDistance, reloaded.Document.Lod[index].MaxDistance, 1e-9);
                Assert.AreEqual(loaded.Document.Lod[index].Resolution, reloaded.Document.Lod[index].Resolution);
            }
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    private static void CopyBitmapFiles(SpriteMetadataDocument document, string sourcePath, string targetDirectory)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))
            ?? throw new InvalidOperationException("Cannot resolve source directory.");

        foreach (var direction in document.Directions) {
            CopyDirectionFiles(direction, sourceDirectory, targetDirectory);
        }

        foreach (var animation in document.Animations) {
            foreach (var direction in animation.Directions) {
                CopyDirectionFiles(direction, sourceDirectory, targetDirectory);
            }

            foreach (var frame in animation.Frames) {
                foreach (var direction in frame.Directions) {
                    CopyDirectionFiles(direction, sourceDirectory, targetDirectory);
                }
            }
        }
    }

    private static void CopyDirectionFiles(
        SpriteDirectionMetadata direction,
        string sourceDirectory,
        string targetDirectory)
    {
        foreach (var file in direction.Files.Values) {
            var sourceFile = Path.Combine(sourceDirectory, file);
            var targetFile = Path.Combine(targetDirectory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            if (File.Exists(sourceFile)) {
                File.Copy(sourceFile, targetFile, overwrite: true);
            }
            else {
                File.WriteAllBytes(targetFile, []);
            }
        }
    }

    private static string LocateRepoSpriteMetadata()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            var candidate = Path.Combine(
                directory.FullName,
                "res",
                "examples",
                "sprite_test",
                "sprite_test.sprite.json");
            if (File.Exists(candidate)) {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find sprite_test.sprite.json fixture.");
    }
}
