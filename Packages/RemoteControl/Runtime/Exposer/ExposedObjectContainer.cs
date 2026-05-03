// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// IExposedObjectのリストをScene上のMonoBehaviourとして保持するコンテナ。
    /// ランタイムでのオブジェクト追加/削除がSceneの変更に留まり、再生終了時に自動的にリセットされる。
    /// </summary>
    [ExposedClass("ObjectContainer", Icon = "widgets", HideInScene = true)]
    [DefaultExecutionOrder(-32760)]
    [ExecuteAlways]
    public class ExposedObjectContainer : MonoBehaviour, IExposedObjectResolver
    {
        [ExposedProperty("name")]
        public string exposedName => gameObject.name;

        public IReadOnlyList<IExposedObject> objects => _objects;


        [SerializeReference, Select]
        [ExposedField(persistable = false)]
        public List<IExposedObject> _objects = new List<IExposedObject>();

        [NonSerialized]
        private ExposedObject _selfExposedObject;

        [NonSerialized]
        private HashSet<string> _persistentIds = new HashSet<string>();

        /// <summary>
        /// 指定IDのオブジェクトがシーン初期配置（persistent）かどうかを返す。
        /// </summary>
        public bool IsPersistent(string id)
        {
            return _persistentIds.Contains(id);
        }

        // --- ライフサイクル（RemoteControlProviderから呼び出す） ---

        const string kObjectContainerId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        void OnEnable()
        {
            if (_selfExposedObject == null)
            {
                Initialize();
            }
        }

        void OnDisable()
        {
        }

        public void Initialize()
        {
            _selfExposedObject = ExposedObjectRegistry.Create<ExposedObjectContainer>(this, kObjectContainerId);

            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                obj.OnEnable();
            }

            // ObjectContainer自身のデフォルト値をキャプチャ（_objectsリストの変更検出に必要）
            ExposedPropertyUtility.SetDefault(_selfExposedObject);

            // 全ExposedObjectのデフォルト値をキャプチャ（比較ベースdirty判定に必要）
            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                var exposedObj = obj.exposedObject;
                if (exposedObj != null)
                {
                    ExposedPropertyUtility.SetDefault(exposedObj);
                }
            }

            // Initialize時点のオブジェクトをpersistentとしてマーク
            _persistentIds.Clear();
            foreach (var obj in _objects)
            {
                if (obj?.exposedObject != null && obj.exposedObject.hasId)
                    _persistentIds.Add(obj.exposedObject.id);
            }

            // inline の UnityEngine.Object 参照（コンポーネント等）もデフォルト登録する。
            // Delta 保存時、pending エントリは target 参照でキャプチャ済みの defaults を使って
            // 差分計算する。ここで defaults を登録しないと pending は常に metadata-only 扱いになり、
            // 実際の変更が保存されない。
            var reachable = ExposedSceneSerializer.ResolveExposedObjects(objects, this);
            foreach (var exposed in reachable)
            {
                if (exposed == null || exposed.hasId) continue; // hasId は既に上で処理済み
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

        // --- オブジェクト管理 ---

        public void AddExposedObject(IExposedObject exposedObject)
        {
            _objects.Add(exposedObject);
        }

        public void RemoveExposedObject(IExposedObject exposedObject)
        {
            _objects.Remove(exposedObject);
        }

        public void RemoveExposedObjectById(string id)
        {
            var obj = _objects.FirstOrDefault(x => x.id == id);
            if (obj != null)
            {
                _objects.Remove(obj);
            }
        }

        public bool HasExposedObject(string id)
        {
            return _objects.Any(x => x.id == id);
        }

        public void RebindExposedObject(string id, UnityEngine.Object obj, IExposedPropertyTable resolver)
        {
            var data = _objects.FirstOrDefault(x => x.id == id);
            if (data != null)
            {
                data.OnDisable();
                if (obj != null && data is ExposedUnityObjectBase unityObj)
                {
                    unityObj.ResolveReferences(resolver);
                }
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
            Debug.Log($"[RemoteControl] Reset all {gameObject.name} container to default values.");
        }

        public void ResolveAllReferences(IExposedPropertyTable resolver)
        {
            foreach (var obj in _objects)
            {
                if (obj == null) continue;
                if (obj is ExposedUnityObjectBase unityObj)
                {
                    unityObj.ResolveReferences(resolver);
                }
            }
        }

        // --- IExposedObjectResolver ---

        public ExposedObject FindById(string id)
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] == null) continue;

                if (_objects[i].id == id)
                {
                    return _objects[i].exposedObject;
                }
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

                // UnityEngine.Object同士の比較（ExposedUnityObjectBaseの場合のみ）
                if (targetUnityObj != null && _objects[i] is ExposedUnityObjectBase u && u.reference == targetUnityObj)
                {
                    return _objects[i].exposedObject;
                }
            }

            return ExposedObjectRegistry.FindByTarget(target);
        }

    }
}
