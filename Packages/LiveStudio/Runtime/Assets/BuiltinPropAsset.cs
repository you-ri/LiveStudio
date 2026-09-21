// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A prop shipped inside the app: a prefab under a <c>Resources</c> folder whose root carries an
    /// <see cref="IProp"/> component. Baked into <see cref="BuiltinAssetCatalog"/> and injected into the
    /// catalog by <see cref="ExternalAssetManager"/>, so an end user's own prefab is offered exactly like
    /// an external <c>*.prop.lsb</c> — no AssetBundle build step, just "drop it under Resources and add a
    /// prop component".
    ///
    /// <para>
    /// Like every prop, this is a catalog entry and not a placement: it is put out as a scene instance
    /// from the scene page's "+" and the live scene holds that instance, so nothing about this entry is
    /// written there (the catalog is re-injected each run — see <see cref="AssetBase.isBuiltin"/>).
    /// </para>
    /// </summary>
    [Serializable]
    [LiveClass("BuiltinPropAsset", Category = "Asset", Icon = "deployed_code", lane = FrameLane.None)]
    public class BuiltinPropAsset : AssetBase, IInstantiableProp
    {
        /// <summary>
        /// The catalog GUID this entry stands for: the key <see cref="BuiltinAssetRegistry.LoadPrefab"/>
        /// resolves the Resources prefab by, and the instance's <c>@prefab</c>.
        /// </summary>
        [LiveField, Hide]
        public string guid;

        public override bool isBuiltin => true;

        // Not backed by a project file: the GUID is this entry's identity.
        public override string persistentId => guid;

        // Nothing is loaded by enabling a catalog entry; a prop reaches the stage as a scene instance.
        public override bool isLoadable => false;

        public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

        public override void Unload(AssetLoadContext context) { }

        // --- IInstantiableProp: spawn as scene instances from the live scene "+" ---

        public bool supportsInstancing => true;

        // The catalog GUID doubles as the @prefab key; PrefabRegistry's built-in resolver
        // (BuiltinAssetRegistry.LoadPrefab) resolves it synchronously on restore, so a built-in prop
        // never needs the deferred prefab store.
        public string instanceKey => guid;

        public Task<GameObject> LoadInstancePrefabAsync()
            => Task.FromResult(BuiltinAssetRegistry.LoadPrefab(guid));
    }
}
