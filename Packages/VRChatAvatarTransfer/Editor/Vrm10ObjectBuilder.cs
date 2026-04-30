// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// VRChat アバターから VRM10Object を生成し、可能な範囲で中身を埋める。
    /// SpringBone の格納先となるダミーではなく、Meta / Expression / LookAt / FirstPerson を
    /// VRC AvatarDescriptor から推定して反映する。
    /// </summary>
    internal static class Vrm10ObjectBuilder
    {
        private const string OutputFolder = "Assets/VRChatAvatarTransfer";

        /// <summary>
        /// avatarRoot 用の VRM10Object をロードまたは生成し、内容を埋めて返す。
        /// </summary>
        public static VRM10Object BuildOrLoad(GameObject avatarRoot)
        {
            EnsureFolder(OutputFolder);

            var assetPath = BuildAssetPath(avatarRoot);
            var asset = AssetDatabase.LoadAssetAtPath<VRM10Object>(assetPath);
            var freshlyCreated = asset == null;
            if (freshlyCreated)
            {
                asset = ScriptableObject.CreateInstance<VRM10Object>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            PopulateMeta(asset.Meta, avatarRoot);
            PopulateLookAt(asset.LookAt, avatarRoot, freshlyCreated);
            PopulateFirstPerson(asset.FirstPerson, avatarRoot, freshlyCreated);
            PopulateExpressions(asset, avatarRoot);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            if (freshlyCreated)
            {
                VRChatAvatarTransferLog.Info($"Created VRM10Object asset at '{assetPath}'.");
            }
            return asset;
        }

        private static string BuildAssetPath(GameObject avatarRoot)
        {
            var safe = MakeFileSafe(avatarRoot.name);
            return $"{OutputFolder}/{safe}.Vrm10.asset";
        }

        private static string MakeFileSafe(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Avatar";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void PopulateMeta(VRM10ObjectMeta meta, GameObject avatarRoot)
        {
            if (string.IsNullOrEmpty(meta.Name))
            {
                meta.Name = avatarRoot.name;
            }
            if (meta.Authors == null)
            {
                meta.Authors = new List<string>();
            }
            if (meta.Authors.Count == 0 || meta.Authors.All(string.IsNullOrWhiteSpace))
            {
                meta.Authors = new List<string> { "Unknown" };
            }
            if (string.IsNullOrEmpty(meta.Version))
            {
                meta.Version = "1.0";
            }
        }

        private static void PopulateLookAt(VRM10ObjectLookAt lookAt, GameObject avatarRoot, bool overwrite)
        {
            // VRC AvatarDescriptor の ViewPosition は avatarRoot のローカル座標。
            // VRM10 の OffsetFromHead は Head ボーン基準なので、Head ローカル座標に変換する。
            var desc = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (desc == null) return;

            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;

            // 既に手動で値が編集されている可能性を尊重し、新規生成時のみ上書きする。
            if (!overwrite) return;

            var worldView = avatarRoot.transform.TransformPoint(desc.ViewPosition);
            lookAt.OffsetFromHead = head.InverseTransformPoint(worldView);
        }

        private static void PopulateFirstPerson(VRM10ObjectFirstPerson firstPerson, GameObject avatarRoot, bool overwrite)
        {
            // 一度埋めた後にユーザが手で auto 以外に切り替えていることがあるため、新規時のみ自動収集する。
            if (!overwrite && firstPerson.Renderers != null && firstPerson.Renderers.Count > 0) return;
            firstPerson.SetDefault(avatarRoot.transform);
        }

        private static void PopulateExpressions(VRM10Object owner, GameObject avatarRoot)
        {
            var desc = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (desc == null) return;

            PopulateVisemes(owner, avatarRoot, desc);
            PopulateBlinkAndLook(owner, avatarRoot, desc);
        }

        private static void PopulateVisemes(VRM10Object owner, GameObject avatarRoot, VRCAvatarDescriptor desc)
        {
            if (desc.lipSync != VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape) return;
            var smr = desc.VisemeSkinnedMesh;
            var blendNames = desc.VisemeBlendShapes;
            if (smr == null || smr.sharedMesh == null || blendNames == null) return;

            var path = smr.transform.RelativePathFrom(avatarRoot.transform);
            // VRM10 の口形状プリセット 5 種に対して VRC の対応するビゼームを割り当てる。
            // VRC の "E" が VRM10 の "ee" にあたる。
            ApplyByBlendShapeName(owner, ExpressionPreset.aa, smr, GetVisemeName(blendNames, VRC_AvatarDescriptor.Viseme.aa), path);
            ApplyByBlendShapeName(owner, ExpressionPreset.ih, smr, GetVisemeName(blendNames, VRC_AvatarDescriptor.Viseme.ih), path);
            ApplyByBlendShapeName(owner, ExpressionPreset.ou, smr, GetVisemeName(blendNames, VRC_AvatarDescriptor.Viseme.ou), path);
            ApplyByBlendShapeName(owner, ExpressionPreset.ee, smr, GetVisemeName(blendNames, VRC_AvatarDescriptor.Viseme.E),  path);
            ApplyByBlendShapeName(owner, ExpressionPreset.oh, smr, GetVisemeName(blendNames, VRC_AvatarDescriptor.Viseme.oh), path);
        }

        private static string GetVisemeName(string[] visemeBlendShapes, VRC_AvatarDescriptor.Viseme viseme)
        {
            int idx = (int)viseme;
            if (idx < 0 || idx >= visemeBlendShapes.Length) return null;
            return visemeBlendShapes[idx];
        }

        private static void PopulateBlinkAndLook(VRM10Object owner, GameObject avatarRoot, VRCAvatarDescriptor desc)
        {
            if (!desc.enableEyeLook) return;
            var settings = desc.customEyeLookSettings;
            if (settings.eyelidType != VRCAvatarDescriptor.EyelidType.Blendshapes) return;

            var smr = settings.eyelidsSkinnedMesh;
            var indices = settings.eyelidsBlendshapes;
            if (smr == null || smr.sharedMesh == null || indices == null) return;

            var path = smr.transform.RelativePathFrom(avatarRoot.transform);
            // VRC の eyelidsBlendshapes は [0]=Blink, [1]=LookingUp, [2]=LookingDown の固定順。
            if (indices.Length >= 1) ApplyByBlendShapeIndex(owner, ExpressionPreset.blink,    smr, indices[0], path);
            if (indices.Length >= 2) ApplyByBlendShapeIndex(owner, ExpressionPreset.lookUp,   smr, indices[1], path);
            if (indices.Length >= 3) ApplyByBlendShapeIndex(owner, ExpressionPreset.lookDown, smr, indices[2], path);
        }

        private static void ApplyByBlendShapeName(VRM10Object owner, ExpressionPreset preset, SkinnedMeshRenderer smr, string blendName, string relativePath)
        {
            if (string.IsNullOrEmpty(blendName)) return;
            int idx = smr.sharedMesh.GetBlendShapeIndex(blendName);
            ApplyByBlendShapeIndex(owner, preset, smr, idx, relativePath);
        }

        private static void ApplyByBlendShapeIndex(VRM10Object owner, ExpressionPreset preset, SkinnedMeshRenderer smr, int index, string relativePath)
        {
            if (index < 0 || index >= smr.sharedMesh.blendShapeCount) return;

            var clip = EnsureClipForPreset(owner, preset);
            clip.MorphTargetBindings = new[] { new MorphTargetBinding(relativePath, index, 1.0f) };
            EditorUtility.SetDirty(clip);
            owner.Expression.AddClip(preset, clip);
        }

        private static VRM10Expression EnsureClipForPreset(VRM10Object owner, ExpressionPreset preset)
        {
            var clipName = preset.ToString();
            var ownerPath = AssetDatabase.GetAssetPath(owner);
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(ownerPath);
            foreach (var sub in subAssets)
            {
                if (sub is VRM10Expression e && e.name == clipName) return e;
            }

            var clip = ScriptableObject.CreateInstance<VRM10Expression>();
            clip.name = clipName;
            AssetDatabase.AddObjectToAsset(clip, owner);
            return clip;
        }
    }
}
