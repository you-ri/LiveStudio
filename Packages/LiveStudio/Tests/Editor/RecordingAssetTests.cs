// Copyright (c) You-Ri, 2026

using NUnit.Framework;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Tests that a take of live data is one of the project's asset kinds — so it is listed and given
    /// a picture the same way a snapshot is, which is what lets one page draw both. Pure logic
    /// (classification is path-only, no file is read).
    /// </summary>
    public class RecordingAssetTests
    {
        [Test]
        public void AssetTypeRegistry_ResolvesTakesToRecordingAsset()
        {
            var asset = AssetTypeRegistry.Create("C:/proj/Recordings/take001_20260906_120000.live.bin");

            Assert.IsInstanceOf<RecordingAsset>(asset, "*.live.bin should be classified as a recording asset.");
            // The derived name is the take's identity: RecordingAsset.Play() passes it to the manager.
            Assert.AreEqual("take001_20260906_120000",
                AssetTypeRegistry.DeriveName("C:/proj/Recordings/take001_20260906_120000.live.bin"));
            Assert.AreEqual(RecordingManager.kFolderName,
                AssetTypeRegistry.ResolveImportSubfolder("take001.live.bin"));
            // A snapshot is the same recording one frame long, so both kinds file into one folder.
            Assert.AreEqual(RecordingManager.kFolderName, SnapshotManager.kSnapshotDirName);

            // Takes written before the current extension stay listed, under the same name.
            Assert.IsInstanceOf<RecordingAsset>(AssetTypeRegistry.Create("C:/proj/Recordings/old.livedata"));
            Assert.AreEqual("old", AssetTypeRegistry.DeriveName("C:/proj/Recordings/old.livedata"));
        }

        [Test]
        public void IsRecordingFile_MatchesOnTheRecordingSuffixesOnly()
        {
            Assert.IsTrue(RecordingManager.IsRecordingFile("C:/proj/Recordings/take001.live.bin"));
            Assert.IsTrue(RecordingManager.IsRecordingFile("C:/proj/Recordings/old.livedata"));
            // Case-insensitive: the crawl sees whatever casing the file system reports.
            Assert.IsTrue(RecordingManager.IsRecordingFile("TAKE001.LIVE.BIN"));
            // Neighbours that must not be listed as takes: the picture beside one, and a live scene
            // (which shares the ".live" fragment the legacy scene extension used).
            Assert.IsFalse(RecordingManager.IsRecordingFile("C:/proj/Recordings/take001.live.png"));
            Assert.IsFalse(RecordingManager.IsRecordingFile("C:/proj/Start.live.json"));
            Assert.IsFalse(RecordingManager.IsRecordingFile(null));
        }

        /// <summary>
        /// A take's preview is its sibling <c>*.live.png</c>, written when recording began and reported
        /// through the generic <see cref="AssetBase.thumbnailFilePath"/> so
        /// <c>GET /live/asset/{key}/@image</c> serves it like every other asset's picture. Path-only, so
        /// no file has to exist for this to hold.
        /// </summary>
        [Test]
        public void RecordingAsset_ReportsItsSiblingPictureAsThumbnailFile()
        {
            var asset = (RecordingAsset)AssetTypeRegistry.Create("C:/proj/Recordings/take001.live.bin");
            asset.filePath = "C:/proj/Recordings/take001.live.bin";

            Assert.AreEqual("C:/proj/Recordings/take001.live.png", asset.thumbnailFilePath);
            // A legacy take answers with the same picture name, so one file serves both extensions.
            Assert.AreEqual("C:/proj/Recordings/old.live.png",
                RecordingManager.ResolveThumbnailPath("C:/proj/Recordings/old.livedata"));
            // Only takes have one; every other kind keeps the base's "no picture file" answer.
            Assert.IsNull(RecordingManager.ResolveThumbnailPath("C:/proj/Start.scene.json"));
        }

        /// <summary>
        /// The name the page shows is the file name without the recording extension — the key every
        /// call on the manager takes. A path that is not a take has no such name.
        /// </summary>
        [Test]
        public void NameFromPath_StripsTheRecordingExtension()
        {
            Assert.AreEqual("take001", RecordingManager.NameFromPath("C:/proj/Recordings/take001.live.bin"));
            Assert.AreEqual("old", RecordingManager.NameFromPath("old.livedata"));
            Assert.IsNull(RecordingManager.NameFromPath("C:/proj/Snapshots/Demo.snapshot.json"));
        }
    }
}
