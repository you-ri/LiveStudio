// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>One exposable member (property/field/method) of a candidate type.</summary>
    internal struct MemberCandidate
    {
        public string path;
        public bool isFunction;
        public Type valueType; // null for functions
        public string typeLabel;
    }

    /// <summary>
    /// Type-level expose/unexpose logic shared by <see cref="LiveClassAssetWindow"/> and
    /// <see cref="LiveClassAssetAddMemberWindow"/>, which edit the same
    /// <see cref="LiveClassAsset"/>, plus the answer to who applies that asset -- the one thing
    /// an edit has to reach for it to take effect before the next domain reload.
    /// </summary>
    internal static class LiveClassAssetMemberExposure
    {
        private static readonly HashSet<Type> kSupportedValueTypes = new HashSet<Type>
        {
            typeof(bool), typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(short), typeof(ushort), typeof(byte), typeof(sbyte),
            typeof(float), typeof(double), typeof(decimal), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion),
            typeof(Color), typeof(Color32), typeof(Rect), typeof(Bounds),
            typeof(Vector2Int), typeof(Vector3Int), typeof(RectInt),
        };

        public static IEnumerable<MemberCandidate> EnumerateCandidates(Type type)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                if (!IsSupportedValueType(prop.PropertyType)) continue;
                if (IsObsolete(prop)) continue;
                yield return new MemberCandidate
                {
                    path = prop.Name,
                    isFunction = false,
                    valueType = prop.PropertyType,
                    typeLabel = prop.CanWrite ? prop.PropertyType.Name : $"{prop.PropertyType.Name} (read only)",
                };
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsSupportedValueType(field.FieldType)) continue;
                if (IsObsolete(field)) continue;
                yield return new MemberCandidate
                {
                    path = field.Name,
                    isFunction = false,
                    valueType = field.FieldType,
                    typeLabel = field.FieldType.Name,
                };
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.IsSpecialName || method.IsGenericMethod) continue;
                if (method.GetParameters().Length > 0) continue;
                // Skip the universal noise every component inherits (ToString, GetInstanceID, ...).
                if (method.DeclaringType == typeof(object) || method.DeclaringType == typeof(UnityEngine.Object)) continue;
                if (IsObsolete(method)) continue;
                yield return new MemberCandidate
                {
                    path = method.Name,
                    isFunction = true,
                    valueType = null,
                    typeLabel = "method",
                };
            }
        }

        public static bool IsSupportedValueType(Type type)
        {
            return type.IsEnum || kSupportedValueTypes.Contains(type);
        }

        public static bool IsObsolete(MemberInfo member)
        {
            return member.GetCustomAttribute<ObsoleteAttribute>() != null;
        }

        public static LiveClassAssetMember FindMember(LiveClassAsset.TypeDefinition definition, string path, bool isFunction)
        {
            foreach (var m in definition.members)
            {
                if (m != null && m.isFunction == isFunction && string.Equals(m.path, path, StringComparison.Ordinal))
                {
                    return m;
                }
            }
            return null;
        }

        /// <summary>
        /// Opens one undo step over the asset. It is snapshotted whole: these edits change list
        /// sizes and [SerializeReference] references, which <see cref="Undo.RecordObject"/>'s
        /// incremental diff does not capture reliably. Everything registered until the next
        /// <c>BeginEdit</c> undoes together.
        /// </summary>
        public static void BeginEdit(LiveClassAsset preset, string name)
        {
            if (preset == null) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            Undo.RegisterCompleteObjectUndo(preset, name);
        }

        /// <summary>
        /// Whether anything currently applies this asset: a container in an open scene that lists
        /// it, or the session-long registration the project settings (or a package) made.
        ///
        /// An asset nothing applies is still worth editing -- it is a declaration, and being
        /// applied is a separate decision, taken in the project settings or on a container -- but
        /// nothing it says reaches the remote until something takes it.
        /// </summary>
        public static bool IsApplied(LiveClassAsset preset)
        {
            if (preset == null) return false;
            if (LiveClassAssetSystem.IsPermanent(preset)) return true;

            var containers = RemoteControlContainer.all;
            for (int i = 0; i < containers.Count; i++)
            {
                if (containers[i] != null && containers[i].assets.Contains(preset)) return true;
            }
            return false;
        }

        /// <summary>
        /// Re-registers the asset through whoever applied it, so an edit reaches the live classes
        /// without waiting for a domain reload.
        ///
        /// Through the appliers rather than by registering it here: applying an asset is the
        /// decision of whoever brought it in, and an asset merely being edited must not start
        /// declaring types nothing asked for. The same rule <c>LivePrefabCatalog.Refresh</c>
        /// follows for the prefabs of the same asset.
        /// </summary>
        public static void Reapply(LiveClassAsset preset)
        {
            if (preset == null) return;

            var containers = RemoteControlContainer.all;
            for (int i = containers.Count - 1; i >= 0; i--)
            {
                var container = containers[i];
                if (container != null && container.assets.Contains(preset)) container.Reload();
            }

            // A permanent registration has no container behind it to reload, so nothing else
            // would pick the edit up.
            if (LiveClassAssetSystem.IsPermanent(preset)) LiveClassAssetSystem.RegisterTypes(preset);
        }

        public static void ExposeTypeMember(LiveClassAsset preset, Type type, in MemberCandidate candidate)
        {
            if (preset == null) return;
            BeginEdit(preset, "Expose Member");

            var definition = preset.GetOrAddTypeDefinition(type);
            if (FindMember(definition, candidate.path, candidate.isFunction) == null)
            {
                definition.members.Add(new LiveClassAssetMember
                {
                    path = candidate.path,
                    isFunction = candidate.isFunction,
                    // Default the label to the display form of the member name
                    // ("_myValue" / "m_MyValue" → "My Value"), same as the Inspector does.
                    label = ObjectNames.NicifyVariableName(candidate.path),
                    persistable = !candidate.isFunction,
                });
            }

            EditorUtility.SetDirty(preset);
        }

        public static void UnexposeTypeMember(LiveClassAsset preset, Type type, in MemberCandidate candidate)
        {
            if (preset == null) return;
            var definition = preset.FindTypeDefinition(type);
            if (definition == null) return;
            var member = FindMember(definition, candidate.path, candidate.isFunction);
            if (member == null) return;

            BeginEdit(preset, "Unexpose Member");

            definition.members.Remove(member);
            if (definition.members.Count == 0)
            {
                // Last member of the type gone: the declaration has nothing left to say.
                preset.typeDefinitions.Remove(definition);
            }

            EditorUtility.SetDirty(preset);
        }
    }
}
