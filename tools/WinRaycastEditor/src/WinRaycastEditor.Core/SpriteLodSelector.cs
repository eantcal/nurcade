namespace WinRaycastEditor.Core;

public static class SpriteLodSelector
{
    public static int SelectResolution(SpriteMetadataDocument document, double distanceCells)
    {
        foreach (var rule in document.Lod.OrderBy(rule => rule.MaxDistance)) {
            if (distanceCells <= rule.MaxDistance) {
                return rule.Resolution;
            }
        }

        return document.Lod.Count == 0
            ? document.DefaultResolution
            : document.Lod.OrderBy(rule => rule.MaxDistance).Last().Resolution;
    }
}
