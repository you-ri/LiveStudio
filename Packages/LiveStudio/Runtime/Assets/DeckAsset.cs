// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A deck file (<c>*.deck.json</c>) discovered in the project folder, listed in
    /// <see cref="ExternalAssetManager.assets"/> like any other asset.
    ///
    /// One deck file is one tab of the remote app's operations page, and <em>every</em> deck file in the
    /// project is a tab — so unlike props / avatars / set bundles there is nothing to load or unload here,
    /// and unlike a live scene there is nothing to open either. The entry exists so the file shows up in the
    /// project listing (and can be deleted from there); <see cref="OperationManager"/> is what reads and
    /// writes it, following the project crawl.
    /// </summary>
    [System.Serializable]
    [LiveClass("DeckAsset", Category = "Asset", Icon = "grid_view")]
    public class DeckAsset : AssetBase
    {
        // Additive group, but never enabled: the operations page shows every deck file whatever this says.
        public override bool isExclusive => false;

        // No load/unload lifecycle. OperationManager owns the file's contents.
        public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

        public override void Unload(AssetLoadContext context) { }
    }
}
