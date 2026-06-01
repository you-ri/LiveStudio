// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Static channel that broadcasts "the hierarchy under <paramref name="owner"/> GameObject
    /// may have changed" to all subscribed <see cref="TransformRef"/> instances.
    ///
    /// Owners that mutate their internal Transform hierarchy at runtime (e.g. AvatarController
    /// swapping the avatar GameObject, dynamic prefab spawn, etc.) call
    /// <see cref="NotifyStructureChanged(GameObject)"/> after the new hierarchy is in place.
    /// Each <see cref="TransformRef"/> compares <c>owner.name</c> against its own
    /// <c>ownerName</c> and, on match, re-fires its <c>onChanged</c> so the holder (e.g.
    /// <c>ExposedGameObjectWithTransform</c>) re-resolves and re-attaches.
    ///
    /// GameObject 引数を採用する理由: TransformRef.ownerName は GameObject の name 文字列で
    /// 永続化されるため、broadcasting する側も「どの GameObject の配下が変わったか」を
    /// 直接渡すのが整合的。ExposedObject インスタンスを経由する必要がなく、owner が
    /// ExposedObject として登録されていないケース (例: MonoBehaviour が直接 ExposedObject 化
    /// されていない AvatarController) でも自然に動く。
    /// </summary>
    public static class TransformStructureService
    {
        /// <summary>
        /// Fires when the hierarchy under <paramref name="owner"/> may have changed.
        /// Subscribers should compare <c>owner.name</c> against their stored ownerName.
        /// </summary>
        public static event Action<GameObject> onStructureChanged;

        public static void NotifyStructureChanged(GameObject owner)
        {
            if (owner == null) return;
            onStructureChanged?.Invoke(owner);
        }
    }
}
