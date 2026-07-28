// Copyright (c) You-Ri, 2026
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// LiveObjectHandle 関連の共通ユーティリティ。
    ///
    /// instanceID 系 API は Unity 6.3 (6000.3) で EntityId 版に置き換えられ、
    /// Unity 6.5 (6000.5) で旧 API と EntityId⇔int の暗黙変換が Obsolete エラーに昇格した
    /// (EntityId は将来 int に収まらなくなるため)。将来の 64bit 化に耐えるよう、
    /// 呼び出し側の ID 表現は long に統一し、バージョン差はこのクラスに閉じ込める。
    /// 値はセッション内でのみ有効で、永続化してはならない。
    /// </summary>
    public static class LiveObjectUtility
    {
        /// <summary>
        /// UnityEngine.Object のセッション内一意 ID を取得する。
        /// </summary>
        public static long GetInstanceID(UnityEngine.Object obj)
        {
#if UNITY_6000_5_OR_NEWER
            return unchecked((long)EntityId.ToULong(obj.GetEntityId()));
#elif UNITY_6000_3_OR_NEWER
            return obj.GetEntityId();
#else
            return obj.GetInstanceID();
#endif
        }

        /// <summary>
        /// <see cref="GetInstanceID"/> が返した ID から生存している UnityEngine.Object を引き当てる。
        /// 見つからない場合は null を返す。メインスレッドからのみ呼び出すこと。
        ///
        /// public API を使う。internal な <c>UnityEngine.Object.FindObjectFromInstanceID</c> は
        /// Unity 6.3 で引数が int → EntityId 構造体へ変わりリフレクション Invoke の
        /// 厳密型バインドで落ちるため使わない。
        /// </summary>
        public static UnityEngine.Object InstanceIDToObject(long instanceId)
        {
#if UNITY_6000_5_OR_NEWER
            return Resources.EntityIdToObject(EntityId.FromULong(unchecked((ulong)instanceId)));
#elif UNITY_6000_3_OR_NEWER
            return Resources.EntityIdToObject(unchecked((int)instanceId));
#else
            return Resources.InstanceIDToObject(unchecked((int)instanceId));
#endif
        }
    }
}
