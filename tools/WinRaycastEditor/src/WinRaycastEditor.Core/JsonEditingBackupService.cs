namespace WinRaycastEditor.Core;

/// <summary>
/// Manages the on-disk backup of in-progress JSON edits. While the editor content is
/// dirty or invalid the latest text is written to a sidecar file so nothing is lost on
/// a crash; the file is kept as the last backup with a <c>.bak</c> extension.
/// </summary>
public static class JsonEditingBackupService
{
    public const string BackupExtension = ".bak";

    public static string BackupPathFor(string sourcePath) => sourcePath + BackupExtension;

    /// <summary>
    /// Persists <paramref name="content"/> to the backup file next to
    /// <paramref name="sourcePath"/>. No-op when no source path is known.
    /// </summary>
    public static void WriteBackup(string sourcePath, string content)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            return;
        }

        File.WriteAllText(BackupPathFor(sourcePath), content);
    }

    public static bool TryReadBackup(string sourcePath, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            return false;
        }

        var backupPath = BackupPathFor(sourcePath);
        if (!File.Exists(backupPath)) {
            return false;
        }

        content = File.ReadAllText(backupPath);
        return true;
    }

    /// <summary>
    /// True when a backup exists and is newer than the source file (or the source is
    /// missing), indicating unsaved in-progress edits worth offering to recover.
    /// </summary>
    public static bool BackupIsNewerThanSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) {
            return false;
        }

        var backupPath = BackupPathFor(sourcePath);
        if (!File.Exists(backupPath)) {
            return false;
        }

        if (!File.Exists(sourcePath)) {
            return true;
        }

        return File.GetLastWriteTimeUtc(backupPath) > File.GetLastWriteTimeUtc(sourcePath);
    }
}
