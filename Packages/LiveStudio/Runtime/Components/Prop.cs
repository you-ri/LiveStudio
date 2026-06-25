// Copyright (c) You-Ri, 2026

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Implemented by a sibling component that drives the prop transform itself (e.g. with damping /
    /// deadzone) instead of letting <see cref="Prop"/> apply the socket follow. When such a sibling is
    /// present, <see cref="Prop"/> computes the follow target (via <see cref="Prop.TryResolveFollowTarget"/>)
    /// but does not move the transform. The override is generic: <see cref="Prop"/> knows nothing about
    /// what filtering the sibling applies.
    /// </summary>
    public interface IPropFollowOverride { }

    /// <summary>
    /// Shared attachment component for a prop loaded under an avatar (from a <c>*.prop.lsb</c>). It binds
    /// the prop to a named <see cref="Socket"/> on the avatar — following its world pose each frame with
    /// authored position / rotation offsets — and applies a scale offset. This is the common, controllable
    /// surface (exposed as <c>"Prop"</c>); behavior components such as <see cref="AvatarItem"/> (avatar
    /// parameter bridge + expressions) and <see cref="AvatarChair"/> (chair controls + damped follow) live
    /// alongside it on the same GameObject.
    ///
    /// Sockets are created per-avatar with a normalized (avatar-root-aligned) orientation, so a single
    /// offset works across models regardless of their bone axis conventions. The follow is applied
    /// directly to the transform in <see cref="LateUpdate"/> (after the avatar's bones are posed for the
    /// frame); offsets / scale are read every frame so runtime edits apply immediately.
    /// </summary>
    [DefaultExecutionOrder(20)] // after the avatar (VRCFTAvatar order 10) has posed its bones this frame
    [ExposedClass("Prop", Category = "Avatar", Icon = "deployed_code")]
    public class Prop : MonoBehaviour
    {
        // Avatar's humanoid Animator, resolved through the avatar service (NOT the parent chain) so the
        // socket can be found regardless of where this prop is parented — an item lives under the avatar,
        // but a chair re-parents itself under the AvatarController to escape avatar root motion. The
        // avatar may load lazily, so resolution is retried until it succeeds.
        Animator _avatarAnimator;

        // Cached socket. The follow runs every frame, so the allocating lookup must not run per frame;
        // re-resolved only when the socket name changes or the socket is destroyed (avatar swap).
        Socket _resolvedSocket;

        // True when a sibling implements IPropFollowOverride and owns the transform follow. Resolved once
        // in Start (siblings do not change at runtime on a loaded prop).
        bool _followOverridden;

        [ExposedProperty("name"), Hide]
        public string displayName => this.name;

        // Target socket name. The prop attaches to the avatar socket with this name.
        // Shadow field (Hide + FormerlyExposedAs) so the [ExposedProperty] below is persistable: its value
        // is serialized to the live scene and restored across a prop unload/reload.
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

        void Start()
        {
            _baseScale = transform.localScale;
            // A follow-override sibling owns the transform follow; Prop must not also drive it.
            _followOverridden = GetComponent<IPropFollowOverride>() != null;
        }

        void Update()
        {
            // Apply the scale offset every frame so runtime edits take effect immediately. The follow
            // drives position/rotation only, so scale is owned here; the avatar's own scale still
            // propagates through the parent's lossyScale.
            transform.localScale = Vector3.Scale(_baseScale, _scaleOffset);
        }

        void LateUpdate()
        {
            // Follow the socket directly, after the avatar's bones are posed for the frame. An override
            // sibling (e.g. AvatarChair) applies its own filtered follow instead.
            if (_followOverridden) return;
            if (TryResolveFollowTarget(out var worldPos, out var worldRot))
            {
                transform.SetPositionAndRotation(worldPos, worldRot);
            }
        }

        /// <summary>
        /// Computes the world pose the prop should follow this frame: the resolved socket pose composed
        /// with the authored position/rotation offsets. Returns false until the avatar and socket are
        /// available. Used by <see cref="LateUpdate"/> for the rigid follow and by an
        /// <see cref="IPropFollowOverride"/> sibling that applies its own filtered follow (e.g. damping /
        /// deadzone). The socket is cached, so this is allocation-free per frame.
        /// </summary>
        internal bool TryResolveFollowTarget(out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = default;
            worldRot = default;

            var socket = _EnsureSocket();
            if (socket == null) return false;

            var st = socket.transform;
            // Rotation offset in normalized bone-local space; translation offset pre-scaled by the socket's
            // lossyScale so it tracks the avatar size (the socket carries the avatar scale).
            worldRot = st.rotation * Quaternion.Euler(_rotationOffset);
            worldPos = st.position + st.rotation * Vector3.Scale(_positionOffset, st.lossyScale);
            return true;
        }

        /// <summary>
        /// The resolved socket's transform (raw socket pose, without this prop's offsets), or null until
        /// the avatar and socket are available. Lets a follow-override sibling (e.g. <see cref="AvatarChair"/>)
        /// read the bone pose directly to drive its own axes. Cached, so allocation-free per frame.
        /// </summary>
        internal Transform ResolveSocketTransform()
        {
            var socket = _EnsureSocket();
            return socket != null ? socket.transform : null;
        }

        // Resolves and caches the target socket. Allocates only when the socket name changes or the cached
        // socket is destroyed (avatar swap); returns null until the avatar and socket exist.
        Socket _EnsureSocket()
        {
            if (_avatarAnimator == null)
            {
                _avatarAnimator = _ResolveAvatarAnimator();
                if (_avatarAnimator == null) return null;
            }
            // Unity-null when the previous avatar's socket was destroyed → re-resolve.
            if (_resolvedSocket == null || _resolvedSocket.socketName != _socketName)
            {
                _resolvedSocket = _ResolveSocket(_socketName);
            }
            return _resolvedSocket;
        }

        // アバターの humanoid Animator を avatar service 経由で取得する。親階層に依存しないので、
        // アバター配下の item でも AvatarController 配下へ移した chair でも同じく解決できる。
        Animator _ResolveAvatarAnimator()
        {
            var target = SingletonService<IAvatarService>.subject?.target;
            if (target == null) return null;
            var animator = target.GetComponent<Animator>();
            if (animator != null && animator.isHuman) return animator;
            return target.GetComponentInChildren<Animator>();
        }

        // Resolve a Socket by name within the parent avatar. Scoping the lookup to this avatar avoids
        // picking up another avatar's identically-named socket. Allocates, so callers cache the result.
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
    }
}
