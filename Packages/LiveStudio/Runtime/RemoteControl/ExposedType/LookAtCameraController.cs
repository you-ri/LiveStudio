// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Assigns a bone (resolved from a <see cref="TransformRef"/>: a target owner name + a bone name or
    /// hierarchy path) to the camera's <c>LookAt</c> target (<see cref="CameraTarget.LookAtTarget"/>).
    ///
    /// The assignment is event-driven, not per-frame: it re-resolves only when the reference changes
    /// (<see cref="TransformRef.onChanged"/>) or when the owning hierarchy is rebuilt
    /// (<see cref="TransformStructureService.onStructureChanged"/>), which fires when the avatar model is
    /// swapped (see <c>AvatarController</c>). This mirrors how <see cref="OrbitalFollowCameraController"/>
    /// and <see cref="FreeCameraController"/> drive their <c>Tracking</c> target, so a look-at bone survives
    /// avatar reloads exactly the same way.
    ///
    /// Only the <c>LookAt</c> target is touched; the <c>Tracking</c> target and any Aim component the camera
    /// already carries are left untouched, so this composes with a camera that is positioned by hand or by a
    /// prefab-authored body.
    /// </summary>
    [Serializable]
    [ExposedClass]
    public class LookAtCameraController : ICameraController
    {
        [SerializeField, ExposedField]
        TransformRef _target = new TransformRef("Main Avatar", "Head", TransformRef.SearchType.Name);

        public TransformRef target => _target;

        CinemachineCamera _camera;

        public override void Setup(CinemachineCamera camera)
        {
            if (camera == null) return;

            _camera = camera;

            _target.onChanged += _OnTargetChanged;
            TransformStructureService.onStructureChanged += _OnStructureChanged;

            _ApplyTarget();
        }

        public override void Teardown(CinemachineCamera camera)
        {
            if (camera == null) return;

            _target.onChanged -= _OnTargetChanged;
            TransformStructureService.onStructureChanged -= _OnStructureChanged;

            // Release the look-at target so a later controller starts from a clean state.
            camera.Target.LookAtTarget = null;
            _camera = null;
        }

        public override void Update(CinemachineCamera camera)
        {
            // Event-driven: the look-at target is assigned in _ApplyTarget, not every frame.
        }

        void _OnTargetChanged() => _ApplyTarget();

        /// <summary>
        /// Internal hierarchy change notification for an owner GameObject. Re-resolves only when the
        /// changed owner is the one this reference points at (e.g. the avatar model was swapped).
        /// </summary>
        void _OnStructureChanged(GameObject owner)
        {
            if (owner == null) return;
            if (_target.ownerName != owner.name) return;
            _ApplyTarget();
        }

        void _ApplyTarget()
        {
            if (_camera == null) return;
            // Leave the target null when it cannot be resolved yet; it is retried on the next change /
            // structure notification once the owner (avatar) is registered.
            _camera.Target.LookAtTarget = _target.Resolve();
        }
    }
}
