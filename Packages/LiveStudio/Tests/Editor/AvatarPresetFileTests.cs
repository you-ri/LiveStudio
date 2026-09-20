// Copyright (c) You-Ri, 2026
using System.IO;
using NUnit.Framework;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Where an avatar's own settings are kept, and what tells such a file apart from a preset the
    /// operator named (<see cref="AvatarPresetFile"/>).
    /// </summary>
    public class AvatarPresetFileTests
    {
        private string _projectPath;

        [SetUp]
        public void SetUp()
        {
            _projectPath = Path.Combine(Path.GetTempPath(), "AvatarPresetFileTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectPath, "Avatars"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectPath)) Directory.Delete(_projectPath, recursive: true);
        }

        private string _AvatarFile(string name)
        {
            var path = Path.Combine(_projectPath, "Avatars", name);
            File.WriteAllText(path, "not really an avatar");
            return path;
        }

        [Test]
        public void TheSettingsOfAnAvatarFile_SitBesideIt()
        {
            var vrm = _AvatarFile("7989649190987387872.vrm");

            var settings = AvatarPresetFile.ForAvatarFile(vrm);

            Assert.AreEqual(Path.GetDirectoryName(vrm), Path.GetDirectoryName(settings));
            Assert.AreEqual("7989649190987387872.preset.json", Path.GetFileName(settings));
        }

        [Test]
        public void ABundleAvatar_LosesItsWholeSuffix()
        {
            var bundle = _AvatarFile("hoge.avatar.lsb");

            Assert.AreEqual("hoge.preset.json", Path.GetFileName(AvatarPresetFile.ForAvatarFile(bundle)));
        }

        [Test]
        public void AnAvatarWithNoFileOfItsOwn_IsKeptInTheProjectSettings()
        {
            var builtin = AvatarPresetFile.ForBuiltin(_projectPath, "0123456789abcdef");
            var fallback = AvatarPresetFile.ForDefaultAvatar(_projectPath);

            StringAssert.EndsWith(Path.Combine("AvatarPresets", "0123456789abcdef.preset.json"), builtin);
            StringAssert.EndsWith(Path.Combine("AvatarPresets", "default.preset.json"), fallback);
        }

        [Test]
        public void TwoAvatarsOfTheSameNameOutsideTheProject_DoNotShareAFile()
        {
            var first = AvatarPresetFile.ForOutsideProject(_projectPath, @"D:\one\hoge.vrm");
            var second = AvatarPresetFile.ForOutsideProject(_projectPath, @"D:\two\hoge.vrm");

            Assert.AreNotEqual(first, second);
            StringAssert.StartsWith("hoge-", Path.GetFileName(first));
        }

        [Test]
        public void AFileBesideAnAvatar_IsThatAvatarsSettings_AndOneElsewhereIsAPreset()
        {
            var vrm = _AvatarFile("hoge.vrm");
            var beside = AvatarPresetFile.ForAvatarFile(vrm);
            var named = Path.Combine(_projectPath, "My favourite look.preset.json");

            Assert.IsTrue(AvatarPresetFile.IsAutoPreset(beside));
            Assert.IsTrue(AvatarPresetFile.IsAutoPreset(AvatarPresetFile.ForBuiltin(_projectPath, "0123")));
            Assert.IsFalse(AvatarPresetFile.IsAutoPreset(named));
            Assert.IsFalse(AvatarPresetFile.IsAutoPreset(vrm));
        }

        [Test]
        public void AnAvatarsOwnSettings_AreNotListedAsAnAssetOfTheirOwn()
        {
            var vrm = _AvatarFile("hoge.vrm");

            Assert.IsTrue(ExternalAssetManager.IsSupportedAssetFile(vrm));
            Assert.IsFalse(ExternalAssetManager.IsSupportedAssetFile(AvatarPresetFile.ForAvatarFile(vrm)));
            Assert.IsTrue(ExternalAssetManager.IsSupportedAssetFile(
                Path.Combine(_projectPath, "My favourite look.preset.json")));
        }
    }
}
