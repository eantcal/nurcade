namespace WinRaycastEditor.Core;

public static class SpriteResolutionSelector
{
    public static int SelectClosestAvailableResolution(
        SpriteDirectionMetadata direction,
        int preferredResolution)
    {
        if (direction.Files.Count == 0) {
            return 0;
        }

        if (direction.Files.ContainsKey(preferredResolution)) {
            return preferredResolution;
        }

        var lower = direction.Files.Keys
            .Where(resolution => resolution < preferredResolution)
            .OrderByDescending(resolution => resolution)
            .FirstOrDefault();
        if (lower != 0) {
            return lower;
        }

        return direction.Files.Keys
            .Where(resolution => resolution > preferredResolution)
            .OrderBy(resolution => resolution)
            .First();
    }
}
