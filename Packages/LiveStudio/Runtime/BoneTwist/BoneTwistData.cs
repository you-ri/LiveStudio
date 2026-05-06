using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// ボーンのねじり補正のための軸を定義
    /// </summary>
    public enum TwistAxis
    {
        X,
        Y,
        Z
    }

    /// <summary>
    /// ボーンのねじり補正設定データ（unmanaged struct）
    /// GCAllocを回避し、高速なコピーを可能にする
    /// </summary>
    public struct BoneTwistData
    {
        /// <summary>
        /// 親ボーンの初期ローカル回転
        /// </summary>
        public Quaternion parentInitialRotation;

        /// <summary>
        /// ターゲットボーン（自身）の初期ローカル回転
        /// </summary>
        public Quaternion targetInitialRotation;

        /// <summary>
        /// 子ボーンの初期ローカル回転
        /// </summary>
        public Quaternion childInitialRotation;

        /// <summary>
        /// ねじり軸（ローカル空間）
        /// </summary>
        public Vector3 twistAxis;

        /// <summary>
        /// 補助軸（ローカル空間）
        /// </summary>
        public Vector3 axis;

        /// <summary>
        /// 親に対する軸の相対方向（初期状態）
        /// </summary>
        public Vector3 axisRelativeToParentDefault;

        /// <summary>
        /// 子に対する軸の相対方向（初期状態）
        /// </summary>
        public Vector3 axisRelativeToChildDefault;

        /// <summary>
        /// ねじり補正の重み（0.0～1.0）
        /// </summary>
        public float weight;

        /// <summary>
        /// 親と子のクロスフェード（0.0=親に追従、1.0=子に追従、0.5=中間）
        /// </summary>
        public float parentChildCrossfade;

        /// <summary>
        /// ねじり角度のオフセット（度数法、-180～180）
        /// </summary>
        public float twistAngleOffset;

        /// <summary>
        /// 初期化済みフラグ
        /// </summary>
        public bool isInitialized;

        /// <summary>
        /// デフォルトのBoneTwistDataを作成
        /// </summary>
        public static BoneTwistData Create()
        {
            return new BoneTwistData
            {
                parentInitialRotation = Quaternion.identity,
                targetInitialRotation = Quaternion.identity,
                childInitialRotation = Quaternion.identity,
                twistAxis = Vector3.right,
                axis = Vector3.forward,
                axisRelativeToParentDefault = Vector3.forward,
                axisRelativeToChildDefault = Vector3.forward,
                weight = 1.0f,
                parentChildCrossfade = 0.5f,
                twistAngleOffset = 0.0f,
                isInitialized = false
            };
        }
    }
}
