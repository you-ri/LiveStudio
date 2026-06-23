// Copyright (c) You-Ri, 2026

using System;
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
    // The socket follow needs a ParentConstraint; require it so authored prop prefabs carry one
    // (added at edit time). Runtime AddComponent<ParentConstraint> is unreliable on some objects,
    // so the component must already exist on the prefab.
    [RequireComponent(typeof(ParentConstraint))]
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

        // Parent avatar's humanoid Animator, used to scope socket lookup to this avatar. The avatar
        // may load lazily, so resolution is retried in Update until it succeeds.
        Animator _avatarAnimator;
        // ParentConstraint onto the resolved socket. Created once the avatar is available.
        ParentConstraint _constraint;
        // Socket name currently applied as the constraint source. When it differs from _socketName
        // the source is rebuilt.
        string _appliedSocketName;

        [ExposedProperty("name"), Hide]
        public string displayName => this.name;

        // Target socket name. The prop attaches to the avatar socket with this name. Sockets are
        // created per-avatar with a normalized (avatar-root-aligned) orientation, so a single offset
        // works across models regardless of their bone axis conventions. Value changes are detected
        // by Update, which rebuilds the constraint source.
        // Shadow field (Hide + FormerlyExposedAs) so the [ExposedProperty] below is persistable:
        // its value is serialized to the live scene and restored across a prop unload/reload.
        [SerializeField, ExposedField, Hide, FormerlyExposedAs("socketName")]
        string _socketName = "WristRight";

        [ExposedProperty]
        [StringSelector(nameof(availableSocketNames))]
        public string socketName
        {
            get => _socketName;
            set => _socketName = value;
        }

        // Socket names available on the parent avatar, surfaced to the RemoteApp dropdown.
        [ExposedProperty, Hide]
        public string[] availableSocketNames
        {
            get
            {
                if (_avatarAnimator == null) _avatarAnimator = _ResolveAvatarAnimator();
                if (_avatarAnimator == null) return System.Array.Empty<string>();

                var sockets = _avatarAnimator.GetComponentsInChildren<Socket>(includeInactive: true);
                var names = new string[sockets.Length];
                for (int i = 0; i < sockets.Length; i++) names[i] = sockets[i].socketName;
                return names;
            }
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

        // 拡縮オフセット。プレハブ本来の localScale への乗算 (1 で等倍)。socket 追従は位置・回転だけを
        // 駆動し scale には触れないので、prop のサイズはここで毎フレーム適用する。アバターのスケールは
        // 親 (avatarRoot) を通じて lossyScale に伝播するため、ここでは socket スケールで補正しない。
        [SerializeField, ExposedField, Hide]
        Vector3 _scaleOffset = Vector3.one;

        [ExposedProperty]
        public Vector3 scaleOffset
        {
            get => _scaleOffset;
            set => _scaleOffset = value;
        }

        // プレハブ本来の localScale。scaleOffset はこれへの乗算として適用する。Start で一度だけ取得。
        Vector3 _baseScale = Vector3.one;

        [Header("Expression")]

        // この prop 固有の表情マッピング。表情名→prop 自身の Animator に書き込む parameters。
        // アバターと同じ VRCExpression 形式で、最大ウェイトの表情の parameters を毎フレーム適用する。
        // ウェイトは親アバターの expressionResolver (smoothedOutputs) を共有して名前で引く。
        [SerializeField]
        [Tooltip("この prop の表情マッピング。表情のウェイト最大値のものをアクティブとし、その parameters を prop の Animator に書き込む")]
        VRCExpression[] _expressions = Array.Empty<VRCExpression>();

        // GetExpressions で公開する表情キー。_expressions から Start で構築。
        FacialKey[] _expressionKeys = Array.Empty<FacialKey>();

        // prop 自身の Animator に書き込む表情ドライバ。最大ウェイト表情の parameters を適用する。
        VRCExpressionDriver _expressionDriver;

        // 親アバターの表情ウェイト供給口。driver はこの GetWeight で各表情の重みを引く。
        // アバターが遅延ロードされる場合があるため _source と同様に Update で再解決する。
        IExpressionAvatar _avatarExpression;

        void Start()
        {
            _animator = GetComponent<Animator>();
            _baseScale = transform.localScale;
            _CacheParameters();
            _ResolveSource();

            // 表情キーを構築し、prop 自身の Animator へ書き込むドライバを用意する。
            var keys = new List<FacialKey>();
            VRCExpressionDriver.AppendExpressionKeys(_expressions, keys);
            _expressionKeys = keys.ToArray();
            _expressionDriver = new VRCExpressionDriver(new AnimatorParameterPort(_animator));

            _ResolveAvatarExpression();
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

        // Follow the named socket via a ParentConstraint. The avatar may load lazily, so resolution
        // is retried each frame until the socket exists; the source is rebuilt only when the socket
        // name changes. Offsets are pushed every frame so runtime edits (RemoteApp / Inspector) apply
        // immediately. The socket carries a normalized orientation, so the offset is model-independent.
        void _UpdateSocketConstraint()
        {
            if (_avatarAnimator == null)
            {
                _avatarAnimator = _ResolveAvatarAnimator();
                if (_avatarAnimator == null) return; // Avatar not loaded yet; retry next frame.
            }

            if (_constraint == null)
            {
                // [RequireComponent] makes authored prefabs carry a ParentConstraint. Fall back to adding
                // one for older bundles exported before that — but AddComponent<ParentConstraint> can fail
                // (returns a Unity-null) on some objects, so bail out instead of dereferencing it.
                _constraint = GetComponent<ParentConstraint>();
                if (_constraint == null) _constraint = gameObject.AddComponent<ParentConstraint>();
                if (_constraint == null) return; // socket follow unavailable; retry next frame.
            }

            if (_appliedSocketName != _socketName || _constraint.sourceCount == 0)
            {
                var socket = _ResolveSocket(_socketName);
                if (socket == null)
                {
                    // Socket not present on this avatar (not created yet, or unknown name). Disable the
                    // constraint and retry next frame WITHOUT latching _appliedSocketName, so a socket
                    // that registers later is still picked up.
                    _constraint.constraintActive = false;
                    while (_constraint.sourceCount > 0) _constraint.RemoveSource(0);
                    return;
                }

                _appliedSocketName = _socketName;

                // Unlock before rewriting source / offset, then re-lock. Setting locked=true first makes
                // Unity mistake the rest/offset.
                _constraint.locked = false;
                while (_constraint.sourceCount > 0) _constraint.RemoveSource(0);
                _constraint.AddSource(new ConstraintSource { sourceTransform = socket.transform, weight = 1f });
                _constraint.weight = 1f;
                _constraint.constraintActive = true;
                _constraint.locked = true;
            }

            if (_constraint.sourceCount > 0)
            {
                // Push offsets every frame so runtime edits apply immediately (works even while locked).
                // ParentConstraint does NOT multiply the translation offset by the source's scale, so a
                // raw offset stays a fixed distance while the bone moves with the avatar's scale, drifting
                // the prop. Pre-scale by the socket's lossyScale (which carries the avatar scale) so the
                // offset tracks the avatar size, matching what true parenting to the bone would do. The
                // offset is authored in normalized (scale-1) bone-local space. Rotation needs no scaling.
                var socketTransform = _constraint.GetSource(0).sourceTransform;
                var socketScale = socketTransform != null ? socketTransform.lossyScale : Vector3.one;
                _constraint.SetTranslationOffset(0, Vector3.Scale(_positionOffset, socketScale));
                _constraint.SetRotationOffset(0, _rotationOffset);
            }
        }

        // Resolve a Socket by name within the parent avatar. Scoping the lookup to this avatar avoids
        // picking up another avatar's identically-named socket. Allocates, so callers only invoke it
        // on a socket change.
        Socket _ResolveSocket(string name)
        {
            if (_avatarAnimator == null || string.IsNullOrEmpty(name)) return null;
            var sockets = _avatarAnimator.GetComponentsInChildren<Socket>(includeInactive: true);
            for (int i = 0; i < sockets.Length; i++)
            {
                if (sockets[i].socketName == name) return sockets[i];
            }
            return null;
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

        // 親階層からアバターの表情ウェイト供給口を得る（prop はアバターの子）。
        void _ResolveAvatarExpression()
        {
            _avatarExpression = GetComponentInParent<IExpressionAvatar>();
        }

        /// <summary>
        /// この prop が公開する表情キー一覧。ExpressionController (AvatarExpression) が
        /// アバターの表情と合わせてマージし、prop の表情も割り当て可能にする。
        /// </summary>
        public ReadOnlySpan<FacialKey> GetExpressions() => _expressionKeys;

        /// <summary>
        /// prop の表情マッピングを注入する (変換ツールから呼ばれる)。VRCAvatar.ConfigureExpressions と同形式。
        /// 各 VRCExpression.name は実行時に親アバターの表情ウェイト (smoothedOutputs) を引くキーとして
        /// 参照される (FacialKey 名を前提とする)。エクスポート時に prop プレハブへ焼き込む。
        /// </summary>
        public void ConfigureExpressions(VRCExpression[] expressions)
        {
            _expressions = expressions ?? Array.Empty<VRCExpression>();
        }

        void Update()
        {
            _UpdateSocketConstraint();

            // Apply the scale offset every frame so runtime edits (RemoteApp / Inspector) take effect
            // immediately. The socket follow (ParentConstraint) drives position/rotation only, so scale
            // is owned here; the avatar's own scale still propagates through the parent's lossyScale.
            transform.localScale = Vector3.Scale(_baseScale, _scaleOffset);

            // パラメータブリッジ: アバターの Animator パラメータを prop へ複製する。
            // IAvatarParameterSource は VRCFTAvatar / VRCAvatar のみ実装するため、VRM/Standard 配下では
            // _source は null のまま。その場合はブリッジをスキップするだけで、表情駆動 (下) は続行する。
            if (_source == null) _ResolveSource();
            if (_source != null)
            {
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

            // prop 固有の表情を駆動する。ウェイトは親アバターの IExpressionAvatar.GetWeight (内部で
            // smoothedOutputs を共有して引く) から取得する。IExpressionAvatar は全アバター型が実装するため、
            // ブリッジ (_source) の有無とは独立に動く。アバター (order 10) が当フレームの Resolve を
            // 済ませた後 (prop は order 20) に走る。
            if (_expressions.Length > 0)
            {
                if (_avatarExpression == null) _ResolveAvatarExpression();
                if (_avatarExpression != null)
                {
                    _expressionDriver.Update(_expressions, _avatarExpression);
                }
            }
        }
    }
}
