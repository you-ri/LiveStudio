// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Lilium.LiveStudio.Social
{
    /// <summary>
    /// The single intake point for <see cref="SocialEvent"/>s. Feeders (the HTTP endpoint, a WebSocket
    /// client, or user C# code) call <see cref="Enqueue"/> from any thread; consumers read
    /// <see cref="currentEvents"/> on the main thread.
    ///
    /// The list handed to consumers is swapped once per frame, so every consumer within a frame sees the
    /// same events in the same order — an input source that fires and an <see cref="onEvent"/> subscriber
    /// that logs can never disagree about what arrived. Static rather than a component: there is exactly
    /// one intake per process, and consumers must not have to find a GameObject to read it.
    /// </summary>
    public static class SocialEventHub
    {
        /// <summary>Maximum number of events buffered between two frames. Beyond this the oldest are
        /// dropped, which bounds the memory a flood can cost while keeping the newest (most relevant)
        /// events.</summary>
        public const int kQueueCapacity = 256;

        // Guards _pending and the buffer swap. Enqueue runs on arbitrary worker threads.
        private static readonly object _lock = new object();

        // Events accepted since the last swap. Written under _lock from any thread.
        private static List<SocialEvent> _pending = new List<SocialEvent>(kQueueCapacity);

        // The frame's published events. Main thread only. The two lists are swapped rather than copied, so
        // steady-state delivery allocates nothing.
        private static List<SocialEvent> _current = new List<SocialEvent>(kQueueCapacity);

        private static int _publishedFrame = -1;
        private static long _totalReceived;
        private static long _totalDropped;

        // Frame counter behind an indirection so edit-mode tests can advance frames deterministically:
        // Time.frameCount does not tick outside play mode, which would make every test run look like a
        // single frame. Reset to the Unity clock by Reset(), so a test's seam can never leak into play mode.
        internal static Func<int> frameProvider = _UnityFrameCount;

        // Clock behind the same kind of indirection, for the same reason: edit-mode tests need to advance
        // time deterministically to exercise anything that measures an interval between events (a
        // cooldown, say), and Unity's clock barely moves inside a synchronous test.
        internal static Func<double> timeProvider = _UnityTime;

        /// <summary>
        /// Raised once per event, on the main thread, right after the frame's list is published. The
        /// extension point for consumers that are not operations — comment overlays, TTS, logging.
        /// A subscriber that throws is logged and skipped; the remaining subscribers and events still
        /// get their turn, and the exception never escapes into whoever happened to pump this frame.
        ///
        /// IMPORTANT: firing is driven by the same lazy pump as <see cref="currentEvents"/>, which only
        /// runs when something reads the hub. In a scene where no <c>SocialGateway</c> and no input
        /// source reads events, subscribing here alone does not make events arrive — they queue up and
        /// surface whenever a read finally happens. Anything relying solely on this event must make sure
        /// the hub is read every frame.
        /// </summary>
        public static event Action<SocialEvent> onEvent;

        /// <summary>Total events accepted since startup, including ones later dropped by the queue bound.</summary>
        public static long totalReceived => Interlocked.Read(ref _totalReceived);

        /// <summary>Total events discarded because the queue was full. Surfaced so a flood is visible
        /// instead of silently eating events.</summary>
        public static long totalDropped => Interlocked.Read(ref _totalDropped);

        /// <summary>
        /// This frame's events, oldest first. Main thread only. Stable for the whole frame: repeated reads
        /// return the same list, and events enqueued mid-frame surface on the next frame.
        ///
        /// Do not hold the list across frames — it is recycled and refilled. Holding an individual
        /// <see cref="SocialEvent"/> is safe. In edit mode <c>Time.frameCount</c> does not advance, so the
        /// list publishes once and then stops changing; intake is a play-mode feature.
        /// </summary>
        public static IReadOnlyList<SocialEvent> currentEvents
        {
            get
            {
                _PublishIfNewFrame();
                return _current;
            }
        }

        /// <summary>
        /// Accepts an event from any thread. Normalizes it (null string fields become empty, a missing
        /// user becomes an empty one, over-long messages are truncated) so consumers can read every field
        /// without null checks in their per-frame loops.
        ///
        /// This is the permissive layer: a malformed-but-present event is repaired rather than rejected,
        /// because rejecting is the wire protocol's job — the HTTP endpoint answers 400 for a missing
        /// <see cref="SocialEvent.source"/> or <see cref="SocialEvent.type"/> before ever reaching here.
        ///
        /// The hub takes ownership of the instance: it is normalized in place and kept until the frame it
        /// is published in. Callers must not reuse or mutate an event after handing it over — the hub does
        /// not copy it, because copying every event would be the one allocation this design exists to avoid.
        /// </summary>
        public static void Enqueue(SocialEvent e)
        {
            if (e == null)
            {
                Debug.LogError("[Social] Enqueue was given a null event.");
                return;
            }

            _Normalize(e);

            lock (_lock)
            {
                if (_pending.Count >= kQueueCapacity)
                {
                    // Drop oldest. O(n) on a 256-slot list, and only on the overflow path, so the shift
                    // costs less than the bookkeeping a ring buffer would add to every other operation.
                    _pending.RemoveAt(0);
                    Interlocked.Increment(ref _totalDropped);
                }

                _pending.Add(e);
                Interlocked.Increment(ref _totalReceived);
            }
        }

        // Publishes _pending as this frame's events, at most once per frame. Called lazily from
        // currentEvents rather than from an Update, so the hub needs no scene presence and costs nothing
        // in a project that never reads it.
        private static void _PublishIfNewFrame()
        {
            int frame = frameProvider();
            if (frame == _publishedFrame) return;
            _publishedFrame = frame;

            lock (_lock)
            {
                var recycled = _current;
                _current = _pending;
                recycled.Clear();
                _pending = recycled;
            }

            if (_current.Count == 0) return;

            double now = timeProvider();
            for (int i = 0; i < _current.Count; i++)
            {
                _current[i].receivedTime = now;
            }

            // Outside the lock: a subscriber may do arbitrary work, including enqueuing further events.
            var handler = onEvent;
            if (handler == null) return;
            for (int i = 0; i < _current.Count; i++)
            {
                // Guard per event, not per subscriber. This is a public extension point invoking arbitrary
                // user code; unguarded, one throw would abandon the rest of the frame's delivery and
                // escape into whichever caller happened to pump the hub — in practice the operation
                // manager's input loop. Per-subscriber isolation would need GetInvocationList(), which
                // allocates every frame, so a thrower still costs the later subscribers this one event.
                // Logged, never swallowed silently.
                try
                {
                    handler(_current[i]);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Social] A SocialEventHub.onEvent subscriber threw; skipping it for this event.");
                    Debug.LogException(ex);
                }
            }
        }

        private static void _Normalize(SocialEvent e)
        {
            e.source ??= string.Empty;
            e.type ??= string.Empty;
            e.id ??= string.Empty;
            e.currency ??= string.Empty;

            // The user object and its own strings are all optional on the wire, so both levels need
            // filling: a feeder that sends {"user":{"isMember":true}} leaves id and name null otherwise.
            e.user ??= new SocialUser();
            e.user.id ??= string.Empty;
            e.user.name ??= string.Empty;

            // NaN is not a quantity, and JSON can carry it (Newtonsoft accepts the literal). Left alone it
            // poisons everything downstream: every comparison against it is false, so a minimum-amount
            // filter lets it through, and a consumer that writes it to a property re-writes it forever
            // because NaN never equals the value already there. Infinities are fine — they compare and
            // clamp the way an absurdly large tip should.
            if (float.IsNaN(e.amount)) e.amount = 0f;

            if (e.message == null)
            {
                e.message = string.Empty;
            }
            else if (e.message.Length > SocialEvent.kMaxMessageLength)
            {
                // Back off one unit when the cut would land inside a surrogate pair. Chat is full of
                // emoji, and half a pair is an ill-formed string that breaks whoever serializes it next.
                int cut = SocialEvent.kMaxMessageLength;
                if (char.IsHighSurrogate(e.message[cut - 1])) cut--;
                e.message = e.message.Substring(0, cut);
            }
        }

        // Clears every static field so entering play mode behaves identically with Domain Reload disabled.
        // Also the reset the edit-mode tests use between cases.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            lock (_lock)
            {
                _pending.Clear();
                _current.Clear();
                Interlocked.Exchange(ref _totalReceived, 0);
                Interlocked.Exchange(ref _totalDropped, 0);
            }

            _publishedFrame = -1;
            onEvent = null;
            frameProvider = _UnityFrameCount;
            timeProvider = _UnityTime;
        }

        private static int _UnityFrameCount() => Time.frameCount;

        private static double _UnityTime() => Time.unscaledTimeAsDouble;
    }
}
