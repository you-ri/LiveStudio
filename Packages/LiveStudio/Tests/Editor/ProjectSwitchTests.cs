// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Opening a second project while one is already open has to re-initialize three things, one per
    /// section below: which live scene the incoming project starts on, which project folder receives
    /// the startup state from then on, and dropping the values the previous project left behind.
    ///
    /// <see cref="ProjectManager.OpenProject"/> itself is not driven end to end: it reloads the base
    /// Unity scene and resets every live host in it, which an edit-mode test cannot do without tearing
    /// down the editor's open scene. The pieces it delegates to are exercised directly instead.
    /// </summary>
    [TestFixture]
    public class ProjectSwitchTests
    {
        /// <summary>
        /// Minimal live-scene object: one exposed value whose default is captured by
        /// <see cref="ExposedObjectContainer.Initialize"/>, the way a real host captures its own at
        /// startup.
        /// </summary>
        [Serializable]
        [ExposedClass("ProjectSwitchTestObject", Icon = "test")]
        public class TestObject : IExposedObject
        {
            private string _id;
            private ExposedObjectHandle? _handle;

            public TestObject(string id) { _id = id; }

            [ExposedField]
            public int value;

            public string name { get => _id; set => _id = value; }
            public string id => _id;
            public ExposedObjectHandle? exposedObject => _handle;

            public void OnEnable() { _handle = ExposedObjectRegistry.Create<TestObject>(this, _id); }
            public void OnDisable() { _handle?.Unregister(); _handle = null; }
            public void OnDispose() { }
            public void Update() { }
            public void Reset() { value = 0; }
        }

        private const string kDefaultSceneFileName = "ProjectSwitchTests.live.json";

        private string _projectA;
        private string _sceneA;
        private string _projectB;
        private string _sceneB;

        [SetUp]
        public void SetUp()
        {
            // Two projects, each recording its own live scene in Settings/startup.json.
            _projectA = _CreateProject("A", out _sceneA);
            _projectB = _CreateProject("B", out _sceneB);

            // Project A is the one currently open (as ProjectManager._ApplySaveDirectory leaves it).
            LiveSceneSaveSystem.SetStateProjectDirectory(_projectA);
        }

        [TearDown]
        public void TearDown()
        {
            // The state directory is process-global, so leave it pointing where a real session would.
            var openProject = PlayerPrefs.GetString(ProjectManager.kProjectPathKey, "");
            LiveSceneSaveSystem.SetStateProjectDirectory(
                string.IsNullOrEmpty(openProject) ? null : openProject);

            _DeleteDirectory(_projectA);
            _DeleteDirectory(_projectB);
        }

        // --- Which scene the incoming project opens (ProjectManager.ResolveProjectScene) ---

        [Test]
        public void ResolveProjectScene_ReturnsTheSceneRecordedByThatProject()
        {
            // Switching from A to B lands on B's scene, never on the one A was left on.
            var resolved = ProjectManager.ResolveProjectScene(_projectB);

            Assert.AreEqual(Path.GetFullPath(_sceneB), Path.GetFullPath(resolved));
            Assert.AreNotEqual(Path.GetFullPath(_sceneA), Path.GetFullPath(resolved));
        }

        [Test]
        public void ResolveProjectScene_ProjectWithoutRecordedScene_ReturnsNull()
        {
            StartupStateStore.Delete(_projectB);

            // null opens a fresh scene. Falling back to the current one would keep the previous
            // project's scene loaded under the new project's name.
            Assert.IsNull(ProjectManager.ResolveProjectScene(_projectB));
        }

        [Test]
        public void ResolveProjectScene_RecordedSceneDeletedFromDisk_ReturnsNull()
        {
            File.Delete(_sceneB);

            Assert.IsNull(ProjectManager.ResolveProjectScene(_projectB));
        }

        [Test]
        public void ResolveProjectScene_FolderWithNoState_ReturnsNull()
        {
            var empty = Path.Combine(Path.GetTempPath(), "ProjectSwitchTests_Empty_" + Path.GetRandomFileName());
            Directory.CreateDirectory(empty);
            try
            {
                Assert.IsNull(ProjectManager.ResolveProjectScene(empty));
            }
            finally
            {
                _DeleteDirectory(empty);
            }
        }

        // --- Where the startup state goes after the switch (ProjectManager._ApplySaveDirectory) ---

        [Test]
        public void HostRebuiltAfterSwitch_ResolvesTheNewProjectsScene()
        {
            LiveSceneSaveSystem.SetStateProjectDirectory(_projectB);

            // The base-scene reload that follows a project switch rebuilds the host, so its
            // constructor re-resolves the current scene from the state file — now B's, not A's.
            var save = _NewSaveSystem();

            Assert.AreEqual(Path.GetFullPath(_sceneB), Path.GetFullPath(save.currentFullPath));
        }

        [Test]
        public void SceneRecordedAfterSwitch_GoesToTheNewProjectOnly()
        {
            var stateA = StartupStateStore.GetStartupFilePath(_projectA);
            var beforeA = File.ReadAllText(stateA);

            LiveSceneSaveSystem.SetStateProjectDirectory(_projectB);
            var save = _NewSaveSystem();
            save.currentFilePath = Path.Combine(_projectB, "Saved.live.json");

            StringAssert.Contains(
                "Saved.live.json",
                File.ReadAllText(StartupStateStore.GetStartupFilePath(_projectB)),
                "The opened project must record the scene it is now on.");
            Assert.AreEqual(beforeA, File.ReadAllText(stateA),
                "The project left behind must keep the scene it was on.");
        }

        // --- Dropping the previous project's values (LiveSceneManager.OpenProjectScene) ---

        [Test]
        public void ResetAllToDefault_DropsValuesEditedUnderThePreviousProject()
        {
            var obj = new TestObject("project-switch-edited");
            var container = _CreateInitializedContainer(obj);
            try
            {
                obj.value = 42; // edited while the previous project was open

                var save = new LiveSceneSaveSystem(container, kDefaultSceneFileName);
                save.ResetAllToDefault();

                // The incoming scene file is a delta, so any value it omits would survive the switch
                // unless every host is reset first.
                Assert.AreEqual(0, obj.value);
            }
            finally
            {
                container.Shutdown();
            }
        }

        [Test]
        public void ResetAllToDefault_ResetsEvenWhenNothingCountsAsEdited()
        {
            var obj = new TestObject("project-switch-clean");
            var container = _CreateInitializedContainer(obj);
            try
            {
                obj.value = 42;
                // A load re-baselines every object as clean, so the previous project's values are not
                // reported as edited any more.
                obj.exposedObject.Value.MarkClean();

                var save = new LiveSceneSaveSystem(container, kDefaultSceneFileName);

                save.RevertAllToDefault();
                Assert.AreEqual(42, obj.value,
                    "Guard: a dirty-only revert cannot drop the previous project's values.");

                save.ResetAllToDefault();
                Assert.AreEqual(0, obj.value);
            }
            finally
            {
                container.Shutdown();
            }
        }

        // --- Helpers ---

        // A project folder holding one live scene, recorded as that project's startup scene.
        private static string _CreateProject(string label, out string scenePath)
        {
            var root = Path.Combine(
                Path.GetTempPath(), "ProjectSwitchTests_" + label + "_" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);

            scenePath = Path.Combine(root, "Scene" + label + ".live.json");
            File.WriteAllText(scenePath, "{\"baseSceneName\":\"Studio\"}");
            StartupStateStore.Write(root, scenePath);
            return root;
        }

        // A host-less save system: only its path / startup-state behaviour is under test.
        private static LiveSceneSaveSystem _NewSaveSystem()
        {
            var container = new ExposedObjectContainer("ProjectSwitchTests", new List<IExposedObject>());
            return new LiveSceneSaveSystem(container, kDefaultSceneFileName);
        }

        // A container whose objects have their defaults captured, as a host does at startup.
        private static ExposedObjectContainer _CreateInitializedContainer(IExposedObject obj)
        {
            var container = new ExposedObjectContainer(
                "ProjectSwitchTests", new List<IExposedObject> { obj });
            container.Initialize();
            return container;
        }

        private static void _DeleteDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
