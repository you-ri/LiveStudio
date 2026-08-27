// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Puts the exposed state-lane members of every live object into the frame, once per frame.
    ///
    /// This is what makes a keyframe a superset of a scene snapshot: a snapshot holds the members
    /// that are persisted, and the frame holds every member declared <see cref="FrameLane.State"/>
    /// whether it is persisted or not. Leaving the unpersisted ones out is what would make a
    /// resynchronised machine drift straight back out of step.
    ///
    /// Only types the generator produced a bridge for take part. A member declared
    /// <see cref="FrameLane.State"/> on a type with no bridge is reported once, because silently
    /// carrying nothing looks exactly like carrying a world where nothing changed.
    /// </summary>
    public static class LiveStateSystem
    {
        private static FrameSource _source;
        private static bool _installed;
        private static long _capturedObjectCount;
        private static long _appliedObjectCount;

        /// <summary>Objects whose state was captured on the most recent frame.</summary>
        public static long capturedObjectCount => _capturedObjectCount;

        /// <summary>Objects whose state was written back on the most recent supplied frame.</summary>
        public static long appliedObjectCount => _appliedObjectCount;

        /// <summary>True while the per-frame capture is running.</summary>
        public static bool isRunning => _installed;

        /// <summary>
        /// Starts carrying exposed state at each frame head: written into the frame on a live one,
        /// read back out of it on a supplied one. Idempotent.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;

            _source = FrameGate.ResolveSource(kSourceName);
            FrameGate.AddFrameHeadHandler(_OnFrameHead);
            _installed = true;
        }

        /// <summary>Stops the per-frame capture. What is already in the frame stays there.</summary>
        public static void Uninstall()
        {
            if (!_installed) return;

            FrameGate.RemoveFrameHeadHandler(_OnFrameHead);
            _installed = false;
        }

        /// <summary>
        /// Writes every bridged live object's state into a set. Exposed separately from the frame
        /// head so a caller can take a snapshot of the world without waiting for one.
        /// </summary>
        public static int CaptureInto(StateBlockSet state, long time)
        {
            if (state == null) return 0;

            var captured = 0;

            foreach (var handle in LiveObjectRegistry.instances)
            {
                var target = handle.target;
                if (target == null) continue;

                var bridge = StateBridgeRegistry.Find(target.GetType());
                if (bridge == null) continue;

                bridge.Capture(target, FrameGate.symbols.Intern(handle.id), state, _source, time);
                captured++;
            }

            return captured;
        }

        /// <summary>
        /// Writes a set back onto the live objects it came from. Used by replay, after the structure
        /// has been reconciled so the objects it names exist.
        /// </summary>
        public static int ApplyFrom(StateBlockSet state)
        {
            if (state == null) return 0;

            var applied = 0;

            foreach (var handle in LiveObjectRegistry.instances)
            {
                var target = handle.target;
                if (target == null) continue;

                var bridge = StateBridgeRegistry.Find(target.GetType());
                if (bridge == null) continue;

                if (bridge.Apply(target, FrameGate.symbols.Intern(handle.id), state)) applied++;
            }

            return applied;
        }

        /// <summary>
        /// Creates a block for every registered type, so a recording can be played into a set that
        /// has somewhere to put each one. Without this a replay reports every type as unknown until
        /// something happens to have written it live first.
        /// </summary>
        public static void PrepareBlocks(StateBlockSet state)
        {
            if (state == null) return;

            var bridges = StateBridgeRegistry.all;
            for (int i = 0; i < bridges.Count; i++) bridges[i].EnsureBlock(state);
        }

        private const string kSourceName = "live";

        private static void _OnFrameHead(ref Frame frame)
        {
            // A supplied frame already carries the world. Capturing into it here would replace the
            // recording with the present, on the very frame meant to reproduce it -- and the replay
            // would then compare the present against itself and find no difference at all.
            if (frame.isSupplied)
            {
                _appliedObjectCount = ApplyFrom(frame.state);
                return;
            }

            _capturedObjectCount = CaptureInto(frame.state, frame.frameNumber);
        }
    }
}
