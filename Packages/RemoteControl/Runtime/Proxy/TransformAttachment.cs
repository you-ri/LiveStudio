// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// <see cref="TransformRef"/> の解決結果に基づいて Transform.parent を実際に付け替える静的ヘルパ。
    /// TransformRef 自身は「どこを指しているか」の検索責務だけを持ち、
    /// アタッチ状態 (現在ぶら下がっている親) のキャッシュと SetParent 副作用はこのクラスに分離している。
    ///
    /// 呼び出し側は <c>ref Transform attached</c> を渡してアタッチ状態を保持する。
    /// 同じ TransformRef を複数の self に付けたい場合でも、それぞれが独立した attached キャッシュを持てる。
    /// </summary>
    public static class TransformAttachment
    {
        /// <summary>
        /// reference の解決結果を self の親としてアタッチする。冪等。
        /// </summary>
        /// <param name="reference">参照先を解決する <see cref="TransformRef"/>。null の場合は何もしない。</param>
        /// <param name="self">親を付け替える対象の Transform。null の場合は何もしない。</param>
        /// <param name="attached">前回アタッチした Transform を保持するキャッシュ。冪等性と detach タイミング判定に使う。</param>
        public static void Attach(TransformRef reference, Transform self, ref Transform attached)
        {
            if (reference == null || self == null) return;

            var target = reference.Resolve();

            // 親が指定されているのに解決できない = 親 ExposedObject がまだ registry に未登録の未解決状態。
            // Unity hierarchy の現状を維持し、親が登録された後の再評価に委ねる。
            // (Play mode 突入時の OnEnable 順で親が後から登録されるケース対策)
            if (target == null && reference.hasOwner)
            {
                return;
            }

            if (attached != null && attached != target)
            {
                GameObjectUtility.SetTransformParent(self, null, "Reparent ExposedObject");
                attached = null;
            }

            if (target == null)
            {
                // parentId 未指定 = 明示的な root 配置の意図
                if (self.parent != null)
                    GameObjectUtility.SetTransformParent(self, null, "Reparent ExposedObject");
                return;
            }

            if (attached == target) return;

            attached = target;
            GameObjectUtility.SetTransformParent(self, target, "Reparent ExposedObject");
        }

        /// <summary>
        /// self を scene root に戻し、アタッチキャッシュをクリアする。
        /// </summary>
        public static void Detach(Transform self, ref Transform attached)
        {
            if (attached == null) return;
            if (self != null)
                GameObjectUtility.SetTransformParent(self, null, "Detach ExposedObject");
            attached = null;
        }
    }
}
