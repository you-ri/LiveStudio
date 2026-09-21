// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A prop file in the project's catalog (<c>*.prop.lsb</c>, <c>*.glb</c> / <c>*.gltf</c>), discovered by
    /// the project crawl and listed by <see cref="ExternalAssetManager"/>.
    ///
    /// <para>
    /// The entry says the app *has* this file; it does not say anything is on stage. A prop is put out as
    /// an ordinary scene object — from the scene page's "+" (<see cref="PropObjectFactory"/>), which spawns
    /// an instance carrying this entry's <see cref="instanceKey"/> as its <c>@prefab</c> — so the live scene
    /// holds the instance and the catalog is left out of it entirely. That is also what lets the same prop
    /// be out several times, which a single per-file flag could not express.
    /// </para>
    /// <para>
    /// A free-standing <c>*.glb</c> / <c>*.gltf</c> is not instanced from here: the *GLTF Model* object
    /// (<c>GltfModel.path</c>, a file picker over the same files) is how one is put out, and it persists as
    /// the plain scene object it is.
    /// </para>
    /// </summary>
    [Serializable]
    [LiveClass("PropAsset", Category = "Asset", Icon = "deployed_code", lane = FrameLane.None)]
    public class PropAsset : AssetBase, IInstantiableProp
    {
        // *.prop.lsb is a prefab that attaches to the avatar; glTF is geometry the GLTF Model host reads.
        private bool _isPropBundle => LiveStudioBundle.IsPropBundle(filePath);

        /// <summary>The prop file this entry stands for.</summary>
        internal string sourceFilePath => filePath;

        // Nothing is loaded by enabling a catalog entry; a prop reaches the stage as a scene instance.
        public override bool isLoadable => false;

        public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

        public override void Unload(AssetLoadContext context) { }

        // --- IInstantiableProp: spawn as scene instances from the live scene "+" ---

        // Only *.prop.lsb props are re-instantiable prefabs. A free-standing glTF prop is covered by the
        // GLTF Model host, which reads the file itself.
        public bool supportsInstancing => _isPropBundle;

        // The portable, project-relative reference is the @prefab key; on restore the deferred prefab
        // provider resolves it back to this asset via ExternalAssetManager.FindAssetByReference.
        public string instanceKey
        {
            get
            {
                var source = sourceFilePath;
                if (string.IsNullOrEmpty(source)) return source;
                var relative = PropPreset.Relativize(source, ProjectManager.projectPath);
                return string.IsNullOrEmpty(relative) ? source : relative;
            }
        }

        public Task<GameObject> LoadInstancePrefabAsync() => PropBundleLoader.GetPrefabAsync(sourceFilePath);
    }
}
