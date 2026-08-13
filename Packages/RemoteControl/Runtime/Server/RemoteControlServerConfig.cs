using System;
using UnityEngine;


namespace Lilium.RemoteControl.Server
{
    /// <summary>
    /// Server configuration stored as a ScriptableObject. Pure data:
    /// server creation and registration live in <see cref="RemoteControlServerManager"/>
    /// (see GetOrCreateServer). Application-specific routes are registered by external
    /// components (e.g. MonoBehaviours next to <see cref="RemoteControlServerRunner"/>),
    /// not by subclassing this config.
    /// </summary>
    [CreateAssetMenu(fileName = "RemoteControlServerConfig", menuName = "Live Studio/Remote Control/Server Config")]
    public class RemoteControlServerConfig : ScriptableObject
    {
        [Tooltip("Server port number")]
        public int port = 3002;

        [Tooltip("Enable CORS for cross-origin requests")]
        public bool enableCors = true;

        [Tooltip("Allow connections from other devices on the network (binds to all interfaces). " +
                 "When off, the server only accepts loopback (localhost) connections. " +
                 "On Windows this may require running as administrator or reserving the URL via netsh.")]
        public bool allowExternalConnections = false;

        [Tooltip("Keep this server running in Unity Editor")]
        public bool runningInEditor = false;

        [Tooltip("Default scene file name used by the scene save/load system. Empty for none.")]
        public string defaultFileName;

        [Tooltip("Auto-save the current scene file when the app quits with unsaved changes.")]
        public bool autoSaveOnQuit = true;

        [Tooltip("Switch the active Unity scene to the file's baseSceneName when loading. " +
                 "Turn off for apps that always run in a single scene (e.g. Fusion).")]
        public bool switchSceneOnLoad = true;
    }
}
