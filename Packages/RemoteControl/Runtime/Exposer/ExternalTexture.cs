using UnityEngine;
using System;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 外部テクスチャファイルを読み込むアセット
    /// 頻繁に更新しないテクスチャ向け
    ///
    /// 使用例:
    /// using (var externalTexture = new ExternalTexture())
    /// {
    ///     externalTexture.SetFilePath("path/to/image.png");
    ///     material.mainTexture = externalTexture.texture;
    /// } // 自動的にDispose()が呼ばれる
    /// </summary>
    [System.Serializable]
    [ExposedClass("ExternalTexture", Icon = "image")]
    public class ExternalTexture : IDisposable, IExposedDeserializeCallback
    {
        private Texture2D _texture;

        public Texture2D texture => _texture;

        private bool _disposed = false;

        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("filePath")]
        private string _filePath;

        [ExposedProperty]
        public string filePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    Reload();
                }
            }
        }

        // Deserialization writes _filePath via reflection and bypasses the setter, so trigger
        // the texture reload here instead.
        void IExposedDeserializeCallback.OnAfterExposedDeserialize()
        {
            Reload();
        }

        public void Load()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                return;
            }

            if (!System.IO.File.Exists(_filePath))
            {
                return;
            }

            try
            {
                // ファイルをバイト配列として読み込み
                byte[] fileData = System.IO.File.ReadAllBytes(_filePath);

                // Texture2Dを作成（仮のサイズ、LoadImageが自動調整）
                Texture2D texture = new Texture2D(2, 2);

                // 画像データを読み込み
                if (texture.LoadImage(fileData))
                {
                    texture.name = System.IO.Path.GetFileNameWithoutExtension(_filePath);
                    _texture = texture;
                }
                else
                {
                    Debug.LogError($"[RemoteControl] Failed to load image: {_filePath}");
                    Destroy(texture);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RemoteControl] Error loading texture: {ex.Message}");
            }
        }

        /// <summary>
        /// テクスチャを再読み込み
        /// </summary>
        public void Reload()
        {
            // 既存のテクスチャを破棄
            if (_texture != null && _texture is Texture2D)
            {
                Destroy(_texture);
                _texture = null;
            }

            Load();
        }

        /// <summary>
        /// ファイルパスを設定して再読み込み
        /// </summary>
        /// <param name="path">画像ファイルパス</param>
        public void SetFilePath(string path)
        {
            _filePath = path;
            Reload();
        }


        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // マネージドリソースの解放
                // （このクラスには特になし）
            }

            // アンマネージドリソース（Texture）の解放
            if (_texture != null && _texture is Texture2D)
            {
                Destroy(_texture);
                _texture = null;
            }

            _disposed = true;
        }

        /// <summary>
        /// ファイナライザー
        /// </summary>
        ~ExternalTexture()
        {
            Dispose(false);
        }


        /// <summary>
        /// GameObjectを削除します。Editorでは次のEditorUpdateで、Runtimeでは次フレームで削除されます。
        /// </summary>
        /// <param name="obj">削除するGameObject</param>
        public static void Destroy(UnityEngine.Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
#if UNITY_EDITOR
                EditorApplication.delayCall += () =>
                {
                    if (obj != null)
                    {
                        UnityEngine.Object.DestroyImmediate(obj);
                    }
                };
#else
                Debug.LogError("Destroy called in Editor mode outside of UNITY_EDITOR block.");
#endif
            }
        }

    }
}
