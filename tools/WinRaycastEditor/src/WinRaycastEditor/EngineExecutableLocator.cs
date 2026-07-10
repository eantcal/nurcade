using System.IO;

namespace WinRaycastEditor;

internal static class EngineExecutableLocator
{
    private const string PlayerExecutableName = "WinRayCastPlayer.exe";
    private const string LegacyExecutableName = "WinRayCast.exe";

    public static string? Find(
        string appBaseDirectory,
        string? processPath,
        Func<string[], string?> repoFileFinder)
    {
        var local = FindNearEditor(appBaseDirectory, processPath);
        if (local is not null) {
            return local;
        }

        var repoCandidates = new List<string[]>();
        foreach (var executableName in new[] { PlayerExecutableName, LegacyExecutableName }) {
            foreach (var preset in new[] { "vs2026-win32", "vs2026-x64", "vs2022-win32", "vs2022-x64" }) {
                repoCandidates.Add(["out", "build", preset, "Debug", executableName]);
                repoCandidates.Add(["out", "build", preset, "Release", executableName]);
            }
        }

        foreach (var candidate in repoCandidates) {
            var path = repoFileFinder(candidate);
            if (path is not null) {
                return path;
            }
        }

        return null;
    }

    private static string? FindNearEditor(string appBaseDirectory, string? processPath)
    {
        foreach (var directory in CandidateBaseDirectories(appBaseDirectory, processPath)) {
            var current = directory;
            for (var depth = 0; depth < 3 && current is not null; ++depth) {
                foreach (var executableName in new[] { PlayerExecutableName, LegacyExecutableName }) {
                    var candidate = Path.Combine(current.FullName, executableName);
                    if (File.Exists(candidate)) {
                        return candidate;
                    }
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static IEnumerable<DirectoryInfo> CandidateBaseDirectories(
        string appBaseDirectory,
        string? processPath)
    {
        if (!string.IsNullOrWhiteSpace(appBaseDirectory)
            && Directory.Exists(appBaseDirectory)) {
            yield return new DirectoryInfo(appBaseDirectory);
        }

        if (string.IsNullOrWhiteSpace(processPath)) {
            yield break;
        }

        var processDirectory = Path.GetDirectoryName(processPath);
        if (!string.IsNullOrWhiteSpace(processDirectory)
            && Directory.Exists(processDirectory)
            && !string.Equals(
                Path.GetFullPath(processDirectory),
                Path.GetFullPath(appBaseDirectory),
                StringComparison.OrdinalIgnoreCase)) {
            yield return new DirectoryInfo(processDirectory);
        }
    }
}
