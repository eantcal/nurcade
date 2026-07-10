using System.IO;

namespace WinRaycastEditor.Tests;

[TestClass]
public sealed class SpriteAnimationEditingTests
{
    [TestMethod]
    public void EditorCanAddDuplicateAndRemoveSpriteAnimationsAndFrames()
    {
        var viewModel = new MainWindowViewModel(loadDefaultWorld: false);
        viewModel.LoadSpriteMetadataFrom(
            FindRepoFile(
                "res",
                "worlds",
                "demo_embedded",
                "sprites",
                "sheet_brute",
                "sheet_brute.sprite.json"),
            registerInDocument: false);

        var initialAnimationCount = viewModel.SpriteAnimations.Count;

        viewModel.AddSpriteAnimationCommand.Execute(null);

        Assert.HasCount(initialAnimationCount + 1, viewModel.SpriteAnimations);
        Assert.IsNotNull(viewModel.SelectedSpriteAnimation);
        StringAssert.StartsWith(viewModel.SelectedSpriteAnimation.Name, "animation");
        Assert.HasCount(1, viewModel.SpriteAnimationFrames);

        viewModel.DuplicateSpriteAnimationFrameCommand.Execute(null);

        Assert.HasCount(2, viewModel.SpriteAnimationFrames);

        viewModel.RemoveSpriteAnimationFrameCommand.Execute(null);

        Assert.HasCount(1, viewModel.SpriteAnimationFrames);

        viewModel.DuplicateSpriteAnimationCommand.Execute(null);

        Assert.HasCount(initialAnimationCount + 2, viewModel.SpriteAnimations);
        Assert.IsNotNull(viewModel.SelectedSpriteAnimation);
        StringAssert.Contains(viewModel.SelectedSpriteAnimation.Name, "_copy");

        viewModel.RemoveSpriteAnimationCommand.Execute(null);

        Assert.HasCount(initialAnimationCount + 1, viewModel.SpriteAnimations);
    }

    private static string FindRepoFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory)) {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate)) {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException("Cannot find repo file.", Path.Combine(parts));
    }
}
