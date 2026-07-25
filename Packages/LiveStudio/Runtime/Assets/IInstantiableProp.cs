// Copyright (c) You-Ri, 2026

using System.Threading.Tasks;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A prop asset that can be spawned as one or more independent scene instances (via the live scene's
    /// "+" factory), rather than only enabled once under the avatar. Source-agnostic: a built-in Resources
    /// prop and an external <c>*.prop.lsb</c> bundle prop both implement this, so the factory enumerates and
    /// instantiates them uniformly without knowing the source.
    ///
    /// Each instance is a plain scene object persisted by <c>@prefab</c> (its <see cref="instanceKey"/>), so
    /// restore re-instantiates it. Because a bundle prefab loads asynchronously, the prefab is obtained via
    /// <see cref="LoadInstancePrefabAsync"/> (the caller instantiates its own copy) and, on restore, resolved
    /// lazily once the owning asset is registered (see <c>PendingPrefabStore</c>).
    /// </summary>
    public interface IInstantiableProp
    {
        /// <summary>
        /// True when this prop can be spawned as scene instances. False for kinds that are not backed by a
        /// re-instantiable prefab (e.g. a free-standing glTF prop, already covered by the GLTF Model host, or
        /// a preset).
        /// </summary>
        bool supportsInstancing { get; }

        /// <summary>
        /// The stable key written as the instance's <c>@prefab</c> and resolved back on restore. A built-in
        /// prop uses its catalog GUID (resolved synchronously via the PrefabRegistry resolver); an external
        /// prop uses its portable project-relative reference (resolved by the deferred prefab provider).
        /// </summary>
        string instanceKey { get; }

        /// <summary>
        /// Obtains the root prefab to instantiate copies from (the caller clones it), loading it once if
        /// needed. Async because a bundle prop reads its file off the CAB-gated loader; a built-in prop
        /// wraps its synchronous <c>Resources.Load</c> in a completed task. Null when the prefab cannot be
        /// obtained (missing file / catalog entry).
        /// </summary>
        Task<GameObject> LoadInstancePrefabAsync();
    }
}
