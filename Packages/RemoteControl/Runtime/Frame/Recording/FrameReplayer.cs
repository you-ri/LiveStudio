// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>One recorded event, with its ids resolved back into what they meant.</summary>
    public readonly struct ReplayEvent
    {
        public readonly EventKind kind;

        /// <summary>Which operation was asked for. The HTTP method for anything that came over REST.</summary>
        public readonly string verb;

        /// <summary>What was addressed, e.g. the property path.</summary>
        public readonly string target;

        /// <summary>The name of the type <see cref="payload"/> holds, or null when it holds nothing.</summary>
        public readonly string payloadTypeName;

        /// <summary>
        /// The value, as bytes of <see cref="payloadTypeName"/>.
        ///
        /// Points into the replayer's own buffer and is valid for the duration of the
        /// <see cref="IEventApplier.Apply"/> call. An applier that wants to keep it copies it --
        /// the next event reuses the same memory.
        /// </summary>
        public readonly ReadOnlyMemory<byte> payload;

        /// <summary>Which producer it came from, for choosing what to replay and what to leave out.</summary>
        public readonly string source;

        /// <summary>True when the payload did not fit the record and was cut short at capture.</summary>
        public readonly bool payloadTruncated;

        /// <summary>
        /// True when the record restates a value rather than reporting a change
        /// (<see cref="EventFlags.Reemitted"/>).
        ///
        /// What it changes is whether applying it is worth doing at all: a restatement whose value
        /// the world already holds is a write with no effect, and some of those effects are
        /// expensive (an asset reference reloads the asset). An applier is expected to compare
        /// first and do nothing when they match.
        /// </summary>
        public readonly bool reemitted;

        public ReplayEvent(EventKind kind, string verb, string target, string payloadTypeName,
            ReadOnlyMemory<byte> payload, string source, bool payloadTruncated,
            bool reemitted = false)
        {
            this.reemitted = reemitted;
            this.kind = kind;
            this.verb = verb;
            this.target = target;
            this.payloadTypeName = payloadTypeName;
            this.payload = payload;
            this.source = source;
            this.payloadTruncated = payloadTruncated;
        }

        /// <summary>True when the payload is a string value rather than a laid-out one.</summary>
        public bool payloadIsString => EventPayload.IsString(payloadTypeName);

        /// <summary>True when the payload is a request body nothing worked out the meaning of.</summary>
        public bool payloadIsRequest => EventPayload.IsRequest(payloadTypeName);

        /// <summary>
        /// The payload as text -- a string value or a request body -- or null when it is neither.
        /// Allocates, so call it once.
        /// </summary>
        public string text
            => EventPayload.IsTextual(payloadTypeName) ? EventPayload.ReadString(payload.Span) : null;

        public override string ToString() => $"{verb} {target} ({kind})";
    }

    /// <summary>
    /// Puts a recorded event back into the application.
    ///
    /// Implemented above this package, because applying one means writing to a property or calling a
    /// method and that pipeline lives with the server. The frame layer knows what happened and in
    /// what order; it does not know how to make it happen again.
    /// </summary>
    public interface IEventApplier
    {
        /// <summary>
        /// Applies one event. False when it could not be, which is counted rather than thrown: a
        /// replay of a long take should report how much of it landed instead of stopping at the
        /// first thing that no longer exists.
        /// </summary>
        bool Apply(in ReplayEvent evt, out string error);
    }

    /// <summary>
    /// Drives a recording back into the application: restores each frame's state and hands its
    /// events to an applier.
    ///
    /// State and event are put back the same way round they were captured -- state first, because
    /// the values of a frame stand on their own, then the events, which are the things that were
    /// asked for during it.
    ///
    /// **Outward side effects are not suppressed yet.** Applying an event goes through the ordinary
    /// path, so a replay also fires whatever that path fires -- change notifications, dirty marking.
    /// For a same-machine record-and-compare that is harmless; for anything that talks to the
    /// outside world it is not, and the suppression the design calls for is still to come.
    /// </summary>
    public sealed class FrameReplayer : IFrameSource, IDisposable
    {
        private readonly FrameRecordPlayer _player;
        private readonly IEventApplier _applier;

        // One buffer for every event. Handed to the applier as a window over it, which is why an
        // applier is told not to hold on to it past the call.
        private readonly byte[] _payloadBuffer = new byte[EventRecord.kPayloadCapacity];

        // The records a seek walked over, and where each target was last written in them. Kept on
        // the replayer so a scrub does not allocate a pair of collections per jump.
        private readonly List<EventRecord> _walked = new List<EventRecord>();
        private readonly Dictionary<int, int> _lastWriteIndex = new Dictionary<int, int>();

        // Records put back since the last frame was supplied, waiting to be written into that
        // frame's event lane. A list rather than writing straight through, because a seek applies
        // its events outside a frame head -- there is no lane to write into until the next one.
        private readonly List<EventRecord> _replayed = new List<EventRecord>();

        /// <summary>Events handed to the applier so far.</summary>
        public int appliedEventCount { get; private set; }

        /// <summary>Events the applier could not put back.</summary>
        public int failedEventCount { get; private set; }

        /// <summary>
        /// Events skipped because what was recorded of them was already incomplete. Replaying a
        /// truncated payload would put a different value back than the one that was applied live,
        /// which is worse than not putting it back at all.
        /// </summary>
        public int skippedTruncatedCount { get; private set; }

        /// <summary>The recording being played. Its structure and state are the restored world.</summary>
        public FrameRecordPlayer player => _player;

        /// <summary>Frame most recently replayed, or -1 before the first.</summary>
        public long frameNumber => _player.frameNumber;

        /// <summary>True once there are no more frames.</summary>
        public bool atEnd => _player.atEnd;

        /// <summary>
        /// Holds the recording on the frame it last played instead of walking on.
        ///
        /// The gate still takes its frame from here, which is the point: detaching instead would
        /// hand the world straight back to the live producers, and the frame that was being looked
        /// at would be overwritten before it could be looked at. A held frame is re-supplied as it
        /// stands -- nothing is read and no event is applied a second time -- so the only thing that
        /// moves it is a <see cref="TrySeek"/>.
        /// </summary>
        public bool isPaused { get; set; }

        public FrameReplayer(Stream stream, IEventApplier applier, bool leaveOpen = false)
            : this(new FrameRecordPlayer(stream, leaveOpen), applier)
        {
        }

        public FrameReplayer(FrameRecordPlayer player, IEventApplier applier)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        }

        /// <summary>
        /// Replays the next frame. False at the end of the recording.
        /// </summary>
        public bool Advance()
        {
            if (!_player.Advance()) return false;

            _ApplyEventsOfCurrentFrame();
            return true;
        }

        /// <summary>
        /// Supplies the frame from the recording: points its lanes at what was recorded, then puts
        /// that frame's events back.
        ///
        /// The lanes are pointed at rather than copied into. The player already holds the restored
        /// world as a structure and a set of blocks, and a frame is a view of those two -- copying
        /// them into the gate's own lanes every frame would buy nothing and would leave the live
        /// world overwritten once the recording stopped. The player outlives the frame head, which
        /// is what the interface asks of a source that does this.
        /// </summary>
        public bool FillFrame(ref Frame frame)
        {
            // Held. The lanes are pointed at what the last frame left, and nothing is advanced or
            // applied: the events of that frame already landed when it played, and putting them back
            // once per frame for as long as the pause lasts is a second helping of the same change.
            //
            // Only once a frame has actually played, though. Paused before the first one, the
            // player's lanes are empty, and supplying those would blank the world rather than hold
            // it -- so the first frame plays either way and the hold starts from there.
            if (isPaused && _player.frameNumber >= 0)
            {
                frame.structure = _player.structure;
                frame.state = _player.state;
                frame.symbols = _player.symbols;

                // Nothing played, but a seek may have put events back since the last frame: they
                // belong to this head, because this is the head they became visible at.
                _PublishReplayed(frame.events);
                return true;
            }

            if (!_player.Advance()) return false;

            // Structure before state, the same way round a keyframe is applied: the container has to
            // exist before the values that belong in it.
            frame.structure = _player.structure;
            frame.state = _player.state;

            // With the lanes, because the ids in them index this table and not the one the gate
            // hands a live frame.
            frame.symbols = _player.symbols;

            _ApplyEventsOfCurrentFrame();

            // A replayed event was applied at this head like any other, so it goes into the lane
            // like any other. Without this the frame carries the recording's state and structure but
            // an empty event lane, and everything downstream -- a viewer's event list first among
            // them -- reports that a replay fired nothing.
            _PublishReplayed(frame.events);
            return true;
        }

        /// <summary>
        /// Moves the records put back since the last frame into that frame's event lane.
        ///
        /// The ids in them are the recording's, not this run's, exactly as the structure and state
        /// pointed at here are. A reader of a supplied frame resolves through
        /// <see cref="player"/> for the same reason.
        /// </summary>
        private void _PublishReplayed(EventFrame lane)
        {
            if (_replayed.Count == 0) return;

            // No lane to publish into (a caller driving the replayer outside the gate). Dropped
            // rather than kept: holding them would hand a later frame events that are not its own.
            if (lane != null)
            {
                for (int i = 0; i < _replayed.Count; i++)
                {
                    lane.Add(_replayed[i]);
                }
            }

            _replayed.Clear();
        }

        /// <summary>
        /// Jumps to a frame, restoring the shape of the world from the keyframe before it, and puts
        /// the world's event-lane values where that frame had them.
        ///
        /// The state lane needs none of this -- every frame states it in full, so landing on one is
        /// enough. The event lane is the opposite: it says only what changed, so the value at a
        /// frame is the last thing written at or before it, and a jump has to go back far enough to
        /// find that. Far enough is the keyframe, because that is where the shape of the world is
        /// written down and therefore where a seek starts reading.
        ///
        /// <para>
        /// ⚠ A value last written before that keyframe is not recovered. Keyframes used to carry a
        /// restatement of every event-lane member for exactly this reason, and no longer do
        /// (2026-09-04). A member that has to survive a seek belongs on the state lane
        /// (<c>lane = FrameLane.State</c>); until it is moved, its value before the keyframe is in
        /// the file nowhere at all.
        /// </para>
        ///
        /// <para>
        /// The walk from there is **collapsed to one write per target**. What is wanted is the value
        /// as of the destination, not the history of how it got there: replaying every intermediate
        /// write would put the same member through a dozen setters, and where a setter loads an
        /// asset that is a dozen loads to arrive at the one that was wanted.
        /// </para>
        ///
        /// <para>
        /// ⚠ Function calls are **not** replayed by a seek. A call is not a value, so there is no
        /// "as of" for it -- it happened a certain number of times in a certain order, and neither
        /// survives being collapsed. Playing forward (<see cref="Advance"/>) still applies them.
        /// </para>
        /// </summary>
        public bool TrySeek(long frame)
        {
            var keyframe = _KeyframeAtOrBefore(frame);

            // No keyframe to read the values from: nothing to walk, so this is the old behaviour --
            // land on the frame and apply what it carried.
            if (keyframe < 0)
            {
                if (!_player.TrySeekWithStructure(frame)) return false;

                _ApplyEventsOfCurrentFrame();
                return true;
            }

            if (!_player.TrySeek(keyframe)) return false;

            _walked.Clear();
            _CollectEventsOfCurrentFrame();

            while (_player.frameNumber < frame && !_player.atEnd)
            {
                if (!_player.Advance()) break;

                _CollectEventsOfCurrentFrame();
            }

            _ApplyCollapsed();
            return _player.frameNumber == frame;
        }

        public void Dispose() => _player.Dispose();

        /// <summary>
        /// The keyframe at or before <paramref name="frame"/>, or -1 when the recording has none at
        /// all before it. Read off the index the tail carries, which is in ascending order.
        /// </summary>
        private long _KeyframeAtOrBefore(long frame)
        {
            var keyframes = _player.keyframes;
            if (keyframes == null) return -1;

            var found = -1L;
            for (int i = 0; i < keyframes.Count; i++)
            {
                if (keyframes[i] > frame) break;

                found = keyframes[i];
            }

            return found;
        }

        /// <summary>
        /// Keeps this frame's records for the collapse at the end of the walk.
        ///
        /// Copied out rather than read in place: the player reuses its list on the next frame, and a
        /// record kept as a reference would read as whatever the walk ended on. The struct holds its
        /// own payload, so a copy is the whole record.
        /// </summary>
        private void _CollectEventsOfCurrentFrame()
        {
            var events = _player.events;

            for (int i = 0; i < events.Count; i++)
            {
                var record = events[i];

                // Nothing to collapse a call with, and nothing that says how many of them a
                // destination has behind it. Left to forward play, which sees them in order.
                if (record.kind == EventKind.FunctionCall) continue;

                _walked.Add(record);
            }
        }

        /// <summary>
        /// Applies the last write to each target, in the order those last writes happened.
        ///
        /// Order is kept rather than sorted: two members can depend on each other (a selection and
        /// the thing it selects from), and the order the recording put them in is the one that
        /// worked live.
        /// </summary>
        private void _ApplyCollapsed()
        {
            if (_walked.Count == 0) return;

            _lastWriteIndex.Clear();
            for (int i = 0; i < _walked.Count; i++)
            {
                _lastWriteIndex[_walked[i].targetId] = i;
            }

            for (int i = 0; i < _walked.Count; i++)
            {
                var record = _walked[i];

                if (_lastWriteIndex.TryGetValue(record.targetId, out var last) && last != i) continue;

                _ApplyRecord(in record);
            }

            _walked.Clear();
        }

        private void _ApplyEventsOfCurrentFrame()
        {
            var events = _player.events;

            for (int i = 0; i < events.Count; i++)
            {
                var record = events[i];
                _ApplyRecord(in record);
            }
        }

        /// <summary>Resolves one record back into what it meant and hands it to the applier.</summary>
        private void _ApplyRecord(in EventRecord record)
        {
            // Kept for the lane before anything can turn it away. What the frame reports is what the
            // recording carried at it -- a record that was skipped or that the applier refused still
            // happened in the take, and a viewer that hides those is the one place the difference
            // between "the recording has nothing here" and "nothing could be put back" is invisible.
            _replayed.Add(record);

            if (record.payloadTruncated)
            {
                skippedTruncatedCount++;
                return;
            }

            var length = record.CopyPayloadTo(_payloadBuffer);

            var evt = new ReplayEvent(
                record.kind,
                _player.Resolve(record.verbId),
                _player.Resolve(record.targetId),
                _player.Resolve(record.payloadTypeId),
                new ReadOnlyMemory<byte>(_payloadBuffer, 0, length),
                _player.Resolve(record.sourceId),
                record.payloadTruncated,
                record.reemitted);

            if (_applier.Apply(in evt, out var error))
            {
                appliedEventCount++;
                return;
            }

            failedEventCount++;
            Debug.LogWarning($"[RemoteControl] Replay could not apply {evt}: {error}");
        }
    }
}
