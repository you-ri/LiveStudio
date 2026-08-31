// Copyright (c) You-Ri, 2026
using System.Collections.Generic;

namespace Lilium.RemoteControl.Frames
{
    /// <summary>
    /// Decides the order of events arriving from several paths and threads.
    ///
    /// Numbering and hand-off happen under one lock so that sequence order is the order events were
    /// accepted. Without that, order is settled by whichever worker thread reached the queue first,
    /// which is well-defined on one machine but different on the next -- so two machines fed the
    /// same events would drift apart permanently.
    ///
    /// Draining swaps the pending list for a spare instead of copying, so a frame with no new
    /// events costs nothing.
    /// </summary>
    internal sealed class EventSequencer
    {
        private readonly object _lock = new object();
        private List<PendingEvent> _pending = new List<PendingEvent>(32);
        private List<PendingEvent> _spare = new List<PendingEvent>(32);
        private long _nextSequence = 1;

        /// <summary>Sequence number the next accepted event will get.</summary>
        public long nextSequence
        {
            get { lock (_lock) { return _nextSequence; } }
        }

        /// <summary>Number of events waiting for the next frame head.</summary>
        public int pendingCount
        {
            get { lock (_lock) { return _pending.Count; } }
        }

        /// <summary>
        /// Accepts an event and stamps its records with their place in the order.
        ///
        /// A group is numbered and handed over inside the one lock, so its records get a run of
        /// consecutive numbers and reach the same drain together. That is what keeps a bundled
        /// request from being split across two frames: the frame head takes the whole pending list
        /// at once, so a group is either entirely in a frame or entirely in the next one.
        /// </summary>
        public long Submit(PendingEvent evt)
        {
            lock (_lock)
            {
                var first = _nextSequence;

                for (int i = 0; i < evt.recordCount; i++)
                {
                    evt.records[i].sequence = first + i;
                }

                _nextSequence = first + evt.recordCount;
                _pending.Add(evt);
                return first;
            }
        }

        /// <summary>
        /// Hands over everything accepted so far. The returned list belongs to the caller until the
        /// next drain, at which point it is taken back and reused.
        /// </summary>
        public List<PendingEvent> Drain()
        {
            lock (_lock)
            {
                var drained = _pending;
                _spare.Clear();
                _pending = _spare;
                _spare = drained;
                return drained;
            }
        }

        /// <summary>Drops everything pending. Used when the run restarts.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _pending.Clear();
                _spare.Clear();
                _nextSequence = 1;
            }
        }
    }
}
