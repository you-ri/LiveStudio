// Copyright (c) You-Ri, 2026
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedObjectHandle 関連の共通ユーティリティ。
    /// </summary>
    public static class ExposedObjectUtility
    {
        /// <summary>
        /// instanceId から生存している UnityEngine.Object を引き当てる。
        /// 見つからない場合は null を返す。メインスレッドからのみ呼び出すこと。
        ///
        /// public API の <see cref="Resources.InstanceIDToObject(int)"/> を使う。
        /// internal な <c>UnityEngine.Object.FindObjectFromInstanceID</c> は Unity 6.1 (6000.3) で
        /// 引数が int → EntityId 構造体へ変わりリフレクション Invoke の厳密型バインドで落ちるが、
        /// この public API は Unity 2021.3〜6000.3 を通じて int 引数のまま据え置かれている。
        /// </summary>
        public static UnityEngine.Object InstanceIDToObject(int instanceId)
        {
            return Resources.InstanceIDToObject(instanceId);
        }
    }
}
