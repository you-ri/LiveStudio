// Copyright (c) You-Ri, 2026

using Lilium.LiveStudio;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Ports a VRChat avatar's eyelid Blink blendshape configuration
    /// (<see cref="VRCAvatarDescriptor.customEyeLookSettings"/>:
    /// <c>eyelidsSkinnedMesh</c> + <c>eyelidsBlendshapes[0]</c>) onto the VRCSDK-independent
    /// <see cref="VRCAvatar"/> component, so the converted avatar drives the blink blend shape
    /// from ARKit at runtime. Only the Blink slot is mapped; "Looking Up" / "Looking Down"
    /// eyelid blendshapes are out of scope (eye gaze is driven through the eye bones).
    /// Must be called before the VRCAvatarDescriptor is stripped.
    /// </summary>
    internal static class VRCEyeBlinkConverter
    {
        // VRCAvatarDescriptor.customEyeLookSettings.eyelidsBlendshapes is a fixed-size int[3]
        // whose entries are blendshape indices (not names): [0]=Blink, [1]=LookingUp,
        // [2]=LookingDown. Only the Blink slot is consumed here.
        private const int kBlinkSlot = 0;

        /// <returns>true when a usable blink blend shape mapping was applied.</returns>
        public static bool Convert(GameObject avatarRoot, VRCAvatar target)
        {
            if (avatarRoot == null || target == null) return false;

            var desc = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (desc == null || !desc.enableEyeLook)
            {
                VRChatAvatarTransferLog.Info(
                    $"'{avatarRoot.name}': eye look is disabled on VRCAvatarDescriptor; blink blendshape not ported.");
                return false;
            }

            var eyeSettings = desc.customEyeLookSettings;
            if (eyeSettings.eyelidType != VRCAvatarDescriptor.EyelidType.Blendshapes)
            {
                VRChatAvatarTransferLog.Info(
                    $"'{avatarRoot.name}': eyelid type is not Blendshapes ({eyeSettings.eyelidType}); blink blendshape not ported.");
                return false;
            }

            var smr = eyeSettings.eyelidsSkinnedMesh;
            var indices = eyeSettings.eyelidsBlendshapes;
            if (smr == null || smr.sharedMesh == null || indices == null || indices.Length <= kBlinkSlot)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarRoot.name}': eyelid Blendshapes lipsync has no eyelidsSkinnedMesh / eyelidsBlendshapes; skipped.");
                return false;
            }

            int blinkIndex = indices[kBlinkSlot];
            if (blinkIndex < 0 || blinkIndex >= smr.sharedMesh.blendShapeCount)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarRoot.name}': Blink blendshape index '{blinkIndex}' is out of range on '{smr.name}'; blink not ported.");
                return false;
            }

            target.ConfigureBlinkBlendShape(smr, blinkIndex);
            EditorUtility.SetDirty(target);

            VRChatAvatarTransferLog.Info(
                $"'{avatarRoot.name}': ported eyelid Blink blendshape '{smr.sharedMesh.GetBlendShapeName(blinkIndex)}' to VRCAvatar (mesh '{smr.name}').");
            return true;
        }
    }
}
