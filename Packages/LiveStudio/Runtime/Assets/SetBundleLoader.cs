// Copyright (c) You-Ri, 2026

using System.IO;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// A scene additively loaded from a set bundle (<c>*.set.lsb</c>), together with the
    /// owning <see cref="AssetBundle"/>. The bundle must stay loaded while the scene is live
    /// because the scene's assets are served from it, so unloading is the loader's responsibility.
    /// </summary>
    internal sealed class LoadedSetBundle
    {
        public AssetBundle bundle;
        public Scene scene;
        public string sceneName;

        /// <summary>Unloads the scene and then the owning bundle (freeing its assets too).</summary>
        public async Task UnloadAsync()
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                await SetBundleLoader.AwaitOperation(SceneManager.UnloadSceneAsync(scene));
            }

            if (bundle != null)
            {
                // true: the scene's GameObjects are already destroyed by UnloadSceneAsync; free the
                // remaining loaded assets as well so the same file can be loaded again later.
                bundle.Unload(true);
                bundle = null;
            }
        }
    }

    /// <summary>
    /// Loads a Unity scene from a set bundle (<c>*.set.lsb</c>, an AssetBundle containing a single
    /// scene) and adds it additively to the running app.
    ///
    /// Unlike <see cref="AvatarBundleLoader"/> — which unloads its bundle immediately because it only
    /// needs the instantiated prefab — a scene keeps referencing assets inside the bundle, so the
    /// bundle is retained in <see cref="LoadedSetBundle"/> until the scene is unloaded.
    /// </summary>
    internal sealed class SetBundleLoader
    {
        public async Task<LoadedSetBundle> LoadAsync(string filePath)
        {
            // [Debug] Temporary crash instrumentation. An intermittent native crash occurs on the
            // PreloadManager worker thread while it deserializes this set scene's [SerializeReference]
            // data (RemoteControlBehaviour._objects) concurrently with main-thread managed work such as
            // a VRM load (scripting_object_get_class from a background thread). These lines record which
            // set file was loading, whether a VRM load overlapped, and how far the load got, so the next
            // crash's Editor.log pins the culprit. Remove once the crash is diagnosed / fixed.
            Debug.Log($"[Debug][SetBundle] LoadAsync begin: {filePath}");

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] .set.lsb file not found: {filePath}");
                return null;
            }

            var bundleRequest = AssetBundle.LoadFromFileAsync(filePath);
            await AwaitOperation(bundleRequest);

            var bundle = bundleRequest.assetBundle;
            if (bundle == null)
            {
                Debug.LogError($"[LiveStudio] Failed to load set bundle: {filePath}");
                return null;
            }

            if (!bundle.isStreamedSceneAssetBundle)
            {
                Debug.LogError($"[LiveStudio] Bundle is not a set bundle: {filePath}");
                bundle.Unload(true);
                return null;
            }

            var scenePaths = bundle.GetAllScenePaths();
            if (scenePaths == null || scenePaths.Length == 0)
            {
                Debug.LogError($"[LiveStudio] .set.lsb contains no scene: {filePath}");
                bundle.Unload(true);
                return null;
            }

            var scenePath = scenePaths[0];

            // Capture the exact Scene handle for this load. Names can collide across bundles, so we
            // never look the scene up by name afterwards — we hold the handle captured here.
            Scene captured = default;
            void OnSceneLoaded(Scene scene, LoadSceneMode mode) => captured = scene;
            SceneManager.sceneLoaded += OnSceneLoaded;
            try
            {
                // [Debug] Last breadcrumb before the crash-prone worker-thread deserialize. If the next
                // crash log ends after this line with no matching "LoadSceneAsync done", the crash was in
                // this async scene load; vrmLoading/vrmFile show whether a VRM load overlapped it.
                Debug.Log($"[Debug][SetBundle] LoadSceneAsync begin: scene={scenePath}, vrmLoading={VRMLoader.IsLoading}, vrmFile={VRMLoader.CurrentLoadingFilePath}");
                await AwaitOperation(
                    SceneManager.LoadSceneAsync(scenePath, new LoadSceneParameters(LoadSceneMode.Additive)));
                Debug.Log($"[Debug][SetBundle] LoadSceneAsync done: scene={captured.name}, valid={captured.IsValid()}");
            }
            finally
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }

            if (!captured.IsValid())
            {
                Debug.LogError($"[LiveStudio] Failed to load scene from bundle: {filePath}");
                bundle.Unload(true);
                return null;
            }

            return new LoadedSetBundle
            {
                bundle = bundle,
                scene = captured,
                sceneName = captured.name,
            };
        }

        /// <summary>
        /// Bridges a Unity <see cref="AsyncOperation"/> to a <see cref="Task"/> so it can be awaited.
        /// Continuations resume on the main thread via Unity's synchronization context.
        /// </summary>
        internal static Task AwaitOperation(AsyncOperation operation)
        {
            if (operation == null || operation.isDone)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.completed += _ => tcs.TrySetResult(true);
            return tcs.Task;
        }
    }
}
