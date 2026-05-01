// Copyright (c) You-Ri, 2026

using UnityEditor;

namespace Lilium.RemoteControl.WebUI.Editor
{
    /// <summary>
    /// 指定 WebUIDefinition 配下の任意の IPage が持つ StandardObjectFactory を走査し、
    /// 各 IExposedObjectFactory が保持する prefab Asset GUID を AssetDatabase から再解決する。
    /// WebUI Simulator の Reset ボタンから呼ばれ、OnValidate に依存せず明示的に更新する。
    /// </summary>
    public static class WebUIDefinitionPrefabKeyRefresher
    {
        /// <summary>
        /// 指定 WebUIDefinition の Factory を走査して prefab GUID を再解決する。
        /// WebUIDefinition 側の OnValidate と同じロジックを共有し、Simulator からの明示更新では
        /// 追加で SetDirty + SaveAssetIfDirty を行う。
        /// </summary>
        /// <returns>対象 def が有効で処理を行った場合 true。</returns>
        public static bool Refresh(WebUIDefinition def)
        {
            if (def == null) return false;
            if (def.menuItems == null) return false;

            WebUIDefinition._RefreshPrefabKeysInternal(def);

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssetIfDirty(def);
            return true;
        }
    }
}
