// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Lilium.LiveStudio.Editor;

namespace Lilium.VRChatAvatarTransfer.Editor
{
    /// <summary>
    /// Builds a standalone avatar-prop object (a sub-hierarchy + the AnimatorController that
    /// drives it) into its own prefab, then optionally exports it as a <c>*.prop.lsb</c> bundle.
    /// Laid out like <see cref="VRChatAvatarTransferWindow"/>: source → verification → convert →
    /// converted prefab → export → result.
    /// </summary>
    internal class VRChatPropBuilderWindow : EditorWindow
    {
        private const string MenuPath = "Window/VRChat Avatar Transfer/VRChat Prop Builder";

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<VRChatPropBuilderWindow>("VRChat Prop Builder");
            window.minSize = new Vector2(420, 320);
            window.Show();
        }

        private enum Status { Ok, Warning, Error, Info }

        private struct Item
        {
            public Status status;
            public string label;
        }

        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private RuntimeAnimatorController controller;
        [SerializeField] private GameObject convertedPrefab;
        private readonly List<Item> items = new List<Item>();
        private readonly List<Item> resultItems = new List<Item>();
        private bool hasResult;
        private bool canConvert;

        private static readonly Color kOkDotColor = new Color(0.36f, 0.78f, 0.36f);
        private static readonly Color kErrorDotColor = new Color(0.85f, 0.33f, 0.33f);
        private static readonly Color kOffDotColor = new Color(0.55f, 0.55f, 0.55f);
        private static readonly GUIContent kDotContent = new GUIContent("●");

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
            sourcePrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Source Object", "アバター配下のシーンオブジェクト、または単体プレハブ。"),
                sourcePrefab, typeof(GameObject), allowSceneObjects: true);
            controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                new GUIContent("Source Controller", "このオブジェクトを駆動する AnimatorController。"),
                controller, typeof(RuntimeAnimatorController), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                resultItems.Clear();
                hasResult = false;
                Verify();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Verification", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                if (items.Count == 0)
                    EditorGUILayout.LabelField("Set a source object and controller.", EditorStyles.miniLabel);
                else
                    foreach (var item in items) DrawItem(item);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!canConvert))
            {
                if (GUILayout.Button("Convert", GUILayout.Height(28)))
                    DoConvert();
            }

            EditorGUILayout.Space();
            convertedPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Converted Prefab", "Convert 後に自動設定。ここにプレハブを置いて .prop.lsb 化もできる。"),
                convertedPrefab, typeof(GameObject), allowSceneObjects: false);

            using (new EditorGUI.DisabledScope(convertedPrefab == null))
            {
                if (GUILayout.Button("Export as Prop Bundle (.prop.lsb)", GUILayout.Height(28)))
                    DoExportPropBundle();
            }

            if (hasResult)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Conversion Result", EditorStyles.boldLabel);
                using (new EditorGUI.IndentLevelScope())
                    foreach (var item in resultItems) DrawItem(item);
            }
        }

        private static void DrawItem(Item item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool ok = item.status == Status.Ok;
                bool error = item.status == Status.Error;
                DotStyle.normal.textColor = ok ? kOkDotColor : error ? kErrorDotColor : kOffDotColor;
                DotStyle.fontSize = (ok || error) ? 0 : 8;
                GUILayout.Label(kDotContent, DotStyle, GUILayout.Width(18), GUILayout.Height(18));
                GUILayout.Label(item.label, GUILayout.Height(18));
            }
        }

        private void Verify()
        {
            items.Clear();
            canConvert = false;

            if (sourcePrefab == null && controller == null) return;

            // 1. Source object
            bool hasSource = sourcePrefab != null;
            items.Add(new Item
            {
                status = hasSource ? Status.Ok : Status.Error,
                label = hasSource ? $"Source object: {sourcePrefab.name}" : "Source object not set",
            });

            // 2. Controller (must be AnimatorController to clone)
            bool hasController = controller != null;
            bool isAnimatorController = controller is AnimatorController;
            if (!hasController)
                items.Add(new Item { status = Status.Error, label = "Controller not set" });
            else if (!isAnimatorController)
                items.Add(new Item { status = Status.Error, label = "Controller must be an AnimatorController (not an override controller)" });
            else
                items.Add(new Item { status = Status.Ok, label = $"Controller: {controller.name}" });

            // 3. Derivable binding prefix (resolves the controller's clips against the source hierarchy)
            if (hasSource && isAnimatorController)
            {
                string prefix = VRChatPropBuilder.DerivePrefix(sourcePrefab.transform, controller);
                if (prefix == null)
                {
                    items.Add(new Item
                    {
                        status = Status.Error,
                        label = "Cannot resolve clip bindings against this object (wrong object/controller pair?)",
                    });
                }
                else
                {
                    items.Add(new Item
                    {
                        status = Status.Ok,
                        label = prefix.Length == 0
                            ? "Binding prefix: (none — clips already relative)"
                            : $"Binding prefix to strip: '{prefix}/'",
                    });
                    canConvert = true;
                }
            }
        }

        private void DoConvert()
        {
            var result = VRChatPropBuilder.Build(sourcePrefab.transform, controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (result.success && !string.IsNullOrEmpty(result.prefabPath))
            {
                convertedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.prefabPath);
                if (convertedPrefab != null) EditorGUIUtility.PingObject(convertedPrefab);
            }

            Verify();
            BuildResultItems(result);
        }

        private void BuildResultItems(VRChatPropBuilder.BuildResult result)
        {
            resultItems.Clear();
            hasResult = result.success;
            if (!result.success) return;

            resultItems.Add(new Item { status = Status.Info, label = $"Created prefab: {Path.GetFileName(result.prefabPath)}" });
            resultItems.Add(new Item
            {
                status = Status.Info,
                label = result.prefix.Length == 0 ? "Binding prefix: (none)" : $"Stripped prefix: '{result.prefix}/'",
            });
            resultItems.Add(new Item { status = Status.Info, label = $"Rebased clips: {result.clipCount}" });
            resultItems.Add(new Item { status = Status.Info, label = $"Bridged parameters: {result.parameterCount}" });
        }

        private void DoExportPropBundle()
        {
            if (convertedPrefab == null) return;
            var assetPath = AssetDatabase.GetAssetPath(convertedPrefab);
            if (string.IsNullOrEmpty(assetPath))
            {
                VRChatAvatarTransferLog.Error("Converted prefab has no asset path.");
                return;
            }

            const string kExtension = ".prop.lsb";
            var defaultName = $"{Path.GetFileNameWithoutExtension(assetPath)}{kExtension}";
            var savePath = EditorUtility.SaveFilePanel("Export Prop Bundle", "", defaultName, "lsb");
            if (string.IsNullOrEmpty(savePath)) return;

            if (!savePath.EndsWith(kExtension, System.StringComparison.OrdinalIgnoreCase))
            {
                savePath = Path.ChangeExtension(savePath, null);
                if (savePath.EndsWith(".prop", System.StringComparison.OrdinalIgnoreCase))
                    savePath = savePath.Substring(0, savePath.Length - ".prop".Length);
                savePath += kExtension;
            }

            PropBundleExporter.Export(assetPath, savePath);
        }
    }
}
