// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// 外部アバターファイルを読み込むアバターソース。拡張子で .vrm / .avatar.lsb (旧 .lsavatar) を判別し、
    /// フォーマット固有処理は <see cref="IExternalAvatarLoader"/> 実装へ委譲する。
    /// </summary>
    [DefaultExecutionOrder(250)]
    [ExposedClass("ExternalAvatarSource", Category = "Avatar", Icon = "deployed_code")]
    [FormerlyExposedAs("VRMAvatarSource")]
    public class ExternalAvatarSource : MonoBehaviour, IAvatarSource, IVRMLoadObserver
    {
        public event Action<GameObject> onAvatarReady;

        // ファイルダイアログは最終拡張子で絞るため "lsb" を渡し、選択後に複合サフィックスを検証する。
        // 旧 ".lsavatar" も後方互換のため受理する。
        [SerializeField]
        [ExposedField(label = "AVATAR_MODELFILEPATH"), GLTFFileSelector("vrm", "lsb", "lsavatar")]
        [ExposedHelp("AVATAR_VRMMODELFILEPATH_HELP")]
        string _modelFilePath;

        public string modelFilePath => _modelFilePath;

        public void RequestLoad(string filepath)
        {
            _modelFilePath = filepath;
            _LoadIfFileExists();
        }

        void OnEnable()
        {
            Service<IVRMLoadObserver>.Register(this);
            ExposedClass.Get<ExternalAvatarSource>().onPropertyChanged += OnPropertyChanged;
        }

        void OnDisable()
        {
            ExposedClass.Get<ExternalAvatarSource>().onPropertyChanged -= OnPropertyChanged;
            Service<IVRMLoadObserver>.Unregister(this);
        }

        void _LoadIfFileExists()
        {
            // パスが空（リセット要求）の場合は AvatarController の既定アバターに戻す。
            if (string.IsNullOrEmpty(_modelFilePath))
            {
                GetComponent<AvatarController>()?.ResetAvatar();
                return;
            }

            if (!File.Exists(_modelFilePath))
            {
                return;
            }

            // .scene.lsb と .avatar.lsb はどちらも Path.GetExtension では ".lsb" になるため、
            // 複合サフィックスで判別する。
            if (_modelFilePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
            {
                // VRM の完了通知は IVRMLoadObserver ブロードキャスト経由で OnVRMLoaded に届く。
                _ = new VrmExternalAvatarLoader().LoadAsync(_modelFilePath, this.transform);
            }
            else if (LiveStudioBundle.IsAvatarBundle(_modelFilePath))
            {
                _ = _LoadLsAvatarAsync(_modelFilePath);
            }
            else
            {
                Debug.LogError($"[LiveStudio] Unsupported avatar file: {_modelFilePath}");
            }
        }

        async Task _LoadLsAvatarAsync(string path)
        {
            var loader = new LsAvatarLoader();
            var instance = await loader.LoadAsync(path, this.transform);
            loader.Dispose();
            if (instance != null)
            {
                onAvatarReady?.Invoke(instance);
            }
        }

        void OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (property.PathContains(nameof(_modelFilePath)))
            {
                _LoadIfFileExists();
            }
        }

        void IVRMLoadObserver.OnVRMLoadStarted(string filePath)
        {
        }

        void IVRMLoadObserver.OnVRMLoaded(GameObject newTarget)
        {
            Debug.Assert(newTarget != null);
            onAvatarReady?.Invoke(newTarget);
        }

        void IVRMLoadObserver.OnVRMLoadError(string error)
        {
            Debug.LogError($"[LiveStudio] VRM Load Error: {error}");
        }

        void IVRMLoadObserver.OnVRMLoadProgress(float progress)
        {
        }
    }
}
