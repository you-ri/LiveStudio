// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using NUnit.Framework;
using UnityEngine.TestTools;

using Lilium.LiveStudio;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Pure-logic tests for the <see cref="PropPreset"/> object-preset file format: build/parse
    /// round-trip for both asset kinds, the crawl-time kind peek, and the path/name helpers.
    /// Assertions use plain string checks so the test assembly needs no JSON library reference of
    /// its own.
    /// </summary>
    public class PropPresetTests
    {
        [Test]
        public void BuildJson_Prop_RoundTripsThroughTryParse()
        {
            var state = "{\"version\":1,\"wrapper\":{\"x\":5}}";
            var json = PropPreset.BuildJson(
                PropPreset.AssetKind.Prop, "My Prop", "Props/foo.glb", state);

            Assert.IsTrue(PropPreset.TryParse(json, out var data));
            Assert.AreEqual("My Prop", data.name);
            Assert.AreEqual("Props/foo.glb", data.source);
            Assert.AreEqual(PropPreset.AssetKind.Prop, data.kind);
            // The embedded state survives the round-trip (compact form, key order preserved).
            StringAssert.Contains("\"wrapper\":{\"x\":5}", data.state);
        }

        [Test]
        public void BuildJson_Avatar_PreservesKindAndUsesObjectPresetFormat()
        {
            var json = PropPreset.BuildJson(
                PropPreset.AssetKind.Avatar, "My Avatar", "Avatars/foo.vrm", "{\"a\":1}");

            // The new format wraps recreation info in a target descriptor.
            StringAssert.Contains(PropPreset.FormatId, json);
            StringAssert.Contains(PropPreset.StrategyAsset, json);
            StringAssert.Contains("avatar", json);

            Assert.IsTrue(PropPreset.TryParse(json, out var data));
            Assert.AreEqual(PropPreset.AssetKind.Avatar, data.kind);
            Assert.AreEqual("Avatars/foo.vrm", data.source);
        }

        [Test]
        public void TryParse_UnknownFormat_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(PropPreset.TryParse("{\"format\":\"something.else\"}", out _));
        }

        [Test]
        public void TryParse_NewerVersion_ReadsBestEffort()
        {
            // Forward tolerance: a newer writer is read best-effort (warns) rather than rejected, so a
            // user's preset is not lost when opened on an older client.
            LogAssert.ignoreFailingMessages = true; // emits a warning, not an error
            var future = "{\"format\":\"jp.lilium.remotecontrol.preset\",\"formatVersion\":999," +
                "\"name\":\"f\",\"target\":{\"strategy\":\"asset\",\"kind\":\"prop\",\"source\":\"a.glb\"}}";
            Assert.IsTrue(PropPreset.TryParse(future, out var data));
            Assert.AreEqual("a.glb", data.source);
            Assert.AreEqual(PropPreset.AssetKind.Prop, data.kind);
        }

        [Test]
        public void TryParse_BareAvatarState_StillParses()
        {
            // An avatar preset whose `state` is a bare AvatarController snapshot (no wrapper/components,
            // from an earlier build) still parses; the bare shape is handled at restore time (AvatarAsset).
            var bare = "{\"format\":\"jp.lilium.remotecontrol.preset\",\"formatVersion\":1," +
                "\"name\":\"old\",\"target\":{\"strategy\":\"asset\",\"kind\":\"avatar\",\"source\":\"a.vrm\"}," +
                "\"state\":{\"bare\":\"controllerSnapshot\"}}";
            Assert.IsTrue(PropPreset.TryParse(bare, out var data));
            Assert.AreEqual(PropPreset.AssetKind.Avatar, data.kind);
            Assert.AreEqual("a.vrm", data.source);
            StringAssert.Contains("controllerSnapshot", data.state);
        }

        [Test]
        public void TryParse_MissingVersion_Parses()
        {
            // A hand-edited / pre-format file with no formatVersion is treated as the minimum supported.
            var noVer = "{\"format\":\"jp.lilium.remotecontrol.preset\"," +
                "\"target\":{\"strategy\":\"asset\",\"kind\":\"prop\",\"source\":\"a.glb\"}}";
            Assert.IsTrue(PropPreset.TryParse(noVer, out var data));
            Assert.AreEqual("a.glb", data.source);
        }

        [Test]
        public void TryParse_EmptyOrGarbage_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(PropPreset.TryParse("", out _));
            Assert.IsFalse(PropPreset.TryParse("not json", out _));
        }

        [TestCase("foo.preset.json", true)]
        [TestCase("a/b/foo.preset.json", true)]
        [TestCase("foo.PRESET.JSON", true)]
        [TestCase("foo.glb", false)]
        [TestCase("foo.json", false)]
        public void IsPresetFile_MatchesCompoundExtension(string path, bool expected)
        {
            Assert.AreEqual(expected, PropPreset.IsPresetFile(path));
        }

        [Test]
        public void SanitizeFileName_ReplacesInvalidCharsAndFallsBack()
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                var result = PropPreset.SanitizeFileName($"a{invalid}b");
                Assert.IsFalse(result.IndexOf(invalid) >= 0, $"char {(int)invalid} should be sanitized");
            }
            Assert.AreEqual("preset", PropPreset.SanitizeFileName(""));
            Assert.AreEqual("preset", PropPreset.SanitizeFileName(null));
        }

        [Test]
        public void ResolveSource_ResolvesRelativeAgainstBaseDir()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "lspreset_base");
            var resolved = PropPreset.ResolveSource("sub/foo.glb", baseDir);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(baseDir, "sub/foo.glb")), resolved);
        }

        [Test]
        public void ResolveSource_KeepsRootedPath()
        {
            var rooted = Path.Combine(Path.GetTempPath(), "abs", "foo.glb");
            Assert.AreEqual(rooted, PropPreset.ResolveSource(rooted, "ignored/base"));
        }

        [Test]
        public void Relativize_ThenResolveSource_RecoversAbsolutePath()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "lspreset_proj");
            var absolute = Path.GetFullPath(Path.Combine(baseDir, "Props", "foo.glb"));

            var relative = PropPreset.Relativize(absolute, baseDir);
            Assert.IsFalse(Path.IsPathRooted(relative), "Relativize should yield a relative path under the base dir");

            var recovered = PropPreset.ResolveSource(relative, baseDir);
            Assert.AreEqual(absolute, Path.GetFullPath(recovered));
        }

        [Test]
        public void PeekKind_ReadsKindFromFile_AndDefaultsToPropOnFailure()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lspreset_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var avatarPath = Path.Combine(dir, "av.preset.json");
                File.WriteAllText(avatarPath, PropPreset.BuildJson(
                    PropPreset.AssetKind.Avatar, "av", "a.vrm", ""));
                Assert.AreEqual(PropPreset.AssetKind.Avatar, PropPreset.PeekKind(avatarPath));

                var propPath = Path.Combine(dir, "pr.preset.json");
                File.WriteAllText(propPath, PropPreset.BuildJson(
                    PropPreset.AssetKind.Prop, "pr", "a.glb", ""));
                Assert.AreEqual(PropPreset.AssetKind.Prop, PropPreset.PeekKind(propPath));

                // Missing / unreadable file defaults to Prop (legacy behavior).
                LogAssert.ignoreFailingMessages = true;
                Assert.AreEqual(PropPreset.AssetKind.Prop,
                    PropPreset.PeekKind(Path.Combine(dir, "missing.preset.json")));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
