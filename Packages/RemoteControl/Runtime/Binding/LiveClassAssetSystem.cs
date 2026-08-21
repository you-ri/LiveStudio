// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using UnityEngine;

using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Owns the type-level LiveClass registrations driven by <see cref="LiveClassAsset"/>
    /// assets, and tracks the active <see cref="LiveBinding"/> instances so their registry
    /// handles can be refreshed when a type definition is re-registered (handles capture the
    /// LiveClass, so a rebuild invalidates them).
    /// </summary>
    public static class LiveClassAssetSystem
    {
        // Active runtime bindings per type (for handle refresh on type rebuild).
        private static readonly Dictionary<Type, List<LiveBinding>> _activeByType
            = new Dictionary<Type, List<LiveBinding>>();

        // Last applied registration signature per type. Skips redundant LiveClass rebuilds
        // (and the handle churn they cause) when the member definition did not actually change.
        private static readonly Dictionary<Type, string> _signatureByType
            = new Dictionary<Type, string>();

        // Which asset most recently registered each type — the owner, so unregistering an asset
        // only drops types it actually won (see _UnregisterType).
        private static readonly Dictionary<Type, LiveClassAsset> _ownerByType
            = new Dictionary<Type, LiveClassAsset>();

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
        /// Registers (or re-registers) the LiveClass of every type definition in the asset.
        /// Idempotent: unchanged definitions are skipped via a signature check.
        /// </summary>
        public static void RegisterTypes(LiveClassAsset asset)
        {
            if (asset == null) return;
            foreach (var definition in asset.typeDefinitions)
            {
                if (definition == null) continue;
                var type = definition.ResolveType();
                if (type == null)
                {
                    Debug.LogWarning($"[RemoteControl] Live class asset '{asset.name}' references unresolvable type '{definition.typeName}'.");
                    continue;
                }
                _RegisterType(asset, type, definition);
            }
        }

        /// <summary>
        /// Undoes <see cref="RegisterTypes"/> for every type this asset registered, so an asset
        /// carried in by an asset bundle can be dropped again when the bundle unloads.
        /// Conservative by design: a type is only really unregistered when this asset is the one
        /// that defined it and no binding instance of it is left (see <see cref="_UnregisterType"/>).
        /// </summary>
        public static void UnregisterTypes(LiveClassAsset asset)
        {
            if (asset == null) return;

            // What the asset actually registered, not what it currently declares: a definition
            // dropped from the asset before the unapply runs (the editor window removes it from
            // the list first, and a hand-edited asset never had it) would otherwise stay
            // registered for the rest of the session. _ownerByType is that record.
            // Collected first because _UnregisterType writes to the dictionary.
            List<Type> owned = null;
            foreach (var pair in _ownerByType)
            {
                if (pair.Value != asset) continue;
                (owned ??= new List<Type>()).Add(pair.Key);
            }
            if (owned == null) return;

            for (int i = 0; i < owned.Count; i++)
            {
                _UnregisterType(asset, owned[i]);
            }
        }

        /// <summary>
        /// Resolves the LiveClass a binding instance of <paramref name="type"/> should use.
        /// Attribute-based [LiveClass] types keep their own definition; asset-defined types
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
                Debug.LogWarning($"[RemoteControl] No live class asset type definition registered for '{type.Name}'. Register the asset before enabling bindings.");
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

        private static void _RegisterType(LiveClassAsset asset, Type type, LiveClassAsset.TypeDefinition definition)
        {
            // Attribute-based types own their member definition; assets only add instances.
            // Rebuilding here would clobber the attribute-registered LiveClass (and its
            // source-generated declaration order), so leave it untouched.
            if (TypeReflectionSystem.GetCustomAttribute<LiveClassAttribute>(type) != null)
            {
                Debug.LogWarning($"[RemoteControl] Type '{type.Name}' already has an attribute-based [LiveClass] definition; the asset member definition is ignored (instances are still exposed).");
                return;
            }

            var sb = _signatureBuilder ??= new System.Text.StringBuilder(256);
            sb.Clear();

            // Category and icon are part of the registration too, so a change to either has to
            // reach the signature — otherwise editing them looks like it did nothing.
            sb.Append("t:").Append(definition.category).Append('|').Append(definition.icon).Append(';');

            var propertyDefines = new List<LivePropertyDefine>();
            var functionDefines = new List<LiveFunctionDefine>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in definition.OrderedMembers())
            {
                if (member == null || string.IsNullOrEmpty(member.path)) continue;
                if (!seen.Add((member.isFunction ? "f:" : "p:") + member.path)) continue;

                if (member.isFunction) functionDefines.Add(member.ToFunctionDefine());
                else propertyDefines.Add(member.ToPropertyDefine());
                member.AppendSignature(sb);
            }

            // Last registration wins by design: a scene's own asset is meant to be able to
            // redefine a type the package asset already declared. Definitions are replaced whole
            // rather than merged, so the winner's member set is the one that reaches the wire.
            _ownerByType[type] = asset;

            var signature = sb.ToString();
            var liveClass = LiveClass.Find(type);
            if (liveClass == null
                || !_signatureByType.TryGetValue(type, out var lastSignature)
                || !string.Equals(lastSignature, signature, StringComparison.Ordinal))
            {
                liveClass = LiveClass.Register(type, type.Name,
                    propertyDefines.ToArray(),
                    functionDefines.Count > 0 ? functionDefines.ToArray() : null,
                    category: string.IsNullOrEmpty(definition.category) ? "Binding" : definition.category,
                    icon: string.IsNullOrEmpty(definition.icon) ? null : definition.icon);
                _signatureByType[type] = signature;
            }

            // Handles created against the previous LiveClass hold stale propertyTypes.
            if (_activeByType.TryGetValue(type, out var active))
            {
                foreach (var b in active) b.RefreshHandle(liveClass);
            }
        }

        private static void _UnregisterType(LiveClassAsset asset, Type type)
        {
            // Attribute-based types keep their own definition; the asset never registered one
            // (see _RegisterType), so there is nothing of ours to take away.
            if (TypeReflectionSystem.GetCustomAttribute<LiveClassAttribute>(type) != null) return;

            // Another asset defines this type too and registered last, so the live definition is
            // not ours. Dropping it here would take that asset's members away with it.
            if (!_ownerByType.TryGetValue(type, out var owner) || owner != asset) return;

            // Instances are still exposed — by a container that is staying, or by one whose own
            // unapply has not run yet. The handles hold this LiveClass, so it has to outlive them;
            // the last container to leave is the one that gets past this check.
            if (_activeByType.TryGetValue(type, out var active) && active.Count > 0) return;

            if (LiveClass.TryGet(type, out var liveClass)) LiveClass.Unregister(liveClass);
            _signatureByType.Remove(type);
            _ownerByType.Remove(type);
            _activeByType.Remove(type);
        }
    }
}
