using NuRcade.Editor;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class EngineExecutableLocatorTests
{
    [TestMethod]
    public void FindLocatesEngineInInstalledParentDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("nurcade-installed-");

        try {
            var editorDirectory = Directory.CreateDirectory(
                Path.Combine(directory.FullName, "editor"));
            var enginePath = Path.Combine(directory.FullName, "nuRCADEPlayer.exe");
            File.WriteAllText(enginePath, string.Empty);

            var found = EngineExecutableLocator.Find(
                editorDirectory.FullName,
                Path.Combine(editorDirectory.FullName, "NuRcade.Editor.exe"),
                _ => null);

            Assert.AreEqual(enginePath, found);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void FindFallsBackToRepositoryBuildLayout()
    {
        var expected = Path.Combine(
            "repo",
            "out",
            "build",
            "vs2026-x64",
            "Release",
            "nuRCADEPlayer.exe");

        var found = EngineExecutableLocator.Find(
            Path.GetTempPath(),
            null,
            parts => parts.SequenceEqual([
                "out",
                "build",
                "vs2026-x64",
                "Release",
                "nuRCADEPlayer.exe"
            ])
                ? expected
                : null);

        Assert.AreEqual(expected, found);
    }

    [TestMethod]
    public void FindAcceptsLegacyExecutableName()
    {
        var directory = Directory.CreateTempSubdirectory("nurcade-legacy-");
        try {
            var legacyPath = Path.Combine(directory.FullName, "nuRCADE.exe");
            File.WriteAllText(legacyPath, string.Empty);

            var found = EngineExecutableLocator.Find(directory.FullName, null, _ => null);

            Assert.AreEqual(legacyPath, found);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }
}
