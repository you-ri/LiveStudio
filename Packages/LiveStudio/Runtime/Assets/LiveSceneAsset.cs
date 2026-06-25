// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A live scene file (<c>*.live.json</c> / <c>*.scene.json</c>) discovered in the project folder,
    /// listed in <see cref="ExternalAssetManager.assets"/> like any other asset.
    ///
    /// Unlike props / avatars / scene bundles, a live scene is not a resource loaded *into* the current
    /// scene — opening it replaces the whole app state (it deserializes over every exposed object). It
    /// therefore does not participate in the load/unload (enabled) machinery: it is a launcher only.
    ///
    /// Surfaced to the remote app's project detail pane through the generic exposed-object UI: the base
    /// identity/state fields are hidden (see <see cref="AssetBase"/>), so the only visible members are
    /// <see cref="AssetBase.name"/> (the title) and the <see cref="Open"/> button.
    /// <see cref="ExternalAssetManager.OpenLiveScene"/> also invokes <see cref="Open"/> to switch scenes.
    /// </summary>
    [System.Serializable]
    [ExposedClass("LiveSceneAsset", Category = "Asset", Icon = "movie")]
    public class LiveSceneAsset : AssetBase
    {
        // Additive group, but never enabled: opening is a one-shot action, not a sticky load state.
        public override bool isExclusive => false;

        // No load/unload lifecycle; the entry exists for listing and is opened via Open().
        public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

        public override void Unload(AssetLoadContext context) { }

        /// <summary>
        /// Opens this live scene, replacing the current app state. The live-scene-specific work.
        /// Exposed as a button in the generic object UI (the project detail pane) so the scene is
        /// switched from there instead of by a bespoke launcher control.
        /// </summary>
        [ExposedFunction]
        public void Open()
        {
            if (string.IsNullOrEmpty(filePath)) return;
            LiveSceneManager.LoadScene(filePath);
        }
    }
}
