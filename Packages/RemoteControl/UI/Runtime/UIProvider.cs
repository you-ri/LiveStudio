// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.UI
{
    /// <summary>
    /// Pure C# helper that registers a <see cref="UIHandler"/> against a running
    /// <see cref="RemoteControlServerCore"/>. Used to be a MonoBehaviour; the host
    /// <see cref="UIRemoteControlBehaviour"/> now drives Unity lifecycle.
    /// </summary>
    [MovedFrom(true, "Lilium.RemoteControl.WebUI", "Lilium.RemoteControl.WebUI", "WebUIProvider")]
    public class UIProvider
    {
        private const string kRoute = "/ui";

        private readonly UIDefinition _uiDefinition;
        private readonly string _ownerName;

        private UIHandler _handler;
        private RemoteControlServerCore _server;

        public UIProvider(UIDefinition uiDefinition, string ownerName)
        {
            _uiDefinition = uiDefinition;
            _ownerName = ownerName;
        }

        public void Register(RemoteControlServerCore server)
        {
            if (_handler != null) return;
            if (_uiDefinition == null)
            {
                Debug.LogWarning($"[RemoteControl] UIProvider on '{_ownerName}' has no UIDefinition assigned.");
                return;
            }
            if (server == null) return;

            _server = server;
            _handler = new UIHandler(_server, _uiDefinition);
            _server.RegisterRoute(kRoute, _handler);
        }

        public void Unregister()
        {
            if (_handler == null) return;

            // UnregisterRoute calls handler.Cleanup() internally.
            _server?.UnregisterRoute(kRoute);
            _handler = null;
            _server = null;
        }
    }
}
