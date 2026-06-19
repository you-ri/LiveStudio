// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Authors the <see cref="BundleThumbnail"/> that exporters pack into a LiveStudio bundle.
    ///
    /// The thumbnail asset is persistent (kept next to the prefab, not deleted after export) so the
    /// user can replace the auto-generated preview with their own image via the BundleThumbnail
    /// inspector. The auto preview is rendered synchronously with a <see cref="PreviewRenderUtility"/>
    /// (unlike <c>AssetPreview.GetAssetPreview</c> which is async and would not be ready during a
    /// synchronous export), framed to the prefab's renderer bounds, and encoded to PNG.
    /// </summary>
    public static class BundleThumbnailAuthoring
    {
        // Square thumbnail edge length in pixels. Matches the avatar card preview which is square.
        private const int kSize = 256;

        /// <summary>Deterministic, persistent thumbnail asset path next to <paramref name="prefabAssetPath"/>.</summary>
        public static string GetThumbnailAssetPath(string prefabAssetPath)
        {
            var dir = Path.GetDirectoryName(prefabAssetPath).Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(prefabAssetPath);
            return $"{dir}/{name} Thumbnail.asset";
        }

        /// <summary>
        /// Returns the persistent thumbnail asset path, creating it (and rendering the prefab preview
        /// into it) when missing or empty. An existing thumbnail that already has an image — auto or
        /// user-provided — is left untouched so a re-export preserves a customized thumbnail. Returns
        /// null only when the prefab cannot be loaded.
        /// </summary>
        public static string GetOrCreateThumbnailAssetPath(string prefabAssetPath)
        {
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                if (prefab == null)
                {
                    return null;
                }

                var assetPath = GetThumbnailAssetPath(prefabAssetPath);
                var thumbnail = AssetDatabase.LoadAssetAtPath<BundleThumbnail>(assetPath);

                bool changed = false;
                if (thumbnail == null)
                {
                    thumbnail = ScriptableObject.CreateInstance<BundleThumbnail>();
                    AssetDatabase.CreateAsset(thumbnail, assetPath);
                    changed = true;
                }

                if (!thumbnail.HasImage)
                {
                    var png = RenderPrefabPreviewPng(prefab);
                    if (png != null && png.Length > 0)
                    {
                        thumbnail.SetImage(png, "image/png");
                        EditorUtility.SetDirty(thumbnail);
                        changed = true;
                    }
                }

                if (changed)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(assetPath);
                }
                return assetPath;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LiveStudio] Failed to author bundle thumbnail for '{prefabAssetPath}': {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Renders <paramref name="prefab"/> to a square PNG framed to its renderer bounds. Returns
        /// null when it has no renderable content. Used both at export time and by the inspector's
        /// "regenerate" action.
        /// </summary>
        public static byte[] RenderPrefabPreviewPng(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            var preview = new PreviewRenderUtility();
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab);
                instance.hideFlags = HideFlags.HideAndDontSave;

                if (!_TryGetBounds(instance, out Bounds bounds))
                {
                    return null;
                }

                preview.AddSingleGO(instance);

                // Frame the camera so the whole bounds fit, viewed from a 3/4 front angle.
                float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
                const float fov = 30f;
                float distance = radius / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 1.1f;
                var dir = new Vector3(0.4f, 0.2f, -1f).normalized;

                var cam = preview.camera;
                cam.fieldOfView = fov;
                cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
                cam.farClipPlane = distance + radius * 4f;
                cam.transform.position = bounds.center + dir * distance;
                cam.transform.rotation = Quaternion.LookRotation(bounds.center - cam.transform.position, Vector3.up);
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

                preview.lights[0].intensity = 1.1f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                if (preview.lights.Length > 1)
                {
                    preview.lights[1].intensity = 0.6f;
                }
                preview.ambientColor = new Color(0.35f, 0.35f, 0.35f, 1f);

                var rect = new Rect(0f, 0f, kSize, kSize);
                preview.BeginStaticPreview(rect);
                preview.camera.Render();
                var tex = preview.EndStaticPreview();
                if (tex == null)
                {
                    return null;
                }

                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
                preview.Cleanup();
            }
        }

        // Combined world-space bounds of all renderers under root. Returns false when there are none.
        private static bool _TryGetBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool any = false;
            foreach (var renderer in renderers)
            {
                // Particle/Trail renderers can report empty/garbage bounds before simulation; skip them.
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer)
                {
                    continue;
                }
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return any && bounds.extents.sqrMagnitude > 0f;
        }
    }
}
