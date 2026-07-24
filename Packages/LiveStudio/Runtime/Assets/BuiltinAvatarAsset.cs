// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A loadable app-embedded (built-in) avatar: a prefab shipped inside the app under a <c>Resources</c>
    /// folder whose root is a humanoid avatar (a UniVRM-imported <c>.vrm</c> prefab or a pre-set-up avatar
    /// prefab equivalent to a <c>*.avatar.lsb</c> export). Baked into <see cref="BuiltinAssetCatalog"/> and
    /// injected by <see cref="ExternalAssetManager"/> so the end user's own avatar prefab lists and loads
    /// exactly like an external <c>*.vrm</c> / <c>*.avatar.lsb</c> avatar — no AssetBundle build step, just
    /// "drop it under Resources".
    ///
    /// The avatar counterpart of <see cref="BuiltinPropAsset"/>: it instantiates scene content, so it
    /// extends <see cref="AssetBase"/> directly and is <see cref="AssetBase.isPersistable"/> (its selected
    /// state and objectId are written to the live scene, keyed by <see cref="persistentId"/> — the catalog
    /// GUID — so the chosen avatar restores on restart). Like the file-backed <see cref="AvatarAsset"/> it
    /// is exclusive: enabling one replaces the current avatar and the manager disables the others (radio).
    ///
    /// Unlike <see cref="AvatarAsset"/>, the source is a Resources prefab rather than a file path, so it
    /// loads through <see cref="AvatarService.LoadPrefab"/> (instantiate + the shared avatar setup path)
    /// rather than <see cref="AvatarService.Load"/>. Built-in avatars have no project file and therefore no
    /// <c>*.preset.json</c> support. The delta-baseline capture and controller-wrapper objectId resolution
    /// are shared with <see cref="AvatarAsset"/> through <see cref="AvatarAssetSupport"/>.
    /// </summary>
    [Serializable]
    [ExposedClass("BuiltinAvatarAsset", Category = "Asset", Icon = "person")]
    public class BuiltinAvatarAsset : AvatarAssetBase
    {
        // Selectable service id of the avatar slot to drive (the id AvatarController registers under).
        private const string kAvatarServiceId = "current";

        /// <summary>
        /// The catalog GUID this entry loads. The stable identity persisted to the live scene (the runtime
        /// <see cref="AssetBase.id"/> is reconstructed from it on restore) and the key
        /// <see cref="BuiltinAssetRegistry.LoadPrefab"/> resolves the Resources prefab by.
        /// </summary>
        [ExposedField, Hide]
        public string guid;

        public override bool isBuiltin => true;

        // Not backed by a project file: restore id from the persisted GUID so the re-injected catalog
        // entry and the persisted one dedup to a single entry.
        public override string persistentId => guid;

        public override Task LoadAsync(AssetLoadContext context)
        {
            var prefab = BuiltinAssetRegistry.LoadPrefab(guid);
            if (prefab == null)
            {
                Debug.LogError($"[LiveStudio] Built-in avatar prefab not found in Resources (guid '{guid}').");
                MarkLoadFailed();
                return Task.CompletedTask;
            }

            // Capture the delta baseline once the new avatar is ready, so dirty tracking reflects only the
            // user's edits — symmetric with how a file-backed avatar baselines on load. (A built-in avatar
            // has no project file and so cannot be saved as a preset; the baseline is purely for dirty /
            // unsaved-change tracking, not preset capture.)
            AvatarAssetSupport.CaptureDefaultsThenRestoreOnReady(null);

            // The avatar swap is driven by AvatarController (instantiate + VRM setup + SetupAvatar);
            // completion is observed by the manager via IAvatarService.onAvatarChanged. Mark loaded
            // optimistically, mirroring AvatarAsset.
            AvatarService.LoadPrefab(kAvatarServiceId, prefab);
            isLoaded = true;
            objectId = AvatarAssetSupport.ResolveControllerObjectId();
            return Task.CompletedTask;
        }

        public override void Unload(AssetLoadContext context)
        {
            // Restore the AvatarController's default avatar. Only invoked when no avatar asset is selected;
            // switching between avatars replaces in place via LoadAsync (no reset between).
            AvatarService.ResetAvatar(kAvatarServiceId);
            isLoaded = false;
        }
    }
}
