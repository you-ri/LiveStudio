// Copyright (c) You-Ri, 2026

using UnityEngine;

namespace Lilium.LiveStudio
{
    public enum ExpressionMode
    {
        Preset,
        PerfectSync,
    }

    public interface IAvatar : IExpressionAvatar
    {
        public void BuildAvatar();

        public void SetExpressionConfig(AvatarExpressionConfig config);

        public void ResetPhysics();

        public void SetMotionSource(MotionSourceBase motionSource);

        /// <summary>
        /// トラッキングされていない部位の姿勢を上書きするアニメーションクリップを設定する。
        /// AvatarBodyDriver を使うアバター (VRM1Avatar / VRCFTAvatar) は未トラッキング部位へ
        /// このクリップの姿勢を流し込む。対応しないアバターは無視してよい。null で解除。
        /// </summary>
        public void SetBodyOverrideClip(AnimationClip clip);

        /// <summary>
        /// 下半身の位置をロックする。ON の間、AvatarBodyDriver を使うアバターは hips のローカル位置を
        /// body override クリップに固定し、root (アバター全体) を位置・回転とも anchor に固定する。
        /// 各ボーンの回転は mocap を反映しつつ、両足は脚の 2 ボーン IK で接地位置に固定される。
        /// 対応しないアバターは無視してよい。
        /// </summary>
        public void SetLowerBodyPoseLock(bool locked);
    }
}
