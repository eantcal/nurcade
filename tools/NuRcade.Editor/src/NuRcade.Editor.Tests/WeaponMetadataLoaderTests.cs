using NuRcade.Editor.Core;

namespace NuRcade.Editor.Tests;

[TestClass]
public sealed class WeaponMetadataLoaderTests
{
    [TestMethod]
    public void LoadAcceptsRepositorySuperShotgunMetadata()
    {
        var result = WeaponMetadataLoader.Load(DemoWeaponPath());

        Assert.IsTrue(result.Success, string.Join(", ", result.Errors));
        Assert.IsNotNull(result.Document);
        Assert.AreEqual("super_shotgun", result.Document!.Weapon);
        Assert.AreEqual("PNG", result.Document.Format);
        Assert.AreEqual(640, result.Document.FrameWidth);
        Assert.AreEqual(440, result.Document.FrameHeight);
        Assert.AreEqual(0.34, result.Document.ScreenHeightFraction, 1e-9);
        Assert.AreEqual(45.0, result.Document.Damage, 1e-9);
        Assert.AreEqual(7.5, result.Document.RangeCells, 1e-9);
        Assert.IsNotNull(result.Document.Sounds);
        Assert.AreEqual("sounds/cannon2.mp3", result.Document.Sounds!.Fire);
        Assert.IsNotNull(result.Document.Ammo);
        Assert.AreEqual(2, result.Document.Ammo!.MagazineSize);
        Assert.AreEqual(14, result.Document.Ammo.MaxAmmo);
        Assert.AreEqual(14, result.Document.Ammo.InitialAmmo);
        Assert.IsTrue(result.Document.Bob.Enabled);
        Assert.AreEqual(0.45, result.Document.Bob.Amount, 1e-9);
        Assert.AreEqual(2.6, result.Document.Bob.FrequencyHz, 1e-9);
        Assert.HasCount(3, result.Document.Animations);
        Assert.IsTrue(result.Document.Animations.Any(animation => animation.Name == "idle"));
        Assert.IsTrue(result.Document.Animations.Any(animation => animation.Name == "fire"));
        Assert.IsTrue(result.Document.Animations.Any(animation => animation.Name == "reload"));
    }

