// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedObjectの静的レジストリ。
    /// インスタンスの登録・検索・ファクトリ機能を提供する。
    /// </summary>
    public static class ExposedObjectRegistry
    {
        /// <summary>
        /// 参照等価比較器（Unity 2021互換）
        /// </summary>
        internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        // --- ストレージ ---

        private static readonly HashSet<ExposedObject> _instances = new HashSet<ExposedObject>();

        private static readonly Dictionary<string, ExposedObject> _byId = new Dictionary<string, ExposedObject>();

        private static readonly Dictionary<object, ExposedObject> _byTarget = new Dictionary<object, ExposedObject>(ReferenceEqualityComparer.Instance);

        // --- 公開プロパティ ---

        /// <summary>
        /// 登録されている全ExposedObjectインスタンス
        /// </summary>
        public static IReadOnlyCollection<ExposedObject> instances => _instances;

        /// <summary>
        /// 指定された型に代入可能な候補を持つ ExposedObject を列挙する。
        /// ObjectSelector 属性の候補列挙に使用する。
        /// - target 自体が代入可能なら直接ヒット
        /// - targetType が Component 派生で、target が GameObject (またはそれを包む Proxy) の場合は、
        ///   GetComponent(targetType) で該当コンポーネントがあればヒット (Unity Editor の Object field と同様の挙動)
        /// </summary>
        public static IEnumerable<ExposedObject> GetByTargetType(Type targetType)
        {
            if (targetType == null) yield break;
            bool lookingForComponent = typeof(Component).IsAssignableFrom(targetType);
            foreach (var obj in _instances)
            {
                if (obj == null || !obj.hasId) continue;
                var target = obj.target;
                if (target == null) continue;
                if (target is UnityEngine.Object unityTarget && unityTarget == null) continue;

                // 1) 直接の型マッチ
                if (targetType.IsAssignableFrom(target.GetType()))
                {
                    yield return obj;
                    continue;
                }

                // 2) GameObject 上のコンポーネントを検索対象に含める
                if (lookingForComponent)
                {
                    var gameObject = _ResolveGameObject(target);
                    if (gameObject != null && gameObject.GetComponent(targetType) != null)
                    {
                        yield return obj;
                    }
                }
            }
        }

        /// <summary>
        /// ObjectSelector の @ref 解決用: ExposedObject の target から GameObject を取り出す。
        /// target が GameObject / Component / Proxy のいずれかを想定。見つからなければ null。
        /// </summary>
        internal static GameObject ResolveGameObject(object target) => _ResolveGameObject(target);

        private static GameObject _ResolveGameObject(object target)
        {
            if (target == null) return null;
            if (target is GameObject go) return go;
            if (target is Component comp) return comp.gameObject;
            if (target is ExposedUnityObjectBase proxy)
            {
                if (proxy.reference is GameObject proxyGo) return proxyGo;
                if (proxy.reference is Component proxyComp) return proxyComp.gameObject;
            }
            return null;
        }

        /// <summary>
        /// UnityEngine.Object の reference から Transform を取り出す。
        /// GameObject / Component を想定、どちらでもなければ null。
        /// 破棄済み Unity Object (Unity == null) は null 扱い (.transform で
        /// MissingReferenceException が出るのを防ぐ)。
        /// </summary>
        internal static Transform ExtractTransform(UnityEngine.Object reference)
        {
            if (reference == null) return null;
            if (reference is GameObject go) return go.transform;
            if (reference is Component comp) return comp.transform;
            return null;
        }

        /// <summary>
        /// 指定 Transform の祖先方向に辿り、最初に見つかった ExposedObject の id を返す。
        /// 自身は含めず parent 方向から探索する。見つからなければ null。
        /// ExposedUnityObjectBase.parentId getter の派生実装で使用。
        /// </summary>
        public static string FindAncestorExposedId(Transform self)
        {
            if (self == null) return null;
            for (var cur = self.parent; cur != null; cur = cur.parent)
            {
                foreach (var obj in _instances)
                {
                    if (obj == null || !obj.isValid) continue;
                    if (!(obj.target is ExposedUnityObjectBase proxy)) continue;
                    if (proxy.reference == null) continue;
                    var tr = ExtractTransform(proxy.reference);
                    if (tr == cur) return obj.id;
                }
            }
            return null;
        }

        // --- 初期化 ---

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ClearInstances()
        {
            ClearAll();
        }

        /// <summary>
        /// すべてのExposedObjectインスタンスをクリアします
        /// </summary>
        public static void ClearAll()
        {
            _instances.Clear();
            _byId.Clear();
            _byTarget.Clear();
            ExposedObjectDefaultRegistry.ClearAll();
        }

        // --- 登録 ---

        /// <summary>
        /// ExposedObjectをレジストリに登録する。ExposedObjectのコンストラクタから呼び出される。
        /// </summary>
        internal static void Register(ExposedObject obj)
        {
            _instances.Add(obj);
            if (!string.IsNullOrEmpty(obj.id))
            {
                _byId[obj.id] = obj;
            }
            if (obj.target != null)
            {
                _byTarget[obj.target] = obj;
            }
        }

        /// <summary>
        /// ExposedObjectをレジストリから登録解除する。
        /// </summary>
        public static void Unregister(ExposedObject obj)
        {
            _instances.Remove(obj);
            if (!string.IsNullOrEmpty(obj.id))
            {
                _byId.Remove(obj.id);
            }
            if (obj.target != null)
            {
                _byTarget.Remove(obj.target);
            }
        }

        /// <summary>
        /// IDなしのExposedObjectにIDを後から割り当てる。
        /// </summary>
        internal static void AssignId(ExposedObject obj, string newId)
        {
            _byId[newId] = obj;
        }

        // --- 検索 ---

        public static bool TryFindById(string id, out ExposedObject exposedObject)
        {
            exposedObject = FindById(id);
            return exposedObject != null;
        }

        public static ExposedObject FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_byId.TryGetValue(id, out var result))
            {
                if (result.isValid) return result;
                // 無効エントリを削除
                Unregister(result);
            }

            return null;
        }

        public static bool TryFindByTarget(object target, out ExposedObject exposedObject)
        {
            exposedObject = FindByTarget(target);
            return exposedObject != null;
        }

        /// <summary>
        /// ターゲットオブジェクトからExposedObjectを検索
        /// </summary>
        public static ExposedObject FindByTarget(object target)
        {
            if (target == null) return null;

            if (_byTarget.TryGetValue(target, out var result))
            {
                if (result.isValid) return result;
                // 無効エントリを削除
                Unregister(result);
            }

            return null;
        }

        // --- クリーンアップ ---

        /// <summary>
        /// 無効なインスタンス（targetがnullまたは破棄済み）を削除
        /// </summary>
        public static void CleanupInvalidInstances()
        {
            // 無効エントリを収集してから一括削除（辞書も同期）
            var toRemove = new List<ExposedObject>();
            foreach (var obj in _instances)
            {
                if (!obj.isValid) toRemove.Add(obj);
            }
            foreach (var obj in toRemove)
            {
                Unregister(obj);
            }
        }

        // --- ファクトリ ---

        public static ExposedObject Create(Type type, object target, string id)
        {
            var exposedClass = ExposedClass.Find(type);
            if (exposedClass == null)
            {
                Debug.LogWarning($"[RemoteControl] ExposedClass type cannot be null for type:{type} id:{id}");
                return null;
            }

            return GetOrCreate(id, exposedClass, target);
        }

        public static ExposedObject Create<T>(T target, string id) where T : class
        {
            var exposedClass = ExposedClass.Find(typeof(T));
            if (exposedClass == null)
            {
                Debug.LogWarning($"[RemoteControl] ExposedClass type cannot be null for type:{typeof(T).Name} id:{id}");
                return null;
            }

            return GetOrCreate(id, exposedClass, target);
        }

        /// <summary>
        /// IDなしのExposedObjectを取得または生成する。
        /// コンテナ管理外のオブジェクトに使用。シリアライズ時はインライン展開される。
        /// </summary>
        public static ExposedObject GetOrCreateWithoutId(ExposedClass type, object target)
        {
            if (target != null)
            {
                var existing = FindByTarget(target);
                if (existing != null) return existing;
            }
            return new ExposedObject(null, type, target);
        }

        /// <summary>
        /// 既存のExposedObjectを取得、なければ新規作成
        /// </summary>
        public static ExposedObject GetOrCreate(string id, ExposedClass type, object target)
        {
            // targetが同じ既存インスタンスがあればそれを返す
            if (target != null)
            {
                var existing = FindByTarget(target);
                if (existing != null)
                {
                    // 既存がIDなしで、新しいリクエストがID付きの場合、IDを割り当てる
                    // （コンテナ登録前にIDなしで生成されたケースの救済）
                    if (!existing.hasId && !string.IsNullOrEmpty(id))
                    {
                        existing.AssignId(id);
                    }
                    return existing;
                }
            }
            else
            {
                // static class (target==null) の場合、IDで既存を検索
                var existing = FindById(id);
                if (existing != null) return existing;
            }

            // なければ新規作成
            return new ExposedObject(id, type, target);
        }

        /// <summary>
        /// IDで既存のExposedObjectを検索、なければ型情報を使って新規作成
        /// FromJsonで@ref解決時に使用
        /// </summary>
        public static ExposedObject GetOrCreate(string id, ExposedClass exposedClass)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (exposedClass == null) return null;

            // 既存のインスタンスをIDで検索
            var existing = FindById(id);
            if (existing != null) return existing;

            // 存在しない場合は新規作成
            // static class はインスタンス化できないためスキップ
            if (exposedClass.type.IsAbstract && exposedClass.type.IsSealed)
                return null;
            // MonoBehaviour/ScriptableObject派生クラスはActivator.CreateInstanceで生成できないためスキップ
            if (typeof(MonoBehaviour).IsAssignableFrom(exposedClass.type) ||
                typeof(ScriptableObject).IsAssignableFrom(exposedClass.type))
            {
                Debug.LogWarning($"[RemoteControl] Cannot create instance of '{exposedClass.type.Name}': Unity Object types must exist in the scene or as assets.");
                return null;
            }
            // デフォルトコンストラクタがない型はインスタンス化できないためスキップ
            if (exposedClass.type.GetConstructor(System.Type.EmptyTypes) == null)
            {
                Debug.LogWarning($"[RemoteControl] Cannot create instance of '{exposedClass.type.Name}': no default constructor.");
                return null;
            }
            var target = System.Activator.CreateInstance(exposedClass.type);
            return new ExposedObject(id, exposedClass, target);
        }

        // --- 親子関係 ---

        /// <summary>
        /// 指定 id を親に持つ ExposedObject を列挙する。
        /// Unity hierarchy を真実として派生判定する。
        /// </summary>
        public static IEnumerable<ExposedObject> GetChildren(string parentId)
        {
            if (string.IsNullOrEmpty(parentId)) yield break;
            foreach (var obj in _instances)
            {
                if (obj == null || !obj.isValid) continue;
                if (!(obj.target is ExposedUnityObjectBase proxy)) continue;
                if (proxy.reference == null) continue;
                var tr = ExtractTransform(proxy.reference);
                if (tr == null) continue;
                if (FindAncestorExposedId(tr) == parentId)
                    yield return obj;
            }
        }

        /// <summary>
        /// 親を持たない ExposedUnityObjectBase 派生のルート群を列挙する。
        /// </summary>
        public static IEnumerable<ExposedObject> GetRootObjects()
        {
            foreach (var obj in _instances)
            {
                if (obj == null || !obj.isValid) continue;
                if (!(obj.target is ExposedUnityObjectBase proxy)) continue;
                if (proxy.reference == null) continue;
                var tr = ExtractTransform(proxy.reference);
                if (tr == null) continue;
                if (FindAncestorExposedId(tr) == null)
                    yield return obj;
            }
        }

        /// <summary>
        /// 親子関係を設定する。Unity Transform.parent を実際に書き換える。
        /// 循環や自己親指定、Prefab 内部子は reject する。
        /// </summary>
        /// <returns>true=設定成功。false=reject (error にメッセージ)。</returns>
        public static bool SetParent(string childId, string newParentId, out string error)
        {
            error = null;

            var child = FindById(childId);
            if (child == null)
            {
                error = $"Child ExposedObject not found: id='{childId}'";
                return false;
            }
            if (!(child.target is ExposedUnityObjectBase childProxy))
            {
                error = $"Child is not parentable (target is not ExposedUnityObjectBase): id='{childId}'";
                return false;
            }
            var childTransform = ExtractTransform(childProxy.reference);
            if (childTransform == null)
            {
                error = $"Child is not reparentable (no Transform): id='{childId}'";
                return false;
            }

            var normalizedParentId = string.IsNullOrEmpty(newParentId) ? null : newParentId;

            // Prefab 保護: child が Prefab インスタンスの内部 (非ルート) なら Unity 側で
            // SetParent が禁止されるため、事前に reject してデータ/階層の不整合を防ぐ。
            if (!GameObjectUtility.CanReparent(childTransform.gameObject, out var reparentError))
            {
                error = reparentError;
                return false;
            }

            if (normalizedParentId == null)
            {
                GameObjectUtility.SetTransformParent(childTransform, null, "Reparent ExposedObject");
                return true;
            }

            if (childId == normalizedParentId)
            {
                error = "Cannot parent ExposedObject to itself";
                return false;
            }

            var parent = FindById(normalizedParentId);
            if (parent == null)
            {
                error = $"Parent ExposedObject not found: id='{normalizedParentId}'";
                return false;
            }
            if (!(parent.target is ExposedUnityObjectBase parentProxy))
            {
                error = $"Parent is not parentable (target is not ExposedUnityObjectBase): id='{normalizedParentId}'";
                return false;
            }
            var parentTransform = ExtractTransform(parentProxy.reference);
            if (parentTransform == null)
            {
                error = $"Parent has no Transform: id='{normalizedParentId}'";
                return false;
            }

            // 循環チェック: 新親の Unity 祖先チェーンに child が含まれていないか
            for (var cur = parentTransform; cur != null; cur = cur.parent)
            {
                if (cur == childTransform)
                {
                    error = "Cyclic parent relationship detected";
                    return false;
                }
            }

            GameObjectUtility.SetTransformParent(childTransform, parentTransform, "Reparent ExposedObject");
            return true;
        }

        // --- 直接アクセスAPI（ExposedObjectインスタンス不要） ---

        /// <summary>
        /// IDでプロパティを検索する。ExposedObjectインスタンスを経由せずにプロパティにアクセスできる。
        /// </summary>
        public static ExposedProperty? FindProperty(string id, string propertyPath)
        {
            var obj = FindById(id);
            return obj?.FindProperty(propertyPath);
        }

        /// <summary>
        /// IDで関数を呼び出す。
        /// </summary>
        public static object InvokeFunction(string id, string functionName, object[] args = null)
        {
            var obj = FindById(id);
            if (obj == null)
            {
                Debug.LogError($"[RemoteControl] ExposedObject not found for id '{id}'");
                return null;
            }
            return obj.InvokeFunction(functionName, args);
        }

        /// <summary>
        /// IDでdirty状態を確認する。
        /// </summary>
        public static bool IsDirty(string id)
        {
            var obj = FindById(id);
            if (obj == null) return false;
            return ExposedObjectDefaultRegistry.IsDirty(obj, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// IDで指定プロパティのdirty状態を確認する。
        /// </summary>
        public static bool IsPropertyDirty(string id, string propertyPath)
        {
            var obj = FindById(id);
            if (obj == null) return false;
            return ExposedObjectDefaultRegistry.IsPropertyDirty(obj, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// IDでデフォルト値をキャプチャする。
        /// </summary>
        public static void SetDefault(string id)
        {
            var obj = FindById(id);
            if (obj != null) ExposedPropertyUtility.SetDefault(obj);
        }

        /// <summary>
        /// IDでdirty状態をクリアする。
        /// </summary>
        public static void ClearDirty(string id)
        {
            var obj = FindById(id);
            if (obj != null) ExposedObjectDefaultRegistry.ClearDirty(obj, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// IDで指定プロパティをデフォルト値に戻す。
        /// </summary>
        public static bool Revert(string id, string propertyPath)
        {
            var obj = FindById(id);
            if (obj == null) return false;
            return ExposedObjectDefaultRegistry.Revert(obj, propertyPath, DefaultExposedObjectResolver.Instance);
        }

        /// <summary>
        /// カテゴリに一致するExposedObjectを収集する。
        /// ExposedObjectContainer登録済みオブジェクト、ExposedObjectRegistry.instances、Staticクラス、シーン上のコンポーネントを対象とする。
        /// </summary>
        public static List<ExposedObject> FindByCategory(string category, ExposedObjectContainer container = null)
        {
            var result = new List<ExposedObject>();
            var added = new HashSet<ExposedObject>();

            // Containerに登録されているオブジェクト
            if (container != null)
            {
                foreach (var item in container.objects)
                {
                    if (item == null) continue;
                    var obj = item.exposedObject;
                    if (obj == null || obj.targetType == null) continue;
                    if (obj.targetType.category == category && added.Add(obj))
                    {
                        result.Add(obj);
                    }
                }
            }

            // 既存のinstancesから検索
            foreach (var obj in _instances)
            {
                if (obj == null || !obj.isValid) continue;
                if (obj.targetType == null) continue;
                if (obj.targetType.category == category && added.Add(obj))
                {
                    result.Add(obj);
                }
            }

            // ExposedClass.allからカテゴリ一致する型を取得
            foreach (var exposedClass in ExposedClass.all.Values)
            {
                if (exposedClass.category != category) continue;

                // Staticクラス
                if (exposedClass.isStatic)
                {
                    var obj = GetOrCreate(exposedClass.typeName, exposedClass, null);
                    if (obj != null && added.Add(obj))
                    {
                        result.Add(obj);
                    }
                    continue;
                }

                // シーン上のコンポーネントを探索（IDなしで生成）
                if (exposedClass.type != null && exposedClass.type.IsSubclassOf(typeof(Component)))
                {
                    var list = GameObject.FindObjectsByType(exposedClass.type, FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (list == null) continue;
                    foreach (var found in list)
                    {
                        // 既に登録済みのtargetはそちらを使用
                        var existing = FindByTarget(found);
                        if (existing != null && added.Contains(existing))
                            continue;

                        var obj = existing ?? ExposedObject.CreateUnregistered(exposedClass, found);
                        if (obj != null && added.Add(obj))
                        {
                            result.Add(obj);
                        }
                    }
                }
            }

            return result;
        }
    }
}
