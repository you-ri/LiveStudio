// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Holds a list of <see cref="IExposedObject"/> instances and acts as a resolver that finds
    /// objects by id or by target reference. Used to be a MonoBehaviour; the host
    /// <see cref="Lilium.RemoteControl.LiveScene.RemoteControlBehaviour"/> now owns the serialized
    /// list and forwards Unity lifecycle calls.
    /// </summary>
    [ExposedClass("ObjectContainer", Icon = "widgets", HideInScene = true)]
    public class ExposedObjectContainer : IExposedObjectResolver
    {
        const string kObjectContainerId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        [ExposedProperty("name")]
        public string exposedName => _name;

        public IReadOnlyList<IExposedObject> objects => _objects;

        // List instance is owned by the host MonoBehaviour (SerializeReference) and shared by reference.
        // Internal so LiveSceneSerializer can append wrapper entries during deserialization.
        internal readonly List<IExposedObject> _objects;
        private string _name;

        private ExposedObjectHandle? _selfExposedObject;
        private readonly HashSet<string> _persistentIds = new HashSet<string>();

        // Additional object lists merged in from other scenes (e.g. RemoteControlContainer
        // components living in additively-loaded .scene.lsb worlds). Each source is keyed by its
        // owner reference. The owning RemoteControlBehaviour adds/removes sources as the
        // containers enable/disable, and this container drives their lifecycle alongside _objects.
        private readonly List<SourceEntry> _sources = new List<SourceEntry>();
        private readonly HashSet<object> _initializedSources = new HashSet<object>();

        private struct SourceEntry
        {
            public List<IExposedObject> list;
            public object owner;
        }

        /// <summary>
        /// Optional host UnityEngine.Object reference. Used for editor undo recording when the
        /// container's _objects list mutates (set by <see cref="Lilium.RemoteControl.LiveScene.RemoteControlBehaviour"/>).
        /// </summary>
        public UnityEngine.Object host { get; }

        public ExposedObjectContainer(string name, List<IExposedObject> objects, UnityEngine.Object host = null)
        {
            _name = name;
            _objects = objects ?? throw new ArgumentNullException(nameof(objects));
            this.host = host;
        }

        /// <summary>
        /// Updates the display name returned by <see cref="exposedName"/>.
        /// Host MonoBehaviour can call this when its GameObject name changes.
        /// </summary>
        public void SetName(string name) => _name = name;

        /// <summary>
        /// Returns true if the object with the given id was present at <see cref="Initialize"/> time.
        /// </summary>
        public bool IsPersistent(string id) => _persistentIds.Contains(id);

        public void Initialize()
        {
            // Idempotent: tolerate being called more than once (some hosts call from both
            // OnEnable and an explicit Initialize path).
            if (_selfExposedObject != null) return;

            _selfExposedObject = ExposedObjectRegistry.Create<ExposedObjectContainer>(this, kObjectContainerId);

            _persistentIds.Clear();

            // Capture defaults of the container itself (needed for diff-based dirty detection
            // on the _objects list).
            if (_selfExposedObject != null)
                ExposedPropertyUtility.SetDefault(_selfExposedObject.Value);

            _InitializeObjectList(_objects);

            // Initialize any sources already registered before Initialize() ran (containers
            // present in the scene at server startup). Late-arriving sources go through
            // InitializeSource().
            for (int i = 0; i < _sources.Count; i++)
            {
                if (_initializedSources.Add(_sources[i].owner))
                    _InitializeObjectList(_sources[i].list);
            }
        }

        public void Shutdown()
        {
            _ShutdownObjectList(_objects);
            for (int i = 0; i < _sources.Count; i++)
                _ShutdownObjectList(_sources[i].list);

            _initializedSources.Clear();
            // Drop sources so a re-enabled host re-gathers them from the live registry rather than
            // retaining owners that may have unregistered while the host was disabled.
            _sources.Clear();
            _selfExposedObject?.Unregister();
            _selfExposedObject = null;
            _persistentIds.Clear();
        }

        public void UpdateObjects()
        {
            // Hot path (every LateUpdate): iterate by index without allocating an enumerator.
            _UpdateObjectList(_objects);
            for (int i = 0; i < _sources.Count; i++)
                _UpdateObjectList(_sources[i].list);
        }

        // --- Source management (objects merged in from other scenes) ---

        /// <summary>
        /// Registers an additional object list as a source. The list is owned by the caller
        /// (e.g. a RemoteControlContainer MonoBehaviour) and merged into enumeration, resolution
        /// and lifecycle. Idempotent per owner. Does not initialize the list; call
        /// <see cref="InitializeSource"/> after the container is already running.
        /// </summary>
        public void AddSource(List<IExposedObject> list, object owner)
        {
            if (list == null || owner == null) return;
            if (_IndexOfSource(owner) >= 0) return;
            _sources.Add(new SourceEntry { list = list, owner = owner });
        }

        public void RemoveSource(object owner)
        {
            int idx = _IndexOfSource(owner);
            if (idx < 0) return;
            _sources.RemoveAt(idx);
        }

        /// <summary>
        /// Initializes a single late-arriving source (OnEnable + defaults capture + persistentIds).
        /// No-op until the container itself has been initialized, in which case Initialize() picks
        /// the source up instead.
        /// </summary>
        public void InitializeSource(object owner)
        {
            if (_selfExposedObject == null) return;
            int idx = _IndexOfSource(owner);
            if (idx < 0) return;
            if (!_initializedSources.Add(owner)) return;
            _InitializeObjectList(_sources[idx].list);
        }

        /// <summary>
        /// Shuts down a single source (OnDisable + persistentIds removal) before it is removed.
        /// </summary>
        public void ShutdownSource(object owner)
        {
            int idx = _IndexOfSource(owner);
            if (idx < 0) return;
            if (!_initializedSources.Remove(owner)) return;

            var list = _sources[idx].list;
            // Drop persistent ids before OnDisable, which clears each object's exposedObject handle.
            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                if (obj?.exposedObject != null && obj.exposedObject.Value.hasId)
                    _persistentIds.Remove(obj.exposedObject.Value.id);
            }
            _ShutdownObjectList(list);
        }

        /// <summary>
        /// Enumerates every object across the main list and all registered sources. Used by the
        /// listing endpoint and serialization (not the per-frame update path). May yield nulls,
        /// matching <see cref="objects"/>; callers null-check.
        /// </summary>
        public IEnumerable<IExposedObject> EnumerateAllObjects()
        {
            for (int i = 0; i < _objects.Count; i++)
                yield return _objects[i];
            for (int s = 0; s < _sources.Count; s++)
            {
                var list = _sources[s].list;
                for (int i = 0; i < list.Count; i++)
                    yield return list[i];
            }
        }

        private int _IndexOfSource(object owner)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (ReferenceEquals(_sources[i].owner, owner)) return i;
            }
            return -1;
        }

        private void _InitializeObjectList(List<IExposedObject> list)
        {
            foreach (var obj in list)
            {
                if (obj == null) continue;
                obj.OnEnable();
            }

            // Capture defaults of each contained ExposedObjectHandle.
            foreach (var obj in list)
            {
                if (obj == null) continue;
                var exposedObj = obj.exposedObject;
                if (exposedObj != null)
                    ExposedPropertyUtility.SetDefault(exposedObj.Value);
            }

            // Mark currently held objects as persistent (i.e. originally part of the scene).
            foreach (var obj in list)
            {
                if (obj?.exposedObject != null && obj.exposedObject.Value.hasId)
                    _persistentIds.Add(obj.exposedObject.Value.id);
            }

            // Inline UnityEngine.Object references (components etc.) also need defaults captured
            // so that subsequent delta saves can compute diffs correctly.
            var reachable = ExposedObjectGraph.ResolveExposedObjects(list, this);
            foreach (var exposed in reachable)
            {
                if (exposed.hasId) continue;
                ExposedObjectDefaultRegistry.EnsureDefaultsCaptured(
                    exposed, DefaultExposedObjectResolver.Instance);
            }
        }

        private static void _ShutdownObjectList(List<IExposedObject> list)
        {
            // Iterate a snapshot: an object's OnDisable may modify this list. For example a manager
            // (e.g. ExternalAssetManager) unloads child objects it previously appended here, removing
            // them mid-iteration. The snapshot avoids "Collection was modified", and the membership check
            // skips objects such a manager already shut down so they are not disabled twice.
            var snapshot = list.ToArray();
            foreach (var obj in snapshot)
            {
                if (obj == null) continue;
                if (!list.Contains(obj)) continue;
                obj.OnDisable();
            }
        }

        private static void _UpdateObjectList(List<IExposedObject> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                if (obj == null) continue;
                obj.Update();
            }
        }

        // --- Object management ---

        public void AddExposedObject(IExposedObject exposedObject) => _objects.Add(exposedObject);

        public void RemoveExposedObject(IExposedObject exposedObject) => _objects.Remove(exposedObject);

        public void RemoveExposedObjectById(string id)
        {
            var obj = _objects.FirstOrDefault(x => x.id == id);
            if (obj != null) _objects.Remove(obj);
        }

        public bool HasExposedObject(string id) => _objects.Any(x => x.id == id);

        public void RebindExposedObject(string id, UnityEngine.Object obj, IExposedPropertyTable resolver)
        {
            var data = _objects.FirstOrDefault(x => x.id == id);
            if (data != null)
            {
                data.OnDisable();
                if (obj != null && data is ExposedUnityObjectBase unityObj)
                    unityObj.ResolveReferences(resolver);
                data.OnEnable();
            }
        }

        public void ResetAll()
        {
            _ResetObjectList(_objects);
            for (int i = 0; i < _sources.Count; i++)
                _ResetObjectList(_sources[i].list);
            Debug.Log($"[RemoteControl] Reset all {_name} container to default values.");
        }

        public void ResolveAllReferences(IExposedPropertyTable resolver)
        {
            _ResolveReferencesInList(_objects, resolver);
            for (int i = 0; i < _sources.Count; i++)
                _ResolveReferencesInList(_sources[i].list, resolver);
        }

        private static void _ResetObjectList(List<IExposedObject> list)
        {
            foreach (var obj in list)
            {
                if (obj == null) continue;
                obj.Reset();
            }
        }

        private static void _ResolveReferencesInList(List<IExposedObject> list, IExposedPropertyTable resolver)
        {
            foreach (var obj in list)
            {
                if (obj == null) continue;
                if (obj is ExposedUnityObjectBase unityObj)
                    unityObj.ResolveReferences(resolver);
            }
        }

        // --- IExposedObjectResolver ---

        public ExposedObjectHandle? FindById(string id)
        {
            var hit = _FindByIdInList(_objects, id);
            if (hit != null) return hit;

            for (int s = 0; s < _sources.Count; s++)
            {
                hit = _FindByIdInList(_sources[s].list, id);
                if (hit != null) return hit;
            }

            return ExposedObjectRegistry.FindById(id);
        }

        public ExposedObjectHandle? FindByTarget(object target)
        {
            if (target == null) return null;

            var targetUnityObj = target as UnityEngine.Object;
            var hit = _FindByTargetInList(_objects, targetUnityObj);
            if (hit != null) return hit;

            for (int s = 0; s < _sources.Count; s++)
            {
                hit = _FindByTargetInList(_sources[s].list, targetUnityObj);
                if (hit != null) return hit;
            }

            return ExposedObjectRegistry.FindByTarget(target);
        }

        private static ExposedObjectHandle? _FindByIdInList(List<IExposedObject> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                if (list[i].id == id)
                    return list[i].exposedObject;
            }
            return null;
        }

        private static ExposedObjectHandle? _FindByTargetInList(List<IExposedObject> list, UnityEngine.Object targetUnityObj)
        {
            if (targetUnityObj == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                if (list[i] is ExposedUnityObjectBase u && u.reference == targetUnityObj)
                    return list[i].exposedObject;
            }
            return null;
        }
    }
}
