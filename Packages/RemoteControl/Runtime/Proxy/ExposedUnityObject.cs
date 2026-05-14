using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.Serialization;


namespace Lilium.RemoteControl
{
    //TODO: ExposedObject役割が違うのに名称がかぶるのはややこしいので、リネームするべき。
    public interface IExposedObject
    {
        string name { get; set; }
        ExposedObject exposedObject { get; }
        string id { get; }
        void OnEnable();
        void OnDisable();
        /// <summary>
        /// オブジェクトが完全に破棄される際に呼ばれるコールバック。
        /// OnDisableによるExposedObject解除に加えて、追加のリソース解放処理を行う。
        /// </summary>
        void OnDispose();
        void Update();
        void Reset();
    }

    [Serializable]
    public class ExposedUnityObjectBase : IExposedObject
    {
        public virtual string name { get; set; }

        public virtual Type referenceType { get; }

        public virtual UnityEngine.Object reference { get; }

        public virtual string id { get; }

        public ExposedObject exposedObject => _exposedObject;

        [NonSerialized]
        protected ExposedObject _exposedObject;

        /// <summary>
        /// このオブジェクトの生成元 Prefab の Asset GUID。
        /// Prefab からインスタンス化された場合に設定される。
        /// </summary>
        [NonSerialized]
        private string _prefabSourceKey;
        public string prefabSourceKey
        {
            get => _prefabSourceKey;
            set => _prefabSourceKey = value;
        }

        /// <summary>
        /// 親 ExposedObject の id。Unity hierarchy を真実として派生する。
        /// reference の Transform を起点に祖先方向へ辿り、最初に見つかった登録済み ExposedObject の id を返す。
        /// Transform を持たない reference (ScriptableObject 等) やルート (親なし) は null。
        /// シリアライズ時はメタデータ `@parent` として出力され、デシリアライズ時は Registry.SetParent で復元される。
        /// </summary>
        public string parentId
        {
            get
            {
                var tr = ExposedObjectRegistry.ExtractTransform(reference);
                return tr != null ? ExposedObjectRegistry.FindAncestorExposedId(tr) : null;
            }
        }

        public ExposedUnityObjectBase()
        {
        }

        public virtual void OnEnable()
        {
        }

        public virtual void OnDisable()
        {
        }

        public virtual void OnDispose()
        {
            OnDisable();
        }

        public virtual void Update()
        {
        }



        public virtual void Reset()
        {
        }

        public virtual void NoticeReset()
        {
            
        }

        public virtual void SetReference(UnityEngine.Object obj)
        {
        }

        /// <summary>
        /// ExposedObjectのIDを差し替える。
        /// Loadで保存済みIDに再登録する際に使用する。
        /// </summary>
        public virtual void ReplaceId(string newId)
        {
        }

        public virtual bool ResolveReferences(IExposedPropertyTable resolver)
        {
            return reference != null;
        }

        /// <summary>
        /// インスタンス単位で上書きするアイコン名を返す。nullまたは空ならクラス側のアイコンにフォールバックされる。
        /// シリアライズ時にメタデータ `@icon` としてRemoteAppへ送られる。
        /// </summary>
        public virtual string GetIconOverride() => null;
    }

