using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class JsonEditingBackupServiceTests
{
    [TestMethod]
    public void BackupPathAppendsBakExtension()
    {
        Assert.AreEqual(
            @"C:\worlds\demo.world.json.bak",
            JsonEditingBackupService.BackupPathFor(@"C:\worlds\demo.world.json"));
    }

    [TestMethod]
    public void WriteBackupPersistsLatestContent()
    {
        var directory = Directory.CreateTempSubdirectory("nurcade-bak-");
        var sourcePath = Path.Combine(directory.FullName, "demo.world.json");

        try {
            JsonEditingBackupService.WriteBackup(sourcePath, "{\"v\":1}");
            JsonEditingBackupService.WriteBackup(sourcePath, "{\"v\":2}");

            var backupPath = JsonEditingBackupService.BackupPathFor(sourcePath);
            Assert.IsTrue(File.Exists(backupPath));
            Assert.AreEqual("{\"v\":2}", File.ReadAllText(backupPath));

            Assert.IsTrue(JsonEditingBackupService.TryReadBackup(sourcePath, out var recovered));
            Assert.AreEqual("{\"v\":2}", recovered);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void WriteBackupIgnoresEmptySourcePath()
    {
        // Must not throw when no world path is known yet.
        JsonEditingBackupService.WriteBackup(string.Empty, "{}");
        Assert.IsFalse(JsonEditingBackupService.TryReadBackup(string.Empty, out _));
    }
}
