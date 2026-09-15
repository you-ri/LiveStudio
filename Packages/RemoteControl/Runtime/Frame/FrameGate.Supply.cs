// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Lilium.RemoteControl.Frames
{
    // What fills a frame and what reads it back: the source a replay supplies frames from, the sink
    // that owns completed frames, the observers watching them, and the holds that make a replay wait
    // for a load.
    public static partial class FrameGate
    {
        private static int _supplyHolds;
        private static long _heldFrameCount;
        private static volatile string _supplyHoldReason;

        private static readonly List<IFrameObserver> _observers = new List<IFrameObserver>();
        private static IFrameObserver[] _observerSnapshot = Array.Empty<IFrameObserver>();
        private static bool _observerSnapshotStale;
        private static int _detachedObserverCount;

        private static IFrameSink _sink;
        private static IFrameSource _source;

        /// <summary>
        /// Where completed frames go: a recorder, a mirror sender, or nothing.
        ///
        /// One at a time. Two consumers at once is a fan-out sink's job rather than a list here --
        /// the gate should not be the place that decides what order they run in.
        /// </summary>
        public static IFrameSink sink
        {
            get => _sink;
            set => _sink = value;
        }

        /// <summary>
        /// Where frames come from when they are not being produced here: a recording being played,
        /// or another machine being followed. Null for an ordinary live run.
        ///
        /// One at a time, for the same reason as <see cref="sink"/>. Retired automatically when it
        /// runs out, which raises <see cref="onSourceEnded"/>.
        /// </summary>
        public static IFrameSource source
        {
            get => _source;
            set => _source = value;
        }

        /// <summary>
        /// Raised on the main thread when a source runs out and is detached, so whoever attached it
        /// can put the run back the way it was. Not raised when a source is cleared by hand -- the
        /// caller doing that already knows.
        /// </summary>
        public static event Action onSourceEnded;

        /// <summary>Observers watching frames go by. For diagnostics.</summary>
        public static int observerCount => _observers.Count;

        /// <summary>
        /// Observers dropped for throwing, since the run started. Not zero means something that was
        /// watching has stopped, which a viewer has to say out loud rather than just going quiet.
        /// </summary>
        public static int detachedObserverCount => _detachedObserverCount;

        /// <summary>
        /// Starts watching frames. Idempotent, and safe to call from inside a notification.
        ///
        /// Unlike <see cref="sink"/> there can be any number, and unlike <see cref="source"/> they
        /// survive <see cref="ResetState"/>: watching is not owning, and a watcher that quietly
        /// stopped at the start of a run is exactly the kind of silence this is here to find.
        /// </summary>
        public static void AddFrameObserver(IFrameObserver observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            if (_observers.Contains(observer)) return;

            _observers.Add(observer);
            _observerSnapshotStale = true;
        }

        /// <summary>Stops watching. Safe to call from inside a notification.</summary>
        public static void RemoveFrameObserver(IFrameObserver observer)
        {
            if (observer == null) return;
            if (!_observers.Remove(observer)) return;

            _observerSnapshotStale = true;
        }

        /// <summary>
        /// Holds a replay where it is until something it needs has finished.
        ///
        /// A recording carries the write that asked for an avatar, not the avatar: replaying it
        /// starts a load that takes as long as this machine takes, and the frames behind it address
        /// an object that is not there yet. Waiting is what keeps the take honest -- the alternative
        /// is a replay whose fidelity depends on disk speed.
        ///
        /// Only supplied frames are held. A live run has nothing to wait for, so a holder may take
        /// one out whenever it starts loading without asking whether a replay is running.
        ///
        /// Balanced by <see cref="ReleaseSupply"/>, and counted: two loads at once are two holds,
        /// and the replay goes on when the last of them is done.
        /// </summary>
        public static void HoldSupply(string reason)
        {
            Interlocked.Increment(ref _supplyHolds);
            _supplyHoldReason = reason;
        }

        /// <summary>Gives back a hold taken by <see cref="HoldSupply"/>.</summary>
        public static void ReleaseSupply(string reason)
        {
            if (Interlocked.Decrement(ref _supplyHolds) >= 0) return;

            // Never below zero: an unbalanced release would let the next hold be cancelled by it,
            // and a replay would then run past the load it was told to wait for.
            Interlocked.Exchange(ref _supplyHolds, 0);
            Debug.LogWarning($"[RemoteControl] Frame supply released more times than held ('{reason}').");
        }

        /// <summary>How many things a replay is currently waiting on.</summary>
        public static int supplyHoldCount => Interlocked.CompareExchange(ref _supplyHolds, 0, 0);

        /// <summary>What was most recently waited on, for a viewer to show while a replay stalls.</summary>
        public static string supplyHoldReason => _supplyHoldReason;

        /// <summary>
        /// Frames a replay stood still for, waiting. Not an error -- but a replay that spends most
        /// of its frames here is one whose timing no longer resembles the take.
        /// </summary>
        public static long heldFrameCount => Interlocked.Read(ref _heldFrameCount);

        private static void _FillFromSource()
        {
            var source = _source;
            if (source == null) return;

            // Something the take needs is still loading. The source is not asked for a frame, so it
            // stays where it is and the replay resumes from the same place -- rather than playing
            // frames into a world that has not finished being built.
            if (supplyHoldCount > 0)
            {
                Interlocked.Increment(ref _heldFrameCount);
                return;
            }

            try
            {
                if (source.FillFrame(ref _frame))
                {
                    _frame.isSupplied = true;
                    return;
                }
            }
            catch (Exception e)
            {
                // Detached rather than left to throw every frame, for the same reason as the sink:
                // one failure must not become one per frame, and the run is still fine live.
                Debug.LogError($"[RemoteControl] Frame source failed and was detached: {e}");
                _RetireSource(source);
                return;
            }

            // Ran out. The frame falls back to the live lanes it was already pointing at.
            _RetireSource(source);
        }

        /// <summary>
        /// Detaches a source that has finished or failed, and says so.
        ///
        /// Only if it is still the one attached. A take can carry the requests that stop it and
        /// start another (a replay of a session in which somebody replayed something), so by the
        /// time the frame comes back the source may already have been replaced -- and clearing the
        /// field then would detach the new one before its first frame.
        /// </summary>
        private static void _RetireSource(IFrameSource source)
        {
            if (!ReferenceEquals(_source, source)) return;

            _source = null;
            _RaiseSourceEnded();
        }

        private static void _RaiseSourceEnded()
        {
            try
            {
                onSourceEnded?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[RemoteControl] Frame source teardown failed: {e}");
            }
        }

        private static void _DeliverToSink()
        {
            var sink = _sink;
            if (sink == null) return;

            try
            {
                sink.OnFrameCompleted(in _frame, _symbols);
            }
            catch (Exception e)
            {
                // Detached rather than left to throw every frame: a recorder that has lost its disk
                // would otherwise turn one failure into one per frame, and the run itself is still
                // fine without it.
                _sink = null;
                Debug.LogError($"[RemoteControl] Frame sink failed and was detached: {e}");
            }
        }

        private static void _NotifyObservers()
        {
            if (_observerSnapshotStale)
            {
                _observerSnapshot = _observers.ToArray();
                _observerSnapshotStale = false;
            }

            var observers = _observerSnapshot;
            for (int i = 0; i < observers.Length; i++)
            {
                var observer = observers[i];

                try
                {
                    observer.OnFrameCompleted(in _frame, _symbols);
                }
                catch (Exception e)
                {
                    // Detached rather than left to throw every frame, like the sink. Counted as well
                    // as logged, so a viewer can say it stopped watching instead of just freezing.
                    RemoveFrameObserver(observer);
                    _detachedObserverCount++;
                    Debug.LogError($"[RemoteControl] Frame observer failed and was detached: {e}");
                }
            }
        }
    }
}
