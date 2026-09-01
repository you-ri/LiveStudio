// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ファイル（シーン）スコープ専用のリゾルバー拡張。
    /// LiveSceneSerializer が保存時に利用し、プロパティ走査中の UnityEngine.Object 参照を
    /// fileid ベースの @ref に置き換えつつ、参照先を別エントリとして収集する。
    /// REST API など file-scope 外の経路は通常の <see cref="ILiveObjectResolver"/> を使うため
    /// 既存挙動に影響しない。シーン保存内部専用のため internal（実装の唯一の利用者は
    /// 同アセンブリの LivePropertySerializer のキャストと、IVT 経由の LiveSceneSerializer）。
    /// </summary>
    internal interface IFileScopedResolver : ILiveObjectResolver
    {
        /// <summary>
        /// 現在のプロパティパスにセグメントを追加する（配列添字は "[i]" 形式）。
        /// </summary>
        void PushPath(string segment);

        /// <summary>
        /// <see cref="PushPath"/> で積んだ最上位セグメントを取り除く。
        /// </summary>
        void PopPath();

        /// <summary>
        /// 現在処理中の root LiveObjectHandle を設定する。null で解除。
        /// </summary>
        void SetCurrentRoot(LiveObjectHandle? root);

        /// <summary>
        /// UnityEngine.Object 参照を fileid 付きの @ref トークンとしてエンコードし、
        /// 本体を objects[] に後で書き出せるよう内部キューに登録する。
        /// </summary>
        /// <returns>置換用の @ref トークン（"{ "@ref": "{guid}" }" 相当）</returns>
        Newtonsoft.Json.Linq.JToken EncodeUnityObjectReference(UnityEngine.Object obj);
    }

    /// <summary>
    /// LiveSceneSerializer がシーン保存時に利用する file-scope リゾルバー。
    /// - プロパティ走査中のパスを追跡する
    /// - UnityEngine.Object 参照を source-key ベースの @ref にエンコードする
    /// - 未登録の UnityEngine.Object は @source (rootId+path) 付きエントリとして後で objects[] に書き出すようキューする
    /// - 登録済み LiveObjectHandle を持つ UnityEngine.Object は、その LiveObjectHandle.id を source-key として再利用する
    /// </summary>
    internal sealed class FileScopedResolver : IFileScopedResolver
    {
        public struct PendingReference
        {
            public string sourceKey;     // rootId + path を結合した source-key。@ref/@source 値としても使う
            public UnityEngine.Object target;
            public string typeName;
            public string rootId;        // 参照が検出された registered root のid（null 可）
            public string path;          // root からのプロパティパス（DotBracket 形式）
        }

        private readonly ILiveObjectResolver _inner;
        private readonly List<string> _pathStack = new List<string>();
        private readonly Dictionary<UnityEngine.Object, string> _objectFileIds = new Dictionary<UnityEngine.Object, string>();
        private readonly List<PendingReference> _pending = new List<PendingReference>();

        private LiveObjectHandle? _currentRoot;

        public FileScopedResolver(ILiveObjectResolver inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<PendingReference> pending => _pending;

        public LiveObjectHandle? FindById(string id) => _inner.FindById(id);
        public LiveObjectHandle? FindByTarget(object target) => _inner.FindByTarget(target);

        public void PushPath(string segment)
        {
            if (segment == null) return;
            _pathStack.Add(segment);
        }

        public void PopPath()
        {
            if (_pathStack.Count == 0) return;
            _pathStack.RemoveAt(_pathStack.Count - 1);
        }

        public void SetCurrentRoot(LiveObjectHandle? root)
        {
            _currentRoot = root;
            _pathStack.Clear();
        }

        /// <summary>
        /// pending 処理時に「既に何段か潜ったパス」を起点としてセットする。
        /// これ以降の PushPath はこのベースパスに積まれる形で解釈される。
        /// </summary>
        public void SetBasePath(string basePath)
        {
            _pathStack.Clear();
            if (!string.IsNullOrEmpty(basePath))
            {
                _pathStack.Add(basePath);
            }
        }

        public string GetCurrentPath()
        {
            if (_pathStack.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < _pathStack.Count; i++)
            {
                var seg = _pathStack[i];
                if (seg.StartsWith("["))
                {
                    sb.Append(seg);
                }
                else
                {
                    if (sb.Length > 0) sb.Append('.');
                    sb.Append(seg);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 対象オブジェクトの source-key を採番（既に割当済みなら再利用）。
        /// 登録済み LiveObjectHandle を持つ target はその LiveObjectHandle.id を再利用する。
        /// 未登録 target には `rootId + 現在パス` を source-key として割り当てる。
        /// pending は未登録の UnityEngine.Object の場合にのみ追加する。
        /// </summary>
        public string AssignFileId(UnityEngine.Object obj, bool registerPending, string overrideRootId = null, string overridePath = null)
        {
            if (_objectFileIds.TryGetValue(obj, out var existing))
            {
                return existing;
            }

            // 登録済み LiveObjectHandle がある場合はその id を source-key として再利用する
            string sourceKey;
            var rootId = overrideRootId ?? _currentRoot?.id;
            var path = overridePath ?? GetCurrentPath();
            var registered = _inner.FindByTarget(obj);
            if (registered != null && registered.Value.hasId)
            {
                sourceKey = registered.Value.id;
            }
            else
            {
                // Rename an index-based component element ("components[0]") to an exposed-type-name-based
                // key ("components[Chair]") so the source key survives bundle re-export / component
                // reordering (a numeric index would silently drift to another component). Both the source
                // key and the pending path use the renamed form so nested references stay consistent.
                path = _NameComponentPath(path, obj);
                sourceKey = _ComposeSourceKey(rootId, path);
            }
            LiveObjectFileRegistry.Register(sourceKey, obj);
            _objectFileIds[obj] = sourceKey;

            if (registerPending)
            {
                _pending.Add(new PendingReference
                {
                    sourceKey = sourceKey,
                    target = obj,
                    typeName = obj.GetType().Name,
                    rootId = rootId,
                    path = path,
                });
            }

            return sourceKey;
        }

        public JToken EncodeUnityObjectReference(UnityEngine.Object obj)
        {
            if (obj == null) return JValue.CreateNull();

            // 登録済み LiveObjectHandle を持つ場合: source-key 採番のみ、pending には積まない
            // （root 側が別エントリとして出力済みになる）
            var registered = _inner.FindByTarget(obj);
            bool isRegisteredRoot = registered != null && registered.Value.hasId;

            var sourceKey = AssignFileId(obj, registerPending: !isRegisteredRoot);
            return new JObject { ["@ref"] = sourceKey };
        }

        /// <summary>
        /// rootId と DotBracket 形式 path を 1 本の文字列 @source キーに結合する。
        /// 前提: rootId は '.' や '[' を含まない (GUID や typeName ベース id)。
        /// path が空 → "rootId"。path が "[" から始まる → "rootId[0]..."。それ以外 → "rootId.foo..."。
        /// （旧 LiveSceneSerializer._ComposeSourceKey。唯一の呼び元である本リゾルバーへ移設。）
        /// </summary>
        private static string _ComposeSourceKey(string rootId, string path)
        {
            if (string.IsNullOrEmpty(path)) return rootId;
            return path[0] == '[' ? rootId + path : rootId + "." + path;
        }

        // The exposed property name of the GameObject wrapper's component list (see LiveGameObject).
        private const string kComponentsPrefix = ComponentElementKey.kMemberName + "[";

        /// <summary>
        /// If <paramref name="path"/> is a single top-level component element ("components[&lt;digits&gt;]")
        /// and <paramref name="obj"/> is a <see cref="Component"/> with a registered <see cref="LiveClass"/>,
        /// replaces the numeric index with the exposed type name ("components[Chair]"). This keeps the
        /// composed source key stable when a bundle is re-exported and its component order changes — an
        /// index would then point at a different component. Any other path shape, non-component target, or
        /// unregistered type is returned unchanged so the generic serialization path is untouched.
        /// </summary>
        private static string _NameComponentPath(string path, UnityEngine.Object obj)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (!(obj is Component)) return path;
            if (!path.StartsWith(kComponentsPrefix, StringComparison.Ordinal)) return path;
            if (!path.EndsWith("]", StringComparison.Ordinal)) return path;

            var inner = path.Substring(kComponentsPrefix.Length, path.Length - kComponentsPrefix.Length - 1);
            if (inner.Length == 0) return path;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] < '0' || inner[i] > '9') return path; // already named, or not a bare index
            }

            // The rule itself lives with the frame side's copy of it, so the two cannot drift.
            var key = ComponentElementKey.Of(obj);
            return key == null ? path : kComponentsPrefix + key + "]";
        }
    }
}
