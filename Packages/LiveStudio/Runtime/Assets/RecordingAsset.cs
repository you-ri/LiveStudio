// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A take of live data (<c>*.live.bin</c>) discovered in the project folder, listed in
    /// <see cref="ExternalAssetManager.assets"/> like any other asset.
    ///
    /// A take belongs to the project the way scenes, decks and snapshots do — it replays by
    /// rebuilding the world it was recorded against, so it only means anything where it came from.
    /// Like a snapshot this is a launcher entry, not a loadable resource: playing one drives the
    /// live values instead of adding something to the scene. The entry exists so a take is visible,
    /// playable and deletable from the project listing along with everything else the project holds —
    /// and, through <see cref="thumbnailFilePath"/>, so the picture taken when recording began is
    /// served by the same <c>GET /live/asset/{key}/@image</c> every other asset's preview comes from.
    /// </summary>
    [System.Serializable]
    [LiveClass("RecordingAsset", Category = "Asset", Icon = "play_circle", lane = FrameLane.None)]
    public class RecordingAsset : AssetBase
    {
        /// <summary>A take is recorded by the app, so the app may delete it.</summary>
        public override bool isAppOwnedFile => true;

        // Additive group, but never enabled: playing is a transport, not a sticky load state.
        // No load/unload lifecycle; the entry exists for listing and is driven via Play().
        public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

        public override void Unload(AssetLoadContext context) { }

        /// <summary>
        /// Plays this take back. Live as a button in the generic object UI (the project detail pane),
        /// mirroring <see cref="SnapshotAsset.Restore"/>.
        /// </summary>
        [LiveFunction]
        public void Play()
        {
            RecordingManager.Play(name);
        }

        // A take's preview is a plain PNG written beside it when recording began, not a picture packed
        // inside it — so it is served from disk rather than through ThumbnailCache.
        public override string thumbnailFilePath => RecordingManager.ResolveThumbnailPath(filePath);

        // The picture is the take's second file, so deletion goes through the manager rather than the
        // base implementation (which only knows the file the crawl found).
        public override void DeleteFiles()
        {
            RecordingManager.DeleteRecording(name);
        }
    }
}
