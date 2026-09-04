// Copyright (c) You-Ri, 2026

using System.IO;
using NUnit.Framework;
using Lilium.LiveStudio;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio.EditorTests
{
    [TestFixture]
    public class StartupSceneSwitcherTests
    {
        private string _stateDir;

        [SetUp]
        public void SetUp()
        {
            // A unique temp folder per test acts as the "project / state" directory.
            _stateDir = Path.Combine(Path.GetTempPath(), "StartupSceneSwitcherTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_stateDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_stateDir) && Directory.Exists(_stateDir))
                Directory.Delete(_stateDir, recursive: true);
        }

        // --- ResolveSwitchTargetIndex (pure) ---

        [Test]
        public void ResolveSwitchTargetIndex_FindsMatchingScene()
        {
            var build = new[] { "Boot", "Studio", "Editor" };
            Assert.AreEqual(1, StartupSceneSwitcher.ResolveSwitchTargetIndex("Studio", "Boot", build));
        }

        [Test]
        public void ResolveSwitchTargetIndex_AlreadyLoadingTarget_ReturnsMinusOne()
        {
            var build = new[] { "Studio", "Editor" };
            // The scene Unity is about to load (initial) already matches; no redirect needed.
            Assert.AreEqual(-1, StartupSceneSwitcher.ResolveSwitchTargetIndex("Studio", "Studio", build));
        }

        [Test]
        public void ResolveSwitchTargetIndex_NotInBuild_ReturnsMinusOne()
        {
            var build = new[] { "Boot", "Studio" };
            Assert.AreEqual(-1, StartupSceneSwitcher.ResolveSwitchTargetIndex("Missing", "Boot", build));
        }

        [Test]
        public void ResolveSwitchTargetIndex_EmptyBaseName_ReturnsMinusOne()
        {
            var build = new[] { "Boot", "Studio" };
            Assert.AreEqual(-1, StartupSceneSwitcher.ResolveSwitchTargetIndex("", "Boot", build));
            Assert.AreEqual(-1, StartupSceneSwitcher.ResolveSwitchTargetIndex(null, "Boot", build));
        }

        [Test]
        public void ResolveSwitchTargetIndex_NoBuildScenes_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, StartupSceneSwitcher.ResolveSwitchTargetIndex("Studio", null, new string[0]));
            Assert.AreEqual(-1, StartupSceneSwitcher.ResolveSwitchTargetIndex("Studio", null, null));
        }

        // --- ReadBaseSceneName (startup.json + live scene file) ---

        [Test]
        public void ReadBaseSceneName_NoStartupFile_ReturnsNull()
        {
            Assert.IsNull(StartupSceneSwitcher.ReadBaseSceneName(_stateDir));
        }

        [Test]
        public void ReadBaseSceneName_RecordedScene_ReturnsBaseSceneName()
        {
            var scenePath = Path.Combine(_stateDir, "MyScene.scene.json");
            File.WriteAllText(scenePath, "{\"baseSceneName\":\"Studio\"}");
            StartupStateStore.Write(_stateDir, scenePath);

            Assert.AreEqual("Studio", StartupSceneSwitcher.ReadBaseSceneName(_stateDir));
        }

        [Test]
        public void ReadBaseSceneName_RecordedFileMissing_ReturnsNull()
        {
            var scenePath = Path.Combine(_stateDir, "Gone.scene.json");
            File.WriteAllText(scenePath, "{\"baseSceneName\":\"Studio\"}");
            StartupStateStore.Write(_stateDir, scenePath);
            File.Delete(scenePath);

            Assert.IsNull(StartupSceneSwitcher.ReadBaseSceneName(_stateDir));
        }

        [Test]
        public void ReadBaseSceneName_SceneWithoutBaseName_ReturnsNull()
        {
            // Legacy file with no baseSceneName field.
            var scenePath = Path.Combine(_stateDir, "Legacy.scene.json");
            File.WriteAllText(scenePath, "{\"objects\":[]}");
            StartupStateStore.Write(_stateDir, scenePath);

            Assert.IsNull(StartupSceneSwitcher.ReadBaseSceneName(_stateDir));
        }
    }
}
