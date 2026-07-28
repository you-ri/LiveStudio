// Copyright (c) You-Ri, 2026
// Shared "VRChat Expressions" logic for VRCAvatar / VRCFTAvatar. A VRChat avatar
// switches facial expressions by writing Animator parameter values from its FX layer;
// this driver selects the highest-weight expression each frame and applies its
// AnimationParameterOverride set, restoring the previous defaults on a switch.
//
// The only thing that differs between the two avatar types is *where* the Animator
// parameters live: VRCAvatar writes them on a plain Animator, while VRCFTAvatar wraps
// its AnimatorController inside a PlayableGraph and must write through
// AvatarBodyDriver.controllerPlayable. That difference is isolated behind
// IAnimatorParameterPort so the selection / apply / restore logic stays single-sourced.

using System;
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// VRChat 表情マッピング。表情名と、その表情がアクティブな間に Animator に書き込む
    /// パラメータ群 (AnimationParameterOverride) のペア。VRChat の FX レイヤーが
    /// Animator パラメータ値で表情を切り替える仕組みに合わせる。
    /// </summary>
    [LiveClass]
    [Serializable]
    public class VRCExpression
    {
        [LiveField]
        [Tooltip("表情名 (FacialKey.name)。AvatarExpression の \"Expression.<name>\" InputAction と紐付く")]
        public string name;

        [LiveField(label = "VRCAVATAR_EXPRESSION_PARAMETERS")]
        [Tooltip("この表情がアクティブな間に Animator に書き込むパラメータ")]
        public AnimationParameterOverride[] parameters = Array.Empty<AnimationParameterOverride>();
    }

    /// <summary>
    /// VRCAvatar / VRCFTAvatar が共有する Animator パラメータの読み書き口。
    /// 書き込み先が素の Animator か PlayableGraph 内の AnimatorControllerPlayable かの
    /// 差分のみをここで吸収し、表情選択ロジック (<see cref="VRCExpressionDriver"/>) を単一化する。
    /// </summary>
    internal interface IAnimatorParameterPort
    {
        /// <summary>AnimatorController が割り当て済みで書き込み可能か。</summary>
        bool isReady { get; }

        bool TryGetParameter(string name, out AnimatorControllerParameter result);

        float GetFloat(int nameHash);
        void SetFloat(int nameHash, float value);
        int GetInteger(int nameHash);
        void SetInteger(int nameHash, int value);
        bool GetBool(int nameHash);
        void SetBool(int nameHash, bool value);
    }

    /// <summary>素の <see cref="Animator"/> に直接書き込む port (VRCAvatar 用)。</summary>
    internal sealed class AnimatorParameterPort : IAnimatorParameterPort
    {
        readonly Animator _animator;

        public AnimatorParameterPort(Animator animator)
        {
            _animator = animator;
        }

        public bool isReady => _animator != null && _animator.runtimeAnimatorController != null;

        public bool TryGetParameter(string name, out AnimatorControllerParameter result)
        {
            var parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == name)
                {
                    result = parameters[i];
                    return true;
                }
            }
            result = default;
            return false;
        }

        public float GetFloat(int nameHash) => _animator.GetFloat(nameHash);
        public void SetFloat(int nameHash, float value) => _animator.SetFloat(nameHash, value);
        public int GetInteger(int nameHash) => _animator.GetInteger(nameHash);
        public void SetInteger(int nameHash, int value) => _animator.SetInteger(nameHash, value);
        public bool GetBool(int nameHash) => _animator.GetBool(nameHash);
        public void SetBool(int nameHash, bool value) => _animator.SetBool(nameHash, value);
    }

    /// <summary>
    /// PlayableGraph 内にラップされた AnimatorController 群に書き込む port (VRCFTAvatar 用)。
    /// 書き込みは <see cref="AvatarBodyDriver"/> のブロードキャストメソッドへ委譲し、同名パラメータを
    /// 宣言する全コントローラ（base + 追加レイヤー）へ配る。
    /// </summary>
    internal sealed class ControllerPlayableParameterPort : IAnimatorParameterPort
    {
        readonly AvatarBodyDriver _driver;

        public ControllerPlayableParameterPort(AvatarBodyDriver driver)
        {
            _driver = driver;
        }

        public bool isReady => _driver.hasControllerPlayable;

        public bool TryGetParameter(string name, out AnimatorControllerParameter result)
            => _driver.TryGetParameter(name, out result);

        public float GetFloat(int nameHash) => _driver.GetFloat(nameHash);
        public void SetFloat(int nameHash, float value) => _driver.SetFloat(nameHash, value);
        public int GetInteger(int nameHash) => _driver.GetInteger(nameHash);
        public void SetInteger(int nameHash, int value) => _driver.SetInteger(nameHash, value);
        public bool GetBool(int nameHash) => _driver.GetBool(nameHash);
        public void SetBool(int nameHash, bool value) => _driver.SetBool(nameHash, value);
    }

    /// <summary>
    /// VRChat ExpressionsMenu から移植した表情マッピングを駆動する共通ロジック。
    /// <see cref="IExpressionAvatar.GetWeight"/> で各 VRCExpression.name の重みを引き、最大ウェイトの
    /// 表情の AnimationParameterOverride を <see cref="IAnimatorParameterPort"/> 経由で書き込む。
    /// 切り替わったタイミングのみ実書き込みが走り、解除時は直前の default 値へ戻す。
    /// ウェイトの供給元 (アバター本体 / 装着 prop) は <see cref="IExpressionAvatar"/> として渡され、
    /// driver 自身はその実体 (resolver 等) を知らない。
    /// </summary>
    internal sealed class VRCExpressionDriver
    {
        readonly IAnimatorParameterPort _port;

        int _activeExpressionIndex = -1;   // 現在適用中のエントリ (-1 = なし)
        bool _expressionsResolved;         // 初回の default 値採取が走ったか

        public VRCExpressionDriver(IAnimatorParameterPort port)
        {
            _port = port;
        }

        /// <summary>
        /// 名前が設定された各 VRCExpression を custom FacialKey として keys に追加する。
        /// IExpressionAvatar.GetExpressions が返すキー一覧の構築に使う。
        /// </summary>
        public static void AppendExpressionKeys(VRCExpression[] expressions, List<FacialKey> keys)
        {
            if (expressions == null || keys == null) return;
            foreach (var exp in expressions)
            {
                if (exp != null && !string.IsNullOrEmpty(exp.name))
                {
                    keys.Add(FacialKey.CreateCustom(exp.name));
                }
            }
        }

        /// <summary>
        /// <paramref name="expression"/> から expressions[*].name の重みを引き、最大ウェイトの
        /// 表情の AnimationParameterOverride を Animator に書き込む。切り替わったタイミングのみ実書き込みが
        /// 走る。全表情ウェイトが 0 のときは直前の parameters を default 値に戻して終了。
        /// ウェイト供給元が未 setup の場合は GetWeight が 0 を返すため、表情なしとして扱われる。
        /// </summary>
        public void Update(VRCExpression[] expressions, IExpressionAvatar expression)
        {
            if (expressions == null || expressions.Length == 0) return;
            if (!_port.isReady) return;
            if (expression == null) return;

            // 最大ウェイト (0 < weight) の表情を選択。同値時は配列の先頭優先。
            int bestIndex = -1;
            float bestWeight = 0f;
            for (int i = 0; i < expressions.Length; i++)
            {
                var exp = expressions[i];
                if (exp == null || string.IsNullOrEmpty(exp.name)) continue;
                float w = expression.GetWeight(FacialKey.CreateCustom(exp.name));
                if (w > bestWeight)
                {
                    bestWeight = w;
                    bestIndex = i;
                }
            }

            // 切り替わりなしかつ resolve 済なら何もしない。Animator 値は前回設定のまま保持される。
            if (bestIndex == _activeExpressionIndex && _expressionsResolved) return;

            // 前のアクティブ表情の parameters を default 値に戻す
            if (_activeExpressionIndex >= 0
                && _activeExpressionIndex < expressions.Length
                && expressions[_activeExpressionIndex] != null)
            {
                _RestoreExpressionParameters(expressions[_activeExpressionIndex].parameters);
            }

            // 新しいアクティブ表情の parameters を適用
            if (bestIndex >= 0)
            {
                _ApplyExpressionParameters(expressions[bestIndex].parameters);
            }

            _activeExpressionIndex = bestIndex;
            _expressionsResolved = true;
        }

        void _ApplyExpressionParameters(AnimationParameterOverride[] overrides)
        {
            if (overrides == null) return;
            for (int i = 0; i < overrides.Length; i++)
            {
                var o = overrides[i];
                if (o == null || string.IsNullOrEmpty(o.name)) continue;
                if (!_port.TryGetParameter(o.name, out var param)) continue;
                if (param.type == AnimatorControllerParameterType.Trigger) continue;

                o.type = param.type;

                // AvatarController._ApplyAnimationParameterOverrides と同じく、最初に出現した時点の
                // Animator 値を default として保存しておき、表情解除時に書き戻す。
                if (!o.resolved)
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Float: o.defaultFloat = _port.GetFloat(param.nameHash); break;
                        case AnimatorControllerParameterType.Int: o.defaultInt = _port.GetInteger(param.nameHash); break;
                        case AnimatorControllerParameterType.Bool: o.defaultBool = _port.GetBool(param.nameHash); break;
                    }
                    o.resolved = true;
                }

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float: _port.SetFloat(param.nameHash, o.floatValue); break;
                    case AnimatorControllerParameterType.Int: _port.SetInteger(param.nameHash, o.intValue); break;
                    case AnimatorControllerParameterType.Bool: _port.SetBool(param.nameHash, o.boolValue); break;
                }
            }
        }

        void _RestoreExpressionParameters(AnimationParameterOverride[] overrides)
        {
            if (overrides == null) return;
            for (int i = 0; i < overrides.Length; i++)
            {
                var o = overrides[i];
                if (o == null || !o.resolved || string.IsNullOrEmpty(o.name)) continue;
                if (!_port.TryGetParameter(o.name, out var param)) continue;
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float: _port.SetFloat(param.nameHash, o.defaultFloat); break;
                    case AnimatorControllerParameterType.Int: _port.SetInteger(param.nameHash, o.defaultInt); break;
                    case AnimatorControllerParameterType.Bool: _port.SetBool(param.nameHash, o.defaultBool); break;
                }
            }
        }
    }
}
