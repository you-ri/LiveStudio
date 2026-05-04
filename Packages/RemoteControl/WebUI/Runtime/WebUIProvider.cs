// Copyright (c) You-Ri, 2026

using UnityEngine;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.WebUI
{
    /// <summary>
    /// Pure C# helper that registers a <see cref="WebUIHandler"/> against a running
    /// <see cref="RemoteControlServerCore"/>. Used to be a MonoBehaviour; the host
    /// <see cref="WebUIRemoteControlBehaviour"/> now drives Unity lifecycle.
    /// </summary>
    public class WebUIProvider
    {
        private const string kRoute = "/webui";

        private readonly WebUIDefinition _webUIDefinition;
        private readonly string _ownerName;

        private WebUIHandler _handler;
        private RemoteControlServerCore _server;

        public WebUIProvider(WebUIDefinition webUIDefinition, string ownerName)
        {
            _webUIDefinition = webUIDefinition;
            _ownerName = ownerName;
        }

        public void Register(RemoteControlServerCore server)
        {
            if (_handler != null) return;
            if (_webUIDefinition == null)
            {
                Debug.LogWarning($"[RemoteControl] WebUIProvider on '{_ownerName}' has no WebUIDefinition assigned.");
                return;
            }
            if (server == null) return;

            _server = server;
            _handler = new WebUIHandler(_server, _webUIDefinition);
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
