// Copyright (c) You-Ri, 2026
using UnityEditor;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Holds in-memory assets (Mesh / Material / AnimationClip / AnimatorController graph, ...)
    /// persisted from a baked avatar as sub-assets of one container file.
    /// </summary>
    internal sealed class TransferAssetContainer : ScriptableObject
    {
    }

    /// <summary>
    /// Turns a Modular Avatar / NDMF manual-bake result (a scene GameObject such as
    /// "Avatar(Clone)") into a prefab. In-memory-only assets referenced by the hierarchy are
    /// first persisted into a single container asset so the saved prefab keeps valid
    /// references. No VRM conversion is performed here.
    /// </summary>
    internal static class BakedAvatarPrefabBuilder
    {
        private const string kCloneSuffix = "(Clone)";

        // VRM 変換 (Convert) とは別フォルダに出力する。
        internal const string kOutputFolder = "Assets/VRCAT_GeneratedAssets/Baked";

        /// <summary>
        /// Builds a prefab from the given scene GameObject. The source scene object is not
        /// modified (work is done on a clone). Returns true when the prefab was saved.
        /// </summary>
        public static bool Build(GameObject sceneObject)
        {
            if (sceneObject == null)
            {
                VRChatAvatarTransferLog.Error("No GameObject given.");
                return false;
            }
            if (EditorUtility.IsPersistent(sceneObject))
            {
                VRChatAvatarTransferLog.Error(
                    $"'{sceneObject.name}' is an asset, not a scene object. Select the baked avatar in the Hierarchy.");
                return false;
            }

            Vrm10ObjectBuilder.EnsureFolder(kOutputFolder);

            var rawName = sceneObject.name;
            if (rawName.EndsWith(kCloneSuffix, System.StringComparison.Ordinal))
            {
                rawName = rawName.Substring(0, rawName.Length - kCloneSuffix.Length).TrimEnd();
            }
            var safeName = Vrm10ObjectBuilder.MakeFileSafe(rawName);

            var containerPath = $"{kOutputFolder}/{safeName}.Assets.asset";
            var prefabPath = $"{kOutputFolder}/{safeName}.prefab";

            // ユーザーのシーンオブジェクトを破壊しないようクローン上で作業する。
            // Mesh / Material 等はクローンと共有参照のままなので、永続化すれば元側でも有効になる。
            var work = Object.Instantiate(sceneObject);
            try
            {
                // 前回生成物が残っていると古いサブアセットが混ざるため作り直す。
                if (AssetDatabase.LoadAssetAtPath<Object>(containerPath) != null)
                {
                    AssetDatabase.DeleteAsset(containerPath);
                }
                var container = ScriptableObject.CreateInstance<TransferAssetContainer>();
                AssetDatabase.CreateAsset(container, containerPath);

                int persisted = InMemoryAssetCollector.CollectAndPersist(work, container);

                // SaveAsPrefabAsset は Missing Script を含む階層の保存を拒否する。
                // 元 VRChat アバターには当プロジェクトに無いコンポーネントが残ることがある。
                int missing = _RemoveMissingScripts(work);
                if (missing > 0)
                {
                    VRChatAvatarTransferLog.Info($"'{safeName}': removed {missing} missing script(s).");
                }

                AssetDatabase.SaveAssets();
                var saved = false;
                PrefabUtility.SaveAsPrefabAsset(work, prefabPath, out saved);

                // サブアセットが 1 つも無いコンテナは不要なので残さない。
                if (persisted == 0)
                {
                    AssetDatabase.DeleteAsset(containerPath);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (!saved)
                {
                    VRChatAvatarTransferLog.Error($"Failed to save prefab to '{prefabPath}'.");
                    return false;
                }

                VRChatAvatarTransferLog.Info(
                    persisted > 0
                        ? $"Saved prefab to '{prefabPath}' with {persisted} in-memory asset(s) stored in '{containerPath}'."
                        : $"Saved prefab to '{prefabPath}' (no in-memory assets needed persisting).");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null) EditorGUIUtility.PingObject(prefab);
                return true;
            }
            finally
            {
                if (work != null) Object.DestroyImmediate(work);
            }
        }

        private static int _RemoveMissingScripts(GameObject root)
        {
            int removed = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            return removed;
        }
    }
}
