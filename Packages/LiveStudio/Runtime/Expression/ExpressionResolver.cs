// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// 表情計算の抽象基底クラス
    /// ICameraControllerと同じ [SerializeReference, Select] パターンで利用可能
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "psychology")]
    [MovedFrom(false, "Lilium.Virgo.Studio", "Lilium.Virgo.Studio2", null)]
    public abstract class IExpressionResolver
    {
        /// <summary>
        /// 表情設定
        /// </summary>
        public abstract AvatarExpressionConfig expressionConfig { get; set; }

        public abstract void Setup();
        public abstract void Dispose();

        /// <summary>
        /// 外部からの表情ウェイト設定（IAvatar.SetWeightから委譲）
        /// </summary>
        public abstract void SetWeight(string name, float weight);

        /// <summary>
        /// 外部からの表情ウェイト削除
        /// </summary>
        public abstract void RemoveWeight(string name);

        /// <summary>
        /// 毎フレーム呼び出し: ARKitWeightData → 計算結果を内部に保持
        /// </summary>
        public abstract void Resolve(in ARKitWeightData arkitWeightData);

        /// <summary>
        /// ARKitウェイトデータ（ApplyBlink, ApplyFacialExpressionsで使用）
        /// </summary>
        public abstract ARKitWeightData arkitWeightData { get; }

        /// <summary>
        /// 目線方向（ApplyLookAtで使用）
        /// </summary>
        public abstract Vector2 eyeDirection { get; }

        /// <summary>
        /// スムージング後の表情ウェイト（ApplySmoothedWeightsで使用）
        /// </summary>
        public abstract ExpressionWeights smoothedOutputs { get; }

        /// <summary>
        /// 全ウェイトの合計値
        /// </summary>
        public abstract float totalWeight { get; }

        /// <summary>
        /// セットアップ済みかどうか
        /// </summary>
        public abstract bool isSetup { get; }
    }
}
