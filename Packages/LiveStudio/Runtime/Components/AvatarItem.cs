// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Behavior of an avatar-held item prop (loaded from a <c>*.prop.lsb</c> and parented under an
    /// avatar). The item carries its OWN Animator + AnimatorController and hierarchy, so its generic
    /// curves (blendShapes, GameObject toggles, materials) are not affected by the avatar's PlayableGraph
    /// — Unity cannot mix generic curves from two AnimatorControllers, which is exactly why a prop runs
    /// as a separate object.
    ///
    /// Each frame this bridges the avatar's animator parameter values onto the item's own Animator: for
    /// every parameter the item's controller declares, the same-named value is read from the parent
    /// avatar (via <see cref="IAvatarParameterSource"/>) and written here. It also drives the item's own
    /// expression mapping from the avatar's expression weights. The avatar is found by walking up the
    /// parent hierarchy, since the item is its child.
    ///
    /// This is one of the mutually-exclusive prop behavior components (<see cref="IProp"/>): it owns the
    /// socket follow itself through its shared <see cref="attachment"/> (socket name + offsets), in
    /// addition to the parameter bridge + expressions. A plain <see cref="Prop"/> or a
    /// <see cref="AvatarChair"/> takes its place when those behaviors are needed instead.
    /// </summary>
    [DefaultExecutionOrder(20)] // after the avatar (VRCFTAvatar order 10) has written its params this frame
    [RequireComponent(typeof(Animator))]
    [MovedFrom(false, null, null, "AvatarProp")]
    [ExposedClass("Item", Category = "Avatar", Icon = "inventory_2")]
    public class AvatarItem : MonoBehaviour, IProp
    {
        // Shared socket-attachment surface (socket name + offsets + follow math), exposed as a nested
        // "PropAttachment" under this component's @type. The item follows the socket itself (an item has
        // no sibling Prop).
        [SerializeField, ExposedField]
        public PropAttachment attachment = new PropAttachment();

        struct BridgedParameter
        {
            public int nameHash;
            public AnimatorControllerParameterType type;
        }

        Animator _animator;
        IAvatarParameterSource _source;
        BridgedParameter[] _parameters = System.Array.Empty<BridgedParameter>();

        [Header("Expression")]

        // この item 固有の表情マッピング。表情名→item 自身の Animator に書き込む parameters。
        // アバターと同じ VRCExpression 形式で、最大ウェイトの表情の parameters を毎フレーム適用する。
        // ウェイトは親アバターの expressionResolver (smoothedOutputs) を共有して名前で引く。
        [SerializeField]
        [Tooltip("この item の表情マッピング。表情のウェイト最大値のものをアクティブとし、その parameters を item の Animator に書き込む")]
        VRCExpression[] _expressions = Array.Empty<VRCExpression>();

        // GetExpressions で公開する表情キー。_expressions から Start で構築。
        FacialKey[] _expressionKeys = Array.Empty<FacialKey>();

        // item 自身の Animator に書き込む表情ドライバ。最大ウェイト表情の parameters を適用する。
        VRCExpressionDriver _expressionDriver;

        // 親アバターの表情ウェイト供給口。driver はこの GetWeight で各表情の重みを引く。
        // アバターが遅延ロードされる場合があるため _source と同様に Update で再解決する。
        IExpressionAvatar _avatarExpression;

        void Start()
        {
            _animator = GetComponent<Animator>();
            attachment.CaptureBaseScale(transform);
            _CacheParameters();
            _ResolveSource();

            // 表情キーを構築し、item 自身の Animator へ書き込むドライバを用意する。
            var keys = new List<FacialKey>();
            VRCExpressionDriver.AppendExpressionKeys(_expressions, keys);
            _expressionKeys = keys.ToArray();
            _expressionDriver = new VRCExpressionDriver(new AnimatorParameterPort(_animator));

            _ResolveAvatarExpression();
        }

        // item 自身の controller が宣言する非 Trigger パラメータをキャッシュ（毎フレームのブリッジ対象）。
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

        // 親階層からアバターのパラメータ読み出し口を取得（item はアバターの子）。
        void _ResolveSource()
        {
            _source = GetComponentInParent<IAvatarParameterSource>();
        }

        // 親階層からアバターの表情ウェイト供給口を得る（item はアバターの子）。
        void _ResolveAvatarExpression()
        {
            _avatarExpression = GetComponentInParent<IExpressionAvatar>();
        }

        /// <summary>
        /// この item が公開する表情キー一覧。ExpressionController (AvatarExpression) が
        /// アバターの表情と合わせてマージし、item の表情も割り当て可能にする。
        /// </summary>
        public ReadOnlySpan<FacialKey> GetExpressions() => _expressionKeys;

        /// <summary>
        /// Read-only list of this item's facial expressions as bindable slots, mirroring
        /// <see cref="AvatarController.expressions"/>. Lets the remote app see which expressions this
        /// item provides and bind/drive a weight generically via "expressions[name].weight". The
        /// element schema is static while the count is per-item; <see cref="ExpressionEntry.weight"/>
        /// reads/writes through <see cref="ExpressionService"/> (the active avatar that drives this item).
        /// Collapsed by default since the list is long and only needed when binding.
        /// </summary>
        [ExposedProperty, Collapsed]
        public ExpressionEntry[] expressions
        {
            get
            {
                var result = new ExpressionEntry[_expressionKeys.Length];
                for (int i = 0; i < _expressionKeys.Length; i++)
                {
                    result[i] = new ExpressionEntry(_expressionKeys[i].name);
                }
                return result;
            }
        }

        /// <summary>
        /// item の表情マッピングを注入する (変換ツールから呼ばれる)。VRCAvatar.ConfigureExpressions と同形式。
        /// 各 VRCExpression.name は実行時に親アバターの表情ウェイト (smoothedOutputs) を引くキーとして
        /// 参照される (FacialKey 名を前提とする)。エクスポート時に item プレハブへ焼き込む。
        /// </summary>
        public void ConfigureExpressions(VRCExpression[] expressions)
        {
            _expressions = expressions ?? Array.Empty<VRCExpression>();
        }

        void Update()
        {
            // Apply the scale offset every frame so runtime edits take effect immediately (the follow in
            // LateUpdate drives position / rotation only).
            attachment.ApplyScale(transform);

            // パラメータブリッジ: アバターの Animator パラメータを item へ複製する。
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

            // item 固有の表情を駆動する。ウェイトは親アバターの IExpressionAvatar.GetWeight (内部で
            // smoothedOutputs を共有して引く) から取得する。IExpressionAvatar は全アバター型が実装するため、
            // ブリッジ (_source) の有無とは独立に動く。アバター (order 10) が当フレームの Resolve を
            // 済ませた後 (item は order 20) に走る。
            if (_expressions.Length > 0)
            {
                if (_avatarExpression == null) _ResolveAvatarExpression();
                if (_avatarExpression != null)
                {
                    _expressionDriver.Update(_expressions, _avatarExpression);
                }
            }
        }

        void LateUpdate()
        {
            // Follow the socket directly, after the avatar's bones are posed for the frame. The item owns
            // its follow (no sibling Prop), so it drives its own transform here.
            if (attachment.TryResolveFollowTarget(out var worldPos, out var worldRot))
            {
                transform.SetPositionAndRotation(worldPos, worldRot);
            }
        }
    }
}
