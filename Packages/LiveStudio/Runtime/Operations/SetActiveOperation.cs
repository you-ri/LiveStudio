// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Drives a scene GameObject's active state from the input: active while the value is above the
    /// activation threshold, inactive otherwise. The target is picked from the scene through the generic
    /// <c>[ObjectSelector]</c> dropdown.
    /// </summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "visibility")]
    [MovedFrom(false, null, null, "SetActiveAction")]
    [FormerlyNamedAs("SetActiveAction")]
    public class SetActiveOperation : OperationBase
    {
        [LiveField, ObjectSelector]
        public GameObject target;

        /// <summary>The operator's own controls, as a producer. See the assembly declaration.</summary>
        private static readonly FrameSource _source = FrameGate.ResolveSource("operation");

        public override void Apply(in OperationContext context)
        {
            if (target == null) return;

            bool desired = context.active;
            if (target.activeSelf == desired) return;

            // The GameObject's own proxy, not one of its components: they share a transform, so the
            // reference is what tells them apart. No proxy means the object is not exposed, and an
            // input with no address cannot be replayed -- applied directly and left out of the
            // record rather than recorded under an address that resolves to nothing.
            var proxyId = LiveObjectRegistry.FindProxyId(target);
            if (string.IsNullOrEmpty(proxyId))
            {
                target.SetActive(desired);
                return;
            }

            var path = $"/live/object/{proxyId}/active";
            var captured = target;

            FrameGate.Post(InputKind.PropertyWrite, _source, "PUT", path, () =>
            {
                if (captured == null) return;

                captured.SetActive(desired);
                FrameGate.StampAppliedPayload(path, typeof(bool), captured.activeSelf);
            });
        }
    }
}
