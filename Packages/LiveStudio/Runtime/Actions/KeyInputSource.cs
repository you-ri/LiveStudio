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
    [ExposedClass(Category = "Action", Icon = "keyboard")]
    public class KeyInputSource : InputSource
    {
        // The bound control path, e.g. "<Keyboard>/a" or "<Gamepad>/buttonSouth". Persisted; restored
        // into the input action on Setup. Hidden from the generic UI — it is surfaced read-only via
        // `binding` and (re)assigned through the rebind button, not typed by hand.
        [SerializeField, ExposedField, Hide]
        private string _controlPath = string.Empty;

        /// <summary>The bound control path (empty when unbound), surfaced read-only to the remote app.</summary>
        [ExposedProperty]
        public string binding => _controlPath;

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
        [ExposedFunction, Hide]
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
