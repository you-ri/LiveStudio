// Copyright (c) You-Ri, 2026
using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.EditorTools
{
    public static class MissingScriptRemover
    {
        private const string kMenuPath = "Assets/Lilium Live Studio/Remove Missing Scripts";

        // Greys out the context-menu entry unless at least one selected asset is a prefab.
        [MenuItem(kMenuPath, true)]
        private static bool RemoveFromSelectedAssetsValidate()
        {
            foreach (var obj in Selection.objects)
            {
                if (_IsPrefabAsset(obj)) return true;
            }
            return false;
        }

        [MenuItem(kMenuPath)]
        public static void RemoveFromSelectedAssets()
        {
            int totalRemoved = 0;
            int changedPrefabs = 0;
            int scannedPrefabs = 0;

            foreach (var obj in Selection.objects)
            {
                if (!_IsPrefabAsset(obj)) continue;

                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                scannedPrefabs++;
                // Edit the prefab through its loaded contents so nested/variant prefabs are handled
                // correctly, then write it back only when something actually changed.
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int removed = 0;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        removed += UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    }

                    if (removed > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        totalRemoved += removed;
                        changedPrefabs++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (totalRemoved > 0)
            {
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[Tools] Removed {totalRemoved} missing scripts from {changedPrefabs}/{scannedPrefabs} prefab assets.");
        }

        private static bool _IsPrefabAsset(Object obj)
        {
            if (obj == null) return false;
            var path = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(path)
                && path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
