// Copyright (c) You-Ri, 2026

using System;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Reflection;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Drives an exposed property of a target object (addressed by its stable
    /// <see cref="ExposedObjectRegistry"/> id) from the input — the generic counterpart of
    /// <see cref="SetActiveOperation"/>, which is hard-wired to <c>GameObject.activeSelf</c>. Created from the
    /// remote app's "bind to key" affordance next to a control, so the control's owning object
    /// (<see cref="targetId"/>) and member (<see cref="propertyPath"/>) are known at creation.
    ///
    /// Handles <c>bool</c> properties (bound to <see cref="InputMode.Toggle"/> by the manager so a key
    /// press flips the latched on/off, written from <see cref="OperationContext.active"/>) and <c>float</c>
    /// properties (bound to <see cref="InputMode.Value"/>, written from the continuous 0..1
    /// <see cref="OperationContext.value"/> — e.g. an avatar expression weight via
    /// <c>expressions[name].weight</c>). Both write only when the value differs, so there is no
    /// per-frame write/broadcast, like <see cref="SetActiveOperation"/>.
    ///
    /// When the id no longer resolves (object gone after a reload) <see cref="valid"/> is false and
    /// <see cref="Apply"/> is a no-op — distinct from <see cref="OperationSet.enabled"/>.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Operation", Icon = "toggle_on")]
    [MovedFrom(false, null, null, "SetPropertyAction")]
    [FormerlyExposedAs("SetPropertyAction")]
    public class SetPropertyOperation : OperationBase
    {
        /// <summary>Stable id of the exposed object that owns the property.</summary>
        [ExposedField]
        public string targetId = string.Empty;

        /// <summary>Path of the property to drive. Stored in the remote app's transport (slash) form so it
        /// round-trips with the bind UI; e.g. "useSpout" or, for a keyed array element,
        /// "expressions/Joy/weight". Resolved via <see cref="_ResolvePath"/> just before lookup.</summary>
        [ExposedField]
        public string propertyPath = string.Empty;

        // Normalize the stored path (slash transport form) to the DotBracket form FindProperty expects.
        // A no-op for bare names ("useSpout") and DotBracket paths without slashes; converts a slash key
        // path "expressions/Joy/weight" to "expressions.Joy.weight" so it resolves by [ExposedKey].
        private string _ResolvePath() => PropertyPath.FromSlash(propertyPath).Value;

        /// <summary>True while the target id resolves and still exposes the property. Read-only and computed
        /// (never serialized); separate from <see cref="OperationSet.enabled"/>.</summary>
        [ExposedProperty]
        public bool valid
        {
            get
            {
                var handle = ExposedObjectRegistry.FindById(targetId);
                return handle != null && handle.Value.FindProperty(_ResolvePath()) != null;
            }
        }

        public override void Apply(in OperationContext context)
        {
            var handle = ExposedObjectRegistry.FindById(targetId);
            if (handle == null) return;

            var property = handle.Value.FindProperty(_ResolvePath());
            if (property == null) return;

            var current = property.Value.GetValue();
            switch (current)
            {
                case bool currentBool:
                    bool desired = context.active;
                    if (currentBool != desired) property.Value.SetValue(desired);
                    break;
                case float currentFloat:
                    // Value モード想定: 入力の連続値 (0..1) を制御の範囲 [min, max] へマッピングして書く
                    // (DeckSlider が min/max を持つ。既定 0..1 では恒等なので素の値を書くのと同じ)。
                    float mapped = context.MappedValue;
                    if (currentFloat != mapped) property.Value.SetValue(mapped);
                    break;
            }
        }
    }
}
