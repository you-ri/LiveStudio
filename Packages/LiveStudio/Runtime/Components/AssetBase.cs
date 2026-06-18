// Copyright (c) You-Ri, 2026

using System;
using System.Threading.Tasks;

using UnityEngine;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Services and handles an <see cref="AssetBase"/> needs to load or unload itself.
    /// Rebuilt by <see cref="ExternalAssetManager"/> for each load/unload so the asset always
    /// sees the current avatar root and container without holding stale references.
    /// </summary>
    public sealed class AssetLoadContext
    {
        /// <summary>Current avatar root transform, or null if no avatar is loaded.</summary>
        public Transform avatarRoot;

        /// <summary>Container the asset's exposed wrapper is added to so the remote app can control it.</summary>
        public RemoteControlContainer container;
    }

    /// <summary>
    /// One entry in <see cref="ExternalAssetManager.assets"/>: an external file the user has added
    /// that can be loaded or unloaded at runtime. Concrete kinds (<see cref="PropAsset"/>,
    /// <see cref="AvatarAsset"/>) carry the format-specific load behavior; this base holds the data
    /// the manager and the live-scene JSON share.
    ///
    /// Deliberately NOT marked <c>[ExposedClass]</c>: the array element type is this abstract base,
    /// and registering it would let the deserializer try to <c>Activator.CreateInstance</c> the
    /// abstract type. Instead each concrete subclass is its own <c>[ExposedClass]</c>; the array is
    /// serialized polymorphically via the per-element <c>@type</c> discriminator, and the base fields
    /// below are still collected onto each subclass because <see cref="ExposedClass"/> walks the base
    /// type chain when gathering exposed members.
    /// </summary>
    public abstract class AssetBase
    {
        [ExposedField]
        public string id;

        [ExposedField]
        public string name;

        [ExposedField]
        public string filePath;

        /// <summary>Desired state. Toggling loads (true) / unloads (false) the asset. Persisted.</summary>
        [ExposedField]
        public bool enabled;

        /// <summary>Actual state, synced after a load/unload completes. Not persisted.</summary>
        [ExposedField(persistable = false)]
        public bool isLoaded;

        /// <summary>
        /// Stable id of this asset's exposed object, so the remote app can keep a durable reference
        /// to the loaded object's property editor across unload/reload cycles. Assigned once and
        /// persisted. Unused by kinds that do not wrap a fresh exposed object (e.g. avatars, which are
        /// exposed through the existing <c>AvatarController</c>).
        /// </summary>
        [ExposedField]
        public string objectId;

        /// <summary>
        /// Serialized snapshot of this asset's exposed parameter values, captured before unload and
        /// reapplied after reload so edits survive an unload/reload cycle. Hidden from the editor.
        /// </summary>
        [ExposedField, Hide]
        public string state;

        /// <summary>True while a load/unload is in flight; the manager skips re-entrant requests.</summary>
        [NonSerialized]
        public bool busy;

        /// <summary>
        /// Exclusive assets form a single-selection (radio) group: enabling one disables the others.
        /// Avatars are exclusive because only one avatar exists at a time; props are additive.
        /// </summary>
        public abstract bool isExclusive { get; }

        /// <summary>
        /// True when the loaded object lives under the avatar and is therefore destroyed when the
        /// avatar is swapped, so the manager must reload it onto the new avatar. Free-standing scene
        /// objects (and the avatar itself) return false.
        /// </summary>
        public virtual bool reloadsOnAvatarChange => false;

        /// <summary>Loads this asset. Implementations set <see cref="isLoaded"/> on success.</summary>
        public abstract Task LoadAsync(AssetLoadContext context);

        /// <summary>Unloads this asset, capturing <see cref="state"/> first when applicable.</summary>
        public abstract void Unload(AssetLoadContext context);

        /// <summary>Refreshes <see cref="state"/> from the live object so a save captures latest edits.</summary>
        public virtual void CaptureState() { }
    }

    /// <summary>
    /// Builds and applies the aggregate JSON snapshot of a loaded object's exposed objects: the
    /// GameObject wrapper plus each <c>[ExposedClass]</c> component on the root. Components are keyed
    /// by their exposed type name (stable across reloads, unlike the per-load object id), so edited
    /// parameter values persist across an unload/reload cycle. Shared by the asset kinds that wrap a
    /// loaded GameObject (props).
    /// </summary>
    internal static class AssetStateSnapshot
    {
        const int kVersion = 1;

        public static string Capture(ExposedGameObject exposed, GameObject instance)
        {
            if (instance == null) return null;

            var root = new JObject { ["version"] = kVersion };

            var wrapperHandle = exposed != null ? ExposedObjectRegistry.FindByTarget(exposed) : null;
            if (wrapperHandle.HasValue)
            {
                var wrapperJson = ExposedObjectSnapshot.Capture(wrapperHandle.Value);
                if (!string.IsNullOrEmpty(wrapperJson))
                {
                    var wrapper = JObject.Parse(wrapperJson);
                    // Parenting is owned by the manager, and component values are stored separately
                    // below, so drop both to avoid clobbering the hierarchy on restore and keep the
                    // snapshot lean. The per-instance @id changes every reload; drop it so restore
                    // onto the new handle does not log a spurious id-mismatch warning.
                    wrapper.Remove("@parent");
                    wrapper.Remove("components");
                    wrapper.Remove("@id");
                    root["wrapper"] = wrapper;
                }
            }

            var components = new JObject();
            foreach (var comp in instance.GetComponents<Component>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                if (!ExposedClass.Has(type)) continue;
                var exposedClass = ExposedClass.Find(type);
                if (exposedClass == null) continue;
                var handle = ExposedObjectRegistry.GetOrCreate(Guid.NewGuid().ToString("N"), exposedClass, comp);
                var json = ExposedObjectSnapshot.Capture(handle);
                if (string.IsNullOrEmpty(json)) continue;
                var componentObj = JObject.Parse(json);
                componentObj.Remove("@id");
                components[exposedClass.typeName] = componentObj;
            }
            root["components"] = components;

            return root.ToString(Formatting.None);
        }

        public static void Restore(string json, ExposedGameObject exposed, GameObject instance)
        {
            if (instance == null || string.IsNullOrEmpty(json)) return;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveStudio] Failed to parse asset state snapshot: {e.Message}");
                return;
            }

            var wrapperHandle = exposed != null ? ExposedObjectRegistry.FindByTarget(exposed) : null;
            if (wrapperHandle.HasValue && root["wrapper"] is JObject wrapper)
            {
                ExposedObjectSnapshot.Restore(wrapper.ToString(Formatting.None), wrapperHandle.Value);
            }

            if (root["components"] is JObject components && components.Count > 0)
            {
                foreach (var comp in instance.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var type = comp.GetType();
                    if (!ExposedClass.Has(type)) continue;
                    var exposedClass = ExposedClass.Find(type);
                    if (exposedClass == null) continue;
                    if (!(components[exposedClass.typeName] is JObject componentJson)) continue;
                    var handle = ExposedObjectRegistry.GetOrCreate(Guid.NewGuid().ToString("N"), exposedClass, comp);
                    ExposedObjectSnapshot.Restore(componentJson.ToString(Formatting.None), handle);
                }
            }
        }
    }
}
