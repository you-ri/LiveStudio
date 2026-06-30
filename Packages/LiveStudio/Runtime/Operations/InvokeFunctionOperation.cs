// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Invokes a no-argument <c>[ExposedFunction]</c> on a target exposed object (addressed by its stable
    /// <see cref="ExposedObjectRegistry"/> id) when the input triggers — the generic counterpart of the
    /// feature-specific switch operations. Created from the remote app's "bind to key" affordance next to a
    /// function button, so the button's owning object (<see cref="targetId"/>) and method
    /// (<see cref="functionName"/>) are known at creation. Bound to <see cref="InputMode.Button"/> by the
    /// manager, so it commits once on key-up (<see cref="OperationContext.triggered"/>).
    ///
    /// The id is not guaranteed to resolve forever (the object may be gone after a reload); when it does
    /// not, <see cref="valid"/> is false and <see cref="Apply"/> is a no-op. This invalid state is distinct
    /// from <see cref="OperationSet.enabled"/> (the user's on/off): the set can be enabled yet dangling.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Operation", Icon = "bolt")]
    [MovedFrom(false, null, null, "InvokeFunctionAction")]
    [FormerlyExposedAs("InvokeFunctionAction")]
    public class InvokeFunctionOperation : OperationBase
    {
        /// <summary>Stable id of the exposed object that owns the function.</summary>
        [ExposedField]
        public string targetId = string.Empty;

        /// <summary>Name of the no-argument <c>[ExposedFunction]</c> to invoke.</summary>
        [ExposedField]
        public string functionName = string.Empty;

        // Functions are keyed by their lower-cased apiName (ExposedFunctionType.apiName), mirroring the REST
        // layer that lower-cases the path segment. The stored name keeps its original case for readability,
        // so it is normalized here at lookup.
        private string _ApiName() => functionName?.ToLowerInvariant();

        /// <summary>True while the target id resolves and still exposes the function. Read-only and computed
        /// (never serialized): the remote app surfaces it so a dangling operation reads as invalid, separate
        /// from <see cref="OperationSet.enabled"/>.</summary>
        [ExposedProperty]
        public bool valid
        {
            get
            {
                var handle = ExposedObjectRegistry.FindById(targetId);
                return handle != null && handle.Value.GetFunction(_ApiName()) != null;
            }
        }

        public override void Apply(in OperationContext context)
        {
            if (!context.triggered) return;
            // Guard the id lookup so a dangling target is a silent no-op rather than logging every press.
            var handle = ExposedObjectRegistry.FindById(targetId);
            if (handle == null) return;
            handle.Value.InvokeFunction(_ApiName(), null);
        }
    }
}
