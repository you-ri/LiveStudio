// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.Scene;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// Base interface for any class declaring a Remote Control UI page.
    /// </summary>
    public interface IPage
    {
    }

    /// <summary>
    /// Provides the list of objects displayed for a given UI page.
    /// </summary>
    public interface IObjectSelector
    {
        object[] objects { get; }
    }

    [Serializable]
    [ExposedClass]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class ObjectSelectorBase : IObjectSelector
    {
        [ExposedProperty]
        public object[] objects => GetObjects();

        protected virtual object[] GetObjects() => new object[0];
    }

    public interface IObjectFactory
    {
        object[] objects { get; }
        string[] objectNames { get; }

        void CreateObject(int index);

        void DestroyObject(string objectId);

        void RegisterPrefabs();
    }

    [Serializable]
    [ExposedClass(HideInScene = true)]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class ObjectFactoryBase : IObjectFactory
    {
        [NonSerialized]
        protected ExposedObjectContainer _container;

        public object[] objects => GetObjects();

        [ExposedProperty]
        public string[] objectNames => GetObjectNames();

        protected virtual object[] GetObjects() => new object[0];
        protected virtual string[] GetObjectNames() => new string[0];

        [ExposedProperty]
        public int[] objectAccessLevels => GetObjectAccessLevels();

        protected virtual int[] GetObjectAccessLevels() => new int[0];

        public virtual void Initialize(ExposedObjectContainer container)
        {
            _container = container;
        }

        [ExposedFunction]
        public virtual void CreateObject(int index) { }

        [ExposedFunction]
        public virtual void DestroyObject(string objectId) { }

        public virtual void RegisterPrefabs() { }
    }

    /// <summary>
    /// UI page definition.
    /// Corresponds to the CategoryPage on the RemoteApp side.
    /// </summary>
    [Serializable]
    [ExposedClass]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class CategoryPage : IPage
    {
        /// <summary>
        /// ページ内のオブジェクトを選択するためのセレクタ。
        /// </summary>
        /// <returns></returns>
        [SerializeReference, Select]
        public IObjectSelector selector = new StandardObjectSelector();

        [SerializeReference, Select]
        public IObjectFactory factory = new StandardObjectFactory();
    }

    /// <summary>
    /// UI page definition.
    /// Standard object selector for the RemoteApp CategoryPage.
    /// </summary>
    [Serializable]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class StandardObjectSelector : ObjectSelectorBase
    {
        public string category;

        protected override object[] GetObjects()
        {
            if (string.IsNullOrEmpty(category))
                return new object[0];
            var list = ExposedObjectRegistry.FindByCategory(category);
            var result = new object[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = list[i].target;
            return result;
        }
    }

    [Serializable]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class StandardObjectFactory : ObjectFactoryBase
    {
        [SerializeReference, Select]
        public IExposedObjectFactory[] factories;

        protected override object[] GetObjects()
        {
            if (factories == null) return new object[0];
            var result = new object[factories.Length];
            for (int i = 0; i < factories.Length; i++)
                result[i] = factories[i];
            return result;
        }

        protected override string[] GetObjectNames()
        {
            if (factories == null) return new string[0];
            var names = new string[factories.Length];
            for (int i = 0; i < factories.Length; i++)
                names[i] = factories[i]?.name ?? "";
            return names;
        }

        protected override int[] GetObjectAccessLevels()
        {
            if (factories == null) return new int[0];
            var levels = new int[factories.Length];
            for (int i = 0; i < factories.Length; i++)
                levels[i] = (int)(factories[i]?.accessLevel ?? AccessLevel.Public);
            return levels;
        }

        public override void CreateObject(int index)
        {
            if (factories == null || index < 0 || index >= factories.Length)
            {
                Debug.LogWarning($"[RemoteControl] StandardObjectFactory.CreateObject: invalid index {index} (factories={(factories?.Length ?? 0)}).");
                return;
            }
            var factory = factories[index];
            if (factory == null)
            {
                Debug.LogWarning($"[RemoteControl] StandardObjectFactory.CreateObject: factories[{index}] is null.");
                return;
            }

            if (_container == null)
            {
#if UNITY_2022_3_OR_NEWER
                var host = UnityEngine.Object.FindFirstObjectByType<RemoteControlBehaviour>();
#else
                var host = UnityEngine.Object.FindObjectOfType<RemoteControlBehaviour>();
#endif
                _container = host != null ? host.objectContainer : null;
                if (_container == null)
                {
                    Debug.LogError("[RemoteControl] StandardObjectFactory.CreateObject: ExposedObjectContainer not found.");
                    return;
                }
            }

            GameObjectUtility.SetCurrentUndoGroup("Create Object");
            GameObjectUtility.RecordObjectUndo(_container.host, "Create Object");

            IExposedObject created;
            try
            {
                created = factory.Create();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RemoteControl] StandardObjectFactory.CreateObject: Create() failed: {e.Message}");
                return;
            }

            if (created == null)
            {
                Debug.LogError("[RemoteControl] StandardObjectFactory.CreateObject: Create() returned null.");
                return;
            }

            created.name = _GenerateUniqueName(created.name);
            _container.AddExposedObject(created);
            created.OnEnable();
        }

        public override void DestroyObject(string objectId)
        {
            // persistentオブジェクトの削除を拒否
            if (_container != null && _container.IsPersistent(objectId))
            {
                Debug.LogWarning($"[RemoteControl] Cannot destroy persistent object: {objectId}");
                return;
            }

            GameObjectUtility.SetCurrentUndoGroup("Delete Object");

            if (_DisposeFromContainer(objectId))
                return;

            // コンテナに見つからなかった場合、ExposedObjectから直接探す
            if (ExposedObjectRegistry.TryFindById(objectId, out var exposed))
            {
                var target = exposed.target;
                GameObject go = null;
                if (target is GameObject g) go = g;
                else if (target is Component c) go = c.gameObject;

                exposed.Unregister();

                if (go != null)
                    GameObjectUtility.DestroyWithUndo(go);
            }
        }

        public override void RegisterPrefabs()
        {
            if (factories == null) return;
            for (int i = 0; i < factories.Length; i++)
            {
                factories[i]?.RegisterPrefabs();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Re-resolves the prefab GUID for each IExposedObjectFactory from its Asset.
        /// Invoked by UIDefinition.OnValidate.
        /// </summary>
        public void RefreshPrefabKeys()
        {
            if (factories == null) return;
            for (int i = 0; i < factories.Length; i++)
            {
                factories[i]?.RefreshPrefabKey();
            }
        }
#endif

        private string _GenerateUniqueName(string baseName)
        {
            if (_container == null) return baseName;

            var objects = _container.objects;
            var existingNames = new HashSet<string>();
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] == null) continue;
                existingNames.Add(objects[i].name);
            }

            if (!existingNames.Contains(baseName)) return baseName;

            int counter = 1;
            while (existingNames.Contains($"{baseName} ({counter})"))
                counter++;

            return $"{baseName} ({counter})";
        }

        private bool _DisposeFromContainer(string objectId)
        {
            if (_container == null) return false;

            GameObjectUtility.RecordObjectUndo(_container.host, "Delete Object");

            var objects = _container.objects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] == null) continue;
                if (objects[i].exposedObject?.id == objectId)
                {
                    var obj = objects[i];
                    obj.OnDispose();
                    _container.RemoveExposedObject(obj);

                    // Factoryに破棄を委譲
                    if (factories != null)
                    {
                        for (int j = 0; j < factories.Length; j++)
                        {
                            if (factories[j] != null)
                            {
                                factories[j].Destroy(obj);
                                break;
                            }
                        }
                    }
                    return true;
                }
            }
            return false;
        }
    }

}
