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

        // Persisted manual hold: the switch on/off state driven by the remote app's toggle / momentary deck
        // tiles. Exposed directly as the field named `held` (NOT a shadow-of-property) so it serializes in the
        // nested array-element path too: the shadow-field indirection is only wired for top-level objects, so
        // a shadow `held` on this nested OperationSet silently drops from the live scene (its value never gets
        // written). A private field with no [SerializeField], so it lives only in the live-scene json, not
        // baked into the scene/prefab template. While held the set fires as if its input were held active.
        [ExposedField("held"), Hide]
        private bool _held;

        /// <summary>Manual-hold state; the remote app reads it (over the wire as <c>held</c>, from the field
        /// above) so its trigger button reflects it. Flipped only through
        /// <see cref="OperationManager.ToggleOperationSet"/>. Persisted so a set left on survives a scene
        /// reload. Plain C# read accessor — the <c>_held</c> field is the wire / persistence member.</summary>
        public bool held => _held;

        /// <summary>Previous frame's <see cref="held"/>, owned by <see cref="OperationManager"/> for edge
        /// detection (rising on toggle-on, falling on toggle-off).</summary>
        [NonSerialized]
        internal bool heldPrev;

        /// <summary>Latches true when <see cref="SetHeld"/> sees a rising edge, and is cleared by
        /// <see cref="OperationManager.Update"/> after it evaluates the set each frame. A momentary deck button
        /// sends its press and release as two separate REST calls (<c>held=true</c> then <c>held=false</c>); if
        /// both land between two Update frames, the manager would never observe <see cref="held"/> as true and
        /// the button's one-shot release trigger would be silently dropped (intermittent "no reaction" on fast
        /// taps). This pulse remembers that a press happened so the release still fires exactly once.</summary>
        [NonSerialized]
        internal bool heldPulse;

        /// <summary>Sets the manual hold. Manager-only; the remote app flips it via
        /// <see cref="OperationManager.ToggleOperationSet"/> / <c>SetOperationSetHeld</c>. A rising edge latches
        /// <see cref="heldPulse"/> so a press released before the next Update frame is not lost.</summary>
        internal void SetHeld(bool value)
        {
            if (value && !_held) heldPulse = true;
            _held = value;
        }

        // Persisted manual value override driven by the remote app's Value-mode slider (sticky once set).
        // -1 = no override (the bound input drives); 0..1 = the override value. Persisted (as an
        // [ExposedField]) so a slider the operator moved survives a scene reload, mirroring _held. -1 is the
        // sentinel rather than NaN because NaN has no JSON representation and breaks value-based dirty
        // comparison; SetOperationSetValue clamps to 0..1, so -1 is always out of band. A private field with
        // no [SerializeField], so it lives only in the live-scene json.
        [ExposedField, Hide]
        private float _manualValue = -1f;

        /// <summary>Manual value override (0..1), or negative when none (see <see cref="hasManualValue"/>).
        /// Owned by <see cref="OperationManager"/>; the remote app sets it via
        /// <see cref="OperationManager.SetOperationSetValue"/>.</summary>
        internal float manualValue => _manualValue;

        /// <summary>True when a manual value override is in effect (the slider has been moved). Until then the
        /// bound input drives the set.</summary>
        internal bool hasManualValue => _manualValue >= 0f;

        /// <summary>Sets the manual value override (callers clamp to 0..1). Manager-only.</summary>
        internal void SetManualValue(float value) => _manualValue = value;

        /// <summary>Runtime firing output (0..1) of the last <see cref="OperationManager.Update"/>: 1 while
        /// held, otherwise the bound input's evaluated value. Written by the manager each frame and read
        /// back through <see cref="OperationManager.operationSetValues"/> so the remote app can poll it. Not
        /// serialized and not individually exposed; resets to 0 on restart.</summary>
        [NonSerialized]
        internal float lastValue;
    }
}
