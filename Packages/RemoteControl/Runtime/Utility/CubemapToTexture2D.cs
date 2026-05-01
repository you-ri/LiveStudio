using UnityEngine;

namespace Lilium.RemoteControl.Utility
{
    /// <summary>
    /// CubemapからTexture2Dに変換するユーティリティ
    /// </summary>
    public static class CubemapToTexture2D
    {
        private const string kShaderName = "Hidden/Lilium/CubemapToEquirect";
        private static Material _convertMaterial;

        /// <summary>
        /// CubemapをEquirectangular（パノラマ）形式のTexture2Dに変換
        /// RenderTextureとシェーダーを使用した高速な変換
        /// </summary>
        /// <param name="cubemap">変換元のCubemap</param>
        /// <param name="width">出力テクスチャの幅（デフォルト: 2048）</param>
        /// <param name="height">出力テクスチャの高さ（デフォルト: 1024、パノラマ比率2:1）</param>
        /// <param name="format">出力テクスチャのフォーマット（デフォルト: RGB24）</param>
        /// <returns>Equirectangular形式のTexture2D</returns>
        public static Texture2D ToEquirectangular(
            Cubemap cubemap,
            int width = 2048,
            int height = 1024,
            TextureFormat format = TextureFormat.RGB24)
        {
            if (cubemap == null)
            {
                Debug.LogError("[RemoteControl] Cubemap is null");
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

            // RenderTextureを作成
            RenderTexture rt = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );

            // シェーダーパラメータを設定
            _convertMaterial.SetTexture("_Tex", cubemap);

            // シェーダーで変換
            Graphics.Blit(null, rt, _convertMaterial);

            // RenderTextureから読み取り
            RenderTexture.active = rt;
            Texture2D result = new Texture2D(width, height, format, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();

            // クリーンアップ
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        /// <summary>
        /// Cubemapの特定の面をTexture2Dとして取得
        /// </summary>
        /// <param name="cubemap">変換元のCubemap</param>
        /// <param name="face">取得する面</param>
        /// <param name="format">出力テクスチャのフォーマット</param>
        /// <returns>指定した面のTexture2D</returns>
        public static Texture2D GetFace(
            Cubemap cubemap,
            CubemapFace face,
            TextureFormat format = TextureFormat.RGB24)
        {
            if (cubemap == null)
            {
                Debug.LogError("[RemoteControl] Cubemap is null");
                return null;
            }

            int size = cubemap.width;
            Texture2D result = new Texture2D(size, size, format, false);

            Color[] pixels = cubemap.GetPixels(face);
            result.SetPixels(pixels);
            result.Apply();

            return result;
        }

        /// <summary>
        /// Cubemapの全6面を横一列に並べたTexture2Dを取得
        /// レイアウト: [+X, -X, +Y, -Y, +Z, -Z]
        /// </summary>
        /// <param name="cubemap">変換元のCubemap</param>
        /// <param name="format">出力テクスチャのフォーマット</param>
        /// <returns>横一列レイアウトのTexture2D</returns>
        public static Texture2D ToHorizontalLayout(
            Cubemap cubemap,
            TextureFormat format = TextureFormat.RGB24)
        {
            if (cubemap == null)
            {
                Debug.LogError("[RemoteControl] Cubemap is null");
                return null;
            }

            int faceSize = cubemap.width;
            Texture2D result = new Texture2D(faceSize * 6, faceSize, format, false);

            CubemapFace[] faces = new CubemapFace[]
            {
                CubemapFace.PositiveX,
                CubemapFace.NegativeX,
                CubemapFace.PositiveY,
                CubemapFace.NegativeY,
                CubemapFace.PositiveZ,
                CubemapFace.NegativeZ
            };

            for (int i = 0; i < 6; i++)
            {
                Color[] pixels = cubemap.GetPixels(faces[i]);
                result.SetPixels(i * faceSize, 0, faceSize, faceSize, pixels);
            }

            result.Apply();
            return result;
        }

        /// <summary>
        /// Cubemapの全6面を縦一列に並べたTexture2Dを取得
        /// レイアウト: [+X, -X, +Y, -Y, +Z, -Z]
        /// </summary>
        /// <param name="cubemap">変換元のCubemap</param>
        /// <param name="format">出力テクスチャのフォーマット</param>
        /// <returns>縦一列レイアウトのTexture2D</returns>
        public static Texture2D ToVerticalLayout(
            Cubemap cubemap,
            TextureFormat format = TextureFormat.RGB24)
        {
            if (cubemap == null)
            {
                Debug.LogError("[RemoteControl] Cubemap is null");
                return null;
            }

            int faceSize = cubemap.width;
            Texture2D result = new Texture2D(faceSize, faceSize * 6, format, false);

            CubemapFace[] faces = new CubemapFace[]
            {
                CubemapFace.PositiveX,
                CubemapFace.NegativeX,
                CubemapFace.PositiveY,
                CubemapFace.NegativeY,
                CubemapFace.PositiveZ,
                CubemapFace.NegativeZ
            };

            for (int i = 0; i < 6; i++)
            {
                Color[] pixels = cubemap.GetPixels(faces[i]);
                result.SetPixels(0, i * faceSize, faceSize, faceSize, pixels);
            }

            result.Apply();
            return result;
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
    }
}
