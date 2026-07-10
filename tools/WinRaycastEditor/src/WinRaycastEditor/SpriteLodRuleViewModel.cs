using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class SpriteLodRuleViewModel
{
    public SpriteLodRuleViewModel(SpriteLodMetadata rule)
    {
        MaxDistance = rule.MaxDistance.ToString("0.##");
        Resolution = $"{rule.Resolution}px";
    }

    public string MaxDistance { get; }
    public string Resolution { get; }
}
