// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Asset that declares a <see cref="LiveClass"/> without code — the counterpart of
    /// <see cref="LiveClassAttribute"/> for types you cannot decorate (built-in Unity types,
    /// third-party components) or don't want to.
    ///
    /// The asset carries two kinds of data:
    /// - <see cref="TypeDefinition"/>: which members of a type are exposed, with their UI metadata.
    ///   One definition per type — this is the single source for the type-level
    ///   <see cref="LiveClass"/> registration.
    /// - <see cref="InstanceBinding"/>: which scene objects are exposed. Each entry holds only a
    ///   stable GUID key; the actual scene reference is resolved through the standard
    ///   <see cref="IExposedPropertyTable"/> mechanism (same as PlayableDirector/Timeline) by a
    ///   <c>RemoteControlContainer</c> in the scene. The key doubles as the LiveObject id,
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

            [Tooltip("Type category shown in RemoteApp. Empty falls back to \"Binding\"")]
            public string category;

            [Tooltip("Material Icons name shown in RemoteApp. Empty uses the type's default icon")]
            public string icon;

            public List<LiveClassAssetMember> members = new List<LiveClassAssetMember>();

            public Type ResolveType()
            {
                if (string.IsNullOrEmpty(typeName)) return null;
                return Type.GetType(typeName);
            }

            /// <summary>
            /// Members in the order they should be exposed. <see cref="LiveClassAssetMember.order"/>
            /// shifts an entry away from its list position (negative first, positive last); entries
            /// sharing an order keep their list order, so leaving every order at 0 changes nothing.
            /// </summary>
            public IEnumerable<LiveClassAssetMember> OrderedMembers()
            {
                // OrderBy is a stable sort, which is what makes list order the tie-breaker.
                return members.OrderBy(m => m != null ? m.order : 0);
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

        [Tooltip("Forbid writes through the API and show the member as display-only")]
        public bool readOnly;

        [Tooltip("Material Icons name for the button. Functions only")]
        public string icon;

        [Tooltip("Section this member starts. Leave the title empty to not start one")]
        public LiveClassAssetSection section = new LiveClassAssetSection();

        [Tooltip("Explicit display order. Negative moves earlier, positive later, 0 keeps list order")]
        public int order;

        [Tooltip("Which lane of the live data carries this member. Auto puts fields on the state lane and properties on the input lane")]
        public LiveClassAssetLane lane = LiveClassAssetLane.Auto;

        [Tooltip("Controller used to render the member in RemoteApp. None = default for the value type")]
        [SerializeReference, Select]
        public LiveBindingControl control;

        /// <summary>
        /// The lane this member asks for, resolving <see cref="LiveClassAssetLane.Auto"/> against
        /// what the member actually is.
        ///
        /// A field defaults to the state lane and a property to the input lane, because that is what
        /// the two usually are: a field holds a value that something else drives every frame, and a
        /// property is written from outside. Either can be said explicitly when it is not.
        /// </summary>
        internal FrameLane ResolveLane(Type ownerType)
        {
            switch (lane)
            {
                case LiveClassAssetLane.Event: return FrameLane.Event;
                case LiveClassAssetLane.State: return FrameLane.State;
                case LiveClassAssetLane.None: return FrameLane.None;
            }

            if (ownerType == null || string.IsNullOrEmpty(path)) return FrameLane.Event;

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance;

            return ownerType.GetField(path, flags) != null
                ? FrameLane.State
                : FrameLane.Event;
        }

        internal LivePropertyDefine ToPropertyDefine(Type ownerType = null)
        {
            return new LivePropertyDefine
            {
                name = path,
                path = path,
                lane = ResolveLane(ownerType),
                isPersistable = persistable,
                isReadOnly = readOnly,
                persistScope = PersistScope.Scene,
                control = control?.ToControlAttribute(),
                label = string.IsNullOrEmpty(label) ? null : label,
                help = string.IsNullOrEmpty(help) ? null : help,
                section = section?.ToSectionAttribute(),
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
                icon = string.IsNullOrEmpty(icon) ? null : icon,
                section = section?.ToSectionAttribute(),
            };
        }

        /// <summary>
        /// Contribution to the per-type registration signature. Any change in the returned string
        /// forces the type's LiveClass to be rebuilt (see <see cref="LiveClassAssetSystem"/>).
        /// Every field that reaches the wire has to appear here, or edits to it look like they
        /// were ignored until something else forces a rebuild.
        /// </summary>
        internal void AppendSignature(System.Text.StringBuilder sb)
        {
            sb.Append(isFunction ? 'f' : 'p').Append(':').Append(path)
                .Append('|').Append(label).Append('|').Append(help)
                .Append('|').Append(persistable ? '1' : '0')
                .Append('|').Append(readOnly ? '1' : '0')
                .Append('|').Append(icon)
                .Append('|').Append(order);
            section?.AppendSignature(sb);
            if (control != null)
            {
                sb.Append("|c:");
                control.AppendSignature(sb);
            }
            sb.Append(';');
        }
    }

    /// <summary>
    /// Section header declared on a <see cref="LiveClassAssetMember"/>. Serializable counterpart of
    /// <see cref="SectionAttribute"/>, which cannot be authored in the inspector.
    /// </summary>
    [Serializable]
    public class LiveClassAssetSection
    {
        [Tooltip("Section title (localization key or literal). Empty means the member starts no section")]
        public string title;

        [Tooltip("Optional section subtitle (localization key or literal)")]
        public string subtitle;

        [Tooltip("Optional Material Icons name shown next to the title")]
        public string icon;

        public bool isEmpty => string.IsNullOrEmpty(title);

        /// <summary>Returns null when no title is set, so the member simply joins the current section.</summary>
        internal SectionAttribute ToSectionAttribute()
        {
            if (isEmpty) return null;
            return new SectionAttribute(
                string.IsNullOrEmpty(icon) ? null : icon,
                title,
                string.IsNullOrEmpty(subtitle) ? null : subtitle);
        }

        internal void AppendSignature(System.Text.StringBuilder sb)
        {
            if (isEmpty) return;
            sb.Append("|s:").Append(icon).Append(',').Append(title).Append(',').Append(subtitle);
        }
    }
}
