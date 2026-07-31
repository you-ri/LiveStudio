// Copyright (c) You-Ri, 2026

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Loading and tinting for the white main-toolbar icons, shared by every toolbar button and
    /// editor-version branch so the buttons read as one set.
    /// </summary>
    public static class ToolbarIcon
    {
        /// <summary>Idle tint matches the other main-toolbar icons (#e3e3e3).</summary>
        public static readonly Color idleTint = new Color(0.89f, 0.89f, 0.89f);

        /// <summary>The icon turns green while whatever the button controls is running.</summary>
        public static readonly Color runningTint = new Color(0.30f, 0.85f, 0.30f);

        public static Texture2D Load(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>
        /// Builds a tinted copy of the icon for hosts that cannot tint the rendered image. The source
        /// icons are white, so multiplying by the tint yields the tint itself while keeping the alpha
        /// silhouette. The PNG is decoded from disk because the imported asset is not readable.
        /// </summary>
        public static Texture2D CreateTinted(string assetPath, Color tint)
        {
            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[RemoteControl] Toolbar icon not found: {fullPath}");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            if (!texture.LoadImage(File.ReadAllBytes(fullPath)))
            {
                Debug.LogError($"[RemoteControl] Failed to decode toolbar icon: {fullPath}");
                Object.DestroyImmediate(texture);
                return null;
            }

            var pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                pixel.r = (byte)(pixel.r * tint.r);
                pixel.g = (byte)(pixel.g * tint.g);
                pixel.b = (byte)(pixel.b * tint.b);
                pixels[i] = pixel;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
