// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Polymorphic controller definition for an exposed binding member, serialized with
    /// [SerializeReference] on <see cref="LiveClassAssetMember.control"/> so the RemoteApp control
    /// can be switched generically in the editor. Derive and implement
    /// <see cref="ToControlAttribute"/> to add new controls. Null means the default control for
    /// the member's value type.
    /// </summary>
    [Serializable]
    public abstract class LiveBindingControl
    {
        /// <summary>Builds the <see cref="ControlAttribute"/> handed to the LiveClass registration.</summary>
        public abstract ControlAttribute ToControlAttribute();

        /// <summary>
        /// Contribution to the type registration signature (rebuild detection). Include every
        /// field that changes the wire output.
        /// </summary>
        public virtual void AppendSignature(System.Text.StringBuilder sb)
        {
            sb.Append(GetType().Name);
        }
    }

    /// <summary>Renders a numeric member as a slider.</summary>
    [Serializable]
    public class SliderControl : LiveBindingControl
    {
        public float min = 0f;

        public float max = 1f;

        [Tooltip("Slider step. 0 auto-derives (range / 20)")]
        public float step = 0f;

        public override ControlAttribute ToControlAttribute()
        {
            return new SliderAttribute(min, max, step);
        }

        public override void AppendSignature(System.Text.StringBuilder sb)
        {
            base.AppendSignature(sb);
            sb.Append(':').Append(min).Append(',').Append(max).Append(',').Append(step);
        }
    }

    /// <summary>Hides the member in RemoteApp (it stays readable/writable over REST).</summary>
    [Serializable]
    public class HideControl : LiveBindingControl
    {
        public override ControlAttribute ToControlAttribute()
        {
            return new HideAttribute();
        }
    }
}
