// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A snapshot file (<c>*.snapshot.json</c>) discovered in the project folder, listed in
    /// <see cref="ExternalAssetManager.assets"/> like any other asset.
    ///
    /// Like a live scene this is a launcher entry, not a loadable resource: applying one replaces the
    /// live values instead of adding something to the scene. The dedicated snapshot page (backed by
    /// <see cref="SnapshotManager.snapshots"/>, which keeps the thumbnails) stays as it is — the entry
    /// exists so a snapshot is visible, restorable and deletable from the project listing along with the
    /// other files the project holds.
    /// </summary>
    [System.Serializable]
    [LiveClass("SnapshotAsset", Category = "Asset", Icon = "photo_library")]
    public class SnapshotAsset : AssetBase
    {
        // Additive group, but never enabled: restoring is a one-shot action, not a sticky load state.
        public override bool isExclusive => false;

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

        // The thumbnail is the snapshot's second file, so deletion goes through the manager rather than
        // the base implementation (which only knows the file the crawl found).
        public override void DeleteFiles()
        {
            SnapshotManager.DeleteSnapshot(name);
        }
    }
}
