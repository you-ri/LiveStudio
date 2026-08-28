// Copyright (c) You-Ri, 2026

using Newtonsoft.Json;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Builds the request body an operation would have sent if it had come in over the network.
    ///
    /// An operation applies its work directly -- it is already inside the application -- but the
    /// frame record it leaves behind has to be replayable, and a replay dispatches through the same
    /// routes a request does. So the body it records has to be shaped the way that dispatcher reads
    /// it, or the recorded call arrives with no arguments.
    ///
    /// Only reached on a discrete action (a press, a switch), never per frame, so building a small
    /// string here is not on a hot path.
    /// </summary>
    internal static class OperationRequest
    {
        /// <summary>
        /// Wraps stored call arguments -- a bare JSON array, the form the bind UI saves -- in the
        /// object the invoke route expects. Null for no arguments, which the route also accepts.
        /// </summary>
        public static string FromArgsJson(string argsJson)
            => string.IsNullOrEmpty(argsJson) ? null : $"{{\"args\":{argsJson}}}";

        /// <summary>
        /// The same, for a call whose one argument is a name the operator chose. Serialized rather
        /// than interpolated: a set or avatar may be named with a quote in it, and a hand-built
        /// string would turn that into a body the parser rejects.
        /// </summary>
        public static string FromArguments(params object[] arguments)
            => arguments == null || arguments.Length == 0
                ? null
                : $"{{\"args\":{JsonConvert.SerializeObject(arguments)}}}";
    }
}
