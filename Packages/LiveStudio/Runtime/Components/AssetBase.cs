// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
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

        // The JSON key under which the GameObject wrapper handle is stored (components use their
        // exposed type name). Kept distinct from any exposed type name.
        const string kWrapperKey = "wrapper";

        /// <summary>Captures the full parameter values of the wrapper and components.</summary>
        public static string Capture(ExposedGameObject exposed, GameObject instance)
            => _Build(exposed, instance, ExposedObjectSnapshot.Capture, skipEmpty: false);

        /// <summary>
        /// Captures only the values that differ from the captured defaults (the delta). Components with
        /// no changes are omitted to keep the snapshot lean. Requires
        /// <see cref="CaptureDefaults"/> to have run on the same live objects beforehand.
        /// </summary>
        public static string CaptureDelta(ExposedGameObject exposed, GameObject instance)
            => _Build(exposed, instance, ExposedObjectSnapshot.CaptureDelta, skipEmpty: true);

        /// <summary>
        /// Records the current values of the wrapper and components as the baseline used for delta
        /// capture. Call this right after a load and BEFORE applying any saved state, so the baseline
        /// represents the source asset's defaults rather than already-overridden values.
        /// </summary>
        public static void CaptureDefaults(ExposedGameObject exposed, GameObject instance)
        {
            foreach (var entry in _EnumerateHandles(exposed, instance))
            {
                ExposedObjectDefaultRegistry.CaptureDefaults(entry.handle, DefaultExposedObjectResolver.Instance);
            }
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

            var components = root["components"] as JObject;
            foreach (var entry in _EnumerateHandles(exposed, instance))
            {
                JObject value = entry.key == kWrapperKey
                    ? root["wrapper"] as JObject
                    : components?[entry.key] as JObject;
                if (value == null) continue;
                ExposedObjectSnapshot.Restore(value.ToString(Formatting.None), entry.handle);
            }
        }

        // Serializes each handle with the given strategy (full Capture or CaptureDelta) into the shared
        // { version, wrapper, components } snapshot. The per-instance @id changes every reload, and
        // wrapper parenting / nested component values are owned elsewhere, so those keys are stripped.
        // When skipEmpty is true (delta mode), handles whose serialization carries no value beyond
        // metadata are omitted.
        private static string _Build(
            ExposedGameObject exposed, GameObject instance, Func<ExposedObjectHandle, string> serialize, bool skipEmpty)
        {
            if (instance == null) return null;

            var root = new JObject { ["version"] = kVersion };
            var components = new JObject();
            foreach (var entry in _EnumerateHandles(exposed, instance))
            {
                var json = serialize(entry.handle);
                if (string.IsNullOrEmpty(json)) continue;

                JObject obj;
                try { obj = JObject.Parse(json); }
                catch { continue; }

                obj.Remove("@id");
                if (entry.key == kWrapperKey)
                {
                    obj.Remove("@parent");
                    obj.Remove("components");
                    if (skipEmpty && !_HasValues(obj)) continue;
                    root["wrapper"] = obj;
                }
                else
                {
                    if (skipEmpty && !_HasValues(obj)) continue;
                    components[entry.key] = obj;
                }
            }
            root["components"] = components;

            return root.ToString(Formatting.None);
        }

        // Enumerates the exposed handles that make up an asset's snapshot: the GameObject wrapper
        // (keyed by kWrapperKey) followed by each [ExposedClass] component on the root (keyed by its
        // exposed type name, which is stable across reloads unlike the per-load object id).
        private static IEnumerable<(string key, ExposedObjectHandle handle)> _EnumerateHandles(
            ExposedGameObject exposed, GameObject instance)
        {
            var wrapperHandle = exposed != null ? ExposedObjectRegistry.FindByTarget(exposed) : null;
            if (wrapperHandle.HasValue) yield return (kWrapperKey, wrapperHandle.Value);

            if (instance == null) yield break;
            foreach (var comp in instance.GetComponents<Component>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                if (!ExposedClass.Has(type)) continue;
                var exposedClass = ExposedClass.Find(type);
                if (exposedClass == null) continue;
                var handle = ExposedObjectRegistry.GetOrCreate(Guid.NewGuid().ToString("N"), exposedClass, comp);
                yield return (exposedClass.typeName, handle);
            }
        }

        // True if the object carries at least one non-metadata property (metadata keys start with '@').
        // Used to drop unchanged entries from a delta snapshot.
        private static bool _HasValues(JObject obj)
        {
            foreach (var p in obj.Properties())
            {
                if (!p.Name.StartsWith("@", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
