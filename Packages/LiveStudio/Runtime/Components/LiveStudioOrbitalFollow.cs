// Copyright (c) You-Ri, 2026

using UnityEngine;
using Unity.Cinemachine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A Body-stage Cinemachine component that orbits the camera on a sphere around the Follow target,
    /// like <see cref="CinemachineOrbitalFollow"/> in <c>Sphere</c> mode, but resolves position and
    /// orientation in its OWN local space — the camera's parent transform — instead of the target's frame
    /// or world space (as if OrbitalFollow had a "LocalSpace" binding mode). Rotating the parent transform
    /// therefore rotates the whole orbit rig even when <see cref="yaw"/> is left untouched.
    ///
    /// Only the pieces this project needs are kept: the sphere orbit (<see cref="yaw"/> / <see cref="pitch"/>
    /// / <see cref="distance"/> mapped from OrbitalFollow's Horizontal / Vertical / Radial axes),
    /// <see cref="Radius"/>, a local-space <see cref="TargetOffset"/>, and <see cref="PositionDamping"/>.
    /// Recentering, input axes and the 3-ring orbit style are intentionally omitted.
    /// </summary>
    [SaveDuringPlay]
    [DisallowMultipleComponent]
    [CameraPipeline(CinemachineCore.Stage.Body)]
    [RequiredTarget(RequiredTargetAttribute.RequiredTargets.Tracking)]
    public class LiveStudioOrbitalFollow : CinemachineComponentBase
    {
        /// <summary>Offset from the Follow target's position, expressed in this component's own local
        /// (parent) frame. Use it when the orbit focus is not the target's origin.</summary>
        public Vector3 TargetOffset;

        /// <summary>Horizontal orbit angle in degrees (rotation about the frame up-axis).
        /// Maps to OrbitalFollow's HorizontalAxis.</summary>
        public float yaw;

        /// <summary>Constant offset (degrees) added to <see cref="yaw"/>, defining which direction
        /// <c>yaw = 0</c> points. Use it to calibrate the orbit's zero direction without shifting the
        /// live <see cref="yaw"/> value. Defaults to 180 so <c>yaw = 0</c> faces the target's front.</summary>
        public float yawOffset = 180f;

        /// <summary>Vertical orbit angle in degrees (rotation about the frame right-axis).
        /// Maps to OrbitalFollow's VerticalAxis.</summary>
        public float pitch;

        /// <summary>Orbit radius: the distance from the target to the camera. This merges OrbitalFollow's
        /// Radius and RadialAxis (they are only separate there because RadialAxis is a zoom input axis).</summary>
        public float distance = 1f;

        /// <summary>Per-axis position damping (seconds), applied in the local frame. Larger values give a
        /// heavier, slower-following camera. Matches OrbitalFollow's <c>TrackerSettings.PositionDamping</c>.</summary>
        public Vector3 PositionDamping = Vector3.one;

        // Damped orbit-center position from the previous frame (world space), the anchor for damping.
        Vector3 _previousTrackedPoint;

        /// <summary>True if the component is enabled and has a valid Follow target.</summary>
        public override bool IsValid => enabled && FollowTarget != null;

        /// <summary>This component implements the Body (position) stage.</summary>
        public override CinemachineCore.Stage Stage => CinemachineCore.Stage.Body;

        /// <summary>Report maximum damping time needed for this component.</summary>
        public override float GetMaxDampTime()
            => Mathf.Max(PositionDamping.x, Mathf.Max(PositionDamping.y, PositionDamping.z));

        void OnValidate()
        {
            PositionDamping.x = Mathf.Max(0f, PositionDamping.x);
            PositionDamping.y = Mathf.Max(0f, PositionDamping.y);
            PositionDamping.z = Mathf.Max(0f, PositionDamping.z);
        }

        // The reference frame the orbit is resolved in: the camera's parent transform (its own local
        // space), or world space when there is no parent. This is the "LocalSpace binding" behavior — the
        // parent's rotation drives the rig.
        Quaternion _ReferenceFrame()
            => transform.parent != null ? transform.parent.rotation : Quaternion.identity;

        /// <summary>Positions the camera on the orbit sphere, resolved in the local (parent) frame.</summary>
        public override void MutateCameraState(ref CameraState curState, float deltaTime)
        {
            if (!IsValid)
                return;

            var frame = _ReferenceFrame();

            // Orbit center: the follow target plus the local-space target offset.
            var targetPoint = FollowTargetPosition + frame * TargetOffset;

            // Position damping, measured in the local frame so the per-axis damping is meaningful. Only the
            // orbit CENTER is damped (matching OrbitalFollow); the orbit offset below is applied instantly
            // so changing yaw / pitch / distance does not lag.
            Vector3 trackedPoint;
            if (deltaTime >= 0 && VirtualCamera.PreviousStateIsValid)
            {
                var localDelta = Quaternion.Inverse(frame) * (targetPoint - _previousTrackedPoint);
                localDelta = Damper.Damp(localDelta, PositionDamping, deltaTime);
                trackedPoint = _previousTrackedPoint + frame * localDelta;
            }
            else
            {
                trackedPoint = targetPoint;
            }
            _previousTrackedPoint = trackedPoint;

            // Sphere orbit offset (OrbitalFollow's Sphere math) in the local frame.
            var localOffset = Quaternion.Euler(pitch, yaw + yawOffset, 0f) * new Vector3(0f, 0f, -distance);

            curState.RawPosition = trackedPoint + frame * localOffset;
            curState.ReferenceUp = frame * Vector3.up;
        }

        /// <summary>Keeps the damping anchor in sync when the follow target is warped.</summary>
        public override void OnTargetObjectWarped(Transform target, Vector3 positionDelta)
        {
            base.OnTargetObjectWarped(target, positionDelta);
            if (target == FollowTarget)
                _previousTrackedPoint += positionDelta;
        }

        /// <summary>Resets the damping anchor so a forced position (e.g. a blend) does not lag afterward.</summary>
        public override void ForceCameraPosition(Vector3 pos, Quaternion rot)
        {
            base.ForceCameraPosition(pos, rot);
            _previousTrackedPoint = FollowTarget != null
                ? FollowTargetPosition + _ReferenceFrame() * TargetOffset
                : pos;
        }
    }
}
