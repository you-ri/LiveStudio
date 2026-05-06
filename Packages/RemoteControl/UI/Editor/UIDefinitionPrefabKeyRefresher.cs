// Copyright (c) You-Ri, 2026

using UnityEditor;

namespace Lilium.RemoteControl.UI.Editor
{
    /// <summary>
    /// Walks every IPage under the given UIDefinition, finds StandardObjectFactory instances,
    /// and re-resolves the prefab Asset GUID held by each IExposedObjectFactory from the AssetDatabase.
    /// Invoked from the UI Designer Reset button so the update is explicit and does not depend on OnValidate.
    /// </summary>
    public static class UIDefinitionPrefabKeyRefresher
    {
        /// <summary>
        /// Walks the Factory of the given UIDefinition and re-resolves the prefab GUIDs.
        /// Shares the same logic as UIDefinition.OnValidate; the explicit update from the Designer
        /// additionally calls SetDirty + SaveAssetIfDirty.
        /// </summary>
        /// <returns>true if the def is valid and the refresh was performed.</returns>
        public static bool Refresh(UIDefinition def)
        {
            if (def == null) return false;
            if (def.menuItems == null) return false;

            UIDefinition._RefreshPrefabKeysInternal(def);

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssetIfDirty(def);
            return true;
        }
    }
}
