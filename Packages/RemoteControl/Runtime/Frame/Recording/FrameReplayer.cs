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

        /// <summary>How the target was addressed. The HTTP method for anything that came over REST.</summary>
        public readonly string method;

        /// <summary>What was addressed, e.g. the property path.</summary>
        public readonly string target;

        /// <summary>The value or arguments, as they arrived.</summary>
        public readonly string payload;

        /// <summary>Which producer it came from, for choosing what to replay and what to leave out.</summary>
        public readonly string source;

        /// <summary>True when the payload did not fit the record and was cut short at capture.</summary>
        public readonly bool payloadTruncated;

        public ReplayInput(InputKind kind, string method, string target, string payload, string source,
            bool payloadTruncated)
        {
            this.kind = kind;
            this.method = method;
            this.target = target;
            this.payload = payload;
            this.source = source;
            this.payloadTruncated = payloadTruncated;
        }

        public override string ToString() => $"{method} {target} ({kind})";
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
    public sealed class FrameReplayer : IDisposable
    {
        private readonly FrameRecordPlayer _player;
        private readonly IInputApplier _applier;

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

                var input = new ReplayInput(
                    record.kind,
                    _player.Resolve(record.methodId),
                    _player.Resolve(record.targetId),
                    record.payload.Length == 0 ? null : record.payload.ToString(),
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
