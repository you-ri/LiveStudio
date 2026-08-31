// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Replay
{
    /// <summary>
    /// Puts recorded events back through the same dispatcher a request would have taken.
    ///
    /// The same table, matched by the same method and path, so replaying a write reaches exactly the
    /// operation the live run reached. Reimplementing the routing for replay would give two answers
    /// to the same question and they would drift.
    ///
    /// What is deliberately not here: the outward side effects of applying are not suppressed. A
    /// replayed write marks the scene dirty and shows up in the change feed the way a live one does.
    /// That is fine for recording and comparing on one machine and wrong for anything that talks to
    /// the outside; the suppression belongs with whoever adds mirroring.
    /// </summary>
    public sealed class LiveEventApplier : IEventApplier
    {
        private readonly LiveObjectContainer _container;
        private readonly ILiveObjectResolver _resolver;

        /// <summary>
        /// Applies against a container, or against the global registry when none is given -- the
        /// same fallback a handler uses when it has no container of its own.
        /// </summary>
        public LiveEventApplier(LiveObjectContainer container = null)
        {
            _container = container;
            _resolver = container ?? (ILiveObjectResolver)DefaultLiveObjectResolver.Instance;
        }

        public bool Apply(in ReplayEvent evt, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(evt.target))
            {
                error = "no target";
                return false;
            }

            if (string.IsNullOrEmpty(evt.verb))
            {
                // Recorded before the verb was carried, or by a source that never had one. Guessing
                // would be worse than refusing: the same path answers to more than one verb.
                error = "no verb recorded";
                return false;
            }

            int status;
            string message;

            // A string value is written straight in too. It is a value like any other; the only
            // difference is that its bytes say their own length rather than having a fixed one.
            if (evt.payloadIsString)
            {
                if (LiveObjectHandler.ApplyRecordedValue(
                        _container, _resolver, evt.target, evt.text, out status, out message))
                {
                    return true;
                }

                error = $"{status} {message}";
                return false;
            }

            // A typed payload is written straight into the property: the bytes already are the
            // value, and going back out through text to come in through the parser again would
            // only add a way for the two to disagree.
            var valueType = evt.payloadIsRequest ? null : EventPayload.Resolve(evt.payloadTypeName);

            if (valueType != null)
            {
                if (!EventPayload.TryUnpack(valueType, evt.payload.Span, out var value))
                {
                    error = $"payload does not fit {evt.payloadTypeName}";
                    return false;
                }

                if (LiveObjectHandler.ApplyRecordedValue(
                        _container, _resolver, evt.target, value, out status, out message))
                {
                    return true;
                }

                error = $"{status} {message}";
                return false;
            }

            if (!evt.payloadIsRequest && !string.IsNullOrEmpty(evt.payloadTypeName))
            {
                // The recording names a type this build does not have. Said rather than guessed at:
                // reading the bytes as something else would apply a plausible wrong value.
                error = $"unknown payload type '{evt.payloadTypeName}'";
                return false;
            }

            if (LiveObjectHandler.ApplyRecordedOperation(
                    _container, _resolver, evt.verb, evt.target, evt.text,
                    out status, out message))
            {
                return true;
            }

            error = $"{status} {message}";
            return false;
        }
    }
}
