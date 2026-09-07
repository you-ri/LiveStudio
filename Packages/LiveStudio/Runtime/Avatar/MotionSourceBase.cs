// Copyright (c) You-Ri, 2026

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Abstract source of motion frame data driving an avatar.
    /// Derived classes update <see cref="frameData"/> each frame; consumers
    /// pull the latest value.
    ///
    /// The state lane lives here rather than in any one source, because putting a pose on the frame
    /// and reading one back is not particular to how the pose was obtained. A source that only knows
    /// how to sample gets recording and replay by existing; a scene that replays a take needs no
    /// source at all beyond one that can read the lane. It used to sit in the VirgoMotion source, and
    /// that made a take unplayable without the machinery that captured it.
    ///
    /// A derived class supplies three things: how to sample this frame (<see cref="TrySample"/>), who
    /// it is on the frame (<see cref="frameSource"/>), and where the sample is placed
    /// (<see cref="PlacementMatrix"/>). Everything else is the same for any source.
    /// </summary>
    public abstract class MotionSourceBase : MonoBehaviour
    {
        public AvatarAnimationData frameData;

        /// <summary>
        /// World anchor that placed output is positioned/rotated relative to.
        /// When null, concrete sources fall back to their own transform.
        /// </summary>
        public Transform anchor { get; set; }

        /// <summary>
        /// Reset camera offset relative to the current avatar pose. Default no-op;
        /// concrete sources that track a real camera (e.g. mocap) should override.
        /// </summary>
        public virtual void ResetCamera() { }

        // Exposed id of the object this source belongs to. The string is kept, not the interned
        // number: finding it walks every registered object, but the number it interns to is only
        // good until the next gate reset.
        private string _ownerLiveId;

        /// <summary>
        /// Announces the pose type so a recording carrying it can be played back into a block, even
        /// on a run that has not published one live -- which is every run that only ever replays.
        ///
        /// Declared here, in the assembly that owns the type, so a replay-only machine does not have
        /// to load whatever produced the take to be able to read it.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _RegisterStateType()
        {
            StateTypeRegistry.Register<AvatarAnimationData>();
        }

        /// <summary>
        /// Samples this frame from whatever the source is fed by. False when there is nothing to
        /// sample, in which case the previous frame is left alone.
        ///
        /// <paramref name="sampledFrom"/> is the producer's own frame number the sample was anchored
        /// on -- its time axis, which is what an alignment is expressed against. -1 when there is no
        /// meaningful one.
        /// </summary>
        protected abstract bool TrySample(out AvatarAnimationData sampled, out long sampledFrom);

        /// <summary>
        /// Who this source is on the frame. Resolved by the derived class, because resolving a name
        /// that was never declared throws -- and the declaration belongs to whichever assembly the
        /// source lives in, not to this one.
        /// </summary>
        protected abstract FrameSource frameSource { get; }

        /// <summary>
        /// Runs at the top of the frame head, before anything is sampled or read. Where a source
        /// builds its reference point and consumes anything queued for the frame head.
        ///
        /// Part of the same handler rather than a handler of its own: frame-head handlers run in
        /// registration order with no way to say "after that one", so a source whose reference point
        /// was built in a separate handler would be placed with last frame's or this frame's
        /// depending on when it happened to be enabled.
        /// </summary>
        protected virtual void OnFrameHeadBegin() { }

        /// <summary>
        /// Where this frame's sample is placed. Applied after the sample reaches the state lane, so
        /// what is recorded is the pose before placement and the reference point stays editable on
        /// a replay.
        /// </summary>
        protected virtual Matrix4x4 PlacementMatrix() => Matrix4x4.identity;

        protected virtual void OnEnable()
        {
            // Sampling runs at the head of a frame rather than in Update, so the pose for frame N
            // is produced at a fixed point instead of wherever this component happens to be in the
            // script order. The gate applies that frame's inputs first, so a write to the reference
            // point lands before the pose that is placed by it.
            FrameGate.AddFrameHeadHandler(OnFrameHead);
        }

        protected virtual void OnDisable()
        {
            FrameGate.RemoveFrameHeadHandler(OnFrameHead);
        }

        /// <summary>
        /// Produces the frame: let the source get ready, resolve this frame's sample (live or from
        /// the recording), then place it.
        ///
        /// Order matters. The reference point used to be applied on the receive thread, which left
        /// two problems: the placement origin was written on the main thread and read there with
        /// nothing to order them, and a recording of the placed pose could never be re-placed, so
        /// editing the camera afterwards would have had no effect. Sampling first and placing last
        /// fixes both.
        ///
        /// Interpolating before placing gives the same result as placing before interpolating: the
        /// placement is a rotation and a translation, spherical interpolation is invariant under a
        /// rotation applied from the left, and translation commutes with linear interpolation. The
        /// two differ only while the reference point itself is moving, and then only by one frame's
        /// worth of its motion.
        ///
        /// Public so it can be driven directly -- by a test, or by a host running its own loop --
        /// without going through the gate's registration.
        /// </summary>
        public void OnFrameHead(ref Frame frame)
        {
            OnFrameHeadBegin();

            if (!_TryResolveFrame(ref frame, out var sampled)) return;

            // Runs on a supplied frame too, and that is the point: what is on the state lane is the
            // sample before placement, so the reference point can be edited and the take drawn
            // again. Fold the placement into what is recorded and this step has nothing left to do.
            AvatarAnimationSystem.Transform(in sampled, PlacementMatrix(), out frameData);
        }

        /// <summary>
        /// This frame's sample, from whichever side is supplying the frame.
        ///
        /// One opening, two suppliers. Live, the sample is taken from the source and written onto the
        /// frame; supplied, it is read straight back off the frame. Everything after this point is
        /// the same code either way, which is what stops a replay from being a second path that can
        /// quietly drift from the first.
        ///
        /// A supplied frame with nothing for this source is not an error -- the recording simply had
        /// no pose at that point -- so the avatar is left as it was rather than snapped to nothing.
        /// </summary>
        private bool _TryResolveFrame(ref Frame frame, out AvatarAnimationData sampled)
        {
            if (frame.isSupplied) return _TryReadState(in frame, out sampled);

            if (!TrySample(out sampled, out var sampledFrom)) return false;

            _PublishState(ref frame, in sampled, sampledFrom);
            return true;
        }

        /// <summary>
        /// Puts this frame's sample into the state lane.
        ///
        /// What is stored is the sample before placement. Placement is a property of the camera rig,
        /// not of the capture, and folding it in would make the recorded pose unusable for anything
        /// but the camera position it happened to be shot with.
        ///
        /// Read back on a supplied frame by <see cref="_TryReadState"/>, so the value that reaches
        /// the avatar comes off the frame either way.
        /// </summary>
        private void _PublishState(ref Frame frame, in AvatarAnimationData sampled, long sampledFrom)
        {
            if (frame.state == null) return;

            var owner = _OwnerId();
            if (owner == FrameSymbolTable.kNone) return;

            ref var element = ref frame.state.GetOrCreate<AvatarAnimationData>().GetOrCreate(owner);
            element.source = frameSource;

            // The producer's frame number, not this frame's. The two run off different clocks, and
            // keeping the producer's is what lets an alignment be applied afterwards.
            element.time = sampledFrom;
            element.value = sampled;
        }

        private bool _TryReadState(in Frame frame, out AvatarAnimationData sampled)
        {
            sampled = default;

            if (frame.state == null) return false;

            // Through the recording's table on a supplied frame: the row was filed under the id
            // the take gave this source, not the one this run interned for the same address.
            var owner = LiveStateSystem.OwnerIdOf(in frame, OwnerAddress());
            if (owner == FrameSymbolTable.kNone) return false;

            var block = frame.state.Find<AvatarAnimationData>();
            if (block == null) return false;

            var index = block.IndexOfOwner(owner);
            if (index < 0) return false;

            sampled = block[index].value;
            return true;
        }

        /// <summary>
        /// Interned id of this source as an exposed object, or none while it has not been registered.
        ///
        /// Two different lifetimes, so two different things are kept. The exposed id is found once,
        /// because finding it walks every registered object and the answer only changes when this
        /// component's object is registered or dropped. The number it interns to is taken every
        /// frame, because a gate reset wipes the symbol table and a kept number would then name
        /// whatever took its place -- object ids are not re-interned in a fixed order the way
        /// declared sources are.
        /// </summary>
        private int _OwnerId()
        {
            var address = OwnerAddress();

            return string.IsNullOrEmpty(address)
                ? FrameSymbolTable.kNone
                : FrameGate.symbols.Intern(address);
        }

        /// <summary>
        /// The address this source publishes its frame under, or null while it has none.
        ///
        /// Kept apart from the interned number because the two are wanted in different tables: a
        /// live frame files the row under this run's id, and a replayed one under the recording's.
        ///
        /// The id is asked of the transform, not of this component. A component is not registered
        /// under its own target -- the registry holds the proxy for its GameObject -- so a lookup by
        /// target returns nothing, and nothing is indistinguishable from "not ready yet". That is
        /// what once made every pose published here go nowhere.
        /// </summary>
        protected string OwnerAddress()
        {
            if (_ownerLiveId == null)
            {
                var id = LiveObjectRegistry.FindOwnLiveId(transform);
                if (string.IsNullOrEmpty(id)) return null;

                _ownerLiveId = id;
            }

            return _ownerLiveId;
        }
    }
}
