using System.IO;
using WinRaycastEditor;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class TextureImporterTests
{
    [TestMethod]
    public void CopyToWorldCopiesImageIntoTexturesFolder()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try {
            var source = Path.Combine(root, "brick.png");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            var worldDirectory = Path.Combine(root, "world");
            Directory.CreateDirectory(worldDirectory);

            var outcome = TextureImporter.CopyToWorld(source, worldDirectory);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("textures/brick.png", outcome.RelativePath);
            Assert.IsTrue(File.Exists(Path.Combine(worldDirectory, "textures", "brick.png")));
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CopyToWorldReusesIdenticalFileInsteadOfDuplicating()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try {
            var source = Path.Combine(root, "brick.png");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            var worldDirectory = Path.Combine(root, "world");
            Directory.CreateDirectory(worldDirectory);

            TextureImporter.CopyToWorld(source, worldDirectory);
            var second = TextureImporter.CopyToWorld(source, worldDirectory);

            Assert.IsTrue(second.Success);
            Assert.AreEqual("textures/brick.png", second.RelativePath);
            Assert.HasCount(
                1,
                Directory.GetFiles(Path.Combine(worldDirectory, "textures")));
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CopyToWorldRenamesOnNameClashWithDifferentImage()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try {
            var worldDirectory = Path.Combine(root, "world");
            var texturesDirectory = Path.Combine(worldDirectory, "textures");
            Directory.CreateDirectory(texturesDirectory);
            File.WriteAllBytes(Path.Combine(texturesDirectory, "brick.png"), [1, 2, 3, 4]);

            var source = Path.Combine(root, "brick.png");
            File.WriteAllBytes(source, [9, 9, 9, 9, 9, 9]);

            var outcome = TextureImporter.CopyToWorld(source, worldDirectory);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("textures/brick_1.png", outcome.RelativePath);
            Assert.HasCount(
                2,
                Directory.GetFiles(texturesDirectory));
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CopyToWorldRejectsUnsupportedFiles()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try {
            var source = Path.Combine(root, "notes.txt");
            File.WriteAllText(source, "not an image");

            var outcome = TextureImporter.CopyToWorld(source, root);

            Assert.IsFalse(outcome.Success);
            Assert.IsNull(outcome.RelativePath);
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }
}
