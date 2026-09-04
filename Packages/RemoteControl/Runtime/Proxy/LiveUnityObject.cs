using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;


namespace Lilium.RemoteControl
{
    // 実体 (GameObject / Component / ScriptableObject など) を公開オブジェクトとして
    // ラップする wrapper の共通インターフェース。値型ハンドルである LiveObjectHandle
    // (旧 ExposedObjectHandle) とは役割が異なる。
    public interface ILiveObject
    {
        string name { get; set; }
        LiveObjectHandle? liveObject { get; }
        string id { get; }
        void OnEnable();
        void OnDisable();
        /// <summary>
        /// オブジェクトが完全に破棄される際に呼ばれるコールバック。
        /// OnDisableによるLiveObject解除に加えて、追加のリソース解放処理を行う。
        /// </summary>
        void OnDispose();
        void Update();
        void Reset();
    }

    [Serializable]
    public class LiveUnityObjectBase : ILiveObject, Frames.ILiveMadeFromRecipe
    {
        public virtual string name { get; set; }

        public virtual Type referenceType { get; }

        public virtual UnityEngine.Object reference { get; }

        public virtual string id { get; }

        public LiveObjectHandle? liveObject => _liveObject;

        [NonSerialized]
        protected LiveObjectHandle? _liveObject;

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
        /// How a replay makes this again: the prefab it came from, or null for something that was in
        /// the scene to begin with.
        ///
        /// The same key a saved scene writes as <c>@prefab</c>, deliberately. An object that a scene
        /// file can rebuild is one a recording can rebuild, and giving the two different answers
        /// would mean a replay standing up a world the save could not.
        /// </summary>
        public string recipeKey => _prefabSourceKey;

        /// <summary>
        /// 親 LiveObjectHandle の id。Unity hierarchy を真実として派生する。
        /// reference の Transform を起点に祖先方向へ辿り、最初に見つかった登録済み LiveObjectHandle の id を返す。
        /// Transform を持たない reference (ScriptableObject 等) やルート (親なし) は null。
        /// シリアライズ時はメタデータ `@parent` として出力され、デシリアライズ時は Registry.SetParent で復元される。
        /// </summary>
        public string parentId
        {
            get
            {
                var tr = LiveObjectRegistry.ExtractTransform(reference);
                return tr != null ? LiveObjectRegistry.FindAncestorLiveId(tr) : null;
            }
        }

        public LiveUnityObjectBase()
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
        /// LiveObjectのIDを差し替える。
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
    public abstract class LiveUnityObject<T> : LiveUnityObjectBase
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

        public LiveUnityObject(T reference) : base()
        {
            _id = System.Guid.NewGuid().ToString();

            _reference = reference;

            // referenceがnullの場合はLiveObject生成しない
            // (Unity [SerializeReference]デシリアライズ時のデフォルトコンストラクタ呼び出し対策)
            if (reference != null)
            {
                _liveObject = LiveObjectRegistry.Create<T>(_reference, id);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (_reference != null)
            {
                _liveObject = LiveObjectRegistry.Create<T>(_reference, id);
            }
        }


        public override void OnDisable()
        {
            base.OnDisable();

            _liveObject?.Unregister();
            _liveObject = null;
        }

        public override void Update()
        {
            // Unity特化の更新処理があれば実装
        }


        public override void NoticeReset()
        {
            (_reference as Component)?.SendMessage("OnLiveReset");
        }


        public override void SetReference(UnityEngine.Object obj)
        {
            _reference = obj as T;
            _liveObject = LiveObjectRegistry.Create<T>(_reference, id);
        }

        public override void ReplaceId(string newId)
        {
            _liveObject?.Unregister();
            _id = newId;
            if (_reference != null)
            {
                _liveObject = LiveObjectRegistry.Create<T>(_reference, id);
            }
        }

        public override bool ResolveReferences(IExposedPropertyTable resolver)
        {
            _liveObject = LiveObjectRegistry.Create<T>(_reference, id);

            return _reference != null;
        }
    }



