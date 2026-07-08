// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    /// <summary>
    /// Builds an AssetBundle from one or more selected <see cref="AnimationClip"/> assets and writes it as
    /// a <c>*.anim.lsb</c> file that the runtime <see cref="AnimationBundleLoader"/> reads. Unlike the prop
    /// / avatar exporters (a single root prefab), this packs a curated set of clips so a whole pack ships
    /// as one file; each clip is later addressed by <c>file:&lt;relative-path&gt;#&lt;clipName&gt;</c>.
    ///
    /// Because clips are addressed by name within the bundle, clip names must be unique; the export is
    /// rejected when two selected clips share a name. The bundle's internal id (CAB-...) is salted with a
    /// hash of ALL member GUIDs, so it is stable for a given clip set and does not shift when the set's
    /// first member changes.
    /// </summary>
    public static class AnimationBundleExporter
    {
        private const string kBundleName = "animation";
        private const string kExtension = ".anim.lsb";
        private const string kMenuPath = "Assets/Lilium Live Studio/Export Animation Bundle (.anim.lsb)";

        /// <summary>
        /// Builds an animation bundle from <paramref name="clipAssetPaths"/> (standalone <c>*.anim</c>
        /// assets) and copies it to <paramref name="destPath"/>. Returns true on success.
        /// </summary>
        public static bool Export(IReadOnlyList<string> clipAssetPaths, string destPath)
        {
            if (clipAssetPaths == null || clipAssetPaths.Count == 0)
            {
                Debug.LogError("[LiveStudio] No animation clips to bundle.");
                return false;
            }

            // Reject duplicate clip names up front: clips are addressed by name in the bundle, so two with
            // the same name would collide into one key and only one would ever resolve.
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in clipAssetPaths)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[LiveStudio] Not an animation clip asset: '{path}'.");
                    return false;
                }
                if (!seenNames.Add(clip.name))
                {
                    Debug.LogError($"[LiveStudio] Duplicate clip name '{clip.name}': clip names in an animation bundle must be unique. Rename one and re-export.");
                    return false;
                }
            }

            var token = _MemberToken(clipAssetPaths);
            return BundleBuildUtility.Build(kBundleName, _ToArray(clipAssetPaths), destPath, token);
        }

        // A stable token derived from every member's GUID (sorted, so member order does not change it), so
        // the bundle's internal id is unique per clip set and reproducible across re-exports.
        private static string _MemberToken(IReadOnlyList<string> clipAssetPaths)
        {
            var guids = new List<string>(clipAssetPaths.Count);
            foreach (var path in clipAssetPaths)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                guids.Add(string.IsNullOrEmpty(guid) ? path : guid);
            }
            guids.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            for (int i = 0; i < guids.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(guids[i]);
            }
            return BundleBuildUtility.StableToken(sb.ToString());
        }

        private static string[] _ToArray(IReadOnlyList<string> list)
        {
            var arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }

        [MenuItem(kMenuPath)]
        private static void _ExportSelectedClips()
        {
            var clipPaths = _SelectedClipAssetPaths();
            if (clipPaths.Count == 0)
            {
                Debug.LogError("[LiveStudio] Select one or more AnimationClip (.anim) assets in the Project window first.");
                return;
            }

            // Default name: the single clip's name, or a generic pack name for several.
            var defaultBase = clipPaths.Count == 1
                ? Path.GetFileNameWithoutExtension(clipPaths[0])
                : "AnimationPack";
            var savePath = EditorUtility.SaveFilePanel("Export Animation Bundle", "", defaultBase + kExtension, "lsb");
            if (string.IsNullOrEmpty(savePath)) return;

            // SaveFilePanel only handles the final extension, so guarantee the compound suffix.
            if (!savePath.EndsWith(kExtension, StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, null);
                if (savePath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = savePath.Substring(0, savePath.Length - ".anim".Length);
                }
                savePath += kExtension;
            }

            Export(clipPaths, savePath);
        }

        [MenuItem(kMenuPath, validate = true)]
        private static bool _ValidateExportSelectedClips() => _SelectedClipAssetPaths().Count > 0;

        // The asset paths of the selected standalone AnimationClips (main asset of a *.anim file). Clips
        // that are sub-assets of another file (e.g. an FBX) are skipped: bundling them would pull in the
        // whole containing asset, not just the clip.
        private static List<string> _SelectedClipAssetPaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in Selection.objects)
            {
                if (!(obj is AnimationClip)) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                if (!AssetDatabase.IsMainAsset(obj)) continue; // skip FBX sub-clips
                if (seen.Add(path)) paths.Add(path);
            }
            return paths;
        }
    }
}
