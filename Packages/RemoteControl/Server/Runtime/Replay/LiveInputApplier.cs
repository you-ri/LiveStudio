// Copyright (c) You-Ri, 2026
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Replay
{
    /// <summary>
    /// Puts recorded inputs back through the same dispatcher a request would have taken.
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
    public sealed class LiveInputApplier : IInputApplier
    {
        private readonly LiveObjectContainer _container;
        private readonly ILiveObjectResolver _resolver;

        /// <summary>
        /// Applies against a container, or against the global registry when none is given -- the
        /// same fallback a handler uses when it has no container of its own.
        /// </summary>
        public LiveInputApplier(LiveObjectContainer container = null)
        {
            _container = container;
            _resolver = container ?? (ILiveObjectResolver)DefaultLiveObjectResolver.Instance;
        }

        public bool Apply(in ReplayInput input, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(input.target))
            {
                error = "no target";
                return false;
            }

            if (string.IsNullOrEmpty(input.verb))
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
            if (input.payloadIsString)
            {
                if (LiveObjectHandler.ApplyRecordedValue(
                        _container, _resolver, input.target, input.text, out status, out message))
                {
                    return true;
                }

                error = $"{status} {message}";
                return false;
            }

            // A typed payload is written straight into the property: the bytes already are the
            // value, and going back out through text to come in through the parser again would
            // only add a way for the two to disagree.
            var valueType = input.payloadIsRequest ? null : InputPayload.Resolve(input.payloadTypeName);

            if (valueType != null)
            {
                if (!InputPayload.TryUnpack(valueType, input.payload.Span, out var value))
                {
                    error = $"payload does not fit {input.payloadTypeName}";
                    return false;
                }

                if (LiveObjectHandler.ApplyRecordedValue(
                        _container, _resolver, input.target, value, out status, out message))
                {
                    return true;
                }

                error = $"{status} {message}";
                return false;
            }

            if (!input.payloadIsRequest && !string.IsNullOrEmpty(input.payloadTypeName))
            {
                // The recording names a type this build does not have. Said rather than guessed at:
                // reading the bytes as something else would apply a plausible wrong value.
                error = $"unknown payload type '{input.payloadTypeName}'";
                return false;
            }

            if (LiveObjectHandler.ApplyRecordedOperation(
                    _container, _resolver, input.verb, input.target, input.text,
                    out status, out message))
            {
                return true;
            }

            error = $"{status} {message}";
            return false;
        }
    }
}