    [Serializable]
    public abstract class LiveUnityObjectProxy<U, T> : LiveUnityObjectBase,
        ILiveSerializeCallback, ILiveDeserializeCallback
        where T : UnityEngine.Object
        where U : LiveUnityObjectProxy<U, T>
    {

        [SerializeField]
        private string _id;

        [SerializeField]
        protected T _reference;

        private string _fallbackName;

        // Shadow Field for name. The Property getter returns the live reference
        // name so RemoteApp queries reflect Unity's current state, while
        // serialization reads/writes this field directly. OnBeforeLiveSerialize
        // refreshes _name from the live state before save; OnAfterLiveDeserialize
        // applies _name to _reference.name and _fallbackName after load.
        [LiveField(lane = FrameLane.State, textCapacity = 128), Hide]
        [FormerlyNamedAs("name")]
        private string _name;

        [LiveProperty]
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

        public virtual void OnBeforeLiveSerialize()
        {
            _name = _reference != null ? _reference.name : _fallbackName;
        }

        public virtual void OnAfterLiveDeserialize()
        {
            if (string.IsNullOrEmpty(_name)) return;
            if (_reference != null) _reference.name = _name;
            _fallbackName = _name;
        }

        public override string id => _id;

        public override Type referenceType => typeof(T);

        public override UnityEngine.Object reference => _reference;

        public LiveUnityObjectProxy(T reference) : base()
        {
            _id = System.Guid.NewGuid().ToString();

            _reference = reference;

            // referenceがnullの場合はLiveObject生成しない
            // (Unity [SerializeReference]デシリアライズ時のデフォルトコンストラクタ呼び出し対策)
            if (reference != null)
            {
                _liveObject = LiveObjectRegistry.Create(GetType(), this, id);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (_reference != null)
            {
                _liveObject = LiveObjectRegistry.Create(GetType(), this, id);
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();

            _liveObject?.Unregister();
            _liveObject = null;
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
            _liveObject = LiveObjectRegistry.Create(GetType(), this, id);
        }

        public override void ReplaceId(string newId)
        {
            _liveObject?.Unregister();
            _id = newId;
            if (_reference != null)
            {
                _liveObject = LiveObjectRegistry.Create(GetType(), this, id);
            }
        }

        public override bool ResolveReferences(IExposedPropertyTable resolver)
        {
            _liveObject = LiveObjectRegistry.Create(GetType(), this, id);

            return _reference != null;
        }

    }


    [System.Serializable]
    [LiveClass("GameObject", Icon = "deployed_code")]
    [MovedFrom(false, null, null, "ExposedGameObject")]
    public class LiveGameObject : LiveUnityObjectProxy<LiveGameObject, GameObject>
    {
        static LiveGameObject()
        {
            LiveUnityObjectFactory.Register<GameObject>("GameObject", (gameObject) => new LiveGameObject(gameObject));
        }


        [LiveField, Hide]
        [FormerlyNamedAs("active")]
        private bool _active = true;

        [LiveProperty]
        [Preserve]
        public bool active
        {
            // Use the Unity-overloaded == (not the C# ?. operator): a destroyed UnityEngine.Object is
            // "fake null" — not C# null — so _reference?.activeSelf would NOT short-circuit and would
            // throw MissingReferenceException. Matches the _reference == null guard used elsewhere.
            get => _reference != null ? _reference.activeSelf : false;
            set
            {
                _active = value;
                if (_reference != null) _reference.SetActive(value);
            }
        }

        //TODO: IEnumerable<Component> _components; でもいけるようににしたいが、現状LiveObject側で配列しか対応していない
        // get-only derived Property だが、ここで列挙されたコンポーネントが scene.json トップレベルの
        // pending entry として個別 save される仕組み。
        // [InlineReference] は LivePropertyType.ctor 側で isPersistable=true 扱いになる。
        [LiveProperty("components"), InlineReference]
        [Preserve]
        protected Component[] _components
        {
            get
            {
                if (_reference == null) return Array.Empty<Component>();
                // LINQ (Where().ToArray()) は WhereArrayIterator + 中間配列を確保する。この getter は
                // キー/添字プロパティ解決 (LiveProperty) から毎フレーム引かれ得るため、手書きの 2 パスで
                // 走査して結果配列 1 個だけを確保する (保持バッファは持たない)。全要素が対象なら
                // GetComponents の配列 (毎回新規) をそのまま返す。
                var allComponents = _reference.GetComponents<Component>();
                int count = 0;
                for (int i = 0; i < allComponents.Length; i++)
                {
                    var c = allComponents[i];
                    if (c != null && LiveClass.Has(c.GetType())) count++;
                }
                if (count == 0) return Array.Empty<Component>();
                if (count == allComponents.Length) return allComponents;

                var filtered = new Component[count];
                int w = 0;
                for (int i = 0; i < allComponents.Length; i++)
                {
                    var c = allComponents[i];
                    if (c != null && LiveClass.Has(c.GetType())) filtered[w++] = c;
                }
                return filtered;
            }
        }


        public override void OnBeforeLiveSerialize()
        {
            base.OnBeforeLiveSerialize();
            if (_reference != null) _active = _reference.activeSelf;
        }

        public override void OnAfterLiveDeserialize()
        {
            base.OnAfterLiveDeserialize();
            if (_reference != null) _reference.SetActive(_active);
        }

        public override void NoticeReset()
        {
            if (_reference == null) return;
            _reference.BroadcastMessage("OnLiveChanged", SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>
        /// GameObjectのアイコンを、LiveClass登録済みの先頭コンポーネントのアイコンで上書きする。
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
                if (LiveClass.TryGet(c.GetType(), out var cls) && !string.IsNullOrEmpty(cls.icon))
                {
                    return cls.icon;
                }
            }
            return null;
        }

        public LiveGameObject() : base(null)
        {
        }

        public LiveGameObject(GameObject reference) : base(reference)
        {
        }
    }


    /// <summary>
    /// Transform (position / rotation / scale) を常時公開するGameObjectプロキシ。
    /// Transform操作をRemoteAppから行いたい場合はこちらを使用する。
    /// 親 Transform を TransformRef で指定でき、scene 内の他 LiveGameObjectWithTransform 配下に
    /// アタッチできる（avatar bone へのデコレーション装着など）。
    /// </summary>
    [System.Serializable]
    [LiveClass("GameObjectWithTransform", Icon = "deployed_code")]
    [MovedFrom(false, null, null, "ExposedGameObjectWithTransform")]
    public partial class LiveGameObjectWithTransform : LiveGameObject
    {
        // LiveUnityObjectFactory.Register は呼ばない。
        // GameObject 型のデフォルト自動ラップ先は LiveGameObject のまま維持する。

        /// <summary>
        /// アバター swap 等、外部システムが managed な子 GO を一時退避する間、
        /// <see cref="_OnHierarchyChanged"/> による <see cref="_parent"/> の silent な再同期を抑制する。
        /// 退避中に actualParent が ControllerGO 等に書き換わっても、ユーザーが
        /// 設定した TransformRef 値 (例: "Main Avatar/Head") を維持し、新アバター生成後の
        /// 構造変化通知 (<see cref="TransformStructureService"/>) で正しいボーンに再アタッチできるようにする。
        /// </summary>
        public static bool suspendHierarchySync;

        [SerializeField]
        [LiveField(order = -20)]
        TransformRef _parent = new TransformRef();

        public TransformRef parent => _parent;

        [NonSerialized]
        Transform _attachedTransform;

        // Shadow Field for transform, and the value persistence reads and writes. The Property
        // getter returns the live Transform instead, the same way name does: what a frame carries
        // and what RemoteApp shows have to be where the object actually is, not where it was last
        // written to. OnBeforeLiveSerialize refreshes this from the live state before save.
        //
        // On the state lane: a gizmo drag writes this many times a second, and animation and
        // parenting move it without any write at all -- neither of which the event lane can carry
        // (the first would cost a record per frame, the second leaves no event to record).
        [LiveField(lane = FrameLane.State), Hide]
        [FormerlyNamedAs("transform")]
        private TransformValue _transform = TransformValue.identity;

        [LiveProperty(order = -10)]
        public TransformValue transform
        {
            get => _reference != null ? TransformValue.FromTransform(_reference.transform) : _transform;
            set
            {
                _transform = value;
                if (_reference != null) value.ApplyTo(_reference.transform);
            }
        }

        public override void OnBeforeLiveSerialize()
        {
            base.OnBeforeLiveSerialize();
            if (_reference != null) _transform = TransformValue.FromTransform(_reference.transform);
        }

        public override void OnAfterLiveDeserialize()
        {
            base.OnAfterLiveDeserialize();
            // Apply only on JSON load, not OnEnable. If we applied identity at
            // OnEnable, scene-placed objects with no JSON would teleport to the
            // origin. The setter still applies on direct SetProperty paths.
            if (_reference != null) _transform.ApplyTo(_reference.transform);
        }

        public LiveGameObjectWithTransform() : base(null)
        {
        }

        public LiveGameObjectWithTransform(GameObject reference) : base(reference)
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
    public abstract class LiveUnityObjectReference<U, T> : LiveUnityObjectBase,
        ILiveSerializeCallback, ILiveDeserializeCallback
        where T : UnityEngine.Object
        where U : LiveUnityObjectReference<U, T>
    {

        [SerializeField]
        private string _id;

        [SerializeField]
        protected T _reference;

        private string _fallbackName;

        // Shadow Field for name. See LiveUnityObjectProxy for the same pattern.
        [LiveField(lane = FrameLane.State, textCapacity = 128), Hide]
        [FormerlyNamedAs("name")]
        private string _name;

        [LiveProperty]
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

        public virtual void OnBeforeLiveSerialize()
        {
            _name = _reference != null ? _reference.name : _fallbackName;
        }

        public virtual void OnAfterLiveDeserialize()
        {
            if (string.IsNullOrEmpty(_name)) return;
            if (_reference != null) _reference.name = _name;
            _fallbackName = _name;
        }

        public override string id => _id;

        public override Type referenceType => typeof(T);

        public override UnityEngine.Object reference => _reference;

        public LiveUnityObjectReference(T reference) : base()
        {
            _id = System.Guid.NewGuid().ToString();

            _reference = reference;

            // referenceがnullの場合はLiveObject生成しない
            // (Unity [SerializeReference]デシリアライズ時のデフォルトコンストラクタ呼び出し対策)
            if (reference != null)
            {
                _liveObject = LiveObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (_reference != null)
            {
                _liveObject = LiveObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
        }

        public override void OnDisable()
        {
            base.OnDisable();

            _liveObject?.Unregister();
            _liveObject = null;
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
            _liveObject?.Unregister();
            _id = newId;
            if (_reference != null)
            {
                _liveObject = LiveObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
        }

        public override bool ResolveReferences(IExposedPropertyTable resolver)
        {
            if (_reference != null)
            {
                _liveObject = LiveObjectRegistry.Create(_reference.GetType(), _reference, id);
            }
            else
            {
                _liveObject = null;
            }


            return _reference != null;
        }
    }


    [System.Serializable]
    [LiveClass("Asset", Icon = "insert_drive_file")]
    [MovedFrom(false, null, null, "ExposedAsset")]
    public class LiveAsset : LiveUnityObjectReference<LiveAsset, ScriptableObject>
    {
        static LiveAsset()
        {
            LiveUnityObjectFactory.Register<ScriptableObject>("Asset", (asset) => new LiveAsset(asset));
        }

        [LiveProperty]
        public ScriptableObject asset
        {
            get => _reference;
            set => _reference = value;
        }

        public override void NoticeReset()
        {
            if (_reference == null) return;
        }

        public LiveAsset() : base(null)
        {
        }

        public LiveAsset(ScriptableObject reference) : base(reference)
        {
        }
    }


    [System.Serializable]
    [LiveClass("Component", Icon = "extension")]
    [MovedFrom(false, null, null, "ExposedComponent")]
    public class LiveComponent : LiveUnityObjectReference<LiveComponent, Component>
    {
        static LiveComponent()
        {
            LiveUnityObjectFactory.Register<Component>("Component", (component) => new LiveComponent(component));
        }

        public override void NoticeReset()
        {
            if (_reference == null) return;
            _reference.SendMessage("OnLiveChanged", SendMessageOptions.DontRequireReceiver);
        }

        public LiveComponent() : base(null)
        {
        }

        public LiveComponent(Component reference) : base(reference)
        {
        }
    }
}