// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;
using UnityEngine.TestTools;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies <see cref="ProjectSettingsStore.WriteAll"/> orphan pruning: a settings file for a
    /// deleted class or a superseded former (renamed) class is removed, while a current class whose
    /// object simply is not loaded this session is kept (so absent objects keep their settings).
    /// </summary>
    public class ProjectSettingsOrphanPruneTests
    {
        // Registered test class with a class-level rename alias, to exercise the former-name branch.
        [ExposedClass("OrphanPruneNew")]
        [FormerlyExposedAs("OrphanPruneOld")]
        public class OrphanPruneNew
        {
            [ExposedField(persistScope = PersistScope.Project)]
            public int v;
        }

        private string _proj;

        [SetUp]
        public void SetUp()
        {
            _proj = Path.Combine(Path.GetTempPath(), "lsprojsettings_" + Guid.NewGuid().ToString("N"));
            // Pre-create the Settings dir so we can drop fixture files before WriteAll.
            var dir = Path.GetDirectoryName(ProjectSettingsStore.GetSettingsFilePath(_proj, "x"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_proj)) Directory.Delete(_proj, recursive: true); } catch { /* best effort */ }
        }

        private static string SettingsJson(string typeName)
            => "{\"format\":\"jp.lilium.remotecontrol.projectsettings\",\"formatVersion\":1,\"objects\":[" +
               "{\"@type\":\"" + typeName + "\",\"@id\":\"id\",\"v\":1}]}";

        private void Drop(string className) =>
            File.WriteAllText(ProjectSettingsStore.GetSettingsFilePath(_proj, className), SettingsJson(className));

        private bool Exists(string className) =>
            File.Exists(ProjectSettingsStore.GetSettingsFilePath(_proj, className));

        [Test]
        public void WriteAll_RemovesDeletedClassOrphan()
        {
            LogAssert.ignoreFailingMessages = true; // deletion emits an info log
            Drop("GhostClassThatDoesNotExist");

            ProjectSettingsStore.WriteAll(_proj,
                new Dictionary<string, string> { { "TestScopeClass", SettingsJson("TestScopeClass") } });

            Assert.IsFalse(Exists("GhostClassThatDoesNotExist"), "deleted-class orphan should be pruned");
            Assert.IsTrue(Exists("TestScopeClass"), "the freshly written file must remain");
        }

        [Test]
        public void WriteAll_KeepsCurrentClassFileForAbsentObject()
        {
            // TestScopeShadowClass is a registered class but is not in the write set (object not loaded).
            // Its settings file must NOT be deleted, or an absent object would lose its project settings.
            Drop("TestScopeShadowClass");

            ProjectSettingsStore.WriteAll(_proj,
                new Dictionary<string, string> { { "TestScopeClass", SettingsJson("TestScopeClass") } });

            Assert.IsTrue(Exists("TestScopeShadowClass"), "a current class's file must be kept even when absent this session");
        }

        [Test]
        public void WriteAll_RemovesRenamedFormerNameFile()
        {
            LogAssert.ignoreFailingMessages = true; // deletion emits an info log
            // Old-name file left behind by a rename; the current-name file is written this round.
            Drop("OrphanPruneOld");

            ProjectSettingsStore.WriteAll(_proj,
                new Dictionary<string, string> { { "OrphanPruneNew", SettingsJson("OrphanPruneNew") } });

            Assert.IsFalse(Exists("OrphanPruneOld"), "stale former-name file should be pruned once the current-name file is written");
            Assert.IsTrue(Exists("OrphanPruneNew"));
        }
    }
}
