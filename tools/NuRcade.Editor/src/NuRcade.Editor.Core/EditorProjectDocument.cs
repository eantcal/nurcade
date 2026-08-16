namespace NuRcade.Editor.Core;

public sealed class EditorProjectDocument
{
    public string? SourcePath { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string WorldFile { get; set; } = string.Empty;
    public string TextureRoot { get; set; } = ".";
    public WorldPlayerStart? PlayerStart { get; set; }
    public WorldCombatStats PlayerStats { get; set; } = new();
    public WorldPlayerWeapon? PlayerWeapon { get; set; }
    public List<WorldPlayerWeapon> PlayerWeapons { get; } = [];
    public List<string> SpriteSets { get; } = [];
    public List<EditorSpriteInstance> SpriteInstances { get; } = [];
}
