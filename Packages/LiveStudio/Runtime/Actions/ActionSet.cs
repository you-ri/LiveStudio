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
    }
}
