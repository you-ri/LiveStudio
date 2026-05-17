// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using Lilium.RemoteControl.Server;
using Lilium.RemoteControl.Scene;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// <see cref="RemoteControlBehaviour"/> with built-in Remote Control UI side-menu registration.
    /// Apps that want the UI sidebar should subclass this; apps that don't can subclass
    /// <see cref="RemoteControlBehaviour"/> directly.
    /// </summary>
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI", "WebUIRemoteControlBehaviour")]
    public class UIRemoteControlBehaviour : RemoteControlBehaviour
    {
        [Header("UI")]
        [SerializeField]
        [FormerlySerializedAs("_webUIDefinition")]
        [Tooltip("UI definition for the remote app side menu. Optional - leave null to disable.")]
        private UIDefinition _uiDefinition;

        private UIProvider _ui;

        public UIDefinition uiDefinition => _uiDefinition;

        protected override void OnPreRegisterHandlers(RemoteControlServerCore server)
        {
            base.OnPreRegisterHandlers(server);
            if (_uiDefinition == null) return;

            _ui ??= new UIProvider(_uiDefinition, gameObject.name);
            _ui.Register(server);
        }

        protected override void OnPreUnregisterHandlers(RemoteControlServerCore server)
        {
            _ui?.Unregister();
            base.OnPreUnregisterHandlers(server);
        }
    }
}
