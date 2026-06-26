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
    public class PropAttachment
    {
        // Target socket name. The prop attaches to the avatar socket with this name.
        [ExposedField]
        [StringSelector(nameof(availableSocketNames))]
        public string socketName = "WristRight";

        // bone ローカルの位置オフセット。
        [ExposedField]
        public Vector3 positionOffset = Vector3.zero;

        // bone ローカルの回転オフセット (euler 度)。
        [ExposedField]
        public Vector3 rotationOffset = Vector3.zero;

        // 拡縮オフセット。プレハブ本来の localScale への乗算 (1 で等倍)。socket 追従は位置・回転だけを
        // 駆動し scale には触れないので、prop のサイズはここで毎フレーム適用する。アバターのスケールは
        // 親 (avatarRoot) を通じて lossyScale に伝播するため、ここでは socket スケールで補正しない。
        [ExposedField]
        public Vector3 scaleOffset = Vector3.one;

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

        // プレハブ本来の localScale。scaleOffset はこれへの乗算として適用する。CaptureBaseScale で一度だけ取得。
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
            t.localScale = Vector3.Scale(_baseScale, scaleOffset);
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
            // Rotation offset in normalized bone-local space; translation offset pre-scaled by the socket's
            // lossyScale so it tracks the avatar size (the socket carries the avatar scale).
            worldRot = st.rotation * Quaternion.Euler(rotationOffset);
            worldPos = st.position + st.rotation * Vector3.Scale(positionOffset, st.lossyScale);
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
