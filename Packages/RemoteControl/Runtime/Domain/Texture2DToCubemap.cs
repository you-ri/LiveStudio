using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.RemoteControl.Utility
{
    /// <summary>
    /// Texture2DからCubemapを生成するユーティリティ
    /// </summary>
    public static class Texture2DToCubemap
    {
        private const string kShaderName = "Hidden/Lilium/EquirectToCubemap";
        private static Material _convertMaterial;

        /// <summary>
        /// Equirectangular（パノラマ）画像からCubemapを生成
        /// RenderTextureとシェーダーを使用した高速な変換
        /// </summary>
        /// <param name="equirectangular">Equirectangularフォーマットのテクスチャ</param>
        /// <param name="cubemapSize">生成するCubemapの各面のサイズ（デフォルト: 1024）</param>
        /// <param name="format">Cubemapのテクスチャフォーマット（デフォルト: RGB24）</param>
        /// <param name="mipmap">ミップマップを生成するか（デフォルト: true）</param>
        /// <returns>生成されたCubemap</returns>
        public static Cubemap FromEquirectangular(
            Texture2D equirectangular,
            int cubemapSize = 1024,
            TextureFormat format = TextureFormat.RGB24,
            bool mipmap = true)
        {
            if (equirectangular == null)
            {
                Debug.LogError("[RemoteControl] Equirectangular texture is null");
                return null;
            }

            // マテリアルの初期化
            if (_convertMaterial == null)
            {
                Shader shader = Shader.Find(kShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[RemoteControl] Shader '{kShaderName}' not found. Make sure the shader is in Resources/Shaders folder.");
                    return null;
                }
                _convertMaterial = new Material(shader);
            }

            // Cubemapを作成
            Cubemap cubemap = new Cubemap(cubemapSize, format, mipmap);

            // 各面を変換
            for (int face = 0; face < 6; face++)
            {
                RenderTexture rt = RenderTexture.GetTemporary(
                    cubemapSize,
                    cubemapSize,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB
                );

                // シェーダーパラメータを設定
                _convertMaterial.SetInt("_Face", face);
                _convertMaterial.SetTexture("_MainTex", equirectangular);

                // シェーダーで変換
                Graphics.Blit(equirectangular, rt, _convertMaterial);

                // RenderTextureから読み取り
                RenderTexture.active = rt;
                Texture2D temp = new Texture2D(cubemapSize, cubemapSize, format, false);
                temp.ReadPixels(new Rect(0, 0, cubemapSize, cubemapSize), 0, 0);
                temp.Apply();

                // Cubemapの面に設定
                cubemap.SetPixels(temp.GetPixels(), (CubemapFace)face);

                // クリーンアップ
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                Destroy(temp);
            }

            // 変更を適用
            cubemap.Apply(mipmap);

            return cubemap;
        }

        /// <summary>
        /// 横一列レイアウトのTexture2DからCubemapを生成
        /// レイアウト: [+X, -X, +Y, -Y, +Z, -Z]（横6分割）
        /// </summary>
        /// <param name="source">横一列レイアウトのテクスチャ</param>
        /// <param name="format">Cubemapのテクスチャフォーマット</param>
        /// <param name="mipmap">ミップマップを生成するか</param>
        /// <returns>生成されたCubemap</returns>
        public static Cubemap FromHorizontalLayout(
            Texture2D source,
            TextureFormat format = TextureFormat.RGB24,
            bool mipmap = true)
        {
            if (source == null)
            {
                Debug.LogError("[RemoteControl] Source texture is null");
                return null;
            }

            if (source.width % 6 != 0)
            {
                Debug.LogError("[RemoteControl] Source texture width must be divisible by 6 for horizontal layout");
                return null;
            }

            int faceSize = source.width / 6;
            Cubemap cubemap = new Cubemap(faceSize, format, mipmap);

            CubemapFace[] faces = new CubemapFace[]
            {
                CubemapFace.PositiveX, // Right
                CubemapFace.NegativeX, // Left
                CubemapFace.PositiveY, // Top
                CubemapFace.NegativeY, // Bottom
                CubemapFace.PositiveZ, // Front
                CubemapFace.NegativeZ  // Back
            };

            for (int i = 0; i < 6; i++)
            {
                Color[] pixels = source.GetPixels(i * faceSize, 0, faceSize, faceSize);
                cubemap.SetPixels(pixels, faces[i]);
            }

            cubemap.Apply(mipmap);
            return cubemap;
        }

        /// <summary>
        /// 縦一列レイアウトのTexture2DからCubemapを生成
        /// レイアウト: [+X, -X, +Y, -Y, +Z, -Z]（縦6分割）
        /// </summary>
        /// <param name="source">縦一列レイアウトのテクスチャ</param>
        /// <param name="format">Cubemapのテクスチャフォーマット</param>
        /// <param name="mipmap">ミップマップを生成するか</param>
        /// <returns>生成されたCubemap</returns>
        public static Cubemap FromVerticalLayout(
            Texture2D source,
            TextureFormat format = TextureFormat.RGB24,
            bool mipmap = true)
        {
            if (source == null)
            {
                Debug.LogError("[RemoteControl] Source texture is null");
                return null;
            }

            if (source.height % 6 != 0)
            {
                Debug.LogError("[RemoteControl] Source texture height must be divisible by 6 for vertical layout");
                return null;
            }

            int faceSize = source.height / 6;
            Cubemap cubemap = new Cubemap(faceSize, format, mipmap);

            CubemapFace[] faces = new CubemapFace[]
            {
                CubemapFace.PositiveX, // Right
                CubemapFace.NegativeX, // Left
                CubemapFace.PositiveY, // Top
                CubemapFace.NegativeY, // Bottom
                CubemapFace.PositiveZ, // Front
                CubemapFace.NegativeZ  // Back
            };

            for (int i = 0; i < 6; i++)
            {
                Color[] pixels = source.GetPixels(0, i * faceSize, faceSize, faceSize);
                cubemap.SetPixels(pixels, faces[i]);
            }

            cubemap.Apply(mipmap);
            return cubemap;
        }

        /// <summary>
        /// マテリアルのクリーンアップ
        /// </summary>
        public static void Cleanup()
        {
            if (_convertMaterial != null)
            {
                Object.Destroy(_convertMaterial);
                _convertMaterial = null;
            }
        }

        /// <summary>
        /// GameObjectを削除します。Editorでは次のEditorUpdateで、Runtimeでは次フレームで削除されます。
        /// </summary>
        /// <param name="obj">削除するGameObject</param>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
#if UNITY_EDITOR
                EditorApplication.delayCall += () =>
                {
                    if (obj != null)
                    {
                        Object.DestroyImmediate(obj);
                    }
                };
#else
                Debug.LogError("Destroy called in Editor mode outside of UNITY_EDITOR block.");
#endif
            }
        }


    }


}
