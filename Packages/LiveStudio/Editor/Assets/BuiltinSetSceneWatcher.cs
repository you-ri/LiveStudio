// Copyright (c) You-Ri, 2026

using System;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Keeps the scene paths cached in <see cref="BuiltinSetList"/> in step with the project. A declared
    /// set is identified by its scene's GUID, which survives a move or a rename; the path stored next to it
    /// is only how runtime finds the scene, so it has to be re-derived whenever a scene moves — otherwise a
    /// set would list fine and then fail to load.
    ///
    /// Also drops <see cref="BuiltinSetSource"/>'s cached copy after the list itself is edited, so an edit
    /// is picked up without a domain reload (the same thing the built-in asset catalog does after a bake).
    /// </summary>
    class BuiltinSetSceneWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool scenesMoved = _ContainsScene(moved);
            bool listTouched = _ContainsList(imported) || _ContainsList(moved);
            if (!scenesMoved && !listTouched) return;

            // Defer past the import that triggered us so the AssetDatabase is settled.
            EditorApplication.delayCall += () =>
            {
                if (scenesMoved) _HealScenePaths();
                BuiltinSetSource.Reload();
            };
        }

        // Rewrites every entry's cached scene path from its GUID. Only writes when something actually
        // changed, so an unrelated scene move never dirties the asset.
        static void _HealScenePaths()
        {
            var list = _FindList();
            if (list == null) return;

            var entries = list.entries;
            bool changed = false;
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.IsNullOrEmpty(entries[i].guid)) continue;

                var path = AssetDatabase.GUIDToAssetPath(entries[i].guid);
                // An empty path means the scene was deleted, not moved. Keep the stale path: the entry is
                // reported as missing from the build, which says more than an entry pointing at nothing.
                if (string.IsNullOrEmpty(path) || path == entries[i].scenePath) continue;

                entries[i].scenePath = path;
                changed = true;
            }
            if (!changed) return;

            list.entries = entries;
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            Debug.Log("[LiveStudio] Built-in set scene paths updated after a scene moved.");
        }

        static BuiltinSetList _FindList()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(BuiltinSetList)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                var list = AssetDatabase.LoadAssetAtPath<BuiltinSetList>(path);
                if (list != null) return list;
            }
            return null;
        }

        static bool _ContainsScene(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] != null && paths[i].EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static bool _ContainsList(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] != null &&
                    AssetDatabase.LoadAssetAtPath<BuiltinSetList>(paths[i]) != null) return true;
            }
            return false;
        }
    }
}
