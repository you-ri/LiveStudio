// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    internal class VRChatAvatarTransferWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Virgo Motion/VRChat Avatar Transfer/Open Converter Window";

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
        private readonly List<Item> items = new List<Item>();
        private bool canConvert;
        private string outputPath;

        private static GUIStyle iconStyle;
        private static GUIStyle IconStyle => iconStyle ??= new GUIStyle
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
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField(string.IsNullOrEmpty(outputPath) ? "—" : outputPath, EditorStyles.miniLabel);
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
                if (GUILayout.Button("Export", GUILayout.Height(28)))
                {
                    DoExportPackage();
                }
            }
        }

        private static GUIContent IconFor(Status s)
        {
            switch (s)
            {
                case Status.Ok:      return EditorGUIUtility.IconContent("TestPassed");
                case Status.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml");
                case Status.Error:   return EditorGUIUtility.IconContent("TestFailed");
                default:             return EditorGUIUtility.IconContent("console.infoicon.sml");
            }
        }

        private static void DrawItem(Item item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(IconFor(item.status), IconStyle, GUILayout.Width(18), GUILayout.Height(18));
                GUILayout.Label(item.label, GUILayout.Height(18));
            }
        }

        private void Verify()
        {
            items.Clear();
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

            // Informational counts
            int physBones = avatarPrefab.GetComponentsInChildren<VRCPhysBone>(true).Length;
            int physColliders = avatarPrefab.GetComponentsInChildren<VRCPhysBoneCollider>(true).Length;
            int constraints = avatarPrefab.GetComponentsInChildren<VRCConstraintBase>(true).Length;
            items.Add(new Item { status = Status.Info, label = $"PhysBone components: {physBones}" });
            items.Add(new Item { status = Status.Info, label = $"PhysBone colliders: {physColliders}" });
            items.Add(new Item { status = Status.Info, label = $"VRC Constraints: {constraints}" });

            // FX AnimatorController (informational)
            string fxLabel = "FX AnimatorController: (none)";
            Status fxStatus = Status.Info;
            if (hasDesc && desc.baseAnimationLayers != null)
            {
                foreach (var layer in desc.baseAnimationLayers)
                {
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX)
                    {
                        if (layer.animatorController != null)
                        {
                            fxLabel = $"FX AnimatorController: {layer.animatorController.name}";
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
            bool ok = PrefabAssetConverter.Convert(assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (ok && !string.IsNullOrEmpty(outputPath))
            {
                var converted = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
                if (converted != null)
                {
                    convertedPrefab = converted;
                    EditorGUIUtility.PingObject(converted);
                }
            }
            Verify();
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
    }
}
