using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WinRaycastEditor;

internal sealed class DemoShortcutInstallResult
{
    public string StartMenuShortcutPath { get; init; } = string.Empty;
    public string DesktopShortcutPath { get; init; } = string.Empty;
}

internal static class WindowsShortcutInstaller
{
    private const string StartMenuFolderName = "WinRayCast Demos";

    public static DemoShortcutInstallResult InstallDemoShortcuts(
        string projectPath,
        string enginePath,
        string displayName)
    {
        if (!File.Exists(projectPath)) {
            throw new FileNotFoundException("Missing exported WinRayCast project.", projectPath);
        }

        if (!File.Exists(enginePath)) {
            throw new FileNotFoundException("Missing exported WinRayCast runtime.", enginePath);
        }

        var shortcutName = SanitizeShortcutName(displayName);
        var workingDirectory =
            Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? Environment.CurrentDirectory;
        var arguments = QuoteArgument(Path.GetFullPath(projectPath));

        var programsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var startMenuDirectory = Path.Combine(programsDirectory, StartMenuFolderName);
        Directory.CreateDirectory(startMenuDirectory);

        var startMenuShortcutPath = Path.Combine(startMenuDirectory, shortcutName + ".lnk");
        var desktopShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            shortcutName + ".lnk");

        CreateShortcut(
            startMenuShortcutPath,
            enginePath,
            arguments,
            workingDirectory,
            "Launch " + shortcutName,
            enginePath);
        CreateShortcut(
            desktopShortcutPath,
            enginePath,
            arguments,
            workingDirectory,
            "Launch " + shortcutName,
            enginePath);

        return new DemoShortcutInstallResult {
            StartMenuShortcutPath = startMenuShortcutPath,
            DesktopShortcutPath = desktopShortcutPath
        };
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string arguments,
        string workingDirectory,
        string description,
        string iconPath)
    {
        var shellLinkType = Type.GetTypeFromCLSID(ShellLinkClassId)
            ?? throw new InvalidOperationException("Windows Shell Link COM class is not available.");
        var shellLink = (IShellLinkW)(Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException("Could not create Windows Shell Link."));
        shellLink.SetPath(Path.GetFullPath(targetPath));
        shellLink.SetArguments(arguments);
        shellLink.SetWorkingDirectory(Path.GetFullPath(workingDirectory));
        shellLink.SetDescription(description);
        shellLink.SetIconLocation(Path.GetFullPath(iconPath), 0);

        var persistFile = (IPersistFile)shellLink;
        persistFile.Save(shortcutPath, true);
    }

    private static string SanitizeShortcutName(string name)
    {
        var sanitized = new string(
            name.Select(character =>
                    Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
                .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized)
            ? "WinRayCast Demo"
            : sanitized;
    }

    private static string QuoteArgument(string argument)
    {
        return "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static readonly Guid ShellLinkClassId =
        new("00021401-0000-0000-C000-000000000046");

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maxPath,
            IntPtr findData,
            uint flags);

        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int maxIconPath,
            out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
