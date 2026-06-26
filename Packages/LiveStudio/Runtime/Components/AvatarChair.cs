// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One tracked axis of an <see cref="AvatarChair"/>: a target child transform driven along a local
    /// <see cref="axis"/> by a value tracked from the hips, smoothed with a <see cref="deadzone"/> (drop
    /// sub-threshold motion) and <see cref="damping"/> (smooth the rest) to absorb hips tremor. The target
    /// and axis are authored into the prop prefab; the deadzone / damping are editable from the remote app.
    /// </summary>
    [Serializable]
    [ExposedClass("ChairAxis", Icon = "tune")]
    public class ChairAxis
    {
        /// <summary>Child transform this axis drives. Authoring data (baked into the prop prefab).</summary>
        [SerializeField] public Transform target;

        /// <summary>Local axis the value rotates about / translates along. Authoring data.</summary>
        [SerializeField] public Vector3 axis = Vector3.up;

        /// <summary>
        /// Play / backlash (degrees for rotation, units for translation) allowed between the follow source
        /// and the follow target — i.e. between where the axis SHOULD be and where it currently IS, not
        /// relative to rest. The follow source (hips) trembles, so while their gap stays within this the
        /// chair does not move (tremor around the current pose is absorbed); once the gap exceeds it the
        /// chair follows, trailing the source by this much slack. Larger = more tremor absorbed / more lag.
        /// </summary>
        [ExposedField] public float deadzone;

        /// <summary>SmoothDamp time (seconds) applied after the deadzone. 0 = snap.</summary>
        [ExposedField] public float damping;

        /// <summary>
        /// Operating range (degrees or units, relative to the rest pose) the tracked value is confined to.
        /// The value is always clamped to <c>[min, max]</c> when <see cref="max"/> &gt;= <see cref="min"/>,
        /// so the default (both 0) LOCKS the axis (no movement) — set a range to allow motion. A
        /// <see cref="max"/> &lt; <see cref="min"/> disables the clamp (unlimited).
        /// </summary>
        [ExposedField] public float min;
        [ExposedField] public float max;

        /// <summary>Current smoothed value (degrees or units), relative to the rest pose.</summary>
        [NonSerialized] public float value;
        [NonSerialized] float _velocity;

        /// <summary>
        /// Drives <see cref="value"/> from <paramref name="raw"/> — the tracked amount relative to the
        /// recorded rest pose (degrees for rotation, units for translation), computed by the owner.
        /// Applies deadzone → operating range → damping. For <paramref name="angular"/> axes the damping
        /// uses <see cref="Mathf.SmoothDampAngle"/> so it interpolates along the SHORTEST arc (a plain
        /// SmoothDamp would sweep the long way around near the ±180 wrap). Deterministic given the inputs.
        /// </summary>
        public void Track(float raw, bool angular)
        {
            // raw = where the axis should be (delta from the recorded rest). Confine the TARGET to range.
            var target = max >= min ? Mathf.Clamp(raw, min, max) : raw;

            // Deadzone = PLAY (backlash) between the source target and the follow target (current value):
            // measured against where the chair currently IS, not against rest. While the gap stays within
            // the deadzone the chair does NOT move (so source tremor around the current pose is absorbed);
            // once the gap exceeds it the chair follows, trailing the source by `deadzone` of slack.
            var gap = angular ? Mathf.DeltaAngle(value, target) : target - value;
            if (Mathf.Abs(gap) <= deadzone) return; // within play → hold current value

            var desired = target - Mathf.Sign(gap) * deadzone;
            if (damping > 0f)
            {
                value = angular
                    ? Mathf.SmoothDampAngle(value, desired, ref _velocity, damping)
                    : Mathf.SmoothDamp(value, desired, ref _velocity, damping);
            }
            else
            {
                value = desired;
            }
        }
    }

    /// <summary>
    /// Controls a chair prop the avatar sits on (loaded from a <c>*.prop.lsb</c>). At load it is parented
    /// under the avatar like any prop, but on the first frame it RE-PARENTS itself under the
    /// <see cref="AvatarController"/> (the avatar service host) so that avatar root motion does NOT drag
    /// the chair around — the chair body stays put at the controller anchor (placed via the
    /// <see cref="attachment"/> position / rotation offsets).
    ///
    /// Its movable parts then track the avatar's pelvis by reading the "Hips" socket (resolved via its
    /// <see cref="attachment"/>, whose <c>socketName</c> selects the bone), all measured in the anchor's
    /// local space relative to a rest reference captured when the avatar first becomes available:
    /// <see cref="swivel"/> follows hips yaw and <see cref="recline"/> follows hips pitch (rotation);
    /// <see cref="lateral"/> (left-right / X), <see cref="height"/> (up-down / Y) and <see cref="depth"/>
    /// (forward-back / Z) follow the hips position (translation).
    ///
    /// This is one of the mutually-exclusive prop behavior components (<see cref="IProp"/>): it drives its
    /// own filtered follow (so there is no rigid follow to override), placing the chair body from the
    /// <see cref="attachment"/> offsets. Unlike <see cref="AvatarItem"/>, a chair does not bridge avatar
    /// parameters or drive expressions, and uses no Animator.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [ExposedClass("Chair", Category = "Avatar", Icon = "chair")]
    public class AvatarChair : MonoBehaviour, IProp
    {
        [Header("Rotation — follows hips orientation")]
        [SerializeField] public ChairAxis swivel = new ChairAxis { axis = Vector3.up };    // hips yaw
        [SerializeField] public ChairAxis recline = new ChairAxis { axis = Vector3.right }; // hips pitch

        [Header("Translation — follows hips position")]
        [SerializeField] public ChairAxis lateral = new ChairAxis { axis = Vector3.right };   // left-right (X)
        [SerializeField] public ChairAxis height = new ChairAxis { axis = Vector3.up };       // up-down (Y)
        [SerializeField] public ChairAxis depth = new ChairAxis { axis = Vector3.forward };   // forward-back (Z)

        // Recorded neutral hips reference. The tracked axes are deltas from this. Set by Activate() (the
        // "register" button) when the avatar is in the desired seated pose, or auto-captured once on the
        // first frame as a provisional default. Persisted to the live scene (Hide: not directly edited),
        // so a recorded rest survives reload. The auto-captured first frame is unreliable (the avatar may
        // still be in a bind / transition pose), which is why an explicit Activate is provided.
        [ExposedField, Hide] public bool restRecorded;
        [ExposedField, Hide] public float restYaw;
        [ExposedField, Hide] public float restPitch;
        [ExposedField, Hide] public Vector3 restHipsLocal;

        // Shared socket-attachment surface (socket name + offsets + socket resolution), exposed as a
        // nested "PropAttachment" under this component's @type. The chair reads the hips socket and its
        // body-placement offsets from here; for a chair the socketName selects the hips bone.
        [SerializeField]
        public PropAttachment attachment = new PropAttachment();

        [NonSerialized] bool _reparented;

        // Authored rest local pose per moving target, captured on first use so the tracked value offsets
        // from the prefab pose. Reused (cleared) each frame for per-target accumulation.
        [NonSerialized] readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> _rest =
            new Dictionary<Transform, (Vector3, Quaternion)>();
        [NonSerialized] readonly Dictionary<Transform, (Quaternion rot, Vector3 pos)> _accum =
            new Dictionary<Transform, (Quaternion, Vector3)>();

        void Start()
        {
            attachment.CaptureBaseScale(transform);
        }

        void LateUpdate()
        {
            // Scale offset applied live so runtime edits take effect (the body placement below drives only
            // local position / rotation).
            attachment.ApplyScale(transform);

            // Hips socket pose, read through the attachment (its socketName selects the bone). Null until
            // the avatar is loaded; retry next frame.
            var socket = attachment.ResolveSocketTransform();
            if (socket == null) return;

            _EnsureReparented();

            // Chair body placement (anchor-local) from the attachment offsets, applied live so runtime
            // edits take effect. The body is NOT driven by the avatar (no root motion) — only by these
            // offsets; scale is applied above.
            transform.SetLocalPositionAndRotation(attachment.positionOffset, Quaternion.Euler(attachment.rotationOffset));

            // Read the hips (yaw / pitch about world; position in the anchor's local space so left-right /
            // depth are relative to the avatar's facing).
            _ReadHips(socket, out var yaw, out var pitch, out var hipsLocalPos);

            // Provisional rest on the very first frame so the chair is not dead before Activate() is used.
            if (!restRecorded) { restYaw = yaw; restPitch = pitch; restHipsLocal = hipsLocalPos; restRecorded = true; }

            // Each tracked axis = deterministic delta from the recorded rest (rotation = shortest signed
            // angle, ±180).
            swivel.Track(Mathf.DeltaAngle(restYaw, yaw), angular: true);
            recline.Track(Mathf.DeltaAngle(restPitch, pitch), angular: true);
            lateral.Track(hipsLocalPos.x - restHipsLocal.x, angular: false);
            height.Track(hipsLocalPos.y - restHipsLocal.y, angular: false);
            depth.Track(hipsLocalPos.z - restHipsLocal.z, angular: false);

            _ApplyAxes();
        }

        /// <summary>
        /// Records the avatar's CURRENT hips pose as the neutral rest reference (the "register" / Activate
        /// button, like a Unity constraint's Activate). Call it from the remote app while the avatar is in
        /// the desired seated pose; every axis then measures its delta from this pose (so swivel / recline
        /// read 0 here). The recorded rest persists in the live scene. No-op until the avatar is loaded.
        /// </summary>
        [ExposedFunction]
        [ContextMenu("Activate (record rest pose)")]
        public void Activate()
        {
            var socket = attachment.ResolveSocketTransform();
            if (socket == null) return;
            _ReadHips(socket, out restYaw, out restPitch, out restHipsLocal);
            restRecorded = true;
        }

        // Reads the hips yaw / pitch (world) and anchor-local position from the socket.
        void _ReadHips(Transform socket, out float yaw, out float pitch, out Vector3 localPos)
        {
            var anchor = transform.parent;
            localPos = anchor != null ? anchor.InverseTransformPoint(socket.position) : socket.position;
            yaw = _Yaw(socket.rotation);
            pitch = _Pitch(socket.rotation);
        }

        // Re-parents the chair under the AvatarController (avatar service host) once, so avatar root motion
        // no longer moves it.
        void _EnsureReparented()
        {
            if (_reparented) return;
            var anchor = (SingletonService<IAvatarService>.subject as Component)?.transform;
            if (anchor == null) return;
            transform.SetParent(anchor, worldPositionStays: false);
            _reparented = true;
            // The chair body's local pose is set from the attachment offsets every frame (see LateUpdate).
        }

        // Composes all axis contributions per target (a target shared by several axes combines its rotation
        // and translation correctly) and applies them relative to each target's rest pose.
        void _ApplyAxes()
        {
            _accum.Clear();
            _AddRotation(swivel);
            _AddRotation(recline);
            _AddTranslation(lateral);
            _AddTranslation(height);
            _AddTranslation(depth);

            foreach (var kv in _accum)
            {
                var rest = _Rest(kv.Key);
                kv.Key.SetLocalPositionAndRotation(rest.pos + kv.Value.pos, rest.rot * kv.Value.rot);
            }
        }

        void _AddRotation(ChairAxis a)
        {
            if (a.target == null || a.axis.sqrMagnitude < 1e-8f) return;
            // Seed a NEW accumulator with identity. (Cannot detect "unset" via `e.rot == default` because
            // Quaternion.== compares by dot product, so (0,0,0,0)==(0,0,0,0) is false — the identity seed
            // would be skipped and the zero quaternion would wipe the rotation.)
            if (!_accum.TryGetValue(a.target, out var e)) e = (Quaternion.identity, Vector3.zero);
            e.rot = e.rot * Quaternion.AngleAxis(a.value, a.axis.normalized);
            _accum[a.target] = e;
        }

        void _AddTranslation(ChairAxis a)
        {
            if (a.target == null || a.axis.sqrMagnitude < 1e-8f) return;
            if (!_accum.TryGetValue(a.target, out var e)) e = (Quaternion.identity, Vector3.zero);
            e.pos += a.axis.normalized * a.value;
            _accum[a.target] = e;
        }

        // Returns the target's authored rest local pose, capturing it on first use.
        (Vector3 pos, Quaternion rot) _Rest(Transform target)
        {
            if (!_rest.TryGetValue(target, out var rest))
            {
                rest = (target.localPosition, target.localRotation);
                _rest[target] = rest;
            }
            return rest;
        }

        // Yaw (degrees) of a rotation about world up, from its forward projected on the horizontal plane.
        static float _Yaw(Quaternion rotation)
        {
            var fwd = rotation * Vector3.forward;
            fwd.y = 0f;
            return fwd.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }

        // Pitch (degrees) = elevation of the forward direction above the horizontal plane.
        static float _Pitch(Quaternion rotation)
        {
            var fwd = (rotation * Vector3.forward).normalized;
            return Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
