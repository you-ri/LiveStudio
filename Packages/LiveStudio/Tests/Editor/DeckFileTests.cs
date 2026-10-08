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

            var json = DeckFile.BuildJson("Talk / Game: 1?", 4, sets);

            Assert.IsTrue(DeckFile.TryParse(json, "test", out var name, out var columns, out var setsJson));
            Assert.AreEqual("Talk / Game: 1?", name, "the display name is stored as is, any characters included");
            Assert.AreEqual(4, columns);
            StringAssert.Contains("\"id\":\"set-1\"", setsJson);
        }

        [Test]
        public void TryParse_FileWithoutName_ReturnsEmptyName()
        {
            // A deck file written before the name was stored: the caller shows the file's stem instead, and
            // the name is written the next time the deck is saved.
            var legacy = "{\"format\":\"jp.lilium.livestudio.deck\",\"formatVersion\":1," +
                "\"columns\":8,\"operationSets\":[]}";

            Assert.IsTrue(DeckFile.TryParse(legacy, "test", out var name, out var columns, out _));
            Assert.AreEqual(string.Empty, name);
            Assert.AreEqual(8, columns);
        }

        [Test]
        public void TryParse_UnknownFormat_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(DeckFile.TryParse("{\"format\":\"something.else\"}", "test", out _, out _, out _));
        }

        [Test]
        public void TryParse_PreviousWholeLayerShape_ReturnsFalse()
        {
            // The first shape held every deck in one file. It never shipped, so it is reported and
            // skipped rather than converted — silently rewriting a user's file is worse.
            LogAssert.ignoreFailingMessages = true;
            var legacy = "{\"format\":\"jp.lilium.livestudio.deck\",\"formatVersion\":1," +
                "\"name\":\"old\",\"state\":{\"decks\":[],\"operationSets\":[]}}";
            Assert.IsFalse(DeckFile.TryParse(legacy, "test", out _, out _, out _));
        }

        [Test]
        public void TryParse_NewerVersion_ReadsBestEffort()
        {
            // Forward tolerance: a deck written by a newer build still opens (warns) rather than being lost.
            LogAssert.ignoreFailingMessages = true;
            var future = "{\"format\":\"jp.lilium.livestudio.deck\",\"formatVersion\":999," +
                "\"columns\":6,\"operationSets\":[]}";
            Assert.IsTrue(DeckFile.TryParse(future, "test", out _, out var columns, out _));
            Assert.AreEqual(6, columns);
        }

        [Test]
        public void TryParse_EmptyOrGarbage_ReturnsFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(DeckFile.TryParse("", "test", out _, out _, out _));
            Assert.IsFalse(DeckFile.TryParse("not json", "test", out _, out _, out _));
        }

        [TestCase("foo.deck.json", true)]
        [TestCase("a/b/foo.deck.json", true)]
        [TestCase("foo.DECK.JSON", true)]
        [TestCase("foo.live.json", false)]
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
            // The derived name is the file's stem: the deck's id, and the tab's name for a file without one.
            Assert.AreEqual("Live", AssetTypeRegistry.DeriveName("C:/proj/Decks/Live.deck.json"));
            Assert.AreEqual(DeckFile.Subfolder, AssetTypeRegistry.ResolveImportSubfolder("x.deck.json"));
            // The ".json" tail must not steal live scenes, which sit below decks in priority.
            Assert.IsInstanceOf<LiveSceneAsset>(AssetTypeRegistry.Create("C:/proj/Start.live.json"));
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
            manager.decks.Add(new Deck { id = "main", name = "Main", columns = 4 });
            manager.operationSets.Add(new OperationSet
            {
                id = "set-1",
                name = "Wave",
                enabled = true,
                input = new KeyInputSource(),
                control = new DeckToggle { deckId = "main", x = 2, y = 1 },
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
            Assert.AreEqual("main", restored.decks[0].id);
            Assert.AreEqual("Main", restored.decks[0].name);
            Assert.AreEqual("main", restored.operationSets[0].control.deckId);
            Assert.AreEqual(4, restored.decks[0].columns);
        }
    }
}
