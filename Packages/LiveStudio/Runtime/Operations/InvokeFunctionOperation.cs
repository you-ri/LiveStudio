// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Invokes an <c>[LiveFunction]</c> on a target exposed object (addressed by its stable
    /// <see cref="LiveObjectRegistry"/> id) when the input triggers — the generic counterpart of the
    /// feature-specific switch operations. Created from the remote app's "bind to key" affordance next to a
    /// function button, so the button's owning object (<see cref="targetId"/>) and method
    /// (<see cref="functionName"/>) are known at creation. Bound to <see cref="InputMode.Button"/> by the
    /// manager, so it commits once on key-up (<see cref="OperationContext.triggered"/>).
    ///
    /// Arguments are optional: <see cref="argsJson"/> stores the positional call arguments as a JSON array
    /// (the same shape the REST invoke sends), captured from the function's argument dialog at creation.
    /// An empty <see cref="argsJson"/> keeps the original no-argument behaviour. The function may live on a
    /// nested property value rather than the target object directly (via <see cref="propertyPath"/>), e.g. a
    /// StageManager set element's <c>WarpTo</c>.
    ///
    /// The id is not guaranteed to resolve forever (the object may be gone after a reload); when it does
    /// not, <see cref="valid"/> is false and <see cref="Apply"/> is a no-op. This invalid state is distinct
    /// from <see cref="OperationSet.enabled"/> (the user's on/off): the set can be enabled yet dangling.
    /// </summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "bolt")]
    [MovedFrom(false, null, null, "InvokeFunctionAction")]
    [FormerlyNamedAs("InvokeFunctionAction")]
    public class InvokeFunctionOperation : OperationBase
    {
        /// <summary>Stable id of the exposed object that owns the function.</summary>
        [LiveField]
        public string targetId = string.Empty;

        /// <summary>Name of the <c>[LiveFunction]</c> to invoke.</summary>
        [LiveField(lane = FrameLane.State, textCapacity = 64)]
        public string functionName = string.Empty;

        /// <summary>Path (remote-app slash transport form; e.g. "sets/0") of the property whose value owns the
        /// function, when it is not on <see cref="targetId"/> directly — e.g. a StageManager set element's
        /// <c>WarpTo</c>. Empty means the function is on the target object itself (the common case). Resolved
        /// the same way the REST invoke resolves a nested function.</summary>
        [LiveField]
        public string propertyPath = string.Empty;

        /// <summary>Positional call arguments as a JSON array string (e.g. <c>[0.5,"hello"]</c>), in the
        /// function's parameter order — the same representation the REST invoke body carries. Empty means no
        /// arguments (the original behaviour). Not editing this by hand is expected: it is captured from the
        /// function argument dialog when the operation is created.</summary>
        [LiveField]
        public string argsJson = string.Empty;

        // Functions are keyed by their lower-cased apiName (LiveFunctionType.apiName), mirroring the REST
        // layer that lower-cases the path segment. The stored name keeps its original case for readability,
        // so it is normalized here at lookup.
        private string _ApiName() => functionName?.ToLowerInvariant();

        /// <summary>True while the target id resolves and still exposes the function (following
        /// <see cref="propertyPath"/> when set). Read-only and computed (never serialized): the remote app
        /// surfaces it so a dangling operation reads as invalid, separate from
        /// <see cref="OperationSet.enabled"/>.</summary>
        [LiveProperty]
        public bool valid
        {
            get
            {
                var handle = LiveObjectRegistry.FindById(targetId);
                return handle != null && handle.Value.ResolveFunction(propertyPath, _ApiName(), out _) != null;
            }
        }

        /// <summary>The operator's own controls, as a producer. See the assembly declaration.</summary>
        private static readonly FrameSource _source = FrameGate.ResolveSource("operation");

        public override void Apply(in OperationContext context)
        {
            if (!context.triggered) return;
            // Guard the id lookup so a dangling target is a silent no-op rather than logging every press.
            var handle = LiveObjectRegistry.FindById(targetId);
            if (handle == null) return;

            // Sent through the frame gate rather than called here: a call never shows up as a value
            // changing, so an unrecorded one is a part of the show a replay simply does not have.
            // The arguments travel as the request text, which is the form the remote path uses too.
            var target = LivePath();

            FrameGate.Post(EventKind.FunctionCall, _source, "POST", target, _Invoke,
                OperationRequest.FromArgsJson(argsJson));
        }

        /// <summary>
        /// The call, run at a frame head. Resolved here rather than where it was posted: a frame
        /// passes in between and the target may be gone by the time this runs.
        /// </summary>
        private void _Invoke()
        {
            var handle = LiveObjectRegistry.FindById(targetId);
            if (handle == null) return;

            // Resolve the function on the target directly, or on the value at propertyPath for a nested member
            // (e.g. a StageManager set's WarpTo). A dangling path/function is a silent no-op: valid surfaces it.
            var function = handle.Value.ResolveFunction(propertyPath, _ApiName(), out var functionTarget);
            if (function == null || !function.isValid) return;

            // A call declared off the live data leaves no record, whichever control pressed it. The
            // remote path has asked this since functions got a lane; this one did not, so the same
            // machine-side call -- reopening a socket, resyncing a clock -- stayed out of the take
            // when it came over the network and went into it when it came from a deck key, and a
            // replay pressed it again on the operator's own machine.
            if (function.lane == FrameLane.None) FrameGate.OmitAppliedRecord(LivePath());
            // No arguments (empty argsJson) keeps passing null, exactly as before. When present, the stored
            // JSON is deserialized to the function's parameter types just like the REST invoke path. A trigger
            // is a discrete user action (key-up / rising edge), so parsing here is not on a per-frame hot path.
            var args = string.IsNullOrEmpty(argsJson)
                ? null
                : LivePropertySerializer.BuildInvokeArgumentsFromJson(function, argsJson);
            // Guard the invoke so a throwing bound function does not break the OperationManager update loop
            // (the same protection LiveObjectHandle.InvokeFunction provides for the direct path).
            try
            {
                function.Invoke(function.isStatic ? null : functionTarget, args);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[LiveStudio] InvokeFunctionOperation failed to invoke '{functionName}': {ex.Message}");
            }
        }

        /// <summary>
        /// The call's address in the same form a remote invoke uses, so a recorded operation
        /// replays through exactly the path a recorded request does.
        /// </summary>
        internal string LivePath()
            => string.IsNullOrEmpty(propertyPath)
                ? $"/live/function/{targetId}/{_ApiName()}"
                : $"/live/function/{targetId}/{propertyPath}/{_ApiName()}";
    }
}
