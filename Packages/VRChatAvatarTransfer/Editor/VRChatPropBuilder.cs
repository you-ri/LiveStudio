// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Builds a standalone avatar-prop prefab from a sub-hierarchy of an avatar plus the
    /// AnimatorController that drives it. The prop carries its OWN Animator + controller, so
    /// at runtime it animates independently (its generic curves are not lost the way they are when
    /// two AnimatorControllers are layered into one PlayableGraph). A <c>AvatarProp</c> bridges
    /// the avatar's parameter values onto the prop each frame.
    ///
    /// The controller's clips bind to paths relative to the AVATAR root (e.g.
    /// <c>"Straw/Particle System"</c>); since the prop's new root is the sub-hierarchy root
    /// (<c>"Straw"</c>), every clip is cloned and its binding paths are rebased by stripping that
    /// prefix (<c>"Straw/Particle System"</c> → <c>"Particle System"</c>). Cloning the clips is
    /// required so the source avatar's shared clips are never modified.
    /// </summary>
    internal static class VRChatPropBuilder
    {
        private const string kOutputFolder = "Assets/VRCAT_GeneratedAssets/Objects";

        /// <summary>変換結果。<see cref="success"/> が false の場合は他フィールド未確定。</summary>
        public struct BuildResult
        {
            public bool success;
            public string prefabPath;   // 生成した prop プレハブのパス
            public string prefix;       // 剥がした binding 接頭辞 (例 "Straw")
            public int clipCount;       // rebase した clip 数
            public int parameterCount;  // controller の (非 Trigger) パラメータ数 = ブリッジ対象数
        }

        /// <summary>
        /// Builds the avatar-prop prefab from <paramref name="subRoot"/> (a scene child of an avatar
        /// or a standalone prefab root) plus the <paramref name="controller"/> that drives it.
        /// The binding prefix is auto-derived from the controller's clips, so no avatar ancestor is required.
        /// </summary>
        public static BuildResult Build(Transform subRoot, RuntimeAnimatorController controller)
        {
            if (subRoot == null)
            {
                VRChatAvatarTransferLog.Error("Prop build: source object is null.");
                return default;
            }
            if (controller == null)
            {
                VRChatAvatarTransferLog.Error("Prop build: controller is null.");
                return default;
            }

            // clip の binding はアバター root 相対 (例 "Straw/Particle System")。サブ階層 root を基準に
            // 解決して剥がすべき接頭辞 (例 "Straw") を自動判定する。アバター配下でも単体でも動く。
            string prefix = DerivePrefix(subRoot, controller);
            if (prefix == null)
            {
                VRChatAvatarTransferLog.Error(
                    $"Prop build: could not determine the binding prefix. The controller's clips must target children of '{subRoot.name}'.");
                return default;
            }

            _EnsureFolder(kOutputFolder);
            string safeName = Vrm10ObjectBuilder.MakeFileSafe(subRoot.name);

            // 1. サブ階層を独立コピー (元アバターは非破壊)。
            var objRoot = Object.Instantiate(subRoot.gameObject);
            objRoot.name = subRoot.name;
            objRoot.transform.localPosition = Vector3.zero;
            objRoot.transform.localRotation = Quaternion.identity;
            objRoot.transform.localScale = subRoot.localScale;

            try
            {
                // 2. controller を複製 (clip は共有参照のまま)。
                string ctrlPath = $"{kOutputFolder}/{safeName}.controller";
                _DeleteIfExists(ctrlPath);
                AnimatorController cloned =
                    controller is AnimatorController srcCtrl
                        ? AnimatorControllerCloner.CloneToPath(srcCtrl, ctrlPath)
                        : null;
                if (cloned == null)
                {
                    VRChatAvatarTransferLog.Error($"Prop build: failed to clone controller '{controller.name}'.");
                    Object.DestroyImmediate(objRoot);
                    return default;
                }

                // 3. 参照される各 clip を複製し binding path を rebase、複製 controller の参照を差し替える。
                int clipCount = _RebaseControllerClips(cloned, prefix, safeName);

                // 4. Animator + 複製 controller + AvatarProp を付与。
                var animator = objRoot.GetComponent<Animator>();
                if (animator == null) animator = objRoot.AddComponent<Animator>();
                animator.runtimeAnimatorController = cloned;
                animator.applyRootMotion = false;
                if (objRoot.GetComponent<Lilium.LiveStudio.AvatarProp>() == null)
                    objRoot.AddComponent<Lilium.LiveStudio.AvatarProp>();

                // 5. プレハブ保存。
                string prefabPath = $"{kOutputFolder}/{safeName}.prefab";
                _DeleteIfExists(prefabPath);
                var saved = PrefabUtility.SaveAsPrefabAsset(objRoot, prefabPath, out bool ok);
                Object.DestroyImmediate(objRoot);

                if (!ok || saved == null)
                {
                    VRChatAvatarTransferLog.Error($"Prop build: failed to save prefab '{prefabPath}'.");
                    return default;
                }

                AssetDatabase.SaveAssets();
                VRChatAvatarTransferLog.Info($"Prop build: created '{prefabPath}' (prefix stripped: '{prefix}/').");

                int paramCount = 0;
                foreach (var p in cloned.parameters)
                    if (p.type != AnimatorControllerParameterType.Trigger) paramCount++;

                return new BuildResult
                {
                    success = true,
                    prefabPath = prefabPath,
                    prefix = prefix,
                    clipCount = clipCount,
                    parameterCount = paramCount,
                };
            }
            catch (System.Exception ex)
            {
                if (objRoot != null) Object.DestroyImmediate(objRoot);
                VRChatAvatarTransferLog.Error($"Accessory build failed: {ex}");
                return default;
            }
        }

        // 複製 controller 内の全 Motion を辿り、AnimationClip を rebase 済みクローンへ差し替える。rebase 数を返す。
        static int _RebaseControllerClips(AnimatorController controller, string prefix, string safeName)
        {
            var clipMap = new Dictionary<AnimationClip, AnimationClip>();
            int clipIndex = 0;

            System.Func<Motion, Motion> remap = null;
            remap = (m) =>
            {
                if (m is AnimationClip clip)
                {
                    if (!clipMap.TryGetValue(clip, out var rebased))
                    {
                        string clipPath = $"{kOutputFolder}/{safeName}.clip{clipIndex++}.anim";
                        _DeleteIfExists(clipPath);
                        rebased = _RebaseClip(clip, prefix);
                        AssetDatabase.CreateAsset(rebased, clipPath);
                        clipMap[clip] = rebased;
                    }
                    return rebased;
                }
                if (m is BlendTree bt)
                {
                    var children = bt.children;
                    for (int i = 0; i < children.Length; i++)
                    {
                        children[i].motion = remap(children[i].motion);
                    }
                    bt.children = children;
                    return bt;
                }
                return m;
            };

            foreach (var layer in controller.layers)
            {
                _RemapStateMachine(layer.stateMachine, remap);
            }
            EditorUtility.SetDirty(controller);
            return clipMap.Count;
        }

        static void _RemapStateMachine(AnimatorStateMachine sm, System.Func<Motion, Motion> remap)
        {
            if (sm == null) return;
            foreach (var cs in sm.states)
            {
                if (cs.state != null)
                {
                    cs.state.motion = remap(cs.state.motion);
                }
            }
            foreach (var ssm in sm.stateMachines)
            {
                _RemapStateMachine(ssm.stateMachine, remap);
            }
        }

        // src の全カーブを binding path rebase しながら新規 clip へコピーする (元 clip は不変)。
        static AnimationClip _RebaseClip(AnimationClip src, string prefix)
        {
            var dst = new AnimationClip { name = src.name, frameRate = src.frameRate };
            var settings = AnimationUtility.GetAnimationClipSettings(src);
            AnimationUtility.SetAnimationClipSettings(dst, settings);

            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, b);
                var nb = b;
                nb.path = _StripPrefix(b.path, prefix);
                AnimationUtility.SetEditorCurve(dst, nb, curve);
            }
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(src))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(src, b);
                var nb = b;
                nb.path = _StripPrefix(b.path, prefix);
                AnimationUtility.SetObjectReferenceCurve(dst, nb, keys);
            }
            return dst;
        }

        // "Straw/Particle System" + prefix "Straw" → "Particle System"。"Straw" 自身 → ""。
        // prefix 配下でない path はサブ階層外を指すため、そのまま残す (警告)。
        static string _StripPrefix(string path, string prefix)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path == prefix) return string.Empty;
            string withSlash = prefix + "/";
            if (path.StartsWith(withSlash, System.StringComparison.Ordinal))
                return path.Substring(withSlash.Length);

            VRChatAvatarTransferLog.Warn(
                $"Prop build: binding '{path}' is outside sub-hierarchy '{prefix}'; left unchanged (won't drive the accessory).");
            return path;
        }

        /// <summary>
        /// controller の各 clip binding を subRoot 基準で解決し、剥がすべき接頭辞を判定する。
        /// 例: binding "Straw/Particle System" で subRoot 直下に "Particle System" があれば "Straw"。
        /// 最短一致を採用 (既に subRoot 相対なら "")。どの binding も解決できなければ null。
        /// Window の Verification プレビューからも呼ばれる。
        /// </summary>
        public static string DerivePrefix(Transform subRoot, RuntimeAnimatorController controller)
        {
            if (subRoot == null || controller == null) return null;
            foreach (var clip in controller.animationClips)
            {
                if (clip == null) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    var prefix = _ResolvePrefix(subRoot, b.path);
                    if (prefix != null) return prefix;
                }
                foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var prefix = _ResolvePrefix(subRoot, b.path);
                    if (prefix != null) return prefix;
                }
            }
            return null;
        }

        // path から先頭セグメントを順に取り除き、残りが subRoot 配下に解決できる最短の接頭辞を返す。
        // 残りが空 (path が subRoot 自身を指す) も解決成功とみなす。解決不能なら null。
        static string _ResolvePrefix(Transform subRoot, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var segs = path.Split('/');
            for (int cut = 0; cut <= segs.Length; cut++)
            {
                string remainder = cut >= segs.Length ? string.Empty : string.Join("/", segs, cut, segs.Length - cut);
                bool resolves = remainder.Length == 0 || subRoot.Find(remainder) != null;
                if (resolves) return string.Join("/", segs, 0, cut);
            }
            return null;
        }

        static void _EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            var leaf = Path.GetFileName(folder);
            _EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static void _DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
        }
    }
}
