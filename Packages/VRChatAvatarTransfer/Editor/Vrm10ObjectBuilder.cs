// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// VRChat アバターから VRM10Object を生成し、可能な範囲で中身を埋める。
    /// SpringBone の格納先となるダミーではなく、Meta / LookAt / FirstPerson を
    /// VRC AvatarDescriptor から推定して反映する。表情 (viseme/blink/look) は
    /// 生成しない (変換後アバターは VRCAvatar 駆動で実行時未使用のため)。
    /// </summary>
    internal static class Vrm10ObjectBuilder
    {
        internal const string OutputFolder = "Assets/VRChatAvatarTransfer/Transferred";

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

            // 表情 / 目線 (viseme・blink・look) 情報は生成しない。変換後アバターは
            // VRCAvatar で駆動され Vrm10Object の Expression は実行時未使用のため。
            // lipsync は VRCLipSyncConverter が VRCAvatar へ直接移植する。
            PopulateMeta(asset.Meta, avatarRoot);
            PopulateLookAt(asset.LookAt, avatarRoot, freshlyCreated);
            PopulateFirstPerson(asset.FirstPerson, avatarRoot, freshlyCreated);

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

        internal static string MakeFileSafe(string name)
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

        internal static void EnsureFolder(string path)
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

    }
}
