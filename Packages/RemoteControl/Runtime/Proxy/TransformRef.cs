// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 親 ExposedObject の配下にある Transform への相対参照。
    ///
    /// `_ownerName` は親 ExposedObject の表示 name (= 紐づく GameObject 名) を保持する source of truth。
    /// 解決は <see cref="ExposedObjectRegistry"/> 配下を name で検索することで行うため、
    /// 起動時に対象がまだ未登録であっても name 文字列だけは復元され、後続フレームで
    /// 対象が登録され次第 <see cref="Resolve"/> / <see cref="ResolveOwner"/> が成功するようになる。
    ///
    /// `_transformPath` は <see cref="searchType"/> によって解釈が変わる:
    /// - <see cref="SearchType.Path"/> (デフォルト): 親 root からの相対 path (スラッシュ区切り、例: "Armature/Hips/Head")。
    ///   空文字は「親 root 自身」。互換のため単一セグメント (スラッシュ無し) のときは name フォールバックで子孫検索する。
    /// - <see cref="SearchType.Name"/>: 親 root 配下を再帰的に探索し、同名の Transform を first-match で返す。
    /// </summary>
    [Serializable]
    [ExposedClass]
    public class TransformRef : IExposedDeserializeCallback
    {
        /// <summary>_transformPath の解釈方法。</summary>
        [ExposedEnum]
        public enum SearchType
        {
            /// <summary>親 root からの相対 path で厳密検索する (旧来の挙動、デフォルト)。</summary>
            Path,
            /// <summary>親 root 配下を再帰的に探索し、同名の Transform を first-match で返す。</summary>
            Name,
        }

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("ownerName")]
        string _ownerName;

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("transformPath")]
        string _transformPath;

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("searchType")]
        SearchType _searchType = SearchType.Path;

        /// <summary>
        /// いずれかのフィールドが変更された際に呼ばれる。
        /// </summary>
        public event Action onChanged;

        [NonSerialized]
        ExposedUnityObjectBase _self;

        public TransformRef() { }

        /// <param name="ownerName">親 ExposedObject の表示 name (= 紐づく GameObject 名)。</param>
        /// <param name="transformPath">searchType に応じて解釈される値: Path モードなら親 root からの相対 path (例: "Armature/Hips/Head")、Name モードなら検索対象の name。</param>
        /// <param name="searchType">検索方式。デフォルトは Path (旧挙動互換)。</param>
        public TransformRef(string ownerName, string transformPath, SearchType searchType = SearchType.Path)
        {
            _ownerName = ownerName;
            _transformPath = transformPath;
            _searchType = searchType;
        }

        /// <summary>
        /// この TransformRef を保持している ExposedObject (= self / 宿主) を紐付ける。
        /// <see cref="availableOwnerNames"/> の循環除外 (self + 子孫をドロップダウンから外す) のためだけに使用する。
        /// </summary>
        public void SetSelf(ExposedUnityObjectBase self)
        {
            _self = self;
        }

        /// <summary>
        /// ownerName / transformPath の両方が未設定か。
        /// OnEnable 時に「初期状態なら Unity hierarchy から snapshot するか」の判定に使う。
        /// </summary>
        public bool isEmpty => string.IsNullOrEmpty(_ownerName) && string.IsNullOrEmpty(_transformPath);

        /// <summary>
        /// ownerName が指定されているか (= 親 ExposedObject の解決を意図しているか)。
        /// 「指定済みだが ResolveOwner が null を返す」= 未登録の未解決状態を判定するために使う。
        /// </summary>
        public bool hasOwner => !string.IsNullOrEmpty(_ownerName);

        /// <summary>
        /// 現在の設定で参照先 Transform を解決できるか。
        /// RemoteApp 側が「target 有効か」を判定するための公開フラグ。
        /// false のときは ResolveOwner / Resolve のいずれかが失敗している
        /// (= 親 ExposedObject 未登録、または bone path 未解決)。
        /// </summary>
        [ExposedProperty, Hide]
        public bool isResolved => Resolve() != null;

        /// <summary>
        /// 親 ExposedObject の表示 name。永続化はこのプロパティが直接担う。
        /// 起動時に対象がまだ Registry に登録されていなくても、name 文字列自体は保存・復元され、
        /// 登録され次第 Resolve が解決できるようになる。
        /// RemoteApp からは StringSelector によるドロップダウンで親 ExposedObject の name を選択する。
        /// </summary>
        [ExposedProperty]
        [StringSelector(nameof(availableOwnerNames))]
        public string ownerName
        {
            get => _ownerName ?? string.Empty;
            set
            {
                var normalized = string.IsNullOrEmpty(value) ? null : value;
                if (_ownerName == normalized) return;
                _ownerName = normalized;
                onChanged?.Invoke();
            }
        }

        /// <summary>
        /// 親 ExposedObject の候補となる name 一覧。
        /// 空文字 (ルート = 親なし) を先頭に、ExposedObject として登録済みの UnityObject 系の名前を列挙する。
        /// self 自身と self の子孫は循環防止のため除外する (除外判定には引き続き Registry の id を内部利用)。
        /// </summary>
        [ExposedProperty, Hide]
        public string[] availableOwnerNames
        {
            get
            {
                var selfId = _self?.id;
                var excluded = selfId != null
                    ? _CollectSelfAndDescendants(selfId)
                    : null;

                var names = new List<string> { string.Empty };
                foreach (var obj in ExposedObjectRegistry.instances)
                {
                    if (obj == null || !obj.isValid) continue;
                    if (excluded != null && excluded.Contains(obj.id)) continue;
                    if (!(obj.target is ExposedUnityObjectBase proxy)) continue;
                    if (proxy.reference == null) continue;
                    var name = proxy.reference.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    names.Add(name);
                }
                return names.Distinct().ToArray();
            }
        }

        /// <summary>
        /// id を起点に self + 全子孫の id 集合を返す (循環参照禁止の除外リスト用)。
        /// </summary>
        static HashSet<string> _CollectSelfAndDescendants(string rootId)
        {
            var result = new HashSet<string>();
            if (string.IsNullOrEmpty(rootId)) return result;
            result.Add(rootId);

            var stack = new Stack<string>();
            stack.Push(rootId);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var child in ExposedObjectRegistry.GetChildren(cur))
                {
                    if (child == null || !child.isValid) continue;
                    if (result.Add(child.id))
                    {
                        stack.Push(child.id);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Transform 参照を name で表示・設定する (UI 向け薄ラッパ、非 Persistable)。
        /// 内部 storage (`_transformPath`) は「親 root からの相対 path」だが、同名ボーンが複数あっても
        /// dropdown 表示・選択は name ベースで十分なケースが多いため、UI レイヤは name に絞る。
        /// 永続化は <see cref="transformPath"/> が担当し、このプロパティは JSON に書き出されない。
        /// - getter: `_transformPath` の末尾セグメント (= leaf の name) を返す。
        /// - setter: 受け取った値を path に正規化して格納 (name → first-match の相対 path)。
        ///   スラッシュを含む値は path とみなしてそのまま格納する。
        ///   root 未解決や子孫未発見の場合は生値のまま保持し、Resolve の name fallback に委ねる。
        /// </summary>
        [ExposedProperty]
        [StringSelector(nameof(availableTransformNames))]
        public string transformName
        {
            get
            {
                if (string.IsNullOrEmpty(_transformPath)) return string.Empty;
                var slash = _transformPath.LastIndexOf('/');
                return slash >= 0 ? _transformPath.Substring(slash + 1) : _transformPath;
            }
            set
            {
                var normalized = _NormalizeTransformInput(value);
                if (_transformPath == normalized) return;
                _transformPath = normalized;
                onChanged?.Invoke();
            }
        }

        /// <summary>
        /// UI から受け取った文字列を `_transformPath` に格納する形に正規化する。
        /// - Name モード: 生値をそのまま返す (path 化しない)。
        /// - Path モード:
        ///   - 空文字 → 空文字 (親 root 自身)。
        ///   - スラッシュを含む → 既に path 形式として尊重しそのまま返す。
        ///   - それ以外 (name) → root 配下で first-match を探し、相対 path 化。見つからなければ生値。
        /// </summary>
        string _NormalizeTransformInput(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            if (_searchType == SearchType.Name) return raw;
            if (raw.IndexOf('/') >= 0) return raw;

            var root = ResolveOwner();
            if (root == null) return raw;

            var target = root.GetComponentsInChildren<Transform>(includeInactive: true)
                .FirstOrDefault(t => t.name == raw);
            if (target == null) return raw;
            if (target == root.transform) return string.Empty;
            return _GetRelativePath(root.transform, target);
        }

        /// <summary>
        /// 内部 path の source of truth としての永続化プロパティ。
        /// UI には露出せず (<see cref="Hide"/>)、JSON への読み書きだけを担う。
        /// </summary>
        [ExposedProperty, Hide]
        public string transformPath
        {
            get => _transformPath ?? string.Empty;
            set
            {
                var v = value ?? string.Empty;
                if (_transformPath == v) return;
                _transformPath = v;
                onChanged?.Invoke();
            }
        }

        /// <summary>
        /// _transformPath の解釈方法を選択する (UI 露出 + 永続化対象)。
        /// _transformPath は保持されるため、Path 形式の path が Name モードでヒットしない場合は
        /// Resolve のフォールバックで root.transform に戻る。
        /// </summary>
        [ExposedProperty]
        public SearchType searchType
        {
            get => _searchType;
            set
            {
                if (_searchType == value) return;
                _searchType = value;
                onChanged?.Invoke();
            }
        }

        [ExposedProperty, Hide]
        public string[] availableTransformNames
        {
            get
            {
                var root = ResolveOwner();
                if (root == null) return new[] { string.Empty };
                return new[] { string.Empty }
                    .Concat(root.GetComponentsInChildren<Transform>(includeInactive: true)
                        .Select(t => t.name)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct())
                    .ToArray();
            }
        }

        /// <summary>
        /// root から target までのスラッシュ区切り相対 path を返す (root 自身の場合は空文字)。
        /// target が root の子孫でない場合は空文字を返す。
        /// </summary>
        static string _GetRelativePath(Transform root, Transform target)
        {
            if (target == null || target == root) return string.Empty;
            var parts = new List<string>();
            var cur = target;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            // ループが root に到達していない (cur == null) = target は root の子孫ではない
            if (cur != root) return string.Empty;
            var sb = new StringBuilder();
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                if (sb.Length > 0) sb.Append('/');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// _ownerName から親 GameObject を取得する。
        /// ExposedObjectRegistry を name で検索し、ヒットした ExposedObject の target から GameObject を抽出する。
        /// 未登録 / name 不一致のときは null を返す。
        /// </summary>
        public GameObject ResolveOwner()
        {
            if (string.IsNullOrEmpty(_ownerName)) return null;
            var parentExposed = _FindExposedByName(_ownerName);
            if (parentExposed == null) return null;
            return _ExtractGameObject(parentExposed.target);
        }

        /// <summary>
        /// 親 root + transformPath から Transform を解決する。
        /// - transformPath が空なら root 自身の Transform を返す。
        /// - <see cref="SearchType.Name"/> なら root 配下を再帰検索して同名 Transform を first-match で返す。
        ///   見つからなければ root 自身にフォールバック。
        /// - <see cref="SearchType.Path"/> (デフォルト) なら相対 path として root.transform.Find(path) で厳密検索。
        ///   見つからず、path が単一セグメント (スラッシュ無し) なら name fallback で子孫全体を検索 (旧データ互換)。
        ///   最終的に見つからなければ root 自身にフォールバックする
        ///   (古い参照情報が残留していても最低限親直下への attach は成功させるため)。
        /// </summary>
        public Transform Resolve()
        {
            var root = ResolveOwner();
            if (root == null) return null;
            if (string.IsNullOrEmpty(_transformPath)) return root.transform;

            if (_searchType == SearchType.Name)
            {
                var byName = root.GetComponentsInChildren<Transform>(includeInactive: true)
                    .FirstOrDefault(t => t.name == _transformPath);
                if (byName != null) return byName;
                return root.transform;
            }

            var found = root.transform.Find(_transformPath);
            if (found != null) return found;

            // 旧データ (name のみ格納されていた) 互換
            if (_transformPath.IndexOf('/') < 0)
            {
                found = root.GetComponentsInChildren<Transform>(includeInactive: true)
                    .FirstOrDefault(t => t.name == _transformPath);
                if (found != null) return found;
            }

            return root.transform;
        }

        /// <summary>
        /// 指定 Transform を指すように参照を初期化する。
        /// t の祖先を辿り、ExposedObjectRegistry に登録済みの ExposedObject (GameObject を伴うもの) を
        /// 親として特定し、その GameObject 名を ownerName にセットする。
        /// _transformPath は target が親 root と同じなら空、子孫なら root からの相対 path (Path モード) または
        /// leaf name (Name モード) を格納する。
        /// </summary>
        /// <param name="t">起点とする Transform。null なら ownerName をクリア。</param>
        /// <param name="silent">true の時は ownerName setter を経由せず _ownerName / _transformPath を直接書き換え、
        /// onChanged を発火させない。hierarchy 変更通知からの同期用。</param>
        public bool InitFromTransform(Transform t, bool silent = false)
        {
            _transformPath = string.Empty;

            if (t == null)
            {
                if (silent) _ownerName = null;
                else ownerName = null;
                return false;
            }

            var selfGameObject = _self != null ? _ExtractGameObject(_self) : null;

            for (var cur = t; cur != null; cur = cur.parent)
            {
                var go = cur.gameObject;
                if (selfGameObject != null && go == selfGameObject) continue;

                var exposed = _FindExposedByGameObject(go);
                if (exposed == null) continue;

                if (silent)
                {
                    _ownerName = go.name;
                }
                else
                {
                    ownerName = go.name;
                }
                _transformPath = (cur == t)
                    ? string.Empty
                    : (_searchType == SearchType.Name ? t.name : _GetRelativePath(cur, t));
                return true;
            }

            if (silent) _ownerName = null;
            else ownerName = null;
            return false;
        }

        /// <summary>
        /// 任意のオブジェクトから GameObject を取り出す。
        /// GameObject / Component / ExposedUnityObjectBase (reference 経由) に対応。
        /// </summary>
        static GameObject _ExtractGameObject(object obj)
        {
            if (obj == null) return null;
            if (obj is GameObject go) return go;
            if (obj is Component comp) return comp.gameObject;
            if (obj is ExposedUnityObjectBase proxy)
            {
                var reference = proxy.reference;
                if (reference is GameObject pgo) return pgo;
                if (reference is Component pcomp) return pcomp.gameObject;
            }
            return null;
        }

        /// <summary>
        /// GameObject に対応する ExposedObject を Registry から検索する。
        /// Proxy 系 (target=Proxy) と直 UnityObject 系 (target=UnityObject) の両方を拾う。
        /// </summary>
        static ExposedObject _FindExposedByGameObject(GameObject go)
        {
            if (go == null) return null;

            var direct = ExposedObjectRegistry.FindByTarget(go);
            if (direct != null) return direct;

            foreach (var obj in ExposedObjectRegistry.instances)
            {
                if (obj == null || !obj.isValid) continue;
                if (obj.target is ExposedUnityObjectBase proxy)
                {
                    var reference = proxy.reference;
                    if (reference is GameObject pgo && pgo == go) return obj;
                    if (reference is Component pcomp && pcomp.gameObject == go) return obj;
                }
            }
            return null;
        }

        /// <summary>
        /// name から ExposedUnityObjectBase 派生を検索する。
        /// </summary>
        static ExposedObject _FindExposedByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var obj in ExposedObjectRegistry.instances)
            {
                if (obj == null || !obj.isValid) continue;
                if (obj.target is ExposedUnityObjectBase proxy
                    && proxy.reference != null
                    && proxy.reference.name == name)
                    return obj;
            }
            return null;
        }

        // Fields are written via reflection during JSON deserialization, which bypasses the
        // property setters that normally fire onChanged. Re-fire here so consumers (parent
        // attachment, RemoteApp UI) can react to restored values exactly once per load.
        void IExposedDeserializeCallback.OnAfterExposedDeserialize()
        {
            onChanged?.Invoke();
        }
    }
}
