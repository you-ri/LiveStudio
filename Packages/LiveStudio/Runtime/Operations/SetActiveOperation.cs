// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;

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

        public override void Apply(in OperationContext context)
        {
            if (target == null) return;
            bool desired = context.active;
            if (target.activeSelf != desired) target.SetActive(desired);
        }
    }
}
