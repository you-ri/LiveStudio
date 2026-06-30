// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One entry in <see cref="OperationManager.operationSets"/>: a single firing <see cref="input"/> and the
    /// ordered <see cref="operations"/> run from it. The polymorphic <see cref="input"/> / <see cref="operations"/>
    /// use <c>[SerializeReference]</c> (Unity-native scene serialization) plus the RemoteControl
    /// <c>@type</c> discriminator, exactly like <see cref="ExposedCamera.controller"/>.
    /// </summary>
    [Serializable]
    [ExposedClass(Category = "Operation", Icon = "bolt")]
    // Renamed from ActionSet: MovedFrom restores old [SerializeReference] YAML, FormerlyExposedAs the @type.
    [MovedFrom(false, null, null, "ActionSet")]
    [FormerlyExposedAs("ActionSet")]
    public class OperationSet
    {
        /// <summary>Stable id, assigned by <see cref="OperationManager"/> on creation. Used to address the
        /// set from the remote app's add/remove functions. Hidden from the generic UI.</summary>
        [ExposedField, Hide]
        public string id = string.Empty;

        [ExposedField]
        public string name = "Operation Set";

        [ExposedField]
        public bool enabled = true;

        /// <summary>Optional group name, like a layer. Sets that share a non-empty group and whose
        /// <see cref="control"/> is in <see cref="InputMode.Toggle"/> (a <see cref="DeckToggle"/>) are
        /// mutually exclusive: turning one on clears the others (toggle-style radio — all-off is still
        /// allowed). Empty means ungrouped (no exclusivity). The remote app also groups its cards by this name.</summary>
        [ExposedField]
        public string group = string.Empty;

        /// <summary>The firing side. A single polymorphic field — the remote app swaps its concrete type
        /// through the <c>[TypeSelector]</c> dropdown (<c>FindDerivedTypes</c> only resolves derived types
        /// for a single field, not a list, so the operations list is edited through explicit add/remove).</summary>
        [ExposedField, TypeSelector]
        [SerializeReference, Select]
        public InputSource input = new KeyInputSource();

        /// <summary>The receiving side, run in order when the set fires.</summary>
        [ExposedField]
        [SerializeReference, Select]
        [FormerlyExposedAs("actions")]
        [FormerlySerializedAs("actions")]
        public List<OperationBase> operations = new List<OperationBase>();

        /// <summary>The deck representation of this set, embedded 1:1 so the operation and its deck tile are
        /// one object (adding/removing the operation adds/removes the control). The concrete kind
        /// (<see cref="DeckButton"/> / <see cref="DeckToggle"/> / <see cref="DeckSlider"/>) decides the
        /// touch behaviour and <see cref="DeckControl.deckId"/> where it is placed (empty = unplaced).
        /// Polymorphic like <see cref="input"/>; the remote app swaps the type via the manager.</summary>
        [ExposedField, TypeSelector]
        [SerializeReference, Select]
        public DeckControl control = new DeckButton();

        // Runtime-only manual hold driven by the remote app's trigger toggle (never serialized). While
        // held the set fires as if its input were held active; toggling off produces one release edge.
        [NonSerialized]
        private bool _held;

        /// <summary>Manual-hold state, read-only over the wire so the remote app's trigger button reflects
        /// it. Flipped only through <see cref="OperationManager.ToggleOperationSet"/>. Hidden from the generic
        /// editor; runtime-only so it resets to false on restart.</summary>
        [ExposedProperty, Hide]
        public bool held => _held;

        /// <summary>Previous frame's <see cref="held"/>, owned by <see cref="OperationManager"/> for edge
        /// detection (rising on toggle-on, falling on toggle-off).</summary>
        [NonSerialized]
        internal bool heldPrev;

        /// <summary>Sets the manual hold. Manager-only; the remote app flips it via
        /// <see cref="OperationManager.ToggleOperationSet"/>.</summary>
        internal void SetHeld(bool value) => _held = value;

        // Runtime-only manual value override driven by the remote app's Value-mode slider (sticky once set;
        // NaN = no override). While set the set fires with this value, overriding the bound input, until
        // restart — mirroring _held. Never serialized.
        [NonSerialized]
        private float _manualValue = float.NaN;

        /// <summary>Manual value override (0..1), or NaN when none. Owned by <see cref="OperationManager"/>;
        /// the remote app sets it via <see cref="OperationManager.SetOperationSetValue"/>.</summary>
        internal float manualValue => _manualValue;

        /// <summary>Sets the manual value override. Manager-only.</summary>
        internal void SetManualValue(float value) => _manualValue = value;

        /// <summary>Runtime firing output (0..1) of the last <see cref="OperationManager.Update"/>: 1 while
        /// held, otherwise the bound input's evaluated value. Written by the manager each frame and read
        /// back through <see cref="OperationManager.operationSetValues"/> so the remote app can poll it. Not
        /// serialized and not individually exposed; resets to 0 on restart.</summary>
        [NonSerialized]
        internal float lastValue;
    }
}
