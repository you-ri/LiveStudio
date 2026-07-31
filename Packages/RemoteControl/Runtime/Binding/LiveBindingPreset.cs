// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Asset holding attribute-less exposure settings, shared across scenes (the counterpart of
    /// Unreal Engine's Remote Control Preset asset).
    ///
    /// The asset carries two kinds of data:
    /// - <see cref="TypeDefinition"/>: which members of a type are exposed, with their UI metadata.
    ///   One definition per type — this is the single source for the type-level
    ///   <see cref="LiveClass"/> registration.
    /// - <see cref="InstanceBinding"/>: which scene objects are exposed. Each entry holds only a
    ///   stable GUID key; the actual scene reference is resolved through the standard
    ///   <see cref="IExposedPropertyTable"/> mechanism (same as PlayableDirector/Timeline) by a
    ///   <c>LiveBindingResolver</c> component in the scene. The key doubles as the LiveObject id,
    ///   so persisted values stay stable across scenes and renames.
    /// </summary>
    [CreateAssetMenu(menuName = "Live Studio/Remote Control/Live Binding Preset", fileName = "LiveBindingPreset")]
    public class LiveBindingPreset : ScriptableObject
    {
        /// <summary>Exposed member set of one type (single source of the LiveClass registration).</summary>
        [Serializable]
        public class TypeDefinition
        {
            [Tooltip("Assembly-qualified name of the target type")]
            public string typeName;

            public List<LiveBindingMember> members = new List<LiveBindingMember>();

            public Type ResolveType()
            {
                if (string.IsNullOrEmpty(typeName)) return null;
                return Type.GetType(typeName);
            }
        }

        /// <summary>
        /// One exposed scene object. <see cref="key"/> is both the IExposedPropertyTable
        /// reference name and the LiveObject id.
        /// </summary>
        [Serializable]
        public class InstanceBinding
        {
            public string key;

            [Tooltip("Assembly-qualified name of the expected type (for validation and missing display)")]
            public string typeName;

            public Type ResolveType()
            {
                if (string.IsNullOrEmpty(typeName)) return null;
                return Type.GetType(typeName);
            }
        }

        public List<TypeDefinition> typeDefinitions = new List<TypeDefinition>();

        public List<InstanceBinding> bindings = new List<InstanceBinding>();

        public TypeDefinition FindTypeDefinition(Type type)
        {
            if (type == null) return null;
            foreach (var def in typeDefinitions)
            {
                if (def != null && def.ResolveType() == type) return def;
            }
            return null;
        }

        public TypeDefinition GetOrAddTypeDefinition(Type type)
        {
            var def = FindTypeDefinition(type);
            if (def == null)
            {
                def = new TypeDefinition { typeName = type.AssemblyQualifiedName };
                typeDefinitions.Add(def);
            }
            return def;
        }
    }
}
