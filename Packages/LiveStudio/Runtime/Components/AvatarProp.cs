// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Root component of an avatar-owned prop object (loaded from a <c>*.prop.lsb</c>
    /// and parented under an avatar). The prop carries its OWN Animator + AnimatorController
    /// and hierarchy, so its generic curves (blendShapes, GameObject toggles, materials) are not
    /// affected by the avatar's PlayableGraph — Unity cannot mix generic curves from two
    /// AnimatorControllers, which is exactly why a prop runs as a separate object.
    ///
    /// Each frame this bridges the avatar's animator parameter values onto the prop's own
    /// Animator: for every parameter the prop's controller declares, the same-named value is
    /// read from the parent avatar (via <see cref="IAvatarParameterSource"/>) and written here.
    /// The avatar is found by walking up the parent hierarchy, since the prop is its child.
    /// </summary>
    [DefaultExecutionOrder(20)] // after the avatar (VRCFTAvatar order 10) has written its params this frame
    [RequireComponent(typeof(Animator))]
    [ExposedClass("Prop", Category = "Avatar", Icon = "deployed_code")]
    public class AvatarProp : MonoBehaviour
    {
        struct BridgedParameter
        {
            public int nameHash;
            public AnimatorControllerParameterType type;
        }

        Animator _animator;
        IAvatarParameterSource _source;
        BridgedParameter[] _parameters = System.Array.Empty<BridgedParameter>();

        // 親アバターの humanoid Animator (GetBoneTransform 用)。アバターは遅延ロードされ得るため
        // 解決できるまで Update で再試行する。
        Animator _avatarAnimator;
        // 指定 bone への ParentConstraint。アバター解決後に生成する。
        ParentConstraint _constraint;
        // 現在 source として適用済みの bone。_targetBone と異なれば source を作り直す。
        HumanBodyBones _appliedBone = (HumanBodyBones)(-1);

        [ExposedProperty("name"), Hide]
        public string displayName => this.name;

        // 拘束先の humanoid bone。値変更は Update が検知して source を作り直す。
        // Shadow field (Hide + FormerlyExposedAs) so the [ExposedProperty] below is persistable:
        // its value is serialized to the live scene and restored across a prop unload/reload.
        [SerializeField, ExposedField, Hide, FormerlyExposedAs("targetBone")]
        HumanBodyBones _targetBone = HumanBodyBones.Hips;

        [ExposedProperty]
        public HumanBodyBones targetBone
        {
            get => _targetBone;
            set => _targetBone = value;
        }

        // bone ローカルの位置オフセット。
        [SerializeField, ExposedField, Hide, FormerlyExposedAs("positionOffset")]
        Vector3 _positionOffset = Vector3.zero;

        [ExposedProperty]
        public Vector3 positionOffset
        {
            get => _positionOffset;
            set => _positionOffset = value;
        }

        // bone ローカルの回転オフセット (euler 度)。
        [SerializeField, ExposedField, Hide, FormerlyExposedAs("rotationOffset")]
        Vector3 _rotationOffset = Vector3.zero;

        [ExposedProperty]
        public Vector3 rotationOffset
        {
            get => _rotationOffset;
            set => _rotationOffset = value;
        }

        void Start()
        {
            _animator = GetComponent<Animator>();
            _CacheParameters();
            _ResolveSource();
        }

        // 自身の Animator を除く、親階層で最初に見つかる humanoid Animator を返す。
        // IAvatar は Animator を公開しないため、この疎結合な探索で取得する。
        Animator _ResolveAvatarAnimator()
        {
            var animators = GetComponentsInParent<Animator>(includeInactive: true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != _animator && animators[i].isHuman) return animators[i];
            }
            return null;
        }

        // 指定 bone へ ParentConstraint で追従させる。アバターは遅延ロードされ得るので解決できるまで
        // 毎フレーム再試行し、bone 変更時のみ source を作り直す。offset は毎フレーム反映するので
        // RemoteApp や Inspector でのランタイム変更が即座に効く。
        void _UpdateBoneConstraint()
        {
            if (_avatarAnimator == null)
            {
                _avatarAnimator = _ResolveAvatarAnimator();
                if (_avatarAnimator == null) return; // アバター未ロード。次フレーム再試行。
            }

            if (_constraint == null)
                _constraint = GetComponent<ParentConstraint>() ?? gameObject.AddComponent<ParentConstraint>();

            if (_appliedBone != _targetBone || _constraint.sourceCount == 0)
            {
                _appliedBone = _targetBone;

                var bone = _avatarAnimator.GetBoneTransform(_targetBone);
                if (bone == null)
                {
                    // 指定 bone を持たないアバター。拘束を無効化して追従を止める。
                    _constraint.constraintActive = false;
                    while (_constraint.sourceCount > 0) _constraint.RemoveSource(0);
                    return;
                }

                // source / offset を書き換える前に locked を解除し、書き込み後に再ロックする。
                // 先に locked=true だと Unity が rest/offset を取り違える。
                _constraint.locked = false;
                while (_constraint.sourceCount > 0) _constraint.RemoveSource(0);
                _constraint.AddSource(new ConstraintSource { sourceTransform = bone, weight = 1f });
                _constraint.weight = 1f;
                _constraint.constraintActive = true;
                _constraint.locked = true;
            }

            if (_constraint.sourceCount > 0)
            {
                // offset は毎フレーム反映 (ランタイム変更を即時に効かせる)。locked=true でも上書き可能。
                _constraint.SetTranslationOffset(0, _positionOffset);
                _constraint.SetRotationOffset(0, _rotationOffset);
            }
        }

        // prop 自身の controller が宣言する非 Trigger パラメータをキャッシュ（毎フレームのブリッジ対象）。
        void _CacheParameters()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                _parameters = System.Array.Empty<BridgedParameter>();
                return;
            }

            var src = _animator.parameters;
            var list = new List<BridgedParameter>(src.Length);
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i].type == AnimatorControllerParameterType.Trigger) continue;
                list.Add(new BridgedParameter { nameHash = src[i].nameHash, type = src[i].type });
            }
            _parameters = list.ToArray();
        }

        // 親階層からアバターのパラメータ読み出し口を取得（prop はアバターの子）。
        void _ResolveSource()
        {
            _source = GetComponentInParent<IAvatarParameterSource>();
        }

        void Update()
        {
            _UpdateBoneConstraint();

            if (_source == null)
            {
                _ResolveSource();
                if (_source == null) return;
            }

            for (int i = 0; i < _parameters.Length; i++)
            {
                var p = _parameters[i];
                if (!_source.HasParameter(p.nameHash)) continue;

                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float:
                        _animator.SetFloat(p.nameHash, _source.GetFloat(p.nameHash));
                        break;
                    case AnimatorControllerParameterType.Int:
                        _animator.SetInteger(p.nameHash, _source.GetInteger(p.nameHash));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        _animator.SetBool(p.nameHash, _source.GetBool(p.nameHash));
                        break;
                }
            }
        }
    }
}
