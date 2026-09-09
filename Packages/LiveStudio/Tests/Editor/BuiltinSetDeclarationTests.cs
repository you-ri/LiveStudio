// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Text.RegularExpressions;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// A scene becomes a set by being declared, and the declaration has to hold up before the list reaches
    /// an operator: a scene missing from the build could never load, the bootstrap scene would duplicate
    /// the studio on top of itself, and two sets answering to one name would make a saved stage ambiguous
    /// (the stage records which set is up by name). Each of those is refused here with an error, rather
    /// than listed and left to fail later.
    /// </summary>
    public class BuiltinSetDeclarationTests
    {
        const string kBasePath = "Assets/Scene/Studio.unity";

        static BuiltinSetList.Entry _Entry(string guid, string scenePath, string displayName = null)
            => new BuiltinSetList.Entry
            {
                guid = guid,
                scenePath = scenePath,
                displayName = displayName,
            };

        // Every declared scene is in the build unless a test says otherwise.
        static IReadOnlyList<AssetBase> _Build(params BuiltinSetList.Entry[] entries)
            => BuiltinSetSource.BuildSets(entries, kBasePath, _ => true);

        [Test]
        public void ADeclaredScene_BecomesASetIdentifiedByItsGuid()
        {
            var sets = _Build(_Entry("abc123", "Assets/Sets/Room.unity"));

            Assert.AreEqual(1, sets.Count);
            var set = (BuiltinSetAsset)sets[0];
            Assert.AreEqual("abc123", set.id, "the guid is the identity");
            Assert.AreEqual("abc123", set.guid);
            Assert.AreEqual("abc123", set.persistentId, "what a saved scene stores");
            Assert.AreEqual("Assets/Sets/Room.unity", set.scenePath);
            Assert.IsTrue(set.isBuiltin);
        }

        [Test]
        public void WithNoDisplayName_TheSceneFileNameIsUsed()
        {
            var sets = _Build(_Entry("abc123", "Assets/Sets/Room.unity"));

            Assert.AreEqual("Room", sets[0].name);
        }

        [Test]
        public void ADeclaredDisplayName_WinsOverTheFileName()
        {
            var sets = _Build(_Entry("abc123", "Assets/Sets/Room.unity", "Night Stage"));

            Assert.AreEqual("Night Stage", sets[0].name);
        }

        [Test]
        public void TheBootstrapScene_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex("base scene"));

            var sets = _Build(_Entry("abc123", kBasePath));

            Assert.AreEqual(0, sets.Count, "loading the studio on top of itself is never offered");
        }

        [Test]
        public void ASceneMissingFromTheBuild_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex("not in the build"));

            var sets = BuiltinSetSource.BuildSets(
                new[] { _Entry("abc123", "Assets/Sets/Room.unity") }, kBasePath, _ => false);

            Assert.AreEqual(0, sets.Count);
        }

        [Test]
        public void TheSameSceneDeclaredTwice_IsListedOnce()
        {
            LogAssert.Expect(LogType.Error, new Regex("declared twice"));

            var sets = _Build(
                _Entry("abc123", "Assets/Sets/Room.unity"),
                _Entry("abc123", "Assets/Sets/Room.unity"));

            Assert.AreEqual(1, sets.Count);
        }

        [Test]
        public void TwoSetsSharingADisplayName_KeepTheFirstOnly()
        {
            LogAssert.Expect(LogType.Error, new Regex("named"));

            var sets = _Build(
                _Entry("abc123", "Assets/Sets/Room.unity", "Stage"),
                _Entry("def456", "Assets/Sets/Hall.unity", "Stage"));

            Assert.AreEqual(1, sets.Count, "a stage recorded by name must resolve to one set");
            Assert.AreEqual("abc123", sets[0].id);
        }

        /// <summary>
        /// The bootstrap set is listed under its scene's file name, so a declared scene with that same file
        /// name — in another folder, so it is not the base scene itself — would list under a name that
        /// always resolved to the bootstrap entry. It is refused rather than listed and unreachable.
        /// </summary>
        [Test]
        public void ASceneNamedLikeTheBootstrapScene_IsRefused()
        {
            LogAssert.Expect(LogType.Error, new Regex("named"));

            var sets = _Build(_Entry("abc123", "Assets/Sets/Studio.unity"));

            Assert.AreEqual(0, sets.Count);
        }

        [Test]
        public void AnIncompleteEntry_IsSkippedQuietly()
        {
            // A row the author has not filled in yet is not a mistake worth an error.
            var sets = _Build(
                _Entry(null, null),
                _Entry("abc123", "Assets/Sets/Room.unity"));

            Assert.AreEqual(1, sets.Count);
        }
    }

    /// <summary>
    /// A set bundle the operator dropped in can be called the same thing as a set the app ships — the two
    /// come from different places and neither knows about the other. Names are how a saved scene and a take
    /// say which stage was up, so the tie has to break the same way every time or a restore picks a
    /// different stage on different machines. It breaks towards the built-in one: that is the copy every
    /// machine running this app is guaranteed to have.
    /// </summary>
    public class SetNameCollisionTests
    {
        static SetBundleEntry _Entry(string id, string name, bool isBuiltin)
            => new SetBundleEntry { id = id, name = name, isBuiltin = isBuiltin };

        [Test]
        public void WithABundleOfTheSameName_TheBuiltInSetWins()
        {
            var sets = new[]
            {
                // The bundle comes first: the catalog is crawled before the built-ins are injected, so
                // "first match wins" would have picked it.
                _Entry("C:/Projects/Show/Room.set.lsb", "Room", isBuiltin: false),
                _Entry("abc123", "Room", isBuiltin: true),
            };

            Assert.AreEqual("abc123", StageManager.FindSetIdByName(sets, "Room"));
        }

        [Test]
        public void WithNoBuiltInOfThatName_TheBundleIsStillFound()
        {
            var sets = new[] { _Entry("C:/Projects/Show/Hall.set.lsb", "Hall", isBuiltin: false) };

            Assert.AreEqual("C:/Projects/Show/Hall.set.lsb", StageManager.FindSetIdByName(sets, "Hall"));
        }

        [Test]
        public void AnUnknownName_ResolvesToNothing()
        {
            var sets = new[] { _Entry("abc123", "Room", isBuiltin: true) };

            Assert.IsNull(StageManager.FindSetIdByName(sets, "Nowhere"));
        }

        [Test]
        public void InTheCatalogToo_TheBuiltInSetWins()
        {
            var view = new AssetBase[]
            {
                new SetBundleAsset { id = "C:/Projects/Show/Room.set.lsb", name = "Room" },
                new BuiltinSetAsset { id = "abc123", guid = "abc123", name = "Room" },
            };

            Assert.AreEqual("abc123", StageManager.FindSetAssetIdByName(view, "Room"));
        }

        [Test]
        public void ANonSetAssetOfTheSameName_IsNeverPicked()
        {
            var view = new AssetBase[]
            {
                new PropAsset { id = "C:/Projects/Show/Room.prop.lsb", name = "Room" },
                new BuiltinSetAsset { id = "abc123", guid = "abc123", name = "Room" },
            };

            Assert.AreEqual("abc123", StageManager.FindSetAssetIdByName(view, "Room"));
        }
    }
}
