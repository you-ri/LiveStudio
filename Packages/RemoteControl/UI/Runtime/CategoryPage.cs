// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.LiveScene;

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
    [LiveClass]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class ObjectSelectorBase : IObjectSelector
    {
        [LiveProperty]
        public object[] objects => GetObjects();

        protected virtual object[] GetObjects() => new object[0];
    }

    public interface IObjectFactory
    {
        object[] objects { get; }
        string[] objectNames { get; }

        void CreateObject(int index);

        void DestroyObject(string objectId);
    }

    [Serializable]
    [LiveClass(HideInScene = true)]
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI")]
    public class ObjectFactoryBase : IObjectFactory
    {
        [NonSerialized]
        protected LiveObjectContainer _container;

        /// <summary>
        /// Category of the page this factory belongs to, or empty for a page that offers every
        /// declared prefab (the scene page). Handed in by the host rather than authored here: the
        /// page already says which category it shows, and writing it twice is a way for the "+" and
        /// the list under it to disagree.
        /// </summary>
        [NonSerialized]
        protected string _category;

        public object[] objects => GetObjects();

        [LiveProperty]
        public string[] objectNames => GetObjectNames();

        protected virtual object[] GetObjects() => new object[0];
        protected virtual string[] GetObjectNames() => new string[0];

        [LiveProperty]
        public int[] objectAccessLevels => GetObjectAccessLevels();

        protected virtual int[] GetObjectAccessLevels() => new int[0];

        public virtual void Initialize(LiveObjectContainer container)
        {
            Initialize(container, null);
        }

        /// <inheritdoc cref="Initialize(LiveObjectContainer)"/>
        /// <param name="category">
        /// Category of the hosting page; empty offers every declared prefab.
        /// </param>
        public virtual void Initialize(LiveObjectContainer container, string category)
        {
            _container = container;
            _category = category;
        }

        /// <summary>
        /// Stands up the maker at <paramref name="index"/>. Kept out of the live data on purpose.
        ///
        /// What this produces is carried by the inventory instead: a spawned object names the
        /// prefab it came from, and the structure lane creates it again under the id it was
        /// recorded with. Recording the press as well would stand up a second one -- this call
        /// takes an index, not an id, so replaying it mints a fresh id that no recorded value
        /// addresses, next to the one the inventory already rebuilt. It is also an index into a
        /// list that grows and shrinks with what is loaded, so it does not mean the same thing in
        /// the next run the way an id does.
        ///
        /// ⚠ The inventory only carries what a recipe can rebuild (see <c>ILiveMadeFromRecipe</c>).
        /// A maker that produces something with no recipe key would have its spawn recorded
        /// nowhere; every one of them names its prefab today, and a new one has to.
        /// </summary>
        [LiveFunction(lane = FrameLane.None)]
        public virtual void CreateObject(int index) { }

        /// <summary>
        /// Takes away the object with this id.
        ///
        /// Recorded, unlike <see cref="CreateObject"/>: it is addressed by an id that means the same
        /// thing on replay, and applying it twice is harmless (the second finds nothing). It also
        /// reaches further than the inventory can -- the structure lane only takes away what it
        /// stood up itself, so a delete of something that was in the scene before the take began is
        /// carried by this alone.
        /// </summary>
        [LiveFunction]
        public virtual void DestroyObject(string objectId) { }
    }

    /// <summary>
    /// UI page definition.
    /// Corresponds to the CategoryPage on the RemoteApp side.
    /// </summary>
    [Serializable]
    [LiveClass]
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
            var list = LiveObjectRegistry.FindByCategory(category);
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
        /// <summary>
        /// The makers this page offers, from the prefabs declared by the applied live class assets
        /// (<see cref="LivePrefabCatalog"/>).
        ///
        /// Read on every call rather than cached: an asset carried in by a bundle adds its prefabs
        /// while running, and a list captured once would go on offering what has been unloaded.
        /// </summary>
        protected List<ILiveObjectFactory> _Factories() => LivePrefabCatalog.FactoriesFor(_category);

        protected override object[] GetObjects()
        {
            var factories = _Factories();
            var result = new object[factories.Count];
            for (int i = 0; i < factories.Count; i++)
                result[i] = factories[i];
            return result;
        }

        protected override string[] GetObjectNames()
        {
            var factories = _Factories();
            var names = new string[factories.Count];
            for (int i = 0; i < factories.Count; i++)
                names[i] = factories[i]?.name ?? "";
            return names;
        }

        protected override int[] GetObjectAccessLevels()
        {
            var factories = _Factories();
            var levels = new int[factories.Count];
            for (int i = 0; i < factories.Count; i++)
                levels[i] = (int)(factories[i]?.accessLevel ?? AccessLevel.Public);
            return levels;
        }

        public override void CreateObject(int index)
        {
            var factories = _Factories();
            if (index < 0 || index >= factories.Count)
            {
                Debug.LogWarning($"[RemoteControl] StandardObjectFactory.CreateObject: invalid index {index} (factories={factories.Count}).");
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
                    Debug.LogError("[RemoteControl] StandardObjectFactory.CreateObject: LiveObjectContainer not found.");
                    return;
                }
            }

            GameObjectUtility.SetCurrentUndoGroup("Create Object");
            GameObjectUtility.RecordObjectUndo(_container.host, "Create Object");

            ILiveObject created;
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
            _container.AddLiveObject(created);
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

            // コンテナに見つからなかった場合、LiveObjectから直接探す
            if (LiveObjectRegistry.TryFindById(objectId, out var exposed))
            {
                var target = exposed.target;
                GameObject go = null;
                if (target is GameObject g) go = g;
                else if (target is Component c) go = c.gameObject;
                // A registered target is usually the wrapper, not the scene object it drives, so
                // ask it for what it wraps. Without this the object is unregistered and its
                // GameObject left standing -- a delete that only half happens.
                else if (target is LiveUnityObjectBase u && u.reference != null)
                {
                    if (u.reference is GameObject wg) go = wg;
                    else if (u.reference is Component wc) go = wc.gameObject;
                }

                exposed.Unregister();

                if (go != null)
                    GameObjectUtility.DestroyWithUndo(go);
            }
        }

        /// <summary>
        /// Destroys the GameObject behind a live object, which is what every
        /// <see cref="ILiveObjectFactory.Destroy"/> does. Used when no factory is on hand to
        /// delegate to, so a deleted object never survives as an orphaned GameObject.
        /// </summary>
        private static void _DestroyGameObjectOf(ILiveObject obj)
        {
            if (!(obj is LiveUnityObjectBase u) || u.reference == null) return;

            GameObject go = null;
            if (u.reference is GameObject g) go = g;
            else if (u.reference is Component c) go = c.gameObject;

            if (go != null)
                GameObjectUtility.DestroyWithUndo(go);
        }

        protected string _GenerateUniqueName(string baseName)
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

            // Every object the container can reach, not only the ones in its own list: an object
            // that stood up from a live scene was appended to the list of the container that
            // restored it, which arrives here as a source. Looking only at the main list made such
            // an object undeletable -- it fell through to the id lookup below, which unregistered
            // it while its GameObject and its entry in the source list stayed put, so it came back
            // in the very next listing.
            ILiveObject obj = null;
            foreach (var candidate in _container.EnumerateAllObjects())
            {
                if (candidate == null) continue;
                if (candidate.liveObject?.id != objectId) continue;
                obj = candidate;
                break;
            }
            if (obj == null) return false;

            // Undo records the object that serializes the list being changed, which is the host for
            // the main list and the source's owner for a merged one.
            GameObjectUtility.RecordObjectUndo(
                _container.FindSerializedOwner(obj) ?? _container.host, "Delete Object");

            obj.OnDispose();
            _container.RemoveLiveObjectAnywhere(obj);

            // Factoryに破棄を委譲。宣言されたプレハブが 1 つも無い場合でも取り残さないよう、
            // 委譲先が無ければ GameObject を直接破棄する (Factory の Destroy と同じ処理)。
            var factories = _Factories();
            if (factories.Count > 0 && factories[0] != null)
                factories[0].Destroy(obj);
            else
                _DestroyGameObjectOf(obj);
            return true;
        }
    }

}
