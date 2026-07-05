using System.IO;
using UnityEngine;

namespace Lilium.LiveStudio
{
    public static class CameraUtility
    {
        public static void CalculateThumbnailSize(float aspect, out int width, out int height)
        {
            const int kMaxSize = 320;

            if (aspect >= 1.0f)
            {
                // 横長の場合
                width = kMaxSize;
                height = Mathf.RoundToInt(kMaxSize / aspect);
            }
            else
            {
                // 縦長の場合
                width = Mathf.RoundToInt(kMaxSize * aspect);
                height = kMaxSize;
            }

            // 最小サイズを保証
            width = Mathf.Max(width, 64);
            height = Mathf.Max(height, 64);
        }

        public static void SaveTextureToPNG(Texture2D texture, string filePath)
        {
            byte[] bytes = texture.EncodeToPNG();
            File.WriteAllBytes(filePath, bytes);
            Debug.Log($"[Studio] CameraUtility.SaveTextureToPNG: Saved texture to {filePath}");
        }

        public static Texture2D ResizeTexture(Texture2D sourceTexture, int targetWidth, int targetHeight)
        {
            if (sourceTexture.width == targetWidth && sourceTexture.height == targetHeight)
            {
                return sourceTexture;
            }

            RenderTexture renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight);
            Graphics.Blit(sourceTexture, renderTexture);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;

            Texture2D resizedTexture = new Texture2D(targetWidth, targetHeight);
            resizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resizedTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);

            return resizedTexture;
        }
    }
}
