// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Inspector for <see cref="BundleThumbnail"/>: shows the current thumbnail and lets the user
    /// replace the auto-rendered preview with their own image (assign a texture, load a PNG/JPEG file,
    /// or re-render from a prefab). Scene-Template-like authoring UI for a bundle's preview image.
    /// </summary>
    [CustomEditor(typeof(BundleThumbnail))]
    public class BundleThumbnailEditor : UnityEditor.Editor
    {
        private Texture2D _preview;

        private void OnDisable()
        {
            _DestroyPreview();
        }

        public override void OnInspectorGUI()
        {
            var thumbnail = (BundleThumbnail)target;
            _EnsurePreview(thumbnail);

            // Current thumbnail preview.
            if (_preview != null)
            {
                var rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
                rect.width = rect.height; // keep it square
                EditorGUI.DrawPreviewTexture(rect, _preview, null, ScaleMode.ScaleToFit);
                EditorGUILayout.LabelField(
                    "Image",
                    $"{_preview.width}x{_preview.height}  /  {thumbnail.ImageData.Length} bytes  /  {thumbnail.MimeType}");
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No thumbnail image. Assign one below or re-render it from a prefab.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();

            // Replace with an in-project texture (png/jpg read verbatim, otherwise re-encoded to PNG).
            var assigned = (Texture2D)EditorGUILayout.ObjectField(
                "Replace with image", null, typeof(Texture2D), false);
            if (assigned != null)
            {
                var (bytes, mime) = _ToImageBytes(assigned);
                _Apply(thumbnail, bytes, mime);
            }

            // Re-render from a prefab's preview.
            var prefab = (GameObject)EditorGUILayout.ObjectField(
                "Regenerate from prefab", null, typeof(GameObject), false);
            if (prefab != null)
            {
                var png = BundleThumbnailAuthoring.RenderPrefabPreviewPng(prefab);
                if (png != null && png.Length > 0)
                {
                    _Apply(thumbnail, png, "image/png");
                }
                else
                {
                    Debug.LogWarning("[LiveStudio] Could not render a preview for the selected prefab.");
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Set from Game view"))
                {
                    _SetFromGameView(thumbnail);
                }
                if (GUILayout.Button("Load image from file…"))
                {
                    _LoadFromFile(thumbnail);
                }
            }
        }

        // Renders the active camera (center-cropped to a square) and uses it as the thumbnail. Works in
        // Edit mode too (renders the camera directly instead of grabbing the Game view framebuffer).
        private void _SetFromGameView(BundleThumbnail thumbnail)
        {
            var png = _CaptureCameraPng();
            if (png != null && png.Length > 0)
            {
                _Apply(thumbnail, png, "image/png");
            }
            else
            {
                Debug.LogWarning(
                    "[LiveStudio] Failed to capture the view. No active camera was found in the scene.");
            }
        }

        private static byte[] _CaptureCameraPng()
        {
            const int kSize = 256;

            var cam = Camera.main;
            if (cam == null)
            {
                var cams = Camera.allCameras; // enabled cameras in loaded scenes
                if (cams != null && cams.Length > 0)
                {
                    cam = cams[0];
                }
            }
            if (cam == null)
            {
                return null;
            }

            // Render at the camera's aspect to avoid distortion, then center-crop to a square.
            float aspect = cam.aspect > 0f ? cam.aspect : 1f;
            int rw = aspect >= 1f ? 512 : Mathf.Max(1, Mathf.RoundToInt(512 * aspect));
            int rh = aspect >= 1f ? Mathf.Max(1, Mathf.RoundToInt(512 / aspect)) : 512;

            var rt = RenderTexture.GetTemporary(rw, rh, 24);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D full = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                full = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
                full.ReadPixels(new Rect(0, 0, rw, rh), 0, 0);
                full.Apply();
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }

            var png = _CropScaleToSquarePng(full, kSize);
            DestroyImmediate(full);
            return png;
        }

        // Center-crops src to a square and scales it to size, returning PNG bytes.
        private static byte[] _CropScaleToSquarePng(Texture2D src, int size)
        {
            int side = Mathf.Min(src.width, src.height);
            var scale = new Vector2((float)side / src.width, (float)side / src.height);
            var offset = new Vector2(
                (src.width - side) * 0.5f / src.width,
                (src.height - side) * 0.5f / src.height);

            var rt = RenderTexture.GetTemporary(size, size);
            Graphics.Blit(src, rt, scale, offset);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            outTex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var png = outTex.EncodeToPNG();
            DestroyImmediate(outTex);
            return png;
        }

        private void _LoadFromFile(BundleThumbnail thumbnail)
        {
            var path = EditorUtility.OpenFilePanel("Select thumbnail image", "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                _Apply(thumbnail, bytes, _MimeFromExtension(path));
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to load image '{path}': {e.Message}");
            }
        }

        // Reads a texture as encoded image bytes. For .png/.jpg/.jpeg source assets the original file
        // bytes are used verbatim (no quality loss); otherwise the texture is blitted into a readable
        // copy and encoded to PNG (handles non-readable / compressed textures).
        private static (byte[] bytes, string mimeType) _ToImageBytes(Texture2D texture)
        {
            var assetPath = AssetDatabase.GetAssetPath(texture);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var lower = assetPath.ToLowerInvariant();
                if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg"))
                {
                    return (File.ReadAllBytes(assetPath), _MimeFromExtension(assetPath));
                }
            }

            var rt = RenderTexture.GetTemporary(texture.width, texture.height);
            Graphics.Blit(texture, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var png = readable.EncodeToPNG();
            DestroyImmediate(readable);
            return (png, "image/png");
        }

        private static string _MimeFromExtension(string path)
        {
            var lower = path.ToLowerInvariant();
            return (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) ? "image/jpeg" : "image/png";
        }

        private void _Apply(BundleThumbnail thumbnail, byte[] bytes, string mimeType)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }
            Undo.RecordObject(thumbnail, "Set Bundle Thumbnail");
            thumbnail.SetImage(bytes, mimeType);
            EditorUtility.SetDirty(thumbnail);
            AssetDatabase.SaveAssets();
            _DestroyPreview();
        }

        private void _EnsurePreview(BundleThumbnail thumbnail)
        {
            if (_preview != null)
            {
                return;
            }
            if (!thumbnail.HasImage)
            {
                return;
            }
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(thumbnail.ImageData))
            {
                _preview = tex;
            }
            else
            {
                DestroyImmediate(tex);
            }
        }

        private void _DestroyPreview()
        {
            if (_preview != null)
            {
                DestroyImmediate(_preview);
                _preview = null;
            }
        }
    }
}
