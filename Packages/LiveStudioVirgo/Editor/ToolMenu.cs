using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Lilium.LiveStudio;
namespace Lilium.LiveStudio.Virgo
{

    public static class ToolMenu
    {
        public static VRMAvatarSetupSettings settings =>
            LiveStudioProjectSettings.Instance?.vrmAvatarSetupSettings;

#if VRMC_VRM10
        [UnityEditor.MenuItem("Tools/Virgo Motion/Setup VRM Avatar")]
        public static void SetupVRMAvatar()
        {
            var selectedObjects = UnityEditor.Selection.objects;
            if (selectedObjects.Length == 0)
            {
                Debug.LogError("No GameObject selected. Please select an avatar GameObject.");
                return;
            }
            if (settings == null)
            {
                Debug.LogError("VRMAvatarSetupSettings is not assigned in LiveStudioProjectSettings. Open 'Project Settings > Virgo Motion > Studio' to configure.");
                return;
            }

            foreach (var obj in selectedObjects)
            {
                if (obj is GameObject avatar)
                {
                    VRMAvatarSetupSystem.SetupVRMTargetAvatar(avatar, settings);
                }
                else
                {
                    Debug.LogWarning($"Selected object '{obj.name}' is not a GameObject. Skipping.");
                }
            }
        }
#endif


        [UnityEditor.MenuItem("Tools/Virgo Motion/Open Persistent Data Folder")]
        public static void OpenPersistentDataFolder()
        {
            UnityEditor.EditorUtility.RevealInFinder(Application.persistentDataPath);
        }

    }
}