// Copyright (c) You-Ri, 2026
#if UNITY_GLTFAST
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using Lilium.RemoteControl;
using UnityEngine;

namespace Lilium.LiveStudio
{
    [ExposedClass(Icon = "deployed_code", Category = "Prop")]
    [FormerlyExposedAs("GLTFModelLoader")]
    public class GltfModel : MonoBehaviour
    {
        [ExposedField, Hide]
        [FormerlyExposedAs("path")]
        private string _path;


        [ExposedProperty("path", label = "GLTF_MODELFILEPATH")]
        [GLTFFileSelector("gltf", "glb")]
        [ExposedHelp("GLTF_MODELFILEPATH_HELP")]
        public string path
        {
            get => _path;
            set
            {
                if (_path == value) return;
                _path = value;
                if (Application.isPlaying && isActiveAndEnabled)
                {
                    _ = LoadAsync(_path);
                }
            }
        }

        public event Action<GameObject> OnLoaded;

        private GltfImport _gltf;
        private GameObject _instance;
        private CancellationTokenSource _cts;

        private async void Start()
        {
            if (!string.IsNullOrEmpty(_path))
            {
                await LoadAsync(_path);
            }
        }

        private void OnDestroy()
        {
            _Cleanup();
        }

        public async Task LoadAsync(string rawPath, CancellationToken externalToken = default)
        {
            _Cleanup();

            if (string.IsNullOrEmpty(rawPath))
            {
                return;
            }

            var resolved = _ResolvePath(rawPath);
            if (!File.Exists(resolved))
            {
                Debug.LogError($"[LiveStudio] GltfModelLoader: file not found at '{resolved}'.");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var ct = _cts.Token;

            var uri = new Uri(resolved);
            _gltf = new GltfImport();

            // path 再設定 / OnDestroy 経由の _Cleanup で _cts.Cancel() が走ると、進行中の
            // GLTFast 内部 await が OperationCanceledException を投げる。これは想定動作なので
            // 黙ってリターンする (リソース解放は _Cleanup 側で完了済み)。
            try
            {
                var loaded = await _gltf.Load(uri, null, ct);
                if (ct.IsCancellationRequested || this == null) return;
                if (!loaded)
                {
                    Debug.LogError($"[LiveStudio] GltfModelLoader: failed to load glTF '{resolved}'.");
                    _gltf.Dispose();
                    _gltf = null;
                    return;
                }

                _instance = new GameObject(Path.GetFileNameWithoutExtension(resolved));
                _instance.transform.SetParent(transform, false);

                var instantiated = await _gltf.InstantiateMainSceneAsync(_instance.transform);
                if (ct.IsCancellationRequested || this == null) return;
                if (!instantiated)
                {
                    Debug.LogError($"[LiveStudio] GltfModelLoader: failed to instantiate scene from '{resolved}'.");
                    Destroy(_instance);
                    _instance = null;
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            OnLoaded?.Invoke(_instance);
        }

        private static string _ResolvePath(string rawPath)
        {
            const string kStreaming = "StreamingAssets:";
            const string kPersistent = "PersistentData:";

            if (rawPath.StartsWith(kStreaming, StringComparison.Ordinal))
            {
                return Path.Combine(Application.streamingAssetsPath, rawPath.Substring(kStreaming.Length));
            }
            if (rawPath.StartsWith(kPersistent, StringComparison.Ordinal))
            {
                return Path.Combine(Application.persistentDataPath, rawPath.Substring(kPersistent.Length));
            }
            if (Path.IsPathRooted(rawPath) && File.Exists(rawPath))
            {
                return rawPath;
            }
            var relative = Path.Combine(Application.streamingAssetsPath, rawPath);
            if (File.Exists(relative))
            {
                return relative;
            }
            return rawPath;
        }

        private void _Cleanup()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }
            if (_gltf != null)
            {
                _gltf.Dispose();
                _gltf = null;
            }
        }
    }
}
#endif