    [Serializable]
    public abstract class ExposedUnityObject<T> : ExposedUnityObjectBase
         where T : UnityEngine.Object
    {
        [SerializeField, FormerlySerializedAs("_referenceName")]
        private string _id;

        [SerializeField]
        protected T _reference;

        private string _fallbackName;

        public override string name
        {
            get => _reference != null ? _reference.name : _fallbackName;
            set
            {
                if (_reference != null)
                {
                    _reference.name = value;
                }
                _fallbackName = value;
            }
        }

        public override string id => _id;

        public override Type referenceType => typeof(T);

        public override UnityEngine.Object reference => _reference;

        public ExposedUnityObject(T reference) : base()
        {
            _id = System.Guid.NewGuid().ToString();

            _reference = reference;

            // referenceがnullの場合はExposedObject生成しない
            // (Unity [SerializeReference]デシリアライズ時のデフォルトコンストラクタ呼び出し対策)
            if (reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create<T>(_reference, id);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (_reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create<T>(_reference, id);
            }
        }


        public override void OnDisable()
        {
            base.OnDisable();

            _exposedObject?.Unregister();
            _exposedObject = null;
        }

        public override void Update()
        {
            // Unity特化の更新処理があれば実装
        }


        public override void NoticeReset()
        {
            (_reference as Component)?.SendMessage("OnExposedReset");
        }


        public override void SetReference(UnityEngine.Object obj)
        {
            _reference = obj as T;
            _exposedObject = ExposedObjectRegistry.Create<T>(_reference, id);
        }

        public override void ReplaceId(string newId)
        {
            _exposedObject?.Unregister();
            _id = newId;
            if (_reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create<T>(_reference, id);
            }
        }

        public override bool ResolveReferences(IExposedPropertyTable resolver)
        {
            _exposedObject = ExposedObjectRegistry.Create<T>(_reference, id);

            return _reference != null;
        }
    }



    [Serializable]
    public abstract class ExposedUnityObjectProxy<U, T> : ExposedUnityObjectBase,
        IExposedSerializeCallback, IExposedDeserializeCallback
        where T : UnityEngine.Object
        where U : ExposedUnityObjectProxy<U, T>
    {

        [SerializeField]
        private string _id;

        [SerializeField]
        protected T _reference;

        private string _fallbackName;

        // Shadow Field for name. The Property getter returns the live reference
        // name so RemoteApp queries reflect Unity's current state, while
        // serialization reads/writes this field directly. OnBeforeExposedSerialize
        // refreshes _name from the live state before save; OnAfterExposedDeserialize
        // applies _name to _reference.name and _fallbackName after load.
        [ExposedField, Hide]
        [FormerlyExposedAs("name")]
        private string _name;

        [ExposedProperty]
        public override string name
        {
            get => _reference != null ? _reference.name : _fallbackName;
            set
            {
                _name = value;
                if (_reference != null)
                {
                    _reference.name = value;
                }
                _fallbackName = value;
            }
        }

        public virtual void OnBeforeExposedSerialize()
        {
            _name = _reference != null ? _reference.name : _fallbackName;
        }

        public virtual void OnAfterExposedDeserialize()
        {
            if (string.IsNullOrEmpty(_name)) return;
            if (_reference != null) _reference.name = _name;
            _fallbackName = _name;
        }

        public override string id => _id;

        public override Type referenceType => typeof(T);

        public override UnityEngine.Object reference => _reference;

        public ExposedUnityObjectProxy(T reference) : base()
        {
            _id = System.Guid.NewGuid().ToString();

            _reference = reference;

            // referenceがnullの場合はExposedObject生成しない
            // (Unity [SerializeReference]デシリアライズ時のデフォルトコンストラクタ呼び出し対策)
            if (reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create(GetType(), this, id);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (_reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create(GetType(), this, id);
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();

            _exposedObject?.Unregister();
            _exposedObject = null;
        }

        public override void Update()
        {
            // Unity特化の更新処理があれば実装
        }



        public override void Reset()
        {
        }

        public override void SetReference(UnityEngine.Object obj)
        {
            _reference = obj as T;
            _exposedObject = ExposedObjectRegistry.Create(GetType(), this, id);
        }

        public override void ReplaceId(string newId)
        {
            _exposedObject?.Unregister();
            _id = newId;
            if (_reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create(GetType(), this, id);
            }
        }

        public override bool ResolveReferences(IExposedPropertyTable resolver)
        {
            _exposedObject = ExposedObjectRegistry.Create(GetType(), this, id);

            return _reference != null;
        }

    }


    [System.Serializable]
    [ExposedClass("GameObject", Icon = "deployed_code")]
    public class ExposedGameObject : ExposedUnityObjectProxy<ExposedGameObject, GameObject>
    {
        static ExposedGameObject()
        {
            ExposedUnityObjectFactory.Register<GameObject>("GameObject", (gameObject) => new ExposedGameObject(gameObject));
        }


        [ExposedField, Hide]
        [FormerlyExposedAs("active")]
        private bool _active = true;

        [ExposedProperty]
        [Preserve]
        public bool active
        {
            get => _reference?.activeSelf ?? false;
            set
            {
                _active = value;
                _reference?.SetActive(value);
            }
        }

        //TODO: IEnumerable<Component> _components; でもいけるようににしたいが、現状ExposedObject側で配列しか対応していない
        // get-only derived Property だが、ここで列挙されたコンポーネントが scene.json トップレベルの
        // pending entry として個別 save される仕組み。
        // [InlineReference] は ExposedPropertyType.ctor 側で isPersistable=true 扱いになる。
        [ExposedProperty("components"), InlineReference]
        [Preserve]
        protected Component[] _components
        {
            get
            {
                if (_reference == null) return Array.Empty<Component>();
                var allComponents = _reference.GetComponents<Component>();
                var filtered = allComponents.Where(c => c != null && ExposedClass.Has(c.GetType())).ToArray();
                return filtered;
            }
        }


        public override void OnBeforeExposedSerialize()
        {
            base.OnBeforeExposedSerialize();
            if (_reference != null) _active = _reference.activeSelf;
        }

        public override void OnAfterExposedDeserialize()
        {
            base.OnAfterExposedDeserialize();
            if (_reference != null) _reference.SetActive(_active);
        }

        public override void NoticeReset()
        {
            if (_reference == null) return;
            _reference.BroadcastMessage("OnExposedChanged", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// GameObjectのアイコンを、ExposedClass登録済みの先頭コンポーネントのアイコンで上書きする。
        /// `_components` getterと同じ条件でフィルタすることで、RemoteApp一覧と一致させる。
        /// </summary>
        public override string GetIconOverride()
        {
            if (_reference == null) return null;
            var allComponents = _reference.GetComponents<Component>();
            for (int i = 0; i < allComponents.Length; i++)
            {
                var c = allComponents[i];
                if (c == null) continue;
                if (ExposedClass.TryGet(c.GetType(), out var cls) && !string.IsNullOrEmpty(cls.icon))
                {
                    return cls.icon;
                }
            }
            return null;
        }

        public ExposedGameObject() : base(null)
        {
        }

        public ExposedGameObject(GameObject reference) : base(reference)
        {
        }
    }


    /// <summary>
    /// Transform (position / rotation / scale) を常時公開するGameObjectプロキシ。
    /// Transform操作をRemoteAppから行いたい場合はこちらを使用する。
    /// 親 Transform を TransformRef で指定でき、scene 内の他 ExposedGameObjectWithTransform 配下に
    /// アタッチできる（avatar bone へのデコレーション装着など）。
    /// </summary>
    [System.Serializable]
    [ExposedClass("GameObjectWithTransform", Icon = "deployed_code")]
    public class ExposedGameObjectWithTransform : ExposedGameObject
    {
        // ExposedUnityObjectFactory.Register は呼ばない。
        // GameObject 型のデフォルト自動ラップ先は ExposedGameObject のまま維持する。

        /// <summary>
        /// アバター swap 等、外部システムが managed な子 GO を一時退避する間、
        /// <see cref="_OnHierarchyChanged"/> による <see cref="_parent"/> の silent な再同期を抑制する。
        /// 退避中に actualParent が ControllerGO 等に書き換わっても、ユーザーが
        /// 設定した TransformRef 値 (例: "Main Avatar/Head") を維持し、新アバター生成後の
        /// 構造変化通知 (<see cref="TransformStructureService"/>) で正しいボーンに再アタッチできるようにする。
        /// </summary>
        public static bool suspendHierarchySync;

        [SerializeField]
        [ExposedField(order = -20)]
        TransformRef _parent = new TransformRef();

        public TransformRef parent => _parent;

        [NonSerialized]
        Transform _attachedTransform;

        [ExposedField, Hide]
        [FormerlyExposedAs("transform")]
        private TransformValue _transform = TransformValue.identity;

        [ExposedProperty(order = -10)]
        public TransformValue transform
        {
            get => _transform;
            set
            {
                _transform = value;
                if (_reference != null) value.ApplyTo(_reference.transform);
            }
        }

        public override void OnBeforeExposedSerialize()
        {
            base.OnBeforeExposedSerialize();
            if (_reference != null) _transform = TransformValue.FromTransform(_reference.transform);
        }

        public override void OnAfterExposedDeserialize()
        {
            base.OnAfterExposedDeserialize();
            // Apply only on JSON load, not OnEnable. If we applied identity at
            // OnEnable, scene-placed objects with no JSON would teleport to the
            // origin. The setter still applies on direct SetProperty paths.
            if (_reference != null) _transform.ApplyTo(_reference.transform);
        }

        public ExposedGameObjectWithTransform() : base(null)
        {
        }

        public ExposedGameObjectWithTransform(GameObject reference) : base(reference)
        {
        }

        public override void OnEnable()
        {
            base.OnEnable();

            _parent.SetSelf(this);

            if (_reference != null
                && _parent.isEmpty
                && _reference.transform.parent != null)
            {
                _parent.InitFromTransform(_reference.transform.parent);
            }

            _parent.onChanged += _OnParentChanged;
            TransformStructureService.onStructureChanged += _OnStructureChanged;
            GameObjectUtility.RegisterHierarchyChanged(_OnHierarchyChanged);
            _UpdateAttachment();
            // 自身が新しい root として登場したことを通知し、別の宿主が ownerName でこの GO を
            // 参照していたら再 attach を促す。
            TransformStructureService.NotifyStructureChanged(_reference);
        }

        public override void OnDisable()
        {
            // 自身が root から外れたことを通知し、依存先の宿主に再解決を促す。
            TransformStructureService.NotifyStructureChanged(_reference);

            base.OnDisable();

            GameObjectUtility.UnregisterHierarchyChanged(_OnHierarchyChanged);
            _parent.onChanged -= _OnParentChanged;
            TransformStructureService.onStructureChanged -= _OnStructureChanged;
        }

        void _OnParentChanged() => _UpdateAttachment();

        /// <summary>
        /// owner GameObject の内部 hierarchy 変化通知を受けて、ownerName 一致時に再 attach する。
        /// avatar swap など、参照先 Transform の resolve 結果が変わる可能性のあるイベントを拾う。
        /// </summary>
        void _OnStructureChanged(GameObject owner)
        {
            if (owner == null) return;
            if (_parent.ownerName != owner.name) return;
            _UpdateAttachment();
        }

        /// <summary>
        /// Unity hierarchy の変更通知を受け、実際の Transform.parent と TransformRef の保持する
        /// desired state にズレがある場合は TransformRef を silent に同期する
        /// （ユーザーの Editor ドラッグを TransformAttachment.Attach で revert してしまうのを防ぐ）。
        /// </summary>
        void _OnHierarchyChanged()
        {
            if (_reference == null) return;
            // suspendHierarchySync: アバター swap 等、外部システムが「一時退避」目的で
            // 子 GO を移し替える間は user-set TransformRef 値を保持する必要がある。
            // この期間は actualParent への silent な再同期を抑制する。
            if (suspendHierarchySync) return;
            var actualParent = _reference.transform.parent;
            if (actualParent == _attachedTransform) return;
            _parent.InitFromTransform(actualParent, silent: true);
            // 次回の Attach で同じ親への余分な SetParent を避けるためキャッシュも同期する。
            _attachedTransform = actualParent;
        }

        void _UpdateAttachment()
        {
            if (_reference == null) return;
            TransformAttachment.Attach(_parent, _reference.transform, ref _attachedTransform);
        }
    }


    [Serializable]
    public abstract class ExposedUnityObjectReference<U, T> : ExposedUnityObjectBase,
        IExposedSerializeCallback, IExposedDeserializeCallback
        where T : UnityEngine.Object
        where U : ExposedUnityObjectReference<U, T>
    {

        [SerializeField]
        private string _id;

        [SerializeField]
        protected T _reference;

        private string _fallbackName;

        // Shadow Field for name. See ExposedUnityObjectProxy for the same pattern.
        [ExposedField, Hide]
        [FormerlyExposedAs("name")]
        private string _name;

        [ExposedProperty]
        public override string name
        {
            get => _reference != null ? _reference.name : _fallbackName;
            set
            {
                _name = value;
                if (_reference != null)
                {
                    _reference.name = value;
                }
                _fallbackName = value;
            }
        }

        public virtual void OnBeforeExposedSerialize()
        {
            _name = _reference != null ? _reference.name : _fallbackName;
        }

        public virtual void OnAfterExposedDeserialize()
        {
            if (string.IsNullOrEmpty(_name)) return;
            if (_reference != null) _reference.name = _name;
            _fallbackName = _name;
        }

        public override string id => _id;

        public override Type referenceType => typeof(T);

        public override UnityEngine.Object reference => _reference;

        public ExposedUnityObjectReference(T reference) : base()
        {
            _id = System.Guid.NewGuid().ToString();

            _reference = reference;

            // referenceがnullの場合はExposedObject生成しない
            // (Unity [SerializeReference]デシリアライズ時のデフォルトコンストラクタ呼び出し対策)
            if (reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (_reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();

            _exposedObject?.Unregister();
            _exposedObject = null;
        }

        public override void Update()
        {
            // Unity特化の更新処理があれば実装
        }



        public override void Reset()
        {
        }

        public override void ReplaceId(string newId)
        {
            _exposedObject?.Unregister();
            _id = newId;
            if (_reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
        }

        public override bool ResolveReferences(IExposedPropertyTable resolver)
        {
            if (_reference != null)
            {
                _exposedObject = ExposedObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
            else
            {
                _exposedObject = null;
            }


            return _reference != null;
        }
    }


    [System.Serializable]
    [ExposedClass("Asset", Icon = "insert_drive_file")]
    public class ExposedAsset : ExposedUnityObjectReference<ExposedAsset, ScriptableObject>
    {
        static ExposedAsset()
        {
            ExposedUnityObjectFactory.Register<ScriptableObject>("Asset", (asset) => new ExposedAsset(asset));
        }

        [ExposedProperty]
        public ScriptableObject asset
        {
            get => _reference;
            set => _reference = value;
        }

        public override void NoticeReset()
        {
            if (_reference == null) return;
        }

        public ExposedAsset() : base(null)
        {
        }

        public ExposedAsset(ScriptableObject reference) : base(reference)
        {
        }
    }


    [System.Serializable]
    [ExposedClass("Component", Icon = "extension")]
    public class ExposedComponent : ExposedUnityObjectReference<ExposedComponent, Component>
    {
        static ExposedComponent()
        {
            ExposedUnityObjectFactory.Register<Component>("Component", (component) => new ExposedComponent(component));
        }

        public override void NoticeReset()
        {
            if (_reference == null) return;
            _reference.SendMessage("OnExposedChanged", SendMessageOptions.DontRequireReceiver);
        }

        public ExposedComponent() : base(null)
        {
        }

        public ExposedComponent(Component reference) : base(reference)
        {
        }
    }
}