// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Describes one kind of app-embedded (built-in) asset the catalog can bake: which Unity asset type
    /// to collect from the <c>Resources</c> folders, and how to create the <see cref="AssetBase"/> entry
    /// that lists it on the project asset page.
    /// </summary>
    public sealed class BuiltinAssetTypeDescriptor
    {
        /// <summary>
        /// The Unity asset type this kind owns. Used as the <c>AssetDatabase</c> search filter and the
        /// main-asset test at bake time, and as the <c>Resources.Load</c> type at runtime. A subtype of the
        /// declared type also matches (e.g. a <c>Texture2D</c> under a <c>Texture</c> kind).
        /// </summary>
        public Type assetType;

        /// <summary>
        /// True for a reference-only kind whose asset is pre-loaded and registered in
        /// <see cref="Lilium.RemoteControl.AssetRegistry"/> at startup so a selector resolves it by GUID
        /// (e.g. an animation clip). False for a loadable kind that is instantiated on demand (e.g. a
        /// prop): such a kind must NOT be force-loaded at startup — it only lists and loads when enabled.
        /// </summary>
        public bool isReference;

        /// <summary>
        /// Optional content narrowing applied after the <see cref="assetType"/> match, so a broad type
        /// filter can be refined by inspecting the asset — e.g. only prefabs whose root carries an
        /// <see cref="IProp"/> component qualify as built-in props (a bare <c>t:GameObject</c> filter would
        /// otherwise sweep up every prefab under a Resources folder). Null accepts every asset of
        /// <see cref="assetType"/>. Receives the loaded main asset; evaluated only at bake time.
        /// </summary>
        public Func<UnityEngine.Object, bool> accept;

        /// <summary>
        /// Creates the catalog entry for one baked asset (an empty instance; <see cref="BuiltinAssetRegistry"/>
        /// fills id / name). Receives the entry so a kind that needs the Resources path or GUID can keep it.
        /// </summary>
        public Func<BuiltinAssetCatalog.Entry, AssetBase> create;

        /// <summary>
        /// The key this kind is baked under (<see cref="BuiltinAssetCatalog.Entry.type"/>): the asset type's
        /// simple name, which is also what <c>GET /api/assets?type=</c> filters by (see
        /// <c>AssetRegistry.CollectAssets</c>), so the two stay consistent without a second name to maintain.
        /// </summary>
        public string typeName => assetType != null ? assetType.Name : null;
    }

    /// <summary>
    /// Maps built-in asset kinds to the Unity asset type they bake and the <see cref="AssetBase"/> entry
    /// they list as — the built-in counterpart of <see cref="AssetTypeRegistry"/>, which does the same for
    /// external files. <c>BuiltinAssetCatalogBuilder</c> scans the <c>Resources</c> folders once per
    /// registered kind, and <see cref="BuiltinAssetRegistry"/> resolves each baked entry back through
    /// <see cref="Find"/>, so adding a kind needs no change to either.
    ///
    /// External packages can add their own kinds by calling <see cref="Register"/> from their own
    /// initialization hook; do so after the built-ins are registered (e.g. a <c>RuntimeInitializeOnLoadMethod</c>
    /// phase later than <c>SubsystemRegistration</c>), since registration order is otherwise undefined. Register
    /// from an <c>[InitializeOnLoadMethod]</c> as well, so the editor baker sees the kind and bakes its assets.
    /// </summary>
    public static class BuiltinAssetTypeRegistry
    {
        // Registered descriptors in registration order (first match wins when two kinds accept the same
        // asset). Built-in descriptors and their delegates are created once in _RegisterBuiltIns.
        private static readonly List<BuiltinAssetTypeDescriptor> _descriptors = new List<BuiltinAssetTypeDescriptor>();

        // Re-register built-ins on every domain (re)load and on entering play mode, so the registry is
        // populated whether or not Domain Reload is enabled. InitializeOnLoadMethod also covers the editor
        // baker, which reads the descriptors outside play mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void _Initialize()
        {
            _descriptors.Clear();
            _RegisterBuiltIns();
        }

        /// <summary>Every registered kind, in registration order (never null).</summary>
        public static IReadOnlyList<BuiltinAssetTypeDescriptor> descriptors
        {
            get
            {
                _EnsureInitialized();
                return _descriptors;
            }
        }

        /// <summary>Adds a kind. Ignored (with an error) when the descriptor is incomplete.</summary>
        public static void Register(BuiltinAssetTypeDescriptor descriptor)
        {
            if (descriptor == null || descriptor.assetType == null || descriptor.create == null)
            {
                Debug.LogError("[LiveStudio] BuiltinAssetTypeRegistry.Register: descriptor, assetType and create must be non-null.");
                return;
            }
            _EnsureInitialized();
            _descriptors.Add(descriptor);
        }

        /// <summary>
        /// The kind baked under <paramref name="typeName"/> (see <see cref="BuiltinAssetCatalog.Entry.type"/>),
        /// or null when no kind is registered for it — e.g. a catalog baked by an app build that had a kind
        /// this one does not.
        /// </summary>
        public static BuiltinAssetTypeDescriptor Find(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            _EnsureInitialized();

            for (int i = 0; i < _descriptors.Count; i++)
            {
                if (string.Equals(_descriptors[i].typeName, typeName, StringComparison.OrdinalIgnoreCase))
                    return _descriptors[i];
            }
            return null;
        }

        /// <summary>
        /// The first kind that accepts an asset of <paramref name="assetType"/> (its own type or a subtype),
        /// or null when none does. Lets the editor baker classify an imported asset by type alone, without
        /// maintaining a per-kind list of file extensions.
        /// </summary>
        public static BuiltinAssetTypeDescriptor FindForAssetType(Type assetType)
        {
            if (assetType == null) return null;
            _EnsureInitialized();

            for (int i = 0; i < _descriptors.Count; i++)
            {
                if (_descriptors[i].assetType.IsAssignableFrom(assetType)) return _descriptors[i];
            }
            return null;
        }

        // Lazy guard: if the built-ins have not been registered yet (e.g. a static caller runs before the
        // init hooks), register them now. Idempotent because _Initialize clears first.
        private static void _EnsureInitialized()
        {
            if (_descriptors.Count == 0) _RegisterBuiltIns();
        }

        // Registers the built-in kinds.
        private static void _RegisterBuiltIns()
        {
            // Animation clips (*.anim in Resources): reference resources selectable from the avatar
            // body-override slot, alongside the clips of an external *.anim.lsb bundle. Reference-only:
            // registered in AssetRegistry at startup, never instantiated.
            _descriptors.Add(new BuiltinAssetTypeDescriptor
            {
                assetType = typeof(AnimationClip),
                isReference = true,
                create = _ => new BuiltinAnimationAsset(),
            });

            // Built-in props: a prefab under a Resources folder whose root carries an IProp component (the
            // same avatar-attached prop root shipped in a *.prop.lsb). A loadable kind — instantiated under
            // the avatar on demand like an external prop, so isReference is false. The accept filter is
            // functionally required, not just a semantic scope: without it a bare t:GameObject search would
            // bake every prefab (UI, internal) under any Resources folder.
            //
            // Only avatar props exist today. A future free-standing (scene-placed) built-in prop is a load
            // path branch within THIS kind (by IProp presence, mirroring PropAsset's suffix branch), NOT a
            // second GameObject kind: BuiltinAssetTypeRegistry.Find keys on the asset type name, so only one
            // GameObject-based kind can be resolved.
            _descriptors.Add(new BuiltinAssetTypeDescriptor
            {
                assetType = typeof(GameObject),
                isReference = false,
                accept = o => o is GameObject go && go.GetComponent<IProp>() != null,
                create = entry => new BuiltinPropAsset { guid = entry.guid },
            });
        }
    }
}
