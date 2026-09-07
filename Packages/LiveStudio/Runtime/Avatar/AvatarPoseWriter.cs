// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Writes frames straight onto an avatar, for the avatars that drive themselves rather than going
    /// through <see cref="AvatarBodyDriver"/>'s PlayableGraph (<see cref="StandardAvatar"/>,
    /// <see cref="VRM0Avatar"/>).
    ///
    /// It exists to own the <see cref="HumanPoseHandler"/>. The pose is in muscle space, so applying
    /// it needs a handler bound to the avatar -- and a handler holds native memory, costs real work to
    /// build, and stops meaning anything the moment the avatar is rebuilt. Rebinding is automatic:
    /// the handler is rebuilt whenever the animator or its avatar changes, so callers can hand over
    /// whatever animator they currently have without tracking rebuilds themselves.
    /// </summary>
    public sealed class AvatarPoseWriter : IDisposable
    {
        private Animator _animator;
        private Avatar _avatar;
        private HumanPoseHandler _handler;

        /// <summary>
        /// Applies a frame (root transform + pose). Does nothing for an avatar that is not humanoid:
        /// the pose is expressed in humanoid muscles and there is nothing to map it onto.
        /// </summary>
        public void Apply(Animator animator, in AvatarAnimationData src)
        {
            if (animator == null) return;

            if (!_Rebind(animator)) return;

            AvatarAnimationSystem.UpdateBodyAnimation(animator, _handler, in src);
        }

        private bool _Rebind(Animator animator)
        {
            if (_handler != null && ReferenceEquals(_animator, animator) && ReferenceEquals(_avatar, animator.avatar))
                return true;

            Dispose();

            if (!animator.isHuman || animator.avatar == null) return false;

            _handler = new HumanPoseHandler(animator.avatar, animator.transform);
            _animator = animator;
            _avatar = animator.avatar;
            return true;
        }

        public void Dispose()
        {
            _handler?.Dispose();
            _handler = null;
            _animator = null;
            _avatar = null;
        }
    }
}
