// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// The base-scene <see cref="RemoteControlContainer"/> the asset's wrapper is registered into so
        /// the remote app can control it. The wrapper lives in the same (reloadable) base scene as the
        /// instantiated GameObject, so both are torn down together on a base-scene reload — no stale
        /// wrapper is left dangling in the persistent host container.
        /// </summary>
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
        // Identity / state fields below are hidden from the generic exposed-object UI (the project
        // detail pane renders a concrete asset, e.g. a LiveSceneAsset, through it): there they would be
        // noise, since load / activate are driven from the list items and only `name` + the asset's own
        // functions belong in the editor. Hide does not skip serialization, so the list keeps reading them.

        /// <summary>
        /// Stable runtime/equality key for this entry (the normalized absolute file path). NOT persisted
        /// (persistable=false): it is machine-specific, so the live scene stores the portable
        /// <see cref="path"/> instead and this is reconstructed from it on load. Still exposed to the
        /// remote app, which uses it as the durable handle for load/remove calls within a session.
        /// </summary>
        [ExposedField(persistable = false), Hide]
        public string id;

        [ExposedField]
        public string name;

        /// <summary>
        /// Absolute file path the loader reads. NOT persisted (persistable=false) — it is machine-specific;
        /// the portable project-relative <see cref="path"/> is persisted instead and this is reconstructed
        /// from it on load. Still exposed to the remote app for display.
        /// </summary>
        [ExposedField(persistable = false), Hide]
        public string filePath;

        /// <summary>
        /// Project-relative path persisted to the live scene (forward slashes), so a saved scene survives
        /// moving the project folder or opening it on another machine. The runtime <see cref="filePath"/>
        /// and <see cref="id"/> are absolute and reconstructed from this on load. Falls back to an
        /// absolute path when no relative path can be formed (different drive / file outside the project).
        /// Legacy scenes without this field restore from the absolute <see cref="id"/> / <see cref="filePath"/>
        /// they stored instead. Maintained by <see cref="ExternalAssetManager"/> at (de)serialization.
        /// </summary>
        [ExposedField, Hide]
        public string path;

        /// <summary>Desired state. Toggling loads (true) / unloads (false) the asset. Persisted.</summary>
        [ExposedField, Hide]
        public bool enabled;

        /// <summary>Actual state, synced after a load/unload completes. Not persisted.</summary>
        [ExposedField(persistable = false), Hide]
        public bool isLoaded;

        /// <summary>
        /// Stable id of this asset's exposed object, so the remote app can keep a durable reference
        /// to the loaded object's property editor across unload/reload cycles. Assigned once and
        /// persisted. Unused by kinds that do not wrap a fresh exposed object (e.g. avatars, which are
        /// exposed through the existing <c>AvatarController</c>).
        /// </summary>
        [ExposedField, Hide]
        public string objectId;

        /// <summary>
        /// Serialized snapshot of this asset's exposed parameter values, captured before unload and
        /// reapplied after reload so edits survive an unload/reload cycle (and an avatar swap, for
        /// avatar-attached props). A runtime carrier only — NOT persisted to the live scene
        /// (<c>persistable=false</c>): a loaded object's persisted state now lives as top-level object
        /// entries in the live scene, applied through the deferred-bind pending store once the asset has
        /// loaded. Legacy scenes that stored <c>state</c> still read it harmlessly (the top-level diff
        /// supersedes it). Hidden from the editor; [RawJson] keeps the (JSON) value inline.
        /// </summary>
        [ExposedField(persistable = false), Hide, RawJson]
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
        /// True for an app-embedded (built-in) entry that is not backed by a project-folder file — e.g. a
        /// <see cref="BuiltinAnimationAsset"/> sourced from a Resources asset. Such entries are injected by
        /// <see cref="ExternalAssetManager"/> from the built-in catalog rather than discovered by the
        /// project crawl, so the crawl must never prune them and they are never persisted (they are always
        /// present, re-injected each run). Defaults to false for ordinary file-backed assets.
        /// </summary>
        public virtual bool isBuiltin => false;

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

        /// <summary>
        /// Reflects a failed load: clears <see cref="enabled"/> so the UI does not show the asset stuck
        /// "on", and <see cref="isLoaded"/> so the manager re-diffs cleanly. Call from a load failure path.
        /// </summary>
        protected void MarkLoadFailed()
        {
            enabled = false;
            isLoaded = false;
        }

        /// <summary>
        /// Shared front-half of loading a preset (<c>*.preset.json</c>) entry: reads the file, parses it,
        /// and resolves the referenced source asset to an existing absolute path. On any failure it logs
        /// (using <paramref name="kindLabel"/> in the message), calls <see cref="MarkLoadFailed"/>, and
        /// returns false. On success returns the parsed <paramref name="preset"/> and absolute
        /// <paramref name="source"/>; the concrete kind then applies its own load behavior.
        /// </summary>
        protected bool TryReadPreset(string kindLabel, out PropPreset.PresetData preset, out string source)
        {
            preset = default;
            source = null;

            string json;
            try { json = File.ReadAllText(filePath); }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to read {kindLabel} preset '{filePath}': {e.Message}");
                MarkLoadFailed();
                return false;
            }

            if (!PropPreset.TryParse(json, out preset))
            {
                MarkLoadFailed();
                return false;
            }

            source = PropPreset.ResolveSource(preset.source, Path.GetDirectoryName(filePath));
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
            {
                Debug.LogError($"[LiveStudio] {kindLabel} preset source not found: '{preset.source}' (from '{filePath}').");
                MarkLoadFailed();
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Builds and applies the aggregate JSON snapshot of an exposed object: the GameObject wrapper plus
    /// each <c>[ExposedClass]</c> component on the root. Components are keyed by their exposed type name
    /// (stable across reloads, unlike the per-load object id), so edited parameter values persist across
    /// an unload/reload cycle and into presets. Object-generic: it works for any exposed GameObject
    /// (a freshly created prop wrapper, or a pre-existing scene wrapper such as the avatar's
    /// AvatarController GameObject), with no per-kind special casing.
    /// </summary>
    internal static class AssetStateSnapshot
    {
        // The JSON key under which the GameObject wrapper handle is stored (components use their
        // exposed type name). Kept distinct from any exposed type name.
        const string kWrapperKey = "wrapper";

        // --- Entry points for callers that already hold the wrapper object (props) ---

        /// <summary>Captures the full parameter values of the wrapper and components.</summary>
        public static string Capture(ExposedGameObject exposed, GameObject instance)
            => _Build(_WrapperHandle(exposed), instance, ExposedObjectSnapshot.Capture, skipEmpty: false);

        /// <summary>
        /// Captures only the values that differ from the captured defaults (the delta). Components with
        /// no changes are omitted to keep the snapshot lean. Requires
        /// <see cref="CaptureDefaults(ExposedGameObject, GameObject)"/> to have run on the same live
        /// objects beforehand.
        /// </summary>
        public static string CaptureDelta(ExposedGameObject exposed, GameObject instance)
            => _Build(_WrapperHandle(exposed), instance, ExposedObjectSnapshot.CaptureDelta, skipEmpty: true);

        /// <summary>
        /// Records the current values of the wrapper and components as the baseline used for delta
        /// capture. Call this right after a load and BEFORE applying any saved state, so the baseline
        /// represents the source asset's defaults rather than already-overridden values.
        /// </summary>
        public static void CaptureDefaults(ExposedGameObject exposed, GameObject instance)
            => _CaptureDefaults(_WrapperHandle(exposed), instance);

        public static void Restore(string json, ExposedGameObject exposed, GameObject instance)
            => _Restore(json, _WrapperHandle(exposed), instance);

        // --- Object-generic entry points: resolve the wrapper from the GameObject ---
        // For objects exposed through a pre-existing scene wrapper (e.g. the avatar's AvatarController
        // GameObject) the caller does not hold the ExposedGameObject, only the live GameObject.

        /// <summary>Delta-captures the wrapper + components of <paramref name="instance"/>, resolving its
        /// exposed wrapper from the registry. See <see cref="CaptureDelta(ExposedGameObject, GameObject)"/>.</summary>
        public static string CaptureDelta(GameObject instance)
            => _Build(_WrapperHandleForGameObject(instance), instance, ExposedObjectSnapshot.CaptureDelta, skipEmpty: true);

        /// <summary>Records the delta baseline for <paramref name="instance"/>. See
        /// <see cref="CaptureDefaults(ExposedGameObject, GameObject)"/>.</summary>
        public static void CaptureDefaults(GameObject instance)
            => _CaptureDefaults(_WrapperHandleForGameObject(instance), instance);

        /// <summary>Restores a snapshot onto <paramref name="instance"/>, resolving its exposed wrapper
        /// from the registry.</summary>
        public static void Restore(string json, GameObject instance)
            => _Restore(json, _WrapperHandleForGameObject(instance), instance);

        // --- Core implementation (wrapper handle + instance) ---

        private static void _CaptureDefaults(ExposedObjectHandle? wrapperHandle, GameObject instance)
        {
            foreach (var entry in _EnumerateHandles(wrapperHandle, instance))
            {
                // Preserving variant: the avatar's wrapper (and its components) is a persistent scene
                // object, so the live-scene restore applies its saved top-level diff BEFORE the avatar's
                // async load completes and this re-baseline runs. A blind capture would adopt those
                // applied values as defaults and silently drop them from the next save. Overridden
                // properties keep their previous default and stay dirty; freshly instantiated objects
                // (props) have no previous baseline, so this behaves exactly like CaptureDefaults.
                ExposedObjectDefaultRegistry.CaptureDefaultsPreservingOverrides(entry.handle, DefaultExposedObjectResolver.Instance);
            }
        }

        private static void _Restore(string json, ExposedObjectHandle? wrapperHandle, GameObject instance)
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
            foreach (var entry in _EnumerateHandles(wrapperHandle, instance))
            {
                JObject value = entry.key == kWrapperKey
                    ? root["wrapper"] as JObject
                    : _ResolveComponentValue(components, entry.key, entry.exposedClass);
                if (value == null) continue;
                ExposedObjectSnapshot.Restore(value.ToString(Formatting.None), entry.handle);
            }
        }

        /// <summary>
        /// Adopts the current state of the wrapper and its components as the user-change baseline.
        ///
        /// Call as the LAST step of an asset's load sequence — after CaptureDefaults, after the
        /// state-carrier Restore, and after the deferred live-scene apply — so everything the load
        /// itself applied stops counting as an unsaved user edit. The serialization baseline is left
        /// alone, so delta saves and save-as-preset still write the full diff.
        /// </summary>
        public static void MarkAllClean(GameObject instance)
            => _MarkAllClean(_WrapperHandleForGameObject(instance), instance);

        /// <inheritdoc cref="MarkAllClean(GameObject)"/>
        public static void MarkAllClean(ExposedGameObject exposed, GameObject instance)
            => _MarkAllClean(_WrapperHandle(exposed), instance);

        private static void _MarkAllClean(ExposedObjectHandle? wrapperHandle, GameObject instance)
        {
            if (instance == null) return;
            foreach (var entry in _EnumerateHandles(wrapperHandle, instance))
            {
                entry.handle.MarkClean();
            }
        }

        // Serializes each handle with the given strategy (full Capture or CaptureDelta) into the shared
        // { wrapper, components } snapshot. The per-instance @id changes every reload, and wrapper
        // parenting / nested component values are owned elsewhere, so those keys are stripped. When
        // skipEmpty is true (delta mode), handles whose serialization carries no value beyond metadata
        // are omitted. No format version is embedded: the preset file's formatVersion is the single
        // source of version truth (see PropPreset).
        private static string _Build(
            ExposedObjectHandle? wrapperHandle, GameObject instance, Func<ExposedObjectHandle, string> serialize, bool skipEmpty)
        {
            if (instance == null) return null;

            var root = new JObject();
            var components = new JObject();
            foreach (var entry in _EnumerateHandles(wrapperHandle, instance))
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
                    if (skipEmpty && !ExposedPropertySerializer.HasNonMetaProperties(obj)) continue;
                    root["wrapper"] = obj;
                }
                else
                {
                    if (skipEmpty && !ExposedPropertySerializer.HasNonMetaProperties(obj)) continue;
                    components[entry.key] = obj;
                }
            }
            root["components"] = components;

            return root.ToString(Formatting.None);
        }

        // (Non-metadata-property check is shared via ExposedPropertySerializer.HasNonMetaProperties.)

        // Looks up a component's saved JSON by its current exposed type name, falling back to any former
        // names ([FormerlyExposedAs]) so a preset written before a type rename still restores. The value
        // payload's own @type / member names are resolved by the serializer; this covers the envelope KEY,
        // which is the current type name and would otherwise miss a snapshot keyed by the old name.
        private static JObject _ResolveComponentValue(JObject components, string currentKey, ExposedClass exposedClass)
        {
            if (components == null) return null;
            if (components[currentKey] is JObject current) return current;

            var formers = exposedClass?.formerTypeNames;
            if (formers != null)
            {
                for (int i = 0; i < formers.Length; i++)
                {
                    if (!string.IsNullOrEmpty(formers[i]) && components[formers[i]] is JObject former) return former;
                }
            }
            return null;
        }

        // The wrapper handle for a prop-style caller: the registered handle whose target is the wrapper.
        private static ExposedObjectHandle? _WrapperHandle(ExposedGameObject exposed)
            => exposed != null ? ExposedObjectRegistry.FindByTarget(exposed) : null;

        // The wrapper handle for an object exposed through a pre-existing scene wrapper: the registered
        // handle whose target is an ExposedUnityObjectBase referencing this GameObject (matches how
        // AvatarAsset resolves the AvatarController's wrapper). Null when the GameObject has no wrapper
        // (e.g. the default avatar with no scene wrapper) — components are still enumerated directly.
        private static ExposedObjectHandle? _WrapperHandleForGameObject(GameObject go)
        {
            if (go == null) return null;
            foreach (var handle in ExposedObjectRegistry.instances)
            {
                if (handle.target is ExposedUnityObjectBase proxy
                    && proxy.reference is GameObject wrappedGo
                    && wrappedGo == go)
                {
                    return handle;
                }
            }
            return null;
        }

        // Enumerates the exposed handles that make up an object's snapshot: the GameObject wrapper
        // (keyed by kWrapperKey, null ExposedClass) followed by each [ExposedClass] component on the root
        // (keyed by its exposed type name, which is stable across reloads unlike the per-load object id).
        // The ExposedClass is carried so Restore can fall back to the component's former type names.
        private static IEnumerable<(string key, ExposedObjectHandle handle, ExposedClass exposedClass)> _EnumerateHandles(
            ExposedObjectHandle? wrapperHandle, GameObject instance)
        {
            if (wrapperHandle.HasValue) yield return (kWrapperKey, wrapperHandle.Value, null);

            if (instance == null) yield break;
            foreach (var comp in instance.GetComponents<Component>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                if (!ExposedClass.Has(type)) continue;
                var exposedClass = ExposedClass.Find(type);
                if (exposedClass == null) continue;
                // Take an id-less handle: the snapshot keys components by exposed type name (above) and
                // its consumers key by target reference (ExposedObjectDefaultRegistry) or operate on the
                // handle directly (ExposedObjectSnapshot), so no registry id is needed. Registering the
                // component with an id would make ExposedGameObject._components serialize it as an @ref,
                // which the remote app then fetches as a stray top-level scene object (a duplicate entry).
                var handle = ExposedObjectRegistry.GetOrCreateWithoutId(exposedClass, comp);
                yield return (exposedClass.typeName, handle, exposedClass);
            }
        }

    }
}
