// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Owns the type-level LiveClass registrations driven by <see cref="LiveBindingPreset"/>
    /// assets, and tracks the active <see cref="LiveBinding"/> instances so their registry
    /// handles can be refreshed when a type definition is re-registered (handles capture the
    /// LiveClass, so a rebuild invalidates them).
    /// </summary>
    public static class LiveBindingSystem
    {
        // Active runtime bindings per type (for handle refresh on type rebuild).
        private static readonly Dictionary<Type, List<LiveBinding>> _activeByType
            = new Dictionary<Type, List<LiveBinding>>();

        // Last applied registration signature per type. Skips redundant LiveClass rebuilds
        // (and the handle churn they cause) when the member definition did not actually change.
        private static readonly Dictionary<Type, string> _signatureByType
            = new Dictionary<Type, string>();

        // Which preset most recently registered each type — used to warn when two presets
        // define the same type with different member sets (last one wins).
        private static readonly Dictionary<Type, LiveBindingPreset> _ownerByType
            = new Dictionary<Type, LiveBindingPreset>();

        [ThreadStatic]
        private static System.Text.StringBuilder _signatureBuilder;

        // Reset statics at runtime startup so disabling Domain Reload does not leak bindings
        // from the previous play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ClearStatics()
        {
            _activeByType.Clear();
            _signatureByType.Clear();
            _ownerByType.Clear();
        }

        /// <summary>
        /// Registers (or re-registers) the LiveClass of every type definition in the preset.
        /// Idempotent: unchanged definitions are skipped via a signature check.
        /// </summary>
        public static void RegisterTypes(LiveBindingPreset preset)
        {
            if (preset == null) return;
            foreach (var definition in preset.typeDefinitions)
            {
                if (definition == null) continue;
                var type = definition.ResolveType();
                if (type == null)
                {
                    Debug.LogWarning($"[RemoteControl] Live binding preset '{preset.name}' references unresolvable type '{definition.typeName}'.");
                    continue;
                }
                _RegisterType(preset, type, definition);
            }
        }

        /// <summary>
        /// Resolves the LiveClass a binding instance of <paramref name="type"/> should use.
        /// Attribute-based [LiveClass] types keep their own definition; preset-defined types
        /// must have been registered through <see cref="RegisterTypes"/> first.
        /// </summary>
        internal static LiveClass ResolveLiveClass(Type type)
        {
            if (type == null) return null;
            if (TypeReflectionSystem.GetCustomAttribute<LiveClassAttribute>(type) != null)
            {
                return LiveClass.Get(type);
            }
            return LiveClass.Find(type);
        }

        /// <summary>Tracks an active binding and creates its registry handle.</summary>
        internal static void Attach(LiveBinding binding)
        {
            var target = binding?.target;
            if (target == null) return;
            var type = target.GetType();

            if (!_activeByType.TryGetValue(type, out var list))
            {
                list = new List<LiveBinding>();
                _activeByType[type] = list;
            }
            if (!list.Contains(binding)) list.Add(binding);

            var liveClass = ResolveLiveClass(type);
            if (liveClass == null)
            {
                Debug.LogWarning($"[RemoteControl] No live binding type definition registered for '{type.Name}'. Register the preset before enabling bindings.");
                return;
            }
            binding.RefreshHandle(liveClass);
        }

        /// <summary>Stops tracking a binding (the binding unregisters its own handle).</summary>
        internal static void Detach(LiveBinding binding)
        {
            if (binding == null) return;
            List<Type> emptied = null;
            foreach (var kv in _activeByType)
            {
                if (kv.Value.Remove(binding) && kv.Value.Count == 0)
                {
                    (emptied ??= new List<Type>()).Add(kv.Key);
                }
            }
            if (emptied != null)
            {
                foreach (var t in emptied) _activeByType.Remove(t);
            }
        }

        private static void _RegisterType(LiveBindingPreset preset, Type type, LiveBindingPreset.TypeDefinition definition)
        {
            // Attribute-based types own their member definition; presets only add instances.
            // Rebuilding here would clobber the attribute-registered LiveClass (and its
            // source-generated declaration order), so leave it untouched.
            if (TypeReflectionSystem.GetCustomAttribute<LiveClassAttribute>(type) != null)
            {
                Debug.LogWarning($"[RemoteControl] Type '{type.Name}' already has an attribute-based [LiveClass] definition; the preset member definition is ignored (instances are still exposed).");
                return;
            }

            var sb = _signatureBuilder ??= new System.Text.StringBuilder(256);
            sb.Clear();

            var propertyDefines = new List<LivePropertyDefine>();
            var functionDefines = new List<LiveFunctionDefine>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in definition.members)
            {
                if (member == null || string.IsNullOrEmpty(member.path)) continue;
                if (!seen.Add((member.isFunction ? "f:" : "p:") + member.path)) continue;

                if (member.isFunction) functionDefines.Add(member.ToFunctionDefine());
                else propertyDefines.Add(member.ToPropertyDefine());
                member.AppendSignature(sb);
            }

            if (_ownerByType.TryGetValue(type, out var owner) && owner != null && owner != preset)
            {
                Debug.LogWarning($"[RemoteControl] Type '{type.Name}' is defined by multiple live binding presets ('{owner.name}' and '{preset.name}'); the last registered definition wins.");
            }
            _ownerByType[type] = preset;

            var signature = sb.ToString();
            var liveClass = LiveClass.Find(type);
            if (liveClass == null
                || !_signatureByType.TryGetValue(type, out var lastSignature)
                || !string.Equals(lastSignature, signature, StringComparison.Ordinal))
            {
                liveClass = LiveClass.Register(type, type.Name,
                    propertyDefines.ToArray(),
                    functionDefines.Count > 0 ? functionDefines.ToArray() : null,
                    category: "Binding");
                _signatureByType[type] = signature;
            }

            // Handles created against the previous LiveClass hold stale propertyTypes.
            if (_activeByType.TryGetValue(type, out var active))
            {
                foreach (var b in active) b.RefreshHandle(liveClass);
            }
        }
    }
}
