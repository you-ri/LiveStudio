// Copyright (c) You-Ri, 2026

using System.Linq;

using NUnit.Framework;
using UnityEngine.TestTools;

using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Tests for the shared <see cref="FormatHeader"/> used by the live scene, project settings and
    /// object-preset formats. Verifies the byte-stable write order and the single versioning policy
    /// (missing => min, below min => reject, above current => best-effort).
    /// </summary>
    public class FormatHeaderTests
    {
        const string kId = "jp.lilium.remotecontrol.test";

        [Test]
        public void Write_PutsFormatThenVersionAsFirstTwoFields()
        {
            var root = new JObject();
            FormatHeader.Write(root, kId, 3);

            var names = root.Properties().Select(p => p.Name).ToArray();
            Assert.AreEqual("format", names[0]);
            Assert.AreEqual("formatVersion", names[1]);
            Assert.AreEqual(kId, root["format"]?.Value<string>());
            Assert.AreEqual(3, root["formatVersion"]?.Value<int>());
        }

        [Test]
        public void TryReadVersion_MissingVersion_TreatedAsMin()
        {
            var root = new JObject { ["format"] = kId };
            Assert.IsTrue(FormatHeader.TryReadVersion(root, "Test", currentVersion: 2, minVersion: 1, out var v));
            Assert.AreEqual(1, v);
        }

        [Test]
        public void TryReadVersion_BelowMin_Rejected()
        {
            LogAssert.ignoreFailingMessages = true; // emits an error log
            var root = new JObject { ["format"] = kId, ["formatVersion"] = 0 };
            Assert.IsFalse(FormatHeader.TryReadVersion(root, "Test", currentVersion: 2, minVersion: 1, out _));
        }

        [Test]
        public void TryReadVersion_NewerThanCurrent_BestEffort()
        {
            LogAssert.ignoreFailingMessages = true; // emits a warning log
            var root = new JObject { ["format"] = kId, ["formatVersion"] = 999 };
            Assert.IsTrue(FormatHeader.TryReadVersion(root, "Test", currentVersion: 2, minVersion: 1, out var v));
            Assert.AreEqual(999, v);
        }

        [Test]
        public void TryReadVersion_CurrentVersion_Ok()
        {
            var root = new JObject { ["format"] = kId, ["formatVersion"] = 2 };
            Assert.IsTrue(FormatHeader.TryReadVersion(root, "Test", currentVersion: 2, minVersion: 1, out var v));
            Assert.AreEqual(2, v);
        }
    }
}
