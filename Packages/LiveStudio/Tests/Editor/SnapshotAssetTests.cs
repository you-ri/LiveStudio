// Copyright (c) You-Ri, 2026

using NUnit.Framework;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Tests that a snapshot file is one of the project's asset kinds — so it is listed on the project
    /// page beside live scenes and decks — and that adding it did not let the shared ".json" tail steal
    /// the other kinds. Pure logic (classification is path-only, no file is read).
    /// </summary>
    public class SnapshotAssetTests
    {
        [Test]
        public void AssetTypeRegistry_ResolvesSnapshotFilesToSnapshotAsset()
        {
            var asset = AssetTypeRegistry.Create("C:/proj/Snapshots/Demo.snapshot.json");

            Assert.IsInstanceOf<SnapshotAsset>(asset, "*.snapshot.json should be classified as a snapshot asset.");
            // The derived name is the snapshot's identity: SnapshotAsset.Restore() passes it to the manager.
            Assert.AreEqual("Demo", AssetTypeRegistry.DeriveName("C:/proj/Snapshots/Demo.snapshot.json"));
            Assert.AreEqual(SnapshotManager.kSnapshotDirName,
                AssetTypeRegistry.ResolveImportSubfolder("Demo.snapshot.json"));

            // The ".json" tail must not steal the kinds that sit below snapshots in priority.
            Assert.IsInstanceOf<LiveSceneAsset>(AssetTypeRegistry.Create("C:/proj/Start.live.json"));
            Assert.IsInstanceOf<DeckAsset>(AssetTypeRegistry.Create("C:/proj/Decks/Live.deck.json"));
        }

        [Test]
        public void IsSnapshotFile_MatchesOnTheCompoundSuffixOnly()
        {
            Assert.IsTrue(SnapshotManager.IsSnapshotFile("C:/proj/Snapshots/Demo.snapshot.json"));
            // Case-insensitive: the crawl sees whatever casing the file system reports.
            Assert.IsTrue(SnapshotManager.IsSnapshotFile("Demo.SNAPSHOT.JSON"));
            // Neighbours that must not be listed as snapshots: the thumbnail and a plain json.
            Assert.IsFalse(SnapshotManager.IsSnapshotFile("C:/proj/Snapshots/Demo.snapshot.png"));
            Assert.IsFalse(SnapshotManager.IsSnapshotFile("C:/proj/Snapshots/NotASnapshot.json"));
            Assert.IsFalse(SnapshotManager.IsSnapshotFile(null));
        }
    }
}
