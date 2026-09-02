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
            /// Why a member that asked for the state lane did not get it.
            /// </summary>
            public enum LaneRefusal
            {
                /// <summary>It got the lane it asked for.</summary>
                None = 0,

                /// <summary>Its value cannot be moved as bytes (see DeclaredStateBridge.CanCarry).</summary>
                UnsupportedType = 1,

            }

            /// <summary>
            /// The lane that will actually carry this member.
            ///
            /// Asking for the state lane and getting it are two different things: the lane moves
            /// values as bytes, so a member whose value cannot be moved that way is carried as
            /// events instead.
            ///
            /// This is the single answer to that question. Registration gives the member the lane
            /// this returns, so what the frame carries and what an editor shows cannot disagree --
            /// and registering the asked-for lane instead is what used to drop such a member out of
            /// both lanes at once, the block leaving it out while the write path went on omitting
            /// its event record.
            /// </summary>
            public FrameLane EffectiveLaneOf(LiveClassAssetMember member, Type ownerType = null)
                => EffectiveLaneOf(member, ownerType, out _);

            /// <inheritdoc cref="EffectiveLaneOf(LiveClassAssetMember, Type)"/>
            public FrameLane EffectiveLaneOf(LiveClassAssetMember member, Type ownerType,
                out LaneRefusal refusal)
            {
                refusal = LaneRefusal.None;
                if (member == null) return FrameLane.Event;

                ownerType = ownerType ?? ResolveType();

                var asked = member.ResolveLane(ownerType);
                if (member.isFunction || asked != FrameLane.State) return asked;

                if (Frames.DeclaredStateBridge.CanCarry(member.ResolveValueType(ownerType)))
                {
                    return FrameLane.State;
                }

                refusal = LaneRefusal.UnsupportedType;
                return FrameLane.Event;
            }

            /// <summary>
            /// Bytes one object of this type adds to every frame: the metadata, the layout hash and
            /// the declared values.
            ///
            /// There is no cap to measure against -- the block is built to fit -- so this is a price
            /// rather than a budget. It is worth showing because it is the one cost of the state
            /// lane that is paid whether or not the value ever changes.
            /// </summary>
            public int MeasureFrameCost(Type ownerType)
            {
                ownerType = ownerType ?? ResolveType();

                var values = 0;
                foreach (var member in OrderedMembers())
                {
                    if (member == null || member.isFunction) continue;
                    if (EffectiveLaneOf(member, ownerType) != FrameLane.State) continue;

                    values += Frames.DeclaredStateBridge.SizeOf(member.ResolveValueType(ownerType));
                }

                if (values == 0) return 0;

                return Frames.DeclaredStateBlock.StrideFor(Frames.DeclaredStateBridge.kLayoutSize + values);
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

        [Tooltip("Which lane of the live data carries this member. Auto puts fields on the state lane and properties on the event lane")]
        public LiveClassAssetLane lane = LiveClassAssetLane.Auto;

        [Tooltip("Controller used to render the member in RemoteApp. None = default for the value type")]
        [SerializeReference, Select]
        public LiveBindingControl control;

        /// <summary>
        /// The lane this member asks for, resolving <see cref="LiveClassAssetLane.Auto"/> against
        /// what the member actually is.
        ///
        /// A field defaults to the state lane and a property to the event lane, because that is what
        /// the two usually are: a field holds a value that something else drives every frame, and a
        /// property is written from outside. Either can be said explicitly when it is not.
        ///
        /// What the member asks for. <see cref="EffectiveLane"/> is what it gets.
        /// </summary>
        public FrameLane ResolveLane(Type ownerType)
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

        /// <summary>
        /// The type of the value behind this member, or null when the owner does not have it (a
        /// renamed member, or a type that failed to resolve) or when it is a function.
        /// </summary>
        public Type ResolveValueType(Type ownerType)
        {
            if (isFunction || ownerType == null || string.IsNullOrEmpty(path)) return null;

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance;

            var property = ownerType.GetProperty(path, flags);
            if (property != null) return property.PropertyType;

            return ownerType.GetField(path, flags)?.FieldType;
        }

        /// <summary>
        /// Builds the registration entry.
        ///
        /// The lane is passed in rather than worked out here: whether this member fits in the state
        /// block depends on the members declared before it, which is a question only the whole type
        /// definition can answer. See <see cref="TypeDefinition.EffectiveLaneOf"/>.
        /// </summary>
        internal LivePropertyDefine ToPropertyDefine(Type ownerType, FrameLane lane)
        {
            return new LivePropertyDefine
            {
                name = path,
                path = path,
                lane = lane,
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
                // A function has no owner type to resolve against, and State would mean nothing on
                // one anyway, so the declared value is taken as it stands.
                lane = ResolveLane(null),
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
                .Append('|').Append(order)
                .Append('|').Append((int)lane);
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
