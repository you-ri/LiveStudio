using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// ボーンのねじり補正を処理する静的システムクラス
    /// </summary>
    public static class BoneTwistSystem
    {
        /// <summary>
        /// BoneTwistDataを初期化
        /// </summary>
        /// <param name="targetTransform">ねじり補正対象のTransform</param>
        /// <param name="parentTransform">親Transform</param>
        /// <param name="childTransform">子Transform</param>
        /// <param name="weight">補正の重み</param>
        /// <param name="parentChildCrossfade">親子のクロスフェード</param>
        /// <param name="twistAngleOffset">ねじり角度のオフセット</param>
        /// <returns>初期化済みのBoneTwistData</returns>
        public static BoneTwistData Initiate(
            Transform targetTransform,
            Transform parentTransform,
            Transform childTransform,
            float weight = 1.0f,
            float parentChildCrossfade = 0.5f,
            float twistAngleOffset = 0.0f)
        {
            if (targetTransform == null || parentTransform == null || childTransform == null)
            {
                Debug.LogError("[Studio] BoneTwistSystem.Initiate: Transform is null");
                return BoneTwistData.Create();
            }

            BoneTwistData data = BoneTwistData.Create();

            // ねじり軸を計算（子への方向ベクトル）
            data.twistAxis = targetTransform.InverseTransformDirection(
                childTransform.position - targetTransform.position
            );

            // 補助軸を計算（ねじり軸と直交する軸）
            data.axis = new Vector3(data.twistAxis.y, data.twistAxis.z, data.twistAxis.x);

            // ワールド空間での補助軸
            Vector3 axisWorld = targetTransform.rotation * data.axis;

            // 親と子に対する軸の相対方向を保存
            data.axisRelativeToParentDefault = Quaternion.Inverse(parentTransform.rotation) * axisWorld;
            data.axisRelativeToChildDefault = Quaternion.Inverse(childTransform.rotation) * axisWorld;

            // 初期回転を保存
            data.parentInitialRotation = parentTransform.localRotation;
            data.targetInitialRotation = targetTransform.localRotation;
            data.childInitialRotation = childTransform.localRotation;

            // パラメータを設定
            data.weight = Mathf.Clamp01(weight);
            data.parentChildCrossfade = Mathf.Clamp01(parentChildCrossfade);
            data.twistAngleOffset = Mathf.Clamp(twistAngleOffset, -180.0f, 180.0f);

            data.isInitialized = true;

            return data;
        }

        /// <summary>
        /// ねじり補正を処理
        /// </summary>
        /// <param name="data">ねじり補正データ</param>
        /// <param name="targetRotation">ターゲットボーンの現在のワールド回転</param>
        /// <param name="parentRotation">親ボーンの現在のワールド回転</param>
        /// <param name="childRotation">子ボーンの現在のワールド回転</param>
        /// <param name="targetPosition">ターゲットボーンのワールド位置</param>
        /// <param name="parentPosition">親ボーンのワールド位置</param>
        /// <param name="childPosition">子ボーンのワールド位置</param>
        /// <returns>補正後のターゲットボーンのワールド回転</returns>
        public static Quaternion Relax(
            in BoneTwistData data,
            Quaternion targetRotation,
            Quaternion parentRotation,
            Quaternion childRotation,
            Vector3 targetPosition,
            Vector3 parentPosition,
            Vector3 childPosition)
        {
            if (!data.isInitialized || Mathf.Approximately(data.weight, 0.0f))
            {
                return targetRotation;
            }

            // ねじりオフセットを適用
            Quaternion twistOffset = Quaternion.AngleAxis(
                data.twistAngleOffset,
                targetRotation * data.twistAxis
            );
            Quaternion rotation = twistOffset * targetRotation;

            // 親から計算された緩和軸
            Vector3 relaxedAxisParent = twistOffset * parentRotation * data.axisRelativeToParentDefault;

            // 親→ターゲット、ターゲット→子の方向を考慮した回転
            Quaternion f = Quaternion.FromToRotation(
                targetPosition - parentPosition,
                childPosition - targetPosition
            );
            relaxedAxisParent = f * relaxedAxisParent;

            // 子から計算された緩和軸
            Vector3 relaxedAxisChild = twistOffset * childRotation * data.axisRelativeToChildDefault;

            // 親と子の緩和軸をクロスフェード
            Vector3 relaxedAxis = Vector3.Slerp(
                relaxedAxisParent,
                relaxedAxisChild,
                data.parentChildCrossfade
            );

            // relaxedAxisを(axis, twistAxis)空間に変換してねじり角度を計算
            Quaternion r = Quaternion.LookRotation(
                rotation * data.axis,
                rotation * data.twistAxis
            );
            relaxedAxis = Quaternion.Inverse(r) * relaxedAxis;

            // ねじり角度を計算
            float angle = Mathf.Atan2(relaxedAxis.x, relaxedAxis.z) * Mathf.Rad2Deg;

            // ねじり回転を適用（重み付き）
            Quaternion twistRotation = Quaternion.AngleAxis(
                angle * data.weight,
                rotation * data.twistAxis
            );

            return twistRotation * rotation;
        }

        /// <summary>
        /// パラメータのみを更新
        /// </summary>
        /// <param name="data">更新対象のBoneTwistData</param>
        /// <param name="weight">補正の重み</param>
        /// <param name="parentChildCrossfade">親子のクロスフェード</param>
        /// <param name="twistAngleOffset">ねじり角度のオフセット</param>
        public static void UpdateParameters(
            ref BoneTwistData data,
            float weight,
            float parentChildCrossfade,
            float twistAngleOffset)
        {
            data.weight = Mathf.Clamp01(weight);
            data.parentChildCrossfade = Mathf.Clamp01(parentChildCrossfade);
            data.twistAngleOffset = Mathf.Clamp(twistAngleOffset, -180.0f, 180.0f);
        }

        /// <summary>
        /// デフォルト回転に戻す
        /// </summary>
        public static Quaternion FixTransform(in BoneTwistData data)
        {
            return data.targetInitialRotation;
        }
    }
}
