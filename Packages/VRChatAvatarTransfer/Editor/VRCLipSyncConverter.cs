// Copyright (c) You-Ri, 2026

using Lilium.LiveStudio;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Ports a VRChat avatar's <c>VisemeBlendShape</c> lip sync configuration
    /// (<see cref="VRCAvatarDescriptor.VisemeSkinnedMesh"/> + <see cref="VRCAvatarDescriptor.VisemeBlendShapes"/>)
    /// onto the VRCSDK-independent <see cref="VRCAvatar"/> component, so the converted avatar
    /// drives the vowel viseme blend shapes directly at runtime. Only the 5 vowels VRCAvatar
    /// estimates from ARKit (aa/E/ih/oh/ou) are mapped; consonant visemes are out of scope.
    /// Must be called before the VRCAvatarDescriptor is stripped.
    /// </summary>
    internal static class VRCLipSyncConverter
    {
        // VRCAvatar のローカル viseme 順 (sil/aa/E/ih/oh/ou) と同じ長さ。
        private const int kVisemeCount = 6;
        private const int kSil = 0;
        private const int kAa = 1;
        private const int kEe = 2;
        private const int kIh = 3;
        private const int kOh = 4;
        private const int kOu = 5;

        /// <returns>true when a usable viseme blend shape mapping was applied.</returns>
        public static bool Convert(GameObject avatarRoot, VRCAvatar target)
        {
            if (avatarRoot == null || target == null) return false;

            var desc = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (desc == null || desc.lipSync != VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape)
            {
                VRChatAvatarTransferLog.Info(
                    $"'{avatarRoot.name}': lip sync is not VisemeBlendShape; VRCAvatar falls back to the Animator Viseme/Voice path.");
                return false;
            }

            var smr = desc.VisemeSkinnedMesh;
            var names = desc.VisemeBlendShapes;
            if (smr == null || smr.sharedMesh == null || names == null)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarRoot.name}': VisemeBlendShape lip sync has no VisemeSkinnedMesh / VisemeBlendShapes; skipped.");
                return false;
            }

            // ローカル viseme 順に解決する。sil は専用 blendshape を持たない (-1 = 口閉じ)。
            var indices = new int[kVisemeCount];
            indices[kSil] = -1;
            indices[kAa] = _ResolveIndex(smr, names, VRC_AvatarDescriptor.Viseme.aa);
            indices[kEe] = _ResolveIndex(smr, names, VRC_AvatarDescriptor.Viseme.E);
            indices[kIh] = _ResolveIndex(smr, names, VRC_AvatarDescriptor.Viseme.ih);
            indices[kOh] = _ResolveIndex(smr, names, VRC_AvatarDescriptor.Viseme.oh);
            indices[kOu] = _ResolveIndex(smr, names, VRC_AvatarDescriptor.Viseme.ou);

            if (indices[kAa] < 0 && indices[kEe] < 0 && indices[kIh] < 0 && indices[kOh] < 0 && indices[kOu] < 0)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarRoot.name}': none of the vowel viseme blend shapes were found on '{smr.name}'; lip sync not ported.");
                return false;
            }

            target.ConfigureVisemeBlendShapes(smr, indices);
            EditorUtility.SetDirty(target);

            var missing = _MissingNames(indices);
            if (missing != null)
            {
                VRChatAvatarTransferLog.Warn(
                    $"'{avatarRoot.name}': viseme blend shape(s) not found and skipped: {missing}.");
            }
            VRChatAvatarTransferLog.Info(
                $"'{avatarRoot.name}': ported VisemeBlendShape lip sync to VRCAvatar (mesh '{smr.name}').");
            return true;
        }

        private static int _ResolveIndex(SkinnedMeshRenderer smr, string[] names, VRC_AvatarDescriptor.Viseme viseme)
        {
            int i = (int)viseme;
            if (i < 0 || i >= names.Length) return -1;
            var name = names[i];
            if (string.IsNullOrEmpty(name)) return -1;
            return smr.sharedMesh.GetBlendShapeIndex(name);
        }

        private static string _MissingNames(int[] indices)
        {
            string s = null;
            if (indices[kAa] < 0) s = _Append(s, "aa");
            if (indices[kEe] < 0) s = _Append(s, "E");
            if (indices[kIh] < 0) s = _Append(s, "ih");
            if (indices[kOh] < 0) s = _Append(s, "oh");
            if (indices[kOu] < 0) s = _Append(s, "ou");
            return s;
        }

        private static string _Append(string acc, string item)
        {
            return acc == null ? item : acc + ", " + item;
        }
    }
}
