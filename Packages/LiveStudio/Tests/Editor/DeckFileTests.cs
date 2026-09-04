// Copyright (c) You-Ri, 2026

using System.IO;

using NUnit.Framework;
using UnityEngine.TestTools;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Tests the <see cref="DeckFile"/> format — one file is one deck (one tab of the operations page) —
    /// and the split that keeps the operation layer out of the live scene: the <see cref="OperationManager"/>'s
    /// deck members are <see cref="PersistScope.Custom"/>, so a live-scene save must not write them, while a
    /// snapshot of that scope must round-trip them. Pure logic — no play mode.
    ///
    /// Assertions use plain string checks so the test assembly needs no JSON library reference of its own
    /// (same reason as PropPresetTests).
    /// </summary>
    public class DeckFileTests
    {
        // -------------------------------------------------------
        // File format
        // -------------------------------------------------------

        [Test]
        public void BuildJson_RoundTripsThroughTryParse()
        {
            var sets = "[{\"@type\":\"OperationSet\",\"id\":\"set-1\"}]";

            var json = DeckFile.BuildJson(4, sets);

            Assert.IsTrue(DeckFile.TryParse(json, "test", out var columns, out var setsJson));
            Assert.AreEqual(4, columns);
            StringAssert.Contains("\"id\":\"set-1\"", setsJson);
        }

        [Test]
        public void BuildJson_DoesNotStoreTheDeckName()
        {
            // The file name is the deck's name. Writing it inside as well would be a second source of
            // truth that a rename (which moves the file) would immediately contradict.
            var json = DeckFile.BuildJson(8, "[]");

            StringAssert.Contains(DeckFile.FormatId, json);
            StringAssert.DoesNotContain("\"name\"", json);
        }

        [Test]
        public void TryParse_UnknownFormat_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(DeckFile.TryParse("{\"format\":\"something.else\"}", "test", out _, out _));
        }

        [Test]
        public void TryParse_PreviousWholeLayerShape_ReturnsFalse()
        {
            // The first shape held every deck in one file. It never shipped, so it is reported and
            // skipped rather than converted — silently rewriting a user's file is worse.
            LogAssert.ignoreFailingMessages = true;
            var legacy = "{\"format\":\"jp.lilium.livestudio.deck\",\"formatVersion\":1," +
                "\"name\":\"old\",\"state\":{\"decks\":[],\"operationSets\":[]}}";
            Assert.IsFalse(DeckFile.TryParse(legacy, "test", out _, out _));
        }

        [Test]
        public void TryParse_NewerVersion_ReadsBestEffort()
        {
            // Forward tolerance: a deck written by a newer build still opens (warns) rather than being lost.
            LogAssert.ignoreFailingMessages = true;
            var future = "{\"format\":\"jp.lilium.livestudio.deck\",\"formatVersion\":999," +
                "\"columns\":6,\"operationSets\":[]}";
            Assert.IsTrue(DeckFile.TryParse(future, "test", out var columns, out _));
            Assert.AreEqual(6, columns);
        }

        [Test]
        public void TryParse_EmptyOrGarbage_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(DeckFile.TryParse("", "test", out _, out _));
            Assert.IsFalse(DeckFile.TryParse("not json", "test", out _, out _));
        }

        [TestCase("foo.deck.json", true)]
        [TestCase("a/b/foo.deck.json", true)]
        [TestCase("foo.DECK.JSON", true)]
        [TestCase("foo.scene.json", false)]
        [TestCase("foo.json", false)]
        public void IsDeckFile_MatchesCompoundExtension(string path, bool expected)
        {
            Assert.AreEqual(expected, DeckFile.IsDeckFile(path));
        }

        [Test]
        public void SanitizeFileName_ReplacesInvalidCharsAndFallsBack()
        {
            // Deck names become file names, so this runs on every rename.
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                var result = DeckFile.SanitizeFileName($"a{invalid}b");
                Assert.IsFalse(result.IndexOf(invalid) >= 0, $"char {(int)invalid} should be sanitized");
            }
            Assert.AreEqual("Deck", DeckFile.SanitizeFileName(""));
            Assert.AreEqual("Deck", DeckFile.SanitizeFileName(null));
        }

        // -------------------------------------------------------
        // Asset kind registration
        // -------------------------------------------------------

        [Test]
        public void AssetTypeRegistry_ResolvesDeckFilesToDeckAsset()
        {
            var asset = AssetTypeRegistry.Create("C:/proj/Decks/Live.deck.json");

            Assert.IsInstanceOf<DeckAsset>(asset, "*.deck.json should be classified as a deck asset.");
            // The derived name is the tab's name, so this is what the operations page shows.
            Assert.AreEqual("Live", AssetTypeRegistry.DeriveName("C:/proj/Decks/Live.deck.json"));
            Assert.AreEqual(DeckFile.Subfolder, AssetTypeRegistry.ResolveImportSubfolder("x.deck.json"));
            // The ".json" tail must not steal live scenes, which sit below decks in priority.
            Assert.IsInstanceOf<LiveSceneAsset>(AssetTypeRegistry.Create("C:/proj/Start.scene.json"));
            // Live scenes saved under the previous ".live.json" name still classify, so an existing
            // project keeps listing them after the extension change.
            Assert.IsInstanceOf<LiveSceneAsset>(AssetTypeRegistry.Create("C:/proj/Start.live.json"));
        }

        // -------------------------------------------------------
        // Scope split: the deck lives in its own file, not in the live scene
        // -------------------------------------------------------

        [Test]
        public void LiveSceneScope_ExcludesOperationSetsAndDecks()
        {
            var manager = new OperationManager();
            var handle = LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Get<OperationManager>(), manager);

            var sceneJson = LiveObjectSnapshot.Capture(handle, PersistScope.Scene);

            StringAssert.DoesNotContain("operationSets", sceneJson, "The deck belongs to its file, not the live scene.");
            StringAssert.DoesNotContain("decks", sceneJson, "The deck belongs to its file, not the live scene.");
        }

        [Test]
        public void CustomScopeSnapshot_RoundTripsOperationSetsAndDecks()
        {
            var manager = new OperationManager();
            manager.decks.Add(new Deck { name = "Main", columns = 4 });
            manager.operationSets.Add(new OperationSet
            {
                id = "set-1",
                name = "Wave",
                enabled = true,
                input = new KeyInputSource(),
                control = new DeckToggle { deckName = "Main", x = 2, y = 1 },
            });
            var handle = LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Get<OperationManager>(), manager);

            var state = LiveObjectSnapshot.Capture(handle, PersistScope.Custom);
            StringAssert.Contains("operationSets", state);

            var restored = new OperationManager();
            Assert.IsTrue(LiveObjectSnapshot.Restore(state, LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Get<OperationManager>(), restored)));

            Assert.AreEqual(1, restored.operationSets.Count);
            Assert.AreEqual("set-1", restored.operationSets[0].id);
            Assert.IsInstanceOf<DeckToggle>(restored.operationSets[0].control, "The tile kind round-trips via @type.");
            Assert.AreEqual(2, restored.operationSets[0].control.x);
            Assert.AreEqual(1, restored.decks.Count);
            Assert.AreEqual("Main", restored.decks[0].name);
            Assert.AreEqual(4, restored.decks[0].columns);
        }
    }
}
