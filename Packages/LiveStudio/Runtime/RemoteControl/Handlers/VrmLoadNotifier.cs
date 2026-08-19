// Copyright (c) You-Ri, 2026

using System.IO;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Core;
using Lilium.RemoteControl.Server;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Reports avatar loading progress to remote clients.
    ///
    /// This is not a route handler. Starting and clearing an avatar are ordinary live-object
    /// operations — a file is registered with the asset manager and enabled, or a model path
    /// property is written — so there is nothing here for a client to POST to. What clients
    /// cannot get from those operations is progress: loading a large VRM takes seconds, and the
    /// write returns long before the avatar is on screen. So this class listens to
    /// <see cref="VRMLoader"/> and pushes `vrm_load_*` events into the inbox.
    ///
    /// Reports every load, whoever started it — a remote write, or the app's own UI.
    /// </summary>
    public class VrmLoadNotifier
    {
        private readonly RemoteControlServerCore _server;

        /// <summary>
        /// File currently being loaded. Null while idle, which is also what gates progress
        /// reporting: a progress event without a preceding start event would leave clients
        /// showing a bar they can never close.
        /// </summary>
        private string _currentFilePath;

        public VrmLoadNotifier(RemoteControlServerCore server)
        {
            _server = server;

            VRMLoader.onLoadStarted += OnVRMLoadStarted;
            VRMLoader.onLoaded += OnVRMLoaded;
            VRMLoader.onLoadError += OnVRMLoadError;
            VRMLoader.onLoadProgress += OnVRMLoadProgress;

            // A client that connects mid-load would otherwise never hear about it: the start
            // event has already been broadcast, and only progress and completion are still to come.
            if (_server != null)
            {
                _server.onClientConnected += OnClientConnected;
            }
        }

        public void Dispose()
        {
            VRMLoader.onLoadStarted -= OnVRMLoadStarted;
            VRMLoader.onLoaded -= OnVRMLoaded;
            VRMLoader.onLoadError -= OnVRMLoadError;
            VRMLoader.onLoadProgress -= OnVRMLoadProgress;

            if (_server != null)
            {
                _server.onClientConnected -= OnClientConnected;
            }
        }

        private void OnClientConnected(RestApiClient client)
        {
            if (!VRMLoader.IsLoading || string.IsNullOrEmpty(VRMLoader.CurrentLoadingFilePath))
            {
                return;
            }

            var startData = new
            {
                type = "vrm_load_start",
                progress = 0f,
                isLoading = true,
                error = (string)null,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = Path.GetFileName(VRMLoader.CurrentLoadingFilePath),
                applicationName = "VirgoMotionStudio"
            };

            _server?.SendEventToClient(client.ClientId, startData, "vrm_load_start");
        }

        private void OnVRMLoadStarted(string filePath)
        {
            _currentFilePath = filePath;

            var startData = new
            {
                type = "vrm_load_start",
                progress = 0f,
                isLoading = true,
                error = (string)null,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = Path.GetFileName(filePath),
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(startData, "vrm_load_start");
        }

        private void OnVRMLoadProgress(float progress)
        {
            if (_currentFilePath == null) return;

            var progressData = new
            {
                type = "vrm_load_progress",
                progress = progress * 100f,
                isLoading = true,
                error = (string)null,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = Path.GetFileName(_currentFilePath),
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(progressData, "vrm_load_progress");
        }

        private void OnVRMLoaded(GameObject vrm)
        {
            Debug.Log($"[LiveStudio] VRM loaded successfully: {vrm?.name}");

            var completeData = new
            {
                type = "vrm_load_complete",
                progress = 100f,
                isLoading = false,
                error = (string)null,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : null,
                avatarName = vrm?.name,
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(completeData, "vrm_load_complete");

            _currentFilePath = null;
        }

        private void OnVRMLoadError(string error)
        {
            Debug.LogError($"[LiveStudio] VRM load failed: {error}");

            var errorData = new
            {
                type = "vrm_load_error",
                progress = 0f,
                isLoading = false,
                error = error,
                timestamp = TimeUtility.GetUnixTimeMilliseconds(),
                filename = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : null,
                applicationName = "VirgoMotionStudio"
            };

            _ = _server?.BroadcastMessage(errorData, "vrm_load_error");

            _currentFilePath = null;
        }
    }
}
