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
    /// round-trip for both asset kinds, backward compatibility with the legacy <c>prop.preset</c>
    /// format, the crawl-time kind peek, and the path/name helpers. Assertions use plain string
    /// checks so the test assembly needs no JSON library reference of its own.
    /// </summary>
    public class PropPresetTests
    {
        [Test]
        public void BuildJson_Prop_RoundTripsThroughTryParse()
        {
            var state = "{\"version\":1,\"wrapper\":{\"x\":5}}";
            var json = PropPreset.BuildJson(
                PropPreset.AssetKind.Prop, "My Prop", "Props/foo.glb", "glb", state);

            Assert.IsTrue(PropPreset.TryParse(json, out var data));
            Assert.AreEqual("My Prop", data.name);
            Assert.AreEqual("Props/foo.glb", data.source);
            Assert.AreEqual("glb", data.sourceKind);
            Assert.AreEqual(PropPreset.AssetKind.Prop, data.kind);
            // The embedded state survives the round-trip (compact form, key order preserved).
            StringAssert.Contains("\"wrapper\":{\"x\":5}", data.state);
        }

        [Test]
        public void BuildJson_Avatar_PreservesKindAndUsesObjectPresetFormat()
        {
            var json = PropPreset.BuildJson(
                PropPreset.AssetKind.Avatar, "My Avatar", "Avatars/foo.vrm", "vrm", "{\"a\":1}");

            // The new format wraps recreation info in a target descriptor.
            StringAssert.Contains(PropPreset.FormatId, json);
            StringAssert.Contains(PropPreset.StrategyAsset, json);
            StringAssert.Contains("avatar", json);

            Assert.IsTrue(PropPreset.TryParse(json, out var data));
            Assert.AreEqual(PropPreset.AssetKind.Avatar, data.kind);
            Assert.AreEqual("Avatars/foo.vrm", data.source);
            Assert.AreEqual("vrm", data.sourceKind);
        }

        [Test]
        public void TryParse_LegacyPropFormat_ParsesAsProp()
        {
            // A file written before the target-descriptor change: root-level source/sourceKind.
            var legacy =
                "{\"format\":\"prop.preset\",\"version\":1,\"name\":\"Old\"," +
                "\"source\":\"Props/old.prop.lsb\",\"sourceKind\":\"prop.lsb\"," +
                "\"state\":{\"version\":1}}";

            Assert.IsTrue(PropPreset.TryParse(legacy, out var data));
            Assert.AreEqual(PropPreset.AssetKind.Prop, data.kind);
            Assert.AreEqual("Old", data.name);
            Assert.AreEqual("Props/old.prop.lsb", data.source);
            Assert.AreEqual("prop.lsb", data.sourceKind);
        }

        [Test]
        public void TryParse_UnknownFormat_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(PropPreset.TryParse("{\"format\":\"something.else\"}", out _));
        }

        [Test]
        public void TryParse_NewerVersion_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            var future = "{\"format\":\"object.preset\",\"version\":999,\"target\":{}}";
            Assert.IsFalse(PropPreset.TryParse(future, out _));
        }

        [Test]
        public void TryParse_EmptyOrGarbage_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(PropPreset.TryParse("", out _));
            Assert.IsFalse(PropPreset.TryParse("not json", out _));
        }

        [TestCase("a/b/foo.prop.lsb", "prop.lsb")]
        [TestCase("foo.glb", "glb")]
        [TestCase("foo.gltf", "gltf")]
        [TestCase("dir/foo.avatar.lsb", "avatar.lsb")]
        [TestCase("foo.vrm", "vrm")]
        [TestCase("foo.lsavatar", "lsavatar")]
        public void GetSourceKind_ReturnsExpectedHint(string path, string expected)
        {
            Assert.AreEqual(expected, PropPreset.GetSourceKind(path));
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
                    PropPreset.AssetKind.Avatar, "av", "a.vrm", "vrm", ""));
                Assert.AreEqual(PropPreset.AssetKind.Avatar, PropPreset.PeekKind(avatarPath));

                var propPath = Path.Combine(dir, "pr.preset.json");
                File.WriteAllText(propPath, PropPreset.BuildJson(
                    PropPreset.AssetKind.Prop, "pr", "a.glb", "glb", ""));
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
