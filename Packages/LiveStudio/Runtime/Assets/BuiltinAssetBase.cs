// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared base for a catalog entry describing a reference-only app-embedded (built-in) asset shipped
    /// inside the app under a <c>Resources</c> folder (baked into <see cref="BuiltinAssetCatalog"/>). Like
    /// <see cref="PackBundleAsset"/> such an entry is a reference resource, not scene content: it has
    /// no load/unload toggle and instantiates nothing. It exists in the catalog so the asset is listed on
    /// the project's asset page and selectable; the asset itself is registered in
    /// <see cref="Lilium.RemoteControl.AssetRegistry"/> under its GUID by <see cref="BuiltinAssetRegistry"/>.
    ///
    /// Marked <see cref="AssetBase.isBuiltin"/> so the project crawl never prunes it, and
    /// <see cref="AssetBase.isPersistable"/> = false so it is never written to the live scene — it carries
    /// no per-scene state and is always re-injected by <see cref="ExternalAssetManager"/> each run. (A
    /// loadable built-in such as <see cref="BuiltinPropAsset"/> extends <see cref="AssetBase"/> directly
    /// instead, because it DOES instantiate and persist its in-use state.)
    ///
    /// Deliberately NOT marked <c>[LiveClass]</c> (like <see cref="AssetBase"/>): each concrete kind
    /// registers its own, so the polymorphic <c>@type</c> discriminator never names this abstract type.
    /// A kind that only needs the default no-op behavior is therefore just an empty subclass carrying the
    /// attribute.
    /// </summary>
    public abstract class BuiltinAssetBase : AssetBase
    {
        // Additive-style (not a single-selection group), though it never actually loads via the toggle.
        public override bool isBuiltin => true;

        // Reference-only: no scene instance, no per-scene state — never written to the live scene.
        public override bool isPersistable => false;

        /// <summary>
        /// No-op "load": a built-in entry holds no scene instance. The asset is registered in
        /// <see cref="Lilium.RemoteControl.AssetRegistry"/> centrally by <see cref="BuiltinAssetRegistry"/>,
        /// not by this toggle.
        /// </summary>
        public override Task LoadAsync(AssetLoadContext context)
        {
            isLoaded = false;
            return Task.CompletedTask;
        }

        /// <summary>No-op unload: nothing was instantiated.</summary>
        public override void Unload(AssetLoadContext context)
        {
            isLoaded = false;
        }
    }
}
