// Copyright (c) You-Ri, 2026

using UnityEngine;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl.WebUI
{
    /// <summary>
    /// Registers a WebUI handler against the RemoteControl server running on a given port.
    /// Acts as the bridge between a <see cref="WebUIDefinition"/> asset and a
    /// <see cref="RemoteControlServerCore"/> instance owned by a <see cref="RemoteControlServerRunner"/>.
    /// Add this component to a scene only when the WebUI side menu endpoint is needed;
    /// the server itself does not depend on WebUI.
    /// </summary>
    public class WebUIProvider : MonoBehaviour
    {
        private const string kRoute = "/webui";

        [SerializeField]
        [Tooltip("WebUI definition for the remote app side menu.")]
        private WebUIDefinition _webUIDefinition;

        [SerializeField]
        [Tooltip("Server runner that owns the target server. If null, a sibling component is used.")]
        private RemoteControlServerRunner _runner;

        private WebUIHandler _handler;
        private RemoteControlServerCore _server;

        void Awake()
        {
            if (_runner == null)
            {
                _runner = GetComponent<RemoteControlServerRunner>();
            }
        }

        void OnEnable()
        {
            TryRegister();
        }

        void Start()
        {
            // Retry once after all Awake/OnEnable have run, in case the server
            // was not yet started when OnEnable fired (e.g., this provider lives
            // on a different GameObject than the runner).
            TryRegister();
        }

        void OnDisable()
        {
            TryUnregister();
        }

        private void TryRegister()
        {
            if (_handler != null) return;
            if (_webUIDefinition == null)
            {
                Debug.LogWarning($"[RemoteControl] WebUIProvider on '{name}' has no WebUIDefinition assigned.");
                return;
            }

            var server = _ResolveServer();
            if (server == null)
            {
                // Server not yet ready; Start() will retry.
                return;
            }

            _server = server;
            _handler = new WebUIHandler(_server, _webUIDefinition);
            _server.RegisterRoute(kRoute, _handler);
        }

        private void TryUnregister()
        {
            if (_handler == null) return;

            // UnregisterRoute calls handler.Cleanup() internally.
            _server?.UnregisterRoute(kRoute);
            _handler = null;
            _server = null;
        }

        private RemoteControlServerCore _ResolveServer()
        {
            if (_runner == null) return null;
            var port = _runner.GetPort();
            return RemoteControlServerManager.GetServer(port);
        }
    }
}
