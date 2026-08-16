namespace NuRcade.Editor;

public sealed class WeaponLibraryItemViewModel
{
    public WeaponLibraryItemViewModel(string absolutePath, string relativePath, string weaponName)
    {
        AbsolutePath = absolutePath;
        RelativePath = relativePath;
        WeaponName = weaponName;
    }

    public string AbsolutePath { get; }
    public string RelativePath { get; }
    public string WeaponName { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(WeaponName)
        ? RelativePath
        : $"{WeaponName} - {RelativePath}";
}
