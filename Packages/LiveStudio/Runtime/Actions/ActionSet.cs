// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One entry in <see cref="ActionManager.actionSets"/>: a single firing <see cref="input"/> and the
    /// ordered <see cref="actions"/> run from it. The polymorphic <see cref="input"/> / <see cref="actions"/>
    /// use <c>[SerializeReference]</c> (Unity-native scene serialization) plus the RemoteControl
    /// <c>@type</c> discriminator, exactly like <see cref="ExposedCamera.controller"/>.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Action", Icon = "bolt")]
    public class ActionSet
    {
        /// <summary>Stable id, assigned by <see cref="ActionManager"/> on creation. Used to address the
        /// set from the remote app's add/remove functions. Hidden from the generic UI.</summary>
        [ExposedField, Hide]
        public string id = string.Empty;

        [ExposedField]
        public string name = "Action Set";

        [ExposedField]
        public bool enabled = true;

        /// <summary>Optional group name, like a layer. Sets that share a non-empty group and whose
        /// <see cref="input"/> is in <see cref="InputMode.Toggle"/> are mutually exclusive: turning one on
        /// clears the others (toggle-style radio — all-off is still allowed). Empty means ungrouped (no
        /// exclusivity). The remote app also groups its cards by this name.</summary>
        [ExposedField]
        public string group = string.Empty;

        /// <summary>The firing side. A single polymorphic field — the remote app swaps its concrete type
        /// through the <c>[TypeSelector]</c> dropdown (<c>FindDerivedTypes</c> only resolves derived types
        /// for a single field, not a list, so the actions list is edited through explicit add/remove).</summary>
        [ExposedField, TypeSelector]
        [SerializeReference, Select]
        public InputSource input = new KeyInputSource();

        /// <summary>The receiving side, run in order when the set fires.</summary>
        [ExposedField]
        [SerializeReference, Select]
        public List<ActionBase> actions = new List<ActionBase>();

        // Runtime-only manual hold driven by the remote app's trigger toggle (never serialized). While
        // held the set fires as if its input were held active; toggling off produces one release edge.
        [NonSerialized]
        private bool _held;

        /// <summary>Manual-hold state, read-only over the wire so the remote app's trigger button reflects
        /// it. Flipped only through <see cref="ActionManager.ToggleActionSet"/>. Hidden from the generic
        /// editor; runtime-only so it resets to false on restart.</summary>
        [ExposedProperty, Hide]
        public bool held => _held;

        /// <summary>Previous frame's <see cref="held"/>, owned by <see cref="ActionManager"/> for edge
        /// detection (rising on toggle-on, falling on toggle-off).</summary>
        [NonSerialized]
        internal bool heldPrev;

        /// <summary>Sets the manual hold. Manager-only; the remote app flips it via
        /// <see cref="ActionManager.ToggleActionSet"/>.</summary>
        internal void SetHeld(bool value) => _held = value;

        /// <summary>Runtime firing output (0..1) of the last <see cref="ActionManager.Update"/>: 1 while
        /// held, otherwise the bound input's evaluated value. Written by the manager each frame and read
        /// back through <see cref="ActionManager.actionSetValues"/> so the remote app can poll it. Not
        /// serialized and not individually exposed; resets to 0 on restart.</summary>
        [NonSerialized]
        internal float lastValue;
    }
}
