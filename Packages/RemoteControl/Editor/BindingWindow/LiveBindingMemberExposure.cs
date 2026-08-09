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
    /// Type-level expose/unexpose logic shared by <see cref="LiveBindingWindow"/> and
    /// <see cref="LiveBindingAddMemberWindow"/>, both of which edit the same
    /// <see cref="LiveBindingPreset"/> / <see cref="LiveBindingResolver"/> pair.
    /// </summary>
    internal static class LiveBindingMemberExposure
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

        public static LiveBindingMember FindMember(LiveBindingPreset.TypeDefinition definition, string path, bool isFunction)
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

        // Ensures the edited preset is registered on the resolver (so its bindings resolve at runtime).
        public static void EnsurePresetOnResolver(LiveBindingPreset preset, LiveBindingResolver resolver)
        {
            if (resolver == null || preset == null) return;
            if (resolver.presets.Contains(preset)) return;
            Undo.RecordObject(resolver, "Add Preset To Resolver");
            resolver.presets.Add(preset);
            EditorUtility.SetDirty(resolver);
        }

        // Drops every instance binding pointing at the given type (its definition is gone).
        public static void RemoveBindingsOfType(LiveBindingPreset preset, LiveBindingResolver resolver, Type type)
        {
            for (int i = preset.bindings.Count - 1; i >= 0; i--)
            {
                var entry = preset.bindings[i];
                if (entry == null || entry.ResolveType() != type) continue;
                if (resolver != null) resolver.ClearReferenceValue(new PropertyName(entry.key));
                preset.bindings.RemoveAt(i);
            }
        }

        public static void ExposeTypeMember(LiveBindingPreset preset, LiveBindingResolver resolver, Type type, in MemberCandidate candidate)
        {
            if (preset == null) return;
            EnsurePresetOnResolver(preset, resolver);
            Undo.RecordObject(preset, "Expose Member");

            var definition = preset.GetOrAddTypeDefinition(type);
            if (FindMember(definition, candidate.path, candidate.isFunction) == null)
            {
                definition.members.Add(new LiveBindingMember
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

        public static void UnexposeTypeMember(LiveBindingPreset preset, LiveBindingResolver resolver, Type type, in MemberCandidate candidate)
        {
            if (preset == null) return;
            var definition = preset.FindTypeDefinition(type);
            if (definition == null) return;
            var member = FindMember(definition, candidate.path, candidate.isFunction);
            if (member == null) return;

            Undo.RecordObject(preset, "Unexpose Member");
            if (resolver != null) Undo.RecordObject(resolver, "Unexpose Member");

            definition.members.Remove(member);
            if (definition.members.Count == 0)
            {
                // Last member of the type gone: drop the definition and every binding of that type.
                preset.typeDefinitions.Remove(definition);
                RemoveBindingsOfType(preset, resolver, type);
            }

            EditorUtility.SetDirty(preset);
            if (resolver != null) EditorUtility.SetDirty(resolver);
        }
    }
}
