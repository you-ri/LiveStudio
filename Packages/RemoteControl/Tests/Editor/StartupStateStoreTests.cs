// Copyright (c) You-Ri, 2026
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    [TestFixture]
    public class StartupStateStoreTests
    {
        private string _projectPath;

        [SetUp]
        public void SetUp()
        {
            // A unique temp folder per test acts as the "project folder".
            _projectPath = Path.Combine(Path.GetTempPath(), "StartupStateStoreTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_projectPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_projectPath) && Directory.Exists(_projectPath))
                Directory.Delete(_projectPath, recursive: true);
        }

        [Test]
        public void Write_StoresUnderSettingsFolder()
        {
            var scene = Path.Combine(_projectPath, "MyScene.live.json");
            StartupStateStore.Write(_projectPath, scene);

            var expected = Path.Combine(_projectPath, "Settings", "startup.json");
            Assert.IsTrue(File.Exists(expected), "startup.json should be written under the Settings folder.");
        }

        [Test]
        public void WriteRead_SceneInsideProject_RoundTripsToSameAbsolutePath()
        {
            var scene = Path.Combine(_projectPath, "Sub", "MyScene.live.json");
            StartupStateStore.Write(_projectPath, scene);

            var read = StartupStateStore.Read(_projectPath);
            Assert.AreEqual(Path.GetFullPath(scene), Path.GetFullPath(read));
        }

        [Test]
        public void Write_SceneInsideProject_StoresRelativePath()
        {
            var scene = Path.Combine(_projectPath, "MyScene.live.json");
            StartupStateStore.Write(_projectPath, scene);

            var json = File.ReadAllText(Path.Combine(_projectPath, "Settings", "startup.json"));
            StringAssert.Contains("\"MyScene.live.json\"", json);
            StringAssert.DoesNotContain(_projectPath.Replace('\\', '/'), json,
                "A scene inside the project should be stored relative, not absolute.");
        }

        [Test]
        public void WriteRead_SceneOutsideProject_KeepsAbsolutePath()
        {
            var outside = Path.Combine(Path.GetTempPath(), "Outside_" + Path.GetRandomFileName() + ".live.json");
            StartupStateStore.Write(_projectPath, outside);

            var read = StartupStateStore.Read(_projectPath);
            Assert.AreEqual(Path.GetFullPath(outside), Path.GetFullPath(read));
        }

        [Test]
        public void Read_NoFile_ReturnsNull()
        {
            Assert.IsNull(StartupStateStore.Read(_projectPath));
        }

        [Test]
        public void Read_EmptyScenePath_ReturnsNull()
        {
            // switchSceneOnLoad == false writes an empty scene path to suppress the startup switch.
            StartupStateStore.Write(_projectPath, "");
            Assert.IsNull(StartupStateStore.Read(_projectPath));
        }

        [Test]
        public void Delete_RemovesFile()
        {
            StartupStateStore.Write(_projectPath, Path.Combine(_projectPath, "MyScene.live.json"));
            var path = Path.Combine(_projectPath, "Settings", "startup.json");
            Assert.IsTrue(File.Exists(path));

            StartupStateStore.Delete(_projectPath);
            Assert.IsFalse(File.Exists(path));
        }

        [Test]
        public void Read_NullProjectPath_ReturnsNull()
        {
            Assert.IsNull(StartupStateStore.Read(null));
            Assert.IsNull(StartupStateStore.Read(""));
        }

        [Test]
        public void StartupFile_IsNotMistakenForALiveSceneFile()
        {
            // The state file must not be picked up by the project crawler as a live scene.
            var path = StartupStateStore.GetStartupFilePath(_projectPath);
            Assert.IsFalse(LiveSceneSaveSystem.IsLiveSceneFile(path),
                "Settings/startup.json must not be classified as a live scene file.");
        }
    }
}
