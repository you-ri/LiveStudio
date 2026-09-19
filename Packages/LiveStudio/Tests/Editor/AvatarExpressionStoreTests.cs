// Copyright (c) You-Ri, 2026
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Expression settings belong to the avatar and are kept in the project, one entry per avatar
    /// (<see cref="AvatarExpressionStore"/>), rather than in the live scene.
    /// </summary>
    public class AvatarExpressionStoreTests
    {
        private string _projectPath;
        private AvatarExpressionConfig _config;

        [SetUp]
        public void SetUp()
        {
            _projectPath = Path.Combine(Path.GetTempPath(), "AvatarExpressionStoreTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectPath);
            _config = ScriptableObject.CreateInstance<AvatarExpressionConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
            if (Directory.Exists(_projectPath)) Directory.Delete(_projectPath, recursive: true);
        }

        private JObject _ReadFile() => JObject.Parse(File.ReadAllText(AvatarExpressionStore.GetFilePath(_projectPath)));

        [Test]
        public void ReturningToAnAvatar_RestoresWhatWasSetForIt()
        {
            var store = new AvatarExpressionStore();
            store.SwitchTo("Avatars/a.vrm", _projectPath, _config);
            _config.syncBlink = true;

            store.SwitchTo("Avatars/b.vrm", _projectPath, _config);
            _config.syncBlink = false;

            store.SwitchTo("Avatars/a.vrm", _projectPath, _config);
            Assert.IsTrue(_config.syncBlink);

            store.SwitchTo("Avatars/b.vrm", _projectPath, _config);
            Assert.IsFalse(_config.syncBlink);
        }

        [Test]
        public void AnAvatarSeenForTheFirstTime_StartsFromTheCurrentValues_AndIsWritten()
        {
            var store = new AvatarExpressionStore();
            store.SwitchTo("Avatars/a.vrm", _projectPath, _config);
            _config.syncBlink = true;

            store.SwitchTo("builtin:0123", _projectPath, _config);

            Assert.IsTrue(_config.syncBlink);
            var avatars = (JObject)_ReadFile()["avatars"];
            Assert.IsTrue(avatars["Avatars/a.vrm"]["syncBlink"].Value<bool>());
            Assert.IsTrue(avatars["builtin:0123"]["syncBlink"].Value<bool>());
        }

        [Test]
        public void AnEdit_IsWrittenOnFlush_ForTheCurrentAvatarOnly()
        {
            var store = new AvatarExpressionStore();
            store.SwitchTo("Avatars/a.vrm", _projectPath, _config);
            store.SwitchTo("Avatars/b.vrm", _projectPath, _config);

            _config.syncBlink = true;
            store.Flush(_config);

            var avatars = (JObject)_ReadFile()["avatars"];
            Assert.IsFalse(avatars["Avatars/a.vrm"]["syncBlink"].Value<bool>());
            Assert.IsTrue(avatars["Avatars/b.vrm"]["syncBlink"].Value<bool>());
        }

        [Test]
        public void AnotherStore_ReadsWhatAnEarlierOneWrote()
        {
            var first = new AvatarExpressionStore();
            first.SwitchTo("Avatars/a.vrm", _projectPath, _config);
            _config.syncBlink = true;
            first.Flush(_config);

            // A later session: a fresh config holding the asset's values.
            var config = ScriptableObject.CreateInstance<AvatarExpressionConfig>();
            try
            {
                new AvatarExpressionStore().SwitchTo("Avatars/a.vrm", _projectPath, config);
                Assert.IsTrue(config.syncBlink);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void WithNoProjectOpen_NothingIsWritten()
        {
            var store = new AvatarExpressionStore();
            store.SwitchTo("Avatars/a.vrm", string.Empty, _config);
            _config.syncBlink = true;
            store.Flush(_config);

            Assert.IsFalse(Directory.Exists(Path.Combine(_projectPath, "Settings")));
        }

        [Test]
        public void AFileThisBuildCannotRead_IsLeftAlone()
        {
            var path = AvatarExpressionStore.GetFilePath(_projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "not json");

            // Read once for the avatar's entry, once more before the write it then declines.
            var failedToRead = new Regex("Failed to read the avatar expression settings");
            LogAssert.Expect(LogType.Error, failedToRead);
            LogAssert.Expect(LogType.Error, failedToRead);

            new AvatarExpressionStore().SwitchTo("Avatars/a.vrm", _projectPath, _config);

            Assert.AreEqual("not json", File.ReadAllText(path));
        }

        [Test]
        public void TheLiveScene_DoesNotSaveTheSettings()
        {
            var handle = LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Get<AvatarExpressionConfig>(), _config);

            var scene = JObject.Parse(LiveObjectSnapshot.Capture(handle, PersistScope.Scene));
            var custom = JObject.Parse(LiveObjectSnapshot.Capture(handle, PersistScope.Custom));

            Assert.IsNull(scene["syncBlink"]);
            Assert.IsNull(scene["expressions"]);
            Assert.IsNotNull(custom["syncBlink"]);
            Assert.IsNotNull(custom["expressions"]);
        }
    }
}
