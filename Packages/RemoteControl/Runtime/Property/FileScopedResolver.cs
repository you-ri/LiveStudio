// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedSceneSerializer がシーン保存時に利用する file-scope リゾルバー。
    /// - プロパティ走査中のパスを追跡する
    /// - UnityEngine.Object 参照を source-key ベースの @ref にエンコードする
    /// - 未登録の UnityEngine.Object は @source (rootId+path) 付きエントリとして後で objects[] に書き出すようキューする
    /// - 登録済み ExposedObject を持つ UnityEngine.Object は、その ExposedObject.id を source-key として再利用する
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

        private readonly IExposedObjectResolver _inner;
        private readonly List<string> _pathStack = new List<string>();
        private readonly Dictionary<UnityEngine.Object, string> _objectFileIds = new Dictionary<UnityEngine.Object, string>();
        private readonly List<PendingReference> _pending = new List<PendingReference>();

        private ExposedObject _currentRoot;

        public FileScopedResolver(IExposedObjectResolver inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<PendingReference> pending => _pending;

        public ExposedObject FindById(string id) => _inner.FindById(id);
        public ExposedObject FindByTarget(object target) => _inner.FindByTarget(target);

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

        public void SetCurrentRoot(ExposedObject root)
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
        /// 登録済み ExposedObject を持つ target はその ExposedObject.id を再利用する。
        /// 未登録 target には `rootId + 現在パス` を source-key として割り当てる。
        /// pending は未登録の UnityEngine.Object の場合にのみ追加する。
        /// </summary>
        public string AssignFileId(UnityEngine.Object obj, bool registerPending, string overrideRootId = null, string overridePath = null)
        {
            if (_objectFileIds.TryGetValue(obj, out var existing))
            {
                return existing;
            }

            // 登録済み ExposedObject がある場合はその id を source-key として再利用する
            string sourceKey;
            var registered = _inner.FindByTarget(obj);
            if (registered != null && registered.hasId)
            {
                sourceKey = registered.id;
            }
            else
            {
                var rootId = overrideRootId ?? _currentRoot?.id;
                var path = overridePath ?? GetCurrentPath();
                sourceKey = ExposedSceneSerializer._ComposeSourceKey(rootId, path);
            }
            ExposedObjectFileRegistry.Register(sourceKey, obj);
            _objectFileIds[obj] = sourceKey;

            if (registerPending)
            {
                _pending.Add(new PendingReference
                {
                    sourceKey = sourceKey,
                    target = obj,
                    typeName = obj.GetType().Name,
                    rootId = overrideRootId ?? _currentRoot?.id,
                    path = overridePath ?? GetCurrentPath(),
                });
            }

            return sourceKey;
        }

        public JToken EncodeUnityObjectReference(UnityEngine.Object obj)
        {
            if (obj == null) return JValue.CreateNull();

            // 登録済み ExposedObject を持つ場合: source-key 採番のみ、pending には積まない
            // （root 側が別エントリとして出力済みになる）
            var registered = _inner.FindByTarget(obj);
            bool isRegisteredRoot = registered != null && registered.hasId;

            var sourceKey = AssignFileId(obj, registerPending: !isRegisteredRoot);
            return new JObject { ["@ref"] = sourceKey };
        }
    }
}
