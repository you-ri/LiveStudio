using System;
using System.Runtime.InteropServices;
using Lilium.RemoteControl;
using UnityEngine;

namespace Lilium.LiveStudio
{
    [Serializable]
    public struct HumanLimit
    {
        public Vector3 min;

        public Vector3 max;

        public Vector3 center;

        public float axisLength;

        public bool useDefaultValues;
    }

    [Serializable]
    public struct HumanBone
    {
        public string boneName;

        public string humanName;

        public HumanLimit limit;
    }

    [Serializable]
    public struct SkeletonBone
    {
        public string name;

        public Vector3 position;

        public Quaternion rotation;

        public Vector3 scale;
    }


    /// <summary>
    /// Runtimeでヒューマノイド用Avatarを構築するためのデータ
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct AvatarBuildData
    {
        /// <summary>
        /// ヒューマノイドボーンのマッピング情報
        /// </summary>
        public HumanBone[] humanBones;

        /// <summary>
        /// スケルトンボーンの階層情報
        /// </summary>
        public SkeletonBone[] skeletonBones;

        /// <summary>
        /// 各スケルトンボーンの親のインデックス（-1 = ルート）
        /// </summary>
        public int[] skeletonBoneParentIndices;

        /// <summary>
        /// 上腕のTwist設定（0.0 - 1.0）
        /// </summary>
        public float upperArmTwist;

        /// <summary>
        /// 前腕のTwist設定（0.0 - 1.0）
        /// </summary>
        public float lowerArmTwist;

        /// <summary>
        /// 大腿のTwist設定（0.0 - 1.0）
        /// </summary>
        public float upperLegTwist;

        /// <summary>
        /// 下腿のTwist設定（0.0 - 1.0）
        /// </summary>
        public float lowerLegTwist;

        /// <summary>
        /// 腕の伸縮許容値
        /// </summary>
        public float armStretch;

        /// <summary>
        /// 脚の伸縮許容値
        /// </summary>
        public float legStretch;

        /// <summary>
        /// 足の間隔
        /// </summary>
        public float feetSpacing;

        /// <summary>
        /// 移動の自由度があるか
        /// </summary>
        public bool hasTranslationDoF;
    }
}
