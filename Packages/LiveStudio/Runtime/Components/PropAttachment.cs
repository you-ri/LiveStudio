// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Shared socket-attachment surface for a prop loaded under an avatar (from a <c>*.prop.lsb</c>): the
    /// target socket name, authored position / rotation / scale offsets, and the socket resolution +
    /// follow math. Held as a single field by each prop behavior component (<see cref="Prop"/>,
    /// <see cref="AvatarItem"/>, <see cref="AvatarChair"/>) so the common surface lives in one place and
    /// is exposed once (as a nested <c>"PropAttachment"</c>) under each component's <c>@type</c>.
    ///
    /// Sockets are created per-avatar with a normalized (avatar-root-aligned) orientation, so a single
    /// offset works across models regardless of their bone axis conventions. All avatar / socket lookups
    /// go through the avatar service (NOT the parent chain), so this resolves identically whether the
    /// owning prop lives under the avatar (an item) or is re-parented under the
    /// <see cref="AvatarController"/> (a chair). The avatar may load lazily, so resolution is retried
    /// until it succeeds; the socket is cached and only re-resolved when the name changes or the socket is
    /// destroyed (avatar swap), keeping the per-frame follow allocation-free.
    /// </summary>
    [Serializable]
    [ExposedClass("PropAttachment", Icon = "link")]
    public class PropAttachment : IManipulatorTarget
    {
        // Target socket name. The prop attaches to the avatar socket with this name.
        [ExposedField]
        [StringSelector(nameof(availableSocketNames))]
        public string socketName = "WristRight";

        // socket からの装着オフセット (位置 / 回転 / 拡縮) を 1 つにまとめた値。socket を親とした
        // ローカル TRS と数学的に等価で、RemoteApp 側は TransformValue 用ウィジェット (数値 + 3D ギズモ) で
        // 編集できる。position は bone ローカル、rotation は bone ローカル回転、scale はプレハブ本来の
        // localScale への乗算 (1 で等倍)。socket 追従は位置・回転だけを駆動し scale には触れないので、prop の
        // サイズは ApplyScale で毎フレーム適用する。アバターのスケールは親 (avatarRoot) を通じて lossyScale に
        // 伝播するため、ここでは socket スケールで補正しない。
        //
        // ExposedLight.transform と同じ「隠し ExposedField + 公開 ExposedProperty」パターン。値は Unity
        // シリアライズ用の _offset に保持し、RemoteApp には offset プロパティを TransformValue として見せる。
        [SerializeField, Hide, ExposedField]
        [FormerlyExposedAs("offset")]
        TransformValue _offset = TransformValue.identity;

        [ExposedProperty]
        public TransformValue offset
        {
            get => _offset;
            set => _offset = value;
        }

        // Socket names available on the parent avatar, surfaced to the RemoteApp dropdown. Resolved
        // through the avatar service, so it does not depend on the owning component.
        [ExposedProperty, Hide]
        public string[] availableSocketNames
        {
            get
            {
                if (_avatarAnimator == null) _avatarAnimator = _ResolveAvatarAnimator();
                if (_avatarAnimator == null) return Array.Empty<string>();

                var sockets = _avatarAnimator.GetComponentsInChildren<Socket>(includeInactive: true);
                var names = new string[sockets.Length];
                for (int i = 0; i < sockets.Length; i++) names[i] = sockets[i].socketName;
                return names;
            }
        }

        // Avatar's humanoid Animator, resolved through the avatar service. The avatar may load lazily, so
        // resolution is retried until it succeeds.
        [NonSerialized] Animator _avatarAnimator;

        // Cached socket. The follow runs every frame, so the allocating lookup must not run per frame;
        // re-resolved only when the socket name changes or the socket is destroyed (avatar swap).
        [NonSerialized] Socket _resolvedSocket;

        // プレハブ本来の localScale。offset.scale はこれへの乗算として適用する。CaptureBaseScale で一度だけ取得。
        [NonSerialized] Vector3 _baseScale = Vector3.one;

        /// <summary>
        /// Records the prop's authored localScale so <see cref="ApplyScale"/> multiplies the scale offset
        /// from it. Call once from the owner's Start (before any re-parent that would alter localScale).
        /// </summary>
        public void CaptureBaseScale(Transform t)
        {
            _baseScale = t.localScale;
        }

        /// <summary>
        /// Applies the scale offset (multiplied onto the captured base scale) to the transform. The follow
        /// drives position / rotation only, so scale is applied here; the avatar's own scale still
        /// propagates through the parent's lossyScale.
        /// </summary>
        public void ApplyScale(Transform t)
        {
            t.localScale = Vector3.Scale(_baseScale, offset.scale);
        }

        /// <summary>
        /// Computes the world pose the prop should follow this frame: the resolved socket pose composed
        /// with the authored position / rotation offsets. Returns false until the avatar and socket are
        /// available. The socket is cached, so this is allocation-free per frame.
        /// </summary>
        public bool TryResolveFollowTarget(out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = default;
            worldRot = default;

            var socket = _EnsureSocket();
            if (socket == null) return false;

            var st = socket.transform;
            // Compose the socket pose with the authored offset, treating the socket as the parent and the
            // offset as the child-local TRS: rotation in normalized bone-local space; translation pre-scaled by
            // the socket's lossyScale so it tracks the avatar size (the socket carries the avatar scale).
            worldRot = st.rotation * offset.rotation;
            worldPos = st.position + st.rotation * Vector3.Scale(offset.position, st.lossyScale);
            return true;
        }

        /// <summary>
        /// The resolved socket's transform (raw socket pose, without any offsets), or null until the avatar
        /// and socket are available. Lets a chair read the bone pose directly to drive its own axes.
        /// Cached, so allocation-free per frame.
        /// </summary>
        public Transform ResolveSocketTransform()
        {
            var socket = _EnsureSocket();
            return socket != null ? socket.transform : null;
        }

        /// <summary>
        /// Describes the offset to the Transform manipulator as a socket-relative local TRS: the socket is the
        /// parent world transform (with its lossyScale, matching the follow pre-scaling) and <see cref="offset"/>
        /// is the edited child-local value. This lets the RemoteApp 3D gizmo edit the offset directly. Returns
        /// false until the avatar and socket are available.
        /// </summary>
        public bool TryGetManipulatorState(out TransformValue parentWorld, out TransformValue targetLocal, out Vector3 pivotWorld)
        {
            parentWorld = TransformValue.identity;
            targetLocal = offset;
            pivotWorld = Vector3.zero;

            var socket = ResolveSocketTransform();
            if (socket == null) return false;

            parentWorld = new TransformValue(socket.position, socket.rotation, socket.lossyScale);
            // Prop world position = socket ∘ offset (matches TryResolveFollowTarget), used to frame the camera.
            pivotWorld = socket.position + socket.rotation * Vector3.Scale(offset.position, socket.lossyScale);
            return true;
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
            if (_resolvedSocket == null || _resolvedSocket.socketName != socketName)
            {
                _resolvedSocket = _ResolveSocket(socketName);
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
