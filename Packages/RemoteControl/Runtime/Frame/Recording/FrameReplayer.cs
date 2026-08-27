// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using UnityEngine;

namespace Lilium.RemoteControl.Frames.Recording
{
    /// <summary>One recorded input, with its ids resolved back into what they meant.</summary>
    public readonly struct ReplayInput
    {
        public readonly InputKind kind;

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
        /// <see cref="IInputApplier.Apply"/> call. An applier that wants to keep it copies it --
        /// the next input reuses the same memory.
        /// </summary>
        public readonly ReadOnlyMemory<byte> payload;

        /// <summary>Which producer it came from, for choosing what to replay and what to leave out.</summary>
        public readonly string source;

        /// <summary>True when the payload did not fit the record and was cut short at capture.</summary>
        public readonly bool payloadTruncated;

        public ReplayInput(InputKind kind, string verb, string target, string payloadTypeName,
            ReadOnlyMemory<byte> payload, string source, bool payloadTruncated)
        {
            this.kind = kind;
            this.verb = verb;
            this.target = target;
            this.payloadTypeName = payloadTypeName;
            this.payload = payload;
            this.source = source;
            this.payloadTruncated = payloadTruncated;
        }

        /// <summary>True when the payload is a string value rather than a laid-out one.</summary>
        public bool payloadIsString => InputPayload.IsString(payloadTypeName);

        /// <summary>True when the payload is a request body nothing worked out the meaning of.</summary>
        public bool payloadIsRequest => InputPayload.IsRequest(payloadTypeName);

        /// <summary>
        /// The payload as text -- a string value or a request body -- or null when it is neither.
        /// Allocates, so call it once.
        /// </summary>
        public string text
            => InputPayload.IsTextual(payloadTypeName) ? InputPayload.ReadString(payload.Span) : null;

        public override string ToString() => $"{verb} {target} ({kind})";
    }

    /// <summary>
    /// Puts a recorded input back into the application.
    ///
    /// Implemented above this package, because applying one means writing to a property or calling a
    /// method and that pipeline lives with the server. The frame layer knows what happened and in
    /// what order; it does not know how to make it happen again.
    /// </summary>
    public interface IInputApplier
    {
        /// <summary>
        /// Applies one input. False when it could not be, which is counted rather than thrown: a
        /// replay of a long take should report how much of it landed instead of stopping at the
        /// first thing that no longer exists.
        /// </summary>
        bool Apply(in ReplayInput input, out string error);
    }

    /// <summary>
    /// Drives a recording back into the application: restores each frame's state and hands its
    /// inputs to an applier.
    ///
    /// State and input are put back the same way round they were captured -- state first, because
    /// the values of a frame stand on their own, then the inputs, which are the things that were
    /// asked for during it.
    ///
    /// **Outward side effects are not suppressed yet.** Applying an input goes through the ordinary
    /// path, so a replay also fires whatever that path fires -- change notifications, dirty marking.
    /// For a same-machine record-and-compare that is harmless; for anything that talks to the
    /// outside world it is not, and the suppression the design calls for is still to come.
    /// </summary>
    public sealed class FrameReplayer : IFrameSource, IDisposable
    {
        private readonly FrameRecordPlayer _player;
        private readonly IInputApplier _applier;

        // One buffer for every input. Handed to the applier as a window over it, which is why an
        // applier is told not to hold on to it past the call.
        private readonly byte[] _payloadBuffer = new byte[InputRecord.kPayloadCapacity];

        /// <summary>Inputs handed to the applier so far.</summary>
        public int appliedInputCount { get; private set; }

        /// <summary>Inputs the applier could not put back.</summary>
        public int failedInputCount { get; private set; }

        /// <summary>
        /// Inputs skipped because what was recorded of them was already incomplete. Replaying a
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

        public FrameReplayer(Stream stream, IInputApplier applier, bool leaveOpen = false)
            : this(new FrameRecordPlayer(stream, leaveOpen), applier)
        {
        }

        public FrameReplayer(FrameRecordPlayer player, IInputApplier applier)
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

            _ApplyInputsOfCurrentFrame();
            return true;
        }

        /// <summary>
        /// Supplies the frame from the recording: points its lanes at what was recorded, then puts
        /// that frame's inputs back.
        ///
        /// The lanes are pointed at rather than copied into. The player already holds the restored
        /// world as a structure and a set of blocks, and a frame is a view of those two -- copying
        /// them into the gate's own lanes every frame would buy nothing and would leave the live
        /// world overwritten once the recording stopped. The player outlives the frame head, which
        /// is what the interface asks of a source that does this.
        /// </summary>
        public bool FillFrame(ref Frame frame)
        {
            if (!_player.Advance()) return false;

            // Structure before state, the same way round a keyframe is applied: the container has to
            // exist before the values that belong in it.
            frame.structure = _player.structure;
            frame.state = _player.state;

            _ApplyInputsOfCurrentFrame();
            return true;
        }

        /// <summary>
        /// Jumps to a frame, restoring the shape of the world from the keyframe before it, and
        /// applies that frame's inputs.
        ///
        /// The inputs of the frames walked through on the way are **not** applied. Their effect is
        /// already in the state that was restored, and applying them again would be a second helping
        /// of the same change.
        /// </summary>
        public bool TrySeek(long frame)
        {
            if (!_player.TrySeekWithStructure(frame)) return false;

            _ApplyInputsOfCurrentFrame();
            return true;
        }

        public void Dispose() => _player.Dispose();

        private void _ApplyInputsOfCurrentFrame()
        {
            var inputs = _player.inputs;

            for (int i = 0; i < inputs.Count; i++)
            {
                var record = inputs[i];

                if (record.payloadTruncated)
                {
                    skippedTruncatedCount++;
                    continue;
                }

                var length = record.CopyPayloadTo(_payloadBuffer);

                var input = new ReplayInput(
                    record.kind,
                    _player.Resolve(record.verbId),
                    _player.Resolve(record.targetId),
                    _player.Resolve(record.payloadTypeId),
                    new ReadOnlyMemory<byte>(_payloadBuffer, 0, length),
                    _player.Resolve(record.sourceId),
                    record.payloadTruncated);

                if (_applier.Apply(in input, out var error))
                {
                    appliedInputCount++;
                    continue;
                }

                failedInputCount++;
                Debug.LogWarning($"[RemoteControl] Replay could not apply {input}: {error}");
            }
        }
    }
}
