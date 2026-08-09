// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Asset that declares a <see cref="LiveClass"/> without code — the counterpart of
    /// <see cref="LiveClassAttribute"/> for types you cannot decorate (built-in Unity types,
    /// third-party components) or don't want to. Modeled on Unreal Engine's Remote Control
    /// Preset asset.
    ///
    /// The asset carries two kinds of data:
    /// - <see cref="TypeDefinition"/>: which members of a type are exposed, with their UI metadata.
    ///   One definition per type — this is the single source for the type-level
    ///   <see cref="LiveClass"/> registration.
    /// - <see cref="InstanceBinding"/>: which scene objects are exposed. Each entry holds only a
    ///   stable GUID key; the actual scene reference is resolved through the standard
    ///   <see cref="IExposedPropertyTable"/> mechanism (same as PlayableDirector/Timeline) by a
    ///   <c>LiveClassBinding</c> component in the scene. The key doubles as the LiveObject id,
    ///   so persisted values stay stable across scenes and renames.
    /// </summary>
    [MovedFrom(false, null, null, "LiveBindingPreset")]
    [CreateAssetMenu(menuName = "Live Studio/Remote Control/Live Class Asset", fileName = "LiveClassAsset")]
    public class LiveClassAsset : ScriptableObject
    {
        /// <summary>Exposed member set of one type (single source of the LiveClass registration).</summary>
        [Serializable]
        public class TypeDefinition
        {
            [Tooltip("Assembly-qualified name of the target type")]
            public string typeName;

            public List<LiveClassAssetMember> members = new List<LiveClassAssetMember>();

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

    /// <summary>
    /// One exposed member (property/field or method) of a <see cref="LiveClassAsset"/> type
    /// definition. Carries the UI metadata that attribute-based exposure would normally provide
    /// via [Control]/[Help]/[LiveProperty], so arbitrary compiled types can be exposed without
    /// any code changes.
    /// </summary>
    [MovedFrom(false, null, null, "LiveBindingMember")]
    [Serializable]
    public class LiveClassAssetMember
    {
        [Tooltip("Member (property/field/method) name on the target type")]
        public string path;

        [Tooltip("True when the member is a method exposed as a RemoteApp button")]
        public bool isFunction;

        [Tooltip("Optional display label shown in RemoteApp instead of the member name")]
        public string label;

        [Tooltip("Optional help text shown in RemoteApp")]
        public string help;

        [Tooltip("Include this member in live-scene save/restore")]
        public bool persistable = true;

        [Tooltip("Controller used to render the member in RemoteApp. None = default for the value type")]
        [SerializeReference, Select]
        public LiveBindingControl control;

        internal LivePropertyDefine ToPropertyDefine()
        {
            return new LivePropertyDefine
            {
                name = path,
                path = path,
                isPersistable = persistable,
                persistScope = PersistScope.Scene,
                control = control?.ToControlAttribute(),
                label = string.IsNullOrEmpty(label) ? null : label,
                help = string.IsNullOrEmpty(help) ? null : help,
            };
        }

        internal LiveFunctionDefine ToFunctionDefine()
        {
            return new LiveFunctionDefine
            {
                name = path,
                path = path,
                label = string.IsNullOrEmpty(label) ? null : label,
                help = string.IsNullOrEmpty(help) ? null : help,
            };
        }

        /// <summary>
        /// Contribution to the per-type registration signature. Any change in the returned string
        /// forces the type's LiveClass to be rebuilt (see <see cref="LiveClassAssetSystem"/>).
        /// </summary>
        internal void AppendSignature(System.Text.StringBuilder sb)
        {
            sb.Append(isFunction ? 'f' : 'p').Append(':').Append(path)
                .Append('|').Append(label).Append('|').Append(help)
                .Append('|').Append(persistable ? '1' : '0');
            if (control != null)
            {
                sb.Append("|c:");
                control.AppendSignature(sb);
            }
            sb.Append(';');
        }
    }
}