    [TestMethod]
    public void SaveRoundTripPreservesWeaponMetadata()
    {
        var sourceDirectory = Path.GetDirectoryName(DemoWeaponPath())!;
        var targetDirectory = Directory.CreateTempSubdirectory("nurcade-weapon-metadata-");
        var targetPath = Path.Combine(targetDirectory.FullName, "super_shotgun.weapon.json");

        try {
            CopyDirectory(sourceDirectory, targetDirectory.FullName);
            var loaded = WeaponMetadataLoader.Load(targetPath);
            Assert.IsTrue(loaded.Success, string.Join(", ", loaded.Errors));
            Assert.IsNotNull(loaded.Document);

            loaded.Document!.ScreenHeightFraction = 0.31;
            loaded.Document.Damage = 12.5;
            loaded.Document.RangeCells = 4.25;
            loaded.Document.Sounds!.Fire = "sounds/alternate.ogg";
            loaded.Document.FireBehavior = new WeaponFireBehaviorMetadata {
                Automatic = true,
                IntervalMs = 125.0,
                SoundIntervalMs = 900.0
            };
            loaded.Document.Ammo!.MaxAmmo = 10;
            loaded.Document.Ammo.InitialAmmo = 8;
            File.Copy(
                Path.Combine(sourceDirectory, "sounds", "shotgun_fire.ogg"),
                Path.Combine(targetDirectory.FullName, "sounds", "alternate.ogg"));
            loaded.Document.Bob.Enabled = false;
            loaded.Document.Bob.Amount = 0.35;
            loaded.Document.Bob.AmplitudeX = 12.5;
            loaded.Document.Animations[0].Files.Add(loaded.Document.Animations[0].Files[0]);
            WeaponMetadataWriter.Save(loaded.Document, targetPath);

            var reloaded = WeaponMetadataLoader.Load(targetPath);

            Assert.IsTrue(reloaded.Success, string.Join(", ", reloaded.Errors));
            Assert.IsNotNull(reloaded.Document);
            Assert.AreEqual(0.31, reloaded.Document!.ScreenHeightFraction, 1e-9);
            Assert.AreEqual(12.5, reloaded.Document.Damage, 1e-9);
            Assert.AreEqual(4.25, reloaded.Document.RangeCells, 1e-9);
            Assert.AreEqual("sounds/alternate.ogg", reloaded.Document.Sounds!.Fire);
            Assert.IsNotNull(reloaded.Document.FireBehavior);
            Assert.IsTrue(reloaded.Document.FireBehavior!.Automatic);
            Assert.AreEqual(125.0, reloaded.Document.FireBehavior.IntervalMs, 1e-9);
            Assert.AreEqual(900.0, reloaded.Document.FireBehavior.SoundIntervalMs, 1e-9);
            Assert.AreEqual(10, reloaded.Document.Ammo!.MaxAmmo);
            Assert.AreEqual(8, reloaded.Document.Ammo.InitialAmmo);
            Assert.IsFalse(reloaded.Document.Bob.Enabled);
            Assert.AreEqual(0.35, reloaded.Document.Bob.Amount, 1e-9);
            Assert.AreEqual(12.5, reloaded.Document.Bob.AmplitudeX, 1e-9);
            Assert.HasCount(2, reloaded.Document.Animations[0].Files);
        }
        finally {
            targetDirectory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void LoadAcceptsDemoPistolAndSubmachineGunMetadata()
    {
        var pistol = WeaponMetadataLoader.Load(DemoWeaponPath("pistol", "pistol.weapon.json"));
        Assert.IsTrue(pistol.Success, string.Join(", ", pistol.Errors));
        Assert.IsNotNull(pistol.Document);
        Assert.AreEqual("pistol", pistol.Document!.Weapon);
        Assert.IsNotNull(pistol.Document.Ammo);
        Assert.AreEqual(18, pistol.Document.Ammo!.MagazineSize);
        Assert.AreEqual(90, pistol.Document.Ammo.MaxAmmo);
        Assert.AreEqual(90, pistol.Document.Ammo.InitialAmmo);
        Assert.IsNotNull(pistol.Document.Sounds);
        Assert.AreEqual(
            "sounds/pistol_shot_outdoor_retro.wav",
            pistol.Document.Sounds!.Fire);

        var submachineGun = WeaponMetadataLoader.Load(
            DemoWeaponPath("submachine_gun", "submachine_gun.weapon.json"));
        Assert.IsTrue(submachineGun.Success, string.Join(", ", submachineGun.Errors));
        Assert.IsNotNull(submachineGun.Document);
        Assert.AreEqual("submachine_gun", submachineGun.Document!.Weapon);
        Assert.IsNotNull(submachineGun.Document.Ammo);
        Assert.AreEqual(40, submachineGun.Document.Ammo!.MagazineSize);
        Assert.AreEqual(240, submachineGun.Document.Ammo.MaxAmmo);
        Assert.AreEqual(240, submachineGun.Document.Ammo.InitialAmmo);
        Assert.IsNotNull(submachineGun.Document.Sounds);
        Assert.AreEqual(
            "sounds/machine_gun_burst_dry_close.wav",
            submachineGun.Document.Sounds!.Fire);
        Assert.IsNotNull(submachineGun.Document.FireBehavior);
        Assert.IsTrue(submachineGun.Document.FireBehavior!.Automatic);
        Assert.AreEqual(125.0, submachineGun.Document.FireBehavior.IntervalMs, 1e-9);
        Assert.AreEqual(900.0, submachineGun.Document.FireBehavior.SoundIntervalMs, 1e-9);
    }

    [TestMethod]
    public void LoadKeepsMissingFramePathsEditable()
    {
        var directory = Directory.CreateTempSubdirectory("nurcade-weapon-missing-");
        var path = Path.Combine(directory.FullName, "broken.weapon.json");

        try {
            File.WriteAllText(
                path,
                """
                {
                  "weapon": "broken",
                  "format": "PNG",
                  "animations": {
                    "idle": {
                      "files": [ "missing.png" ]
                    }
                  }
                }
                """);

            var loaded = WeaponMetadataLoader.Load(path);

            Assert.IsFalse(loaded.Success);
            Assert.IsNotNull(loaded.Document);
            Assert.HasCount(1, loaded.Document!.Animations);
            Assert.AreEqual("missing.png", loaded.Document.Animations[0].Files[0]);
        }
        finally {
            directory.Delete(recursive: true);
        }
    }

    private static string DemoWeaponPath()
    {
        return DemoWeaponPath("super_shotgun", "super_shotgun.weapon.json");
    }

    private static string DemoWeaponPath(string folderName, string fileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "..",
            "res",
            "worlds",
            "demo_embedded",
            "weapons",
            folderName,
            fileName));
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            File.Copy(file, Path.Combine(targetDirectory, relative), overwrite: true);
        }
    }
}
