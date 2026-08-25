// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Holds a list of <see cref="ILiveObject"/> instances and acts as a resolver that finds
    /// objects by id or by target reference. Used to be a MonoBehaviour; the host
    /// <c>RemoteControlBehaviour</c> now owns the serialized
    /// list and forwards Unity lifecycle calls.
    /// </summary>
    [LiveClass("ObjectContainer", Icon = "widgets", HideInScene = true)]
    public class LiveObjectContainer : ILiveObjectResolver
    {
        const string kObjectContainerId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        [LiveProperty("name")]
        public string liveName => _name;

        public IReadOnlyList<ILiveObject> objects => _objects;

        /// <summary>
        /// The container the running host merged every source into, or null when no host is active.
        /// Scene restore has to resolve ids against that merged set, but it must not reach for the
        /// host itself: the host lives with the HTTP server, and persistence has to keep working
        /// with no server running. The host installs itself here on startup and clears it on
        /// shutdown.
        /// </summary>
        public static LiveObjectContainer main { get; set; }

        // List instance is owned by the host MonoBehaviour (SerializeReference) and shared by reference.
        // Internal so LiveSceneSerializer can append wrapper entries during deserialization.
        internal readonly List<ILiveObject> _objects;
        private string _name;

        private LiveObjectHandle? _selfLiveObject;
        private readonly HashSet<string> _persistentIds = new HashSet<string>();

        // Additional object lists merged in from other scenes (e.g. RemoteControlContainer
        // components living in additively-loaded .set.lsb sets). Each source is keyed by its
        // owner reference. The owning RemoteControlBehaviour adds/removes sources as the
        // containers enable/disable, and this container drives their lifecycle alongside _objects.
        private readonly List<SourceEntry> _sources = new List<SourceEntry>();
        private readonly HashSet<object> _initializedSources = new HashSet<object>();

        private struct SourceEntry
        {
            public List<ILiveObject> list;
            public object owner;
        }

        /// <summary>
        /// Optional host UnityEngine.Object reference. Used for editor undo recording when the
        /// container's _objects list mutates (set by <c>RemoteControlBehaviour</c>).
        /// </summary>
        public UnityEngine.Object host { get; }

        public LiveObjectContainer(string name, List<ILiveObject> objects, UnityEngine.Object host = null)
        {
            _name = name;
            _objects = objects ?? throw new ArgumentNullException(nameof(objects));
            this.host = host;
        }

        /// <summary>
        /// Updates the display name returned by <see cref="liveName"/>.
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
            if (_selfLiveObject != null) return;

            _selfLiveObject = LiveObjectRegistry.Create<LiveObjectContainer>(this, kObjectContainerId);

            _persistentIds.Clear();

            // Capture defaults of the container itself (needed for diff-based dirty detection
            // on the _objects list).
            if (_selfLiveObject != null)
                LivePropertyUtility.SetDefault(_selfLiveObject.Value);

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
            _selfLiveObject?.Unregister();
            _selfLiveObject = null;
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
        public void AddSource(List<ILiveObject> list, object owner)
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
            if (_selfLiveObject == null) return;
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
            // Drop persistent ids before OnDisable, which clears each object's liveObject handle.
            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                if (obj?.liveObject != null && obj.liveObject.Value.hasId)
                    _persistentIds.Remove(obj.liveObject.Value.id);
            }
            _ShutdownObjectList(list);
        }

        /// <summary>
        /// Enumerates every object across the main list and all registered sources. Used by the
        /// listing endpoint and serialization (not the per-frame update path). May yield nulls,
        /// matching <see cref="objects"/>; callers null-check.
        /// </summary>
        public IEnumerable<ILiveObject> EnumerateAllObjects()
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

        /// <summary>
        /// The <see cref="UnityEngine.Object"/> that serializes <paramref name="liveObject"/>, or null
        /// when nothing does.
        /// </summary>
        /// <remarks>
        /// A live object is written into a scene by whoever holds the list it sits in: the
        /// <see cref="host"/> for the main list, and the source's owner for a merged one. A source
        /// registered under a plain C# owner — the runtime-only binding wrappers do exactly that —
        /// has no serialized home and answers null.
        /// <para/>
        /// The editor asks this to decide whether a write while not playing has anywhere to land:
        /// members held by the live object itself (a camera's priority, say, rather than a value
        /// forwarded to the wrapped component) are saved by the editor's own Save only if the object
        /// is serialized somewhere.
        /// </remarks>
        public UnityEngine.Object FindSerializedOwner(object liveObject)
        {
            if (liveObject == null) return null;

            for (int i = 0; i < _objects.Count; i++)
            {
                if (ReferenceEquals(_objects[i], liveObject)) return host;
            }

            for (int s = 0; s < _sources.Count; s++)
            {
                var list = _sources[s].list;
                for (int i = 0; i < list.Count; i++)
                {
                    if (ReferenceEquals(list[i], liveObject)) return _sources[s].owner as UnityEngine.Object;
                }
            }

            return null;
        }

        private int _IndexOfSource(object owner)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (ReferenceEquals(_sources[i].owner, owner)) return i;
            }
            return -1;
        }

        private void _InitializeObjectList(List<ILiveObject> list)
        {
            foreach (var obj in list)
            {
                if (obj == null) continue;
                obj.OnEnable();
            }

            // Capture defaults of each contained LiveObjectHandle.
            foreach (var obj in list)
            {
                if (obj == null) continue;
                var liveObj = obj.liveObject;
                if (liveObj != null)
                    LivePropertyUtility.SetDefault(liveObj.Value);
            }

            // Mark currently held objects as persistent (i.e. originally part of the scene).
            foreach (var obj in list)
            {
                if (obj?.liveObject != null && obj.liveObject.Value.hasId)
                    _persistentIds.Add(obj.liveObject.Value.id);
            }

            // Inline UnityEngine.Object references (components etc.) also need defaults captured
            // so that subsequent delta saves can compute diffs correctly.
            var reachable = LiveObjectGraph.ResolveLiveObjects(list, this);
            foreach (var exposed in reachable)
            {
                if (exposed.hasId) continue;
                LiveObjectDefaultRegistry.EnsureDefaultsCaptured(
                    exposed, DefaultLiveObjectResolver.Instance);
            }
        }

        private static void _ShutdownObjectList(List<ILiveObject> list)
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

        private static void _UpdateObjectList(List<ILiveObject> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var obj = list[i];
                if (obj == null) continue;
                obj.Update();
            }
        }

        // --- Object management ---

        public void AddLiveObject(ILiveObject liveObject) => _objects.Add(liveObject);

        public void RemoveLiveObject(ILiveObject liveObject) => _objects.Remove(liveObject);

        public void RemoveLiveObjectById(string id)
        {
            var obj = _objects.FirstOrDefault(x => x.id == id);
            if (obj != null) _objects.Remove(obj);
        }

        public bool HasLiveObject(string id) => _objects.Any(x => x.id == id);

        public void RebindLiveObject(string id, UnityEngine.Object obj, IExposedPropertyTable resolver)
        {
            var data = _objects.FirstOrDefault(x => x.id == id);
            if (data != null)
            {
                data.OnDisable();
                if (obj != null && data is LiveUnityObjectBase unityObj)
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

        private static void _ResetObjectList(List<ILiveObject> list)
        {
            foreach (var obj in list)
            {
                if (obj == null) continue;
                obj.Reset();
            }
        }

        private static void _ResolveReferencesInList(List<ILiveObject> list, IExposedPropertyTable resolver)
        {
            foreach (var obj in list)
            {
                if (obj == null) continue;
                if (obj is LiveUnityObjectBase unityObj)
                    unityObj.ResolveReferences(resolver);
            }
        }

        // --- ILiveObjectResolver ---

        public LiveObjectHandle? FindById(string id)
        {
            var hit = _FindByIdInList(_objects, id);
            if (hit != null) return hit;

            for (int s = 0; s < _sources.Count; s++)
            {
                hit = _FindByIdInList(_sources[s].list, id);
                if (hit != null) return hit;
            }

            return LiveObjectRegistry.FindById(id);
        }

        public LiveObjectHandle? FindByTarget(object target)
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

            return LiveObjectRegistry.FindByTarget(target);
        }

        private static LiveObjectHandle? _FindByIdInList(List<ILiveObject> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                if (list[i].id == id)
                    return list[i].liveObject;
            }
            return null;
        }

        private static LiveObjectHandle? _FindByTargetInList(List<ILiveObject> list, UnityEngine.Object targetUnityObj)
        {
            if (targetUnityObj == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                if (list[i] is LiveUnityObjectBase u && u.reference == targetUnityObj)
                    return list[i].liveObject;
            }
            return null;
        }
    }
}
