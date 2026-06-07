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

        [UnityEditor.MenuItem("Tools/Virgo Motion/Allow Capture Ports through Firewall")]
        public static void AllowCapturePortsThroughFirewall()
        {
            const string kDialogTitle = "Allow Capture Ports";

            // The rule name / ports are owned by FusionApp now. This maintenance menu must work even
            // when no FusionApp instance exists in the open scene, so use the component's defaults.
            var ruleName = FusionApp.kCaptureFirewallRuleName;
            var ports = FusionApp.kDefaultCapturePorts;

            // The rule is idempotent and added automatically when Fusion launches, so it
            // often already exists. Report that explicitly instead of silently doing nothing.
            if (WindowsFirewall.IsInboundRulePresent(ruleName))
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    kDialogTitle,
                    $"Capture ports ({ports}) are already allowed through Windows Firewall.",
                    "OK");
                return;
            }

            bool added = WindowsFirewall.EnsureInboundUdpPortsAllowed(ports, ruleName);
            UnityEditor.EditorUtility.DisplayDialog(
                kDialogTitle,
                added
                    ? $"Capture ports ({ports}) are now allowed through Windows Firewall."
                    : $"Could not add the firewall rule (it may have been cancelled).\n\n" +
                      $"Run manually as administrator:\n" +
                      $"netsh advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=UDP localport={ports} profile=any",
                "OK");
        }

    }
}