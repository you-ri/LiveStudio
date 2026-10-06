// Copyright (c) You-Ri, 2026

using System.IO;
using System.Reflection;
using System.Threading;
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

        /// <summary>
        /// A snapshot's preview is its sibling <c>*.snapshot.png</c>, reported through the generic
        /// <see cref="AssetBase.thumbnailFilePath"/> so <c>GET /live/asset/{key}/@image</c> serves it like every
        /// other asset's picture. Path-only, so no file has to exist for this to hold.
        /// </summary>
        [Test]
        public void SnapshotAsset_ReportsItsSiblingScreenshotAsThumbnailFile()
        {
            var asset = (SnapshotAsset)AssetTypeRegistry.Create("C:/proj/Snapshots/Demo.snapshot.json");
            asset.filePath = "C:/proj/Snapshots/Demo.snapshot.json";

            Assert.AreEqual("C:/proj/Snapshots/Demo.snapshot.png", asset.thumbnailFilePath);
            // Derived from the path, not the name, so a snapshot found anywhere the crawl reaches works.
            Assert.AreEqual("D:/elsewhere/A.B.snapshot.png",
                SnapshotManager.ResolveThumbnailPath("D:/elsewhere/A.B.snapshot.json"));
            // Only snapshots have one; every other kind keeps the base's "no picture file" answer.
            Assert.IsNull(SnapshotManager.ResolveThumbnailPath("C:/proj/Start.live.json"));
            Assert.IsNull(AssetTypeRegistry.Create("C:/proj/Start.live.json").thumbnailFilePath);
        }

        /// <summary>
        /// The facts the snapshot list publishes (capture time, whether a screenshot sits beside it) are
        /// kept on the catalog entry and re-read only when the catalog reconciles, so the polled list
        /// never stats the folder per read. A screenshot added later is picked up on the next refresh.
        /// </summary>
        [Test]
        public void SnapshotAsset_KeepsItsFileFactsUntilRefreshed()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LiliumSnapshotFacts");
            Directory.CreateDirectory(dir);
            try
            {
                var file = Path.Combine(dir, "Demo.snapshot.json");
                File.WriteAllText(file, "{}");
                var capturedAt = new System.DateTime(2026, 7, 31, 10, 0, 0, System.DateTimeKind.Local);
                File.SetLastWriteTime(file, capturedAt);

                var asset = (SnapshotAsset)AssetTypeRegistry.Create(file);
                asset.filePath = file;
                asset.RefreshFileFacts();

                Assert.AreEqual(capturedAt.ToString("o"), asset.capturedAt, "the capture time is the file's last-write time");
                Assert.IsFalse(asset.hasThumbnail, "no screenshot beside it yet");

                File.WriteAllText(Path.Combine(dir, "Demo.snapshot.png"), "png");
                Assert.IsFalse(asset.hasThumbnail, "kept until refreshed (the list does not touch the disk)");

                asset.RefreshFileFacts();
                Assert.IsTrue(asset.hasThumbnail, "a screenshot added later is picked up on the next refresh");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// The snapshot list is projected from the project's asset catalog and polled while its page is
        /// open. A read must not rescan the project when nothing changed, yet files put into or removed
        /// from the folder by something other than this app must show on the next read.
        ///
        /// The catalog and the project folder are swapped in by reflection (both are private state the
        /// app sets up on start) and put back afterwards.
        /// </summary>
        [Test]
        public void SnapshotList_FollowsTheFolderWithoutRescanningUnchangedReads()
        {
            var projectField = typeof(ProjectManager).GetField("_projectPath", BindingFlags.NonPublic | BindingFlags.Static);
            var currentField = typeof(ExternalAssetManager).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(projectField, "ProjectManager._projectPath was renamed; update this test");
            Assert.IsNotNull(currentField, "ExternalAssetManager._current was renamed; update this test");

            var savedProject = projectField.GetValue(null);
            var savedCurrent = currentField.GetValue(null);
            var root = Path.Combine(Path.GetTempPath(), "LiliumSnapshotList");
            var dir = Path.Combine(root, SnapshotManager.kSnapshotDirName);
            Directory.CreateDirectory(dir);
            var manager = new ExternalAssetManager();
            try
            {
                projectField.SetValue(null, root);
                currentField.SetValue(null, manager);

                Assert.AreEqual(0, SnapshotManager.snapshots.Length, "an empty folder lists nothing");

                // Folder times can be kept to the second, so right after a crawl the list keeps looking
                // until that second is over. Read once after it to let it settle.
                Thread.Sleep(1100);
                _ = SnapshotManager.snapshots;
                var revision = manager.catalogRevision;
                _ = SnapshotManager.snapshots;
                _ = SnapshotManager.snapshots;
                Assert.AreEqual(revision, manager.catalogRevision, "unchanged reads do not rescan the project");

                var json = Path.Combine(dir, "Dropped.snapshot.json");
                File.WriteAllText(json, "{}");
                var added = SnapshotManager.snapshots;
                Assert.AreEqual(1, added.Length, "a file put there from outside is listed");
                Assert.AreEqual("Dropped", added[0].name);
                Assert.IsFalse(added[0].hasThumbnail);

                File.WriteAllText(Path.Combine(dir, "Dropped.snapshot.png"), "png");
                Assert.IsTrue(SnapshotManager.snapshots[0].hasThumbnail, "a screenshot added later is noticed");

                File.Delete(json);
                Assert.AreEqual(0, SnapshotManager.snapshots.Length, "a file removed from outside drops out");
            }
            finally
            {
                projectField.SetValue(null, savedProject);
                currentField.SetValue(null, savedCurrent);
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
