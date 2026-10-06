// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using System.Threading.Tasks;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A snapshot file (<c>*.snapshot.json</c>) discovered in the project folder, listed in
    /// <see cref="ExternalAssetManager.assets"/> like any other asset.
    ///
    /// Like a live scene this is a launcher entry, not a loadable resource: applying one replaces the
    /// live values instead of adding something to the scene. The entry exists so a snapshot is visible,
    /// restorable and deletable from the project listing along with the other files the project holds —
    /// and, through <see cref="thumbnailFilePath"/>, so its screenshot is served by the same
    /// <c>GET /live/asset/{key}/@image</c> every other asset's preview comes from.
    /// </summary>
    [System.Serializable]
    [LiveClass("SnapshotAsset", Category = "Asset", Icon = "image", lane = FrameLane.None)]
    public class SnapshotAsset : AssetBase
    {
        /// <summary>A snapshot is taken by the app, so the app may delete it.</summary>
        public override bool isAppOwnedFile => true;

        // Additive group, but never enabled: restoring is a one-shot action, not a sticky load state.
        // No load/unload lifecycle; the entry exists for listing and is applied via Restore().
        public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

        public override void Unload(AssetLoadContext context) { }

        /// <summary>
        /// Applies this snapshot on top of the live scene. Live as a button in the generic object UI
        /// (the project detail pane), mirroring <see cref="LiveSceneAsset.Open"/>.
        /// </summary>
        [LiveFunction]
        public void Restore()
        {
            SnapshotManager.RestoreSnapshot(name);
        }

        // A snapshot's preview is a plain PNG written beside the file at capture time, not a picture packed
        // inside it — so it is served from disk rather than through ThumbnailCache.
        public override string thumbnailFilePath => SnapshotManager.ResolveThumbnailPath(filePath);

        // Kept by the catalog (RefreshFileFacts) so the snapshot list, which is polled while its page is open,
        // never stats the folder per read. Not persisted and not exposed: the list republishes them.
        [NonSerialized] private string _capturedAt;
        [NonSerialized] private bool _hasThumbnail;

        /// <summary>When the snapshot was taken (the file's last-write time, ISO 8601), as of the last crawl.</summary>
        internal string capturedAt => _capturedAt;

        /// <summary>Whether a screenshot sits beside the snapshot, as of the last crawl.</summary>
        internal bool hasThumbnail => _hasThumbnail;

        public override void RefreshFileFacts()
        {
            _capturedAt = File.GetLastWriteTime(filePath).ToString("o");
            var thumbnail = thumbnailFilePath;
            _hasThumbnail = thumbnail != null && File.Exists(thumbnail);
        }

        // The thumbnail is the snapshot's second file, so deletion goes through the manager rather than
        // the base implementation (which only knows the file the crawl found).
        public override void DeleteFiles()
        {
            SnapshotManager.DeleteSnapshot(name);
        }
    }
}
