// Copyright (c) You-Ri, 2026

using UnityEngine;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.WebUI
{
    /// <summary>
    /// <see cref="RemoteControlBehaviour"/> with built-in WebUI side-menu registration.
    /// Apps that want the WebUI sidebar should subclass this; apps that don't can subclass
    /// <see cref="RemoteControlBehaviour"/> directly.
    /// </summary>
    public class WebUIRemoteControlBehaviour : RemoteControlBehaviour
    {
        [Header("WebUI")]
        [SerializeField]
        [Tooltip("WebUI definition for the remote app side menu. Optional - leave null to disable.")]
        private WebUIDefinition _webUIDefinition;

        private WebUIProvider _webUI;

        public WebUIDefinition webUIDefinition => _webUIDefinition;

        protected override void OnPreRegisterHandlers(RemoteControlServerCore server)
        {
            base.OnPreRegisterHandlers(server);
            if (_webUIDefinition == null) return;

            _webUI ??= new WebUIProvider(_webUIDefinition, gameObject.name);
            _webUI.Register(server);
        }

        protected override void OnPreUnregisterHandlers(RemoteControlServerCore server)
        {
            _webUI?.Unregister();
            base.OnPreUnregisterHandlers(server);
        }
    }
}
