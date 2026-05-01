// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// シーンファイル保存/読み込み中の source-key (文字列) と UnityEngine.Object の対応を保持する
    /// ランタイム専用キャッシュ。
    ///
    /// source-key は登録済み ExposedObject の id (rootId) またはルートからの `rootId+path` 形式。
    /// SceneSerializer は保存/読み込みの先頭で <see cref="Clear"/> を呼び、
    /// 必要に応じてエントリから再登録する。永続化はしない。
    /// </summary>
    public static class ExposedObjectFileRegistry
    {
        private static readonly Dictionary<string, UnityEngine.Object> _byId = new Dictionary<string, UnityEngine.Object>();

        public static void Clear() => _byId.Clear();

        public static bool TryGetObject(string id, out UnityEngine.Object obj)
        {
            if (string.IsNullOrEmpty(id))
            {
                obj = null;
                return false;
            }
            return _byId.TryGetValue(id, out obj);
        }

        /// <summary>
        /// source-key と target の対応を登録する。
        /// </summary>
        public static void Register(string id, UnityEngine.Object obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id cannot be null or empty", nameof(id));
            _byId[id] = obj;
        }
    }
}
