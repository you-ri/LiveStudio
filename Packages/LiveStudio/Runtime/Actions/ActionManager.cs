// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting.APIUpdating;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Reflection;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The general "fire and act" base feature of LiveStudio: a list of <see cref="ActionSet"/>s the user
    /// authors from the remote app, each binding one input <see cref="InputSource"/> to an ordered set of
    /// <see cref="ActionBase"/>s. Each frame every enabled set evaluates its input and runs its actions in
    /// order.
    ///
    /// A plain serializable <see cref="IExposedObject"/> (like <see cref="StageManager"/> /
    /// <see cref="ExternalAssetManager"/>), stored in the scene's <c>RemoteControlBehaviour._objects</c>
    /// through its <c>[SerializeReference]</c> list, so the authored sets persist in the scene.
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "bolt", Category = "Action")]
    [MovedFrom(false, null, null, "TriggerManager")]
    public class ActionManager : IExposedObject, IExposedDeserializeCallback
    {
        const string kId = "c4e8b2d6-7a91-4f53-8e0c-1d9a6b3f2e74";

        // The active manager, so actions / sources can reach it. Set in OnEnable, cleared in OnDisable.
        // Reset on subsystem registration for safety when Domain Reload is disabled.
        [NonSerialized]
        private static ActionManager _current;

        public static ActionManager current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeCurrent() => _current = null;

        public string name { get; set; } = "Action Manager";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        /// <summary>The authored action sets. Polymorphic input/actions serialize via SerializeReference.</summary>
        [SerializeReference, Select]
        [ExposedField]
        public List<ActionSet> actionSets = new List<ActionSet>();

        // The shared input map all KeyInputSources create their actions in. Rebuilt when the set of
        // inputs changes or an input's binding/type changes. Runtime-only.
        [NonSerialized]
        private InputActionMap _map;

        [NonSerialized]
        private bool _initialized;

        public void OnEnable()
        {
            _current = this;

            ExposedObjectRegistry.Create<ActionManager>(this, kId);
            ExposedClass.Get<ActionManager>().onPropertyChanged += _OnPropertyChanged;

            _RebuildInputMap();

            _initialized = true;
        }

        public void OnDisable()
        {
            _initialized = false;

            ExposedClass.Get<ActionManager>().onPropertyChanged -= _OnPropertyChanged;

            _TeardownInputMap();

            ExposedObjectRegistry.FindByTarget(this)?.Unregister();

            if (_current == this) _current = null;
        }

        public void OnDispose() => OnDisable();

        public void Update()
        {
            if (!_initialized) return;
            if (!Application.isPlaying) return;

            for (int i = 0; i < actionSets.Count; i++)
            {
                var set = actionSets[i];
                if (set == null || !set.enabled || set.input == null) continue;

                var context = set.input.Evaluate();

                var actions = set.actions;
                if (actions == null) continue;
                for (int j = 0; j < actions.Count; j++)
                {
                    actions[j]?.Apply(in context);
                }
            }
        }

        public void Reset() { }

        public void OnAfterExposedDeserialize()
        {
            // A live-scene restore replaces the action sets list; rebuild the input map so the restored
            // inputs are bound. Idempotent, so harmless if it also fires on an unrelated property write.
            _RebuildInputMap();
        }

        /// <summary>Adds a new action set (default key input, no actions) and rebuilds the input map.</summary>
        [ExposedFunction]
        public void AddActionSet()
        {
            actionSets.Add(new ActionSet
            {
                id = Guid.NewGuid().ToString(),
                name = "Action Set",
                enabled = true,
                input = new KeyInputSource(),
                actions = new List<ActionBase>(),
            });
            _RebuildInputMap();
            _Broadcast();
        }

        /// <summary>Removes the action set with the given id and rebuilds the input map.</summary>
        [ExposedFunction]
        public void RemoveActionSet(string actionSetId)
        {
            int index = _IndexOf(actionSetId);
            if (index < 0) return;
            actionSets.RemoveAt(index);
            _RebuildInputMap();
            _Broadcast();
        }

        /// <summary>Appends an action of the given exposed type to the set with the given id.</summary>
        [ExposedFunction]
        public void AddAction(string actionSetId, [StringSelector(nameof(actionTypeNames))] string actionType)
        {
            var set = _Find(actionSetId);
            if (set == null) return;

            var action = _CreateAction(actionType);
            if (action == null) return;

            set.actions ??= new List<ActionBase>();
            set.actions.Add(action);
            _Broadcast();
        }

        /// <summary>Removes the action at the given index from the set with the given id.</summary>
        [ExposedFunction]
        public void RemoveAction(string actionSetId, int index)
        {
            var set = _Find(actionSetId);
            if (set?.actions == null) return;
            if (index < 0 || index >= set.actions.Count) return;
            set.actions.RemoveAt(index);
            _Broadcast();
        }

        /// <summary>Exposed action type names — the dropdown source for <see cref="AddAction"/>.</summary>
        [ExposedProperty, Hide]
        public string[] actionTypeNames
        {
            get
            {
                var derived = TypeReflectionSystem.FindDerivedTypes(typeof(ActionBase));
                var names = new List<string>();
                foreach (var type in derived)
                {
                    var ec = ExposedClass.Find(type);
                    if (ec != null) names.Add(ec.typeName);
                }
                return names.ToArray();
            }
        }

        // Rebuilds enabled-set property edits that change input wiring (input type / binding) into a
        // fresh input map. Action edits do not affect input, so they are ignored here.
        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains("input")) return;
            _RebuildInputMap();
        }

        // Tears down the previous map and binds every current input into a fresh one. Building a new map
        // discards stale actions in one step; inputs re-create their actions in Setup.
        private void _RebuildInputMap()
        {
            _TeardownInputMap();

            _map = new InputActionMap("Actions");
            for (int i = 0; i < actionSets.Count; i++)
            {
                var set = actionSets[i];
                if (set?.input == null || string.IsNullOrEmpty(set.id)) continue;
                set.input.Setup(_map, _ActionName(set.id));
            }
            _map.Enable();
        }

        private void _TeardownInputMap()
        {
            if (_map == null) return;
            _map.Disable();
            _map.Dispose();
            _map = null;
        }

        private static string _ActionName(string actionSetId) => "ActionSet." + actionSetId;

        private ActionSet _Find(string actionSetId)
        {
            int index = _IndexOf(actionSetId);
            return index >= 0 ? actionSets[index] : null;
        }

        private int _IndexOf(string actionSetId)
        {
            if (string.IsNullOrEmpty(actionSetId)) return -1;
            for (int i = 0; i < actionSets.Count; i++)
            {
                if (actionSets[i] != null && actionSets[i].id == actionSetId) return i;
            }
            return -1;
        }

        private static ActionBase _CreateAction(string actionType)
        {
            if (string.IsNullOrEmpty(actionType)) return null;
            var ec = ExposedClass.Find(actionType);
            if (ec?.type == null)
            {
                Debug.LogError($"[LiveStudio] Unknown action type: {actionType}");
                return null;
            }
            return Activator.CreateInstance(ec.type) as ActionBase;
        }

        private void _Broadcast() => ExposedPropertyBroadcast.BroadcastProperty(this, "actionSets");
    }
}
