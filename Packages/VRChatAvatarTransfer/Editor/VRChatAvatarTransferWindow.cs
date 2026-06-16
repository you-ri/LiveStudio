// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal class VRChatAvatarTransferWindow : EditorWindow
    {
        private const string MenuPath = "Window/VRChat Avatar Transfer/Transfer";

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<VRChatAvatarTransferWindow>("VRChat Avatar Transfer");
            window.minSize = new Vector2(420, 320);
            window.Show();
        }

        private enum Status { Ok, Warning, Error, Info }

        private struct Item
        {
            public Status status;
            public string label;
        }

        [SerializeField] private GameObject avatarPrefab;
        [SerializeField] private GameObject convertedPrefab;
        [SerializeField] private float gravityCoefficient = 1f;
        [SerializeField] private float stiffnessCoefficient = 1f;
        private readonly List<Item> items = new List<Item>();
        private readonly List<Item> resultItems = new List<Item>();
        private bool hasResult;
        private bool canConvert;
        private string outputPath;

        private static readonly Color kOkDotColor = new Color(0.36f, 0.78f, 0.36f);   // green
        private static readonly Color kErrorDotColor = new Color(0.85f, 0.33f, 0.33f); // red
        private static readonly Color kOffDotColor = new Color(0.55f, 0.55f, 0.55f); // gray
        private static readonly GUIContent kDotContent = new GUIContent("●");    // ●

        private static GUIStyle dotStyle;
        private static GUIStyle DotStyle => dotStyle ??= new GUIStyle(EditorStyles.label)
        {
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 4, 0, 0),
            alignment = TextAnchor.MiddleCenter,
        };

        private void OnEnable()
        {
            Verify();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            avatarPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("VRChat Avatar Prefab", "Drop a VRChat avatar prefab asset here."),
                avatarPrefab,
                typeof(GameObject),
                allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                // 対象 prefab が変わったら前回の変換結果を破棄する。
                resultItems.Clear();
                hasResult = false;
                Verify();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Verification", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                if (items.Count == 0)
                {
                    EditorGUILayout.LabelField("Drop a prefab to verify.", EditorStyles.miniLabel);
                }
                else
                {
                    foreach (var item in items)
                    {
                        DrawItem(item);
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Conversion Options", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                gravityCoefficient = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Gravity Coefficient", "Multiplier for the converted SpringBone gravity power. 1 = default mapping."),
                    gravityCoefficient));
                stiffnessCoefficient = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Stiffness Coefficient", "Multiplier for the converted SpringBone stiffness force. 1 = default mapping."),
                    stiffnessCoefficient));
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!canConvert))
            {
                if (GUILayout.Button("Convert", GUILayout.Height(28)))
                {
                    DoConvert();
                }
            }

            EditorGUILayout.Space();
            convertedPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Converted Prefab", "Set automatically after Convert. Drop a prefab here to export it as a Unity package."),
                convertedPrefab,
                typeof(GameObject),
                allowSceneObjects: false);

            using (new EditorGUI.DisabledScope(convertedPrefab == null))
            {
                if (GUILayout.Button("Export as Unity Package (.unitypackage)", GUILayout.Height(28)))
                {
                    DoExportPackage();
                }

                if (GUILayout.Button("Export as Avatar Bundle (.avatar.lsb)", GUILayout.Height(28)))
                {
                    DoExportLsAvatar();
                }
            }

            if (hasResult)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Conversion Result", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var item in resultItems)
                    {
                        DrawItem(item);
                    }
                }
            }
        }

        private static void DrawItem(Item item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                // Ok は緑丸、Error(NG) は赤丸でどちらも通常サイズで目立たせる。
                // Warning/Info は灰色丸で一回り小さくして「未処理」に見えないようにする。
                bool ok = item.status == Status.Ok;
                bool error = item.status == Status.Error;
                DotStyle.normal.textColor = ok ? kOkDotColor : error ? kErrorDotColor : kOffDotColor;
                DotStyle.fontSize = (ok || error) ? 0 : 8; // 0 = デフォルトサイズ
                GUILayout.Label(kDotContent, DotStyle, GUILayout.Width(18), GUILayout.Height(18));
                GUILayout.Label(item.label, GUILayout.Height(18));
            }
        }

        private void Verify()
        {
            items.Clear();
            // 変換結果は対象 prefab が変わると無効になるためここでは消さず、
            // ClearResult() を介してプレハブ変更時のみクリアする。
            canConvert = false;
            outputPath = null;

            if (avatarPrefab == null) return;

            // 1. Prefab asset (required)
            var assetPath = AssetDatabase.GetAssetPath(avatarPrefab);
            var prefabType = PrefabUtility.GetPrefabAssetType(avatarPrefab);
            bool isPrefab = !string.IsNullOrEmpty(assetPath)
                && prefabType != PrefabAssetType.NotAPrefab
                && prefabType != PrefabAssetType.MissingAsset;
            items.Add(new Item
            {
                status = isPrefab ? Status.Ok : Status.Error,
                label = isPrefab ? $"Prefab asset ({prefabType})" : "Not a prefab asset"
            });
            if (!isPrefab) return;

            // 2. VRCAvatarDescriptor on root (required)
            var desc = avatarPrefab.GetComponent<VRCAvatarDescriptor>();
            bool hasDesc = desc != null;
            items.Add(new Item
            {
                status = hasDesc ? Status.Ok : Status.Error,
                label = hasDesc ? "VRCAvatarDescriptor present" : "VRCAvatarDescriptor missing on root"
            });

            // 3. Animator on root (required) + 4. Humanoid (required)
            var animator = avatarPrefab.GetComponent<Animator>();
            bool hasAnimator = animator != null;
            bool isHumanoid = hasAnimator && animator.isHuman;
            if (!hasAnimator)
            {
                items.Add(new Item { status = Status.Error, label = "Animator missing on root" });
            }
            else if (!isHumanoid)
            {
                items.Add(new Item { status = Status.Error, label = "Animator is not Humanoid" });
            }
            else
            {
                items.Add(new Item { status = Status.Ok, label = "Animator (Humanoid)" });
            }

            // FX AnimatorController (informational)
            string fxLabel = "FX AnimatorController: (none)";
            Status fxStatus = Status.Info;
            RuntimeAnimatorController fxController = null;
            if (hasDesc && desc.baseAnimationLayers != null)
            {
                foreach (var layer in desc.baseAnimationLayers)
                {
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                    {
                        if (layer.animatorController != null)
                        {
                            fxController = layer.animatorController;
                            fxLabel = $"FX AnimatorController: {fxController.name}";
                            fxStatus = Status.Ok;
                        }
                        break;
                    }
                }
            }
            items.Add(new Item { status = fxStatus, label = fxLabel });

            canConvert = isPrefab && hasDesc && hasAnimator && isHumanoid;

            var safeName = Vrm10ObjectBuilder.MakeFileSafe(Path.GetFileNameWithoutExtension(assetPath));
            outputPath = $"{Vrm10ObjectBuilder.OutputFolder}/{safeName}.prefab";
        }

        private void DoConvert()
        {
            var assetPath = AssetDatabase.GetAssetPath(avatarPrefab);
            if (string.IsNullOrEmpty(assetPath)) return;

            Vrm10ObjectBuilder.EnsureFolder(Vrm10ObjectBuilder.OutputFolder);
            var options = new PhysBoneToSpringBoneConverter.ConversionOptions(
                Mathf.Max(0f, gravityCoefficient), Mathf.Max(0f, stiffnessCoefficient));
            var result = PrefabAssetConverter.Convert(assetPath, options);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (result.success && !string.IsNullOrEmpty(outputPath))
            {
                var converted = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
                if (converted != null)
                {
                    convertedPrefab = converted;
                    EditorGUIUtility.PingObject(converted);
                }
            }
            // Verify() は items のみ作り直すので、変換結果はその後で構築する。
            Verify();
            BuildResultItems(result);
        }

        private void BuildResultItems(PrefabAssetConverter.ConvertResult result)
        {
            resultItems.Clear();
            hasResult = result.success;
            if (!result.success) return;

            // 採用した IAvatar ランタイムコンポーネント
            resultItems.Add(new Item
            {
                status = Status.Info,
                label = result.usesVRCFTAvatar
                    ? "Runtime: VRCFTAvatar (FT/v2/* detected)"
                    : "Runtime: VRCAvatar"
            });

            // PhysBone / Constraint の変換数
            resultItems.Add(new Item { status = Status.Info, label = $"Converted PhysBones: {result.physBonesConverted}" });
            resultItems.Add(new Item { status = Status.Info, label = $"Converted PhysBone colliders: {result.physCollidersConverted}" });
            resultItems.Add(new Item { status = Status.Info, label = $"Converted VRC Constraints: {result.vrcConstraintsConverted}" });

            // 削除したコンポーネント数
            resultItems.Add(new Item { status = Status.Info, label = $"Removed VRChat components: {result.vrchatComponentsRemoved}" });
            resultItems.Add(new Item { status = Status.Info, label = $"Removed editor-only components: {result.editorOnlyRemoved}" });
            resultItems.Add(new Item { status = Status.Info, label = $"Removed missing scripts: {result.missingScriptsRemoved}" });

            // AnimatorController 内の変換結果
            if (result.fxControllerApplied)
            {
                resultItems.Add(new Item { status = Status.Info, label = $"Converted parameter drivers: {result.parameterDriversConverted}" });
                resultItems.Add(new Item { status = Status.Info, label = $"Converted tracking controls: {result.trackingControlsConverted}" });
            }
            else
            {
                resultItems.Add(new Item { status = Status.Info, label = "FX AnimatorController: not applied" });
            }
        }

        private void DoExportPackage()
        {
            if (convertedPrefab == null) return;
            var assetPath = AssetDatabase.GetAssetPath(convertedPrefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                VRChatAvatarTransferLog.Error("Converted prefab has no asset path.");
                return;
            }

            var defaultName = $"{Path.GetFileNameWithoutExtension(assetPath)}.unitypackage";
            var savePath = EditorUtility.SaveFilePanel(
                "Export Package",
                "",
                defaultName,
                "unitypackage");
            if (string.IsNullOrEmpty(savePath)) return;

            var deps = AssetDatabase.GetDependencies(assetPath, recursive: true);
            var filtered = new List<string>();
            int skipped = 0;
            foreach (var dep in deps)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                if (dep.StartsWith("Assets/", System.StringComparison.Ordinal))
                {
                    filtered.Add(dep);
                }
                else
                {
                    skipped++;
                }
            }

            AssetDatabase.ExportPackage(filtered.ToArray(), savePath, ExportPackageOptions.Default);
            VRChatAvatarTransferLog.Info(
                $"Exported {filtered.Count} asset(s) to '{savePath}' (excluded {skipped} dependency outside 'Assets/').");
        }

        private void DoExportLsAvatar()
        {
            if (convertedPrefab == null) return;
            var assetPath = AssetDatabase.GetAssetPath(convertedPrefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                VRChatAvatarTransferLog.Error("Converted prefab has no asset path.");
                return;
            }

            const string kExtension = ".avatar.lsb";
            var defaultName = $"{Path.GetFileNameWithoutExtension(assetPath)}{kExtension}";
            var savePath = EditorUtility.SaveFilePanel(
                "Export Avatar Bundle",
                "",
                defaultName,
                "lsb");
            if (string.IsNullOrEmpty(savePath)) return;

            // SaveFilePanel は最終拡張子しか扱えないため、複合サフィックスを保証する。
            if (!savePath.EndsWith(kExtension, System.StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, null);
                if (savePath.EndsWith(".avatar", System.StringComparison.OrdinalIgnoreCase))
                {
                    savePath = savePath.Substring(0, savePath.Length - ".avatar".Length);
                }
                savePath += kExtension;
            }

            LsAvatarExporter.Export(assetPath, savePath);
        }
    }
}
