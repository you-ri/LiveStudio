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
    /// <see cref="Lilium.RemoteControl.Server.RemoteControlBehaviour"/> now owns the serialized
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
        // Internal so ExposedSceneSerializer can append wrapper entries during deserialization.
        internal readonly List<IExposedObject> _objects;
        private string _name;

        private ExposedObject _selfExposedObject;
        private readonly HashSet<string> _persistentIds = new HashSet<string>();

        /// <summary>
        /// Optional host UnityEngine.Object reference. Used for editor undo recording when the
        /// container's _objects list mutates (set by <see cref="Lilium.RemoteControl.Server.RemoteControlBehaviour"/>).
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

            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                obj.OnEnable();
            }

            // Capture defaults of the container itself (needed for diff-based dirty detection
            // on the _objects list).
            ExposedPropertyUtility.SetDefault(_selfExposedObject);

            // Capture defaults of each contained ExposedObject.
            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                var exposedObj = obj.exposedObject;
                if (exposedObj != null)
                    ExposedPropertyUtility.SetDefault(exposedObj);
            }

            // Mark currently held objects as persistent (i.e. originally part of the scene).
            _persistentIds.Clear();
            foreach (var obj in _objects)
            {
                if (obj?.exposedObject != null && obj.exposedObject.hasId)
                    _persistentIds.Add(obj.exposedObject.id);
            }

            // Inline UnityEngine.Object references (components etc.) also need defaults captured
            // so that subsequent delta saves can compute diffs correctly.
            var reachable = ExposedSceneSerializer.ResolveExposedObjects(objects, this);
            foreach (var exposed in reachable)
            {
                if (exposed == null || exposed.hasId) continue;
                ExposedObjectDefaultRegistry.EnsureDefaultsCaptured(
                    exposed, DefaultExposedObjectResolver.Instance);
            }
        }

        public void Shutdown()
        {
            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                obj.OnDisable();
            }

            _selfExposedObject?.Unregister();
            _selfExposedObject = null;
            _persistentIds.Clear();
        }

        public void UpdateObjects()
        {
            foreach (var obj in _objects)
            {
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
            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                obj.Reset();
            }
            Debug.Log($"[RemoteControl] Reset all {_name} container to default values.");
        }

        public void ResolveAllReferences(IExposedPropertyTable resolver)
        {
            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                if (obj is ExposedUnityObjectBase unityObj)
                    unityObj.ResolveReferences(resolver);
            }
        }

        // --- IExposedObjectResolver ---

        public ExposedObject FindById(string id)
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] == null) continue;
                if (_objects[i].id == id)
                    return _objects[i].exposedObject;
            }

            return ExposedObjectRegistry.FindById(id);
        }

        public ExposedObject FindByTarget(object target)
        {
            if (target == null) return null;

            var targetUnityObj = target as UnityEngine.Object;
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] == null) continue;

                if (targetUnityObj != null && _objects[i] is ExposedUnityObjectBase u && u.reference == targetUnityObj)
                    return _objects[i].exposedObject;
            }

            return ExposedObjectRegistry.FindByTarget(target);
        }
    }
}
