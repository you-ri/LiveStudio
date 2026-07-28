// Copyright (c) You-Ri, 2026

using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// An <see cref="InputSource"/> driven by a keyboard / gamepad control, read through the Unity
    /// Input System. The bound control is captured on the Studio machine via <see cref="StartRebind"/>
    /// (the remote app cannot capture the host's keys) and persisted as <see cref="binding"/>.
    /// </summary>
    [Serializable]
    [LiveClass(Category = "Operation", Icon = "keyboard")]
    public class KeyInputSource : InputSource
    {
        // The bound control path, e.g. "<Keyboard>/a" or "<Gamepad>/buttonSouth". Persisted; restored
        // into the input action on Setup. This is the shadow field backing the read-only `binding`
        // property: [FormerlyNamedAs("binding")] pairs it with that property so it persists under the
        // `binding` name and is excluded from the exposed member list (no separate, editable
        // `controlPath` surfaces in the remote app). (Re)assigned through the rebind button, not by hand.
        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("binding")]
        private string _controlPath = string.Empty;

        /// <summary>The bound control path (empty when unbound), surfaced read-only to the remote app.
        /// Persisted via the <see cref="_controlPath"/> shadow field.</summary>
        [LiveProperty]
        public string binding => _controlPath;

        /// <summary>Sets the initial bound control path before the source is wired into the input map, so a
        /// set can be created already bound (e.g. the remote app's add-action dialog captures the key first,
        /// then commits). <see cref="Setup"/>'s restore picks it up. Manager-only.</summary>
        internal void SetInitialBinding(string controlPath) => _controlPath = controlPath ?? string.Empty;

        [NonSerialized] private InputAction _action;
        [NonSerialized] private InputActionMap _map;
        [NonSerialized] private string _actionName;

        public override void Setup(InputActionMap map, string actionName)
        {
            _map = map;
            _actionName = actionName;
            _action = InputActionMapUtils.SafeCreateAction(map, actionName, "<Value>");
            _RestoreBinding();
            ResetState();
        }

        public override void Teardown(InputActionMap map)
        {
            if (map != null && !string.IsNullOrEmpty(_actionName))
            {
                InputActionMapUtils.SafeRemoveAction(map, _actionName);
            }
            _action = null;
            _map = null;
        }

        protected override float ReadRawValue()
            => _action != null ? _action.ReadValue<float>() : 0f;

        /// <summary>
        /// Listens for the next control pressed on the Studio machine and binds it. Invoked from the
        /// remote app. Reuses <see cref="RuntimeKeyBindingSystem"/> (same path as expression rebinding).
        /// Hidden from the generic object UI (like the expression-side rebind functions): the remote app
        /// drives it through the Actions page's dedicated "Assign Input" button, which shows the key-capture
        /// modal and polls for the result. A bare generic button would fire it with no such feedback.
        /// </summary>
        [LiveFunction, Hide]
        public void StartRebind() => _StartRebindAsync();

        private async void _StartRebindAsync()
        {
            if (_map == null || _action == null)
            {
                Debug.LogError("[LiveStudio] KeyInputSource is not bound to an input map; cannot rebind.");
                return;
            }

            var (success, _) = await RuntimeKeyBindingSystem.StartBindingAsync(
                new RuntimeKeyBindingData(), _map, _actionName, 0);

            if (success && _action.bindings.Count > 0)
            {
                _controlPath = _action.bindings[0].effectivePath;
            }
        }

        // Re-adds the persisted control path onto the freshly created action so the binding survives a
        // reload. The map is briefly disabled because binding edits require it.
        private void _RestoreBinding()
        {
            if (_action == null || string.IsNullOrEmpty(_controlPath)) return;
            if (_action.bindings.Count > 0) return;

            bool wasEnabled = _map != null && _map.enabled;
            if (wasEnabled) _map.Disable();
            _action.AddBinding(_controlPath);
            if (wasEnabled) _map.Enable();
        }
    }
}
