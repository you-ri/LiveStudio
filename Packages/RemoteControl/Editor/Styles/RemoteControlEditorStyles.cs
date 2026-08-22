// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Shared stylesheet of the Lilium Remote Control editor windows, plus the USS class names
    /// the code applies. The names live here rather than as literals at each call site so a
    /// rename stays a single edit across every window.
    ///
    /// Every window calls <see cref="Apply"/> from its <c>CreateGUI</c>, optionally naming its
    /// own sheet for the parts the shared vocabulary does not cover. Paths are relative to the
    /// package root, so a window never spells out where the package is installed.
    /// </summary>
    internal static class RemoteControlEditorStyles
    {
        /// <summary>Bold label - the title of a pane, a section or a card.</summary>
        public const string kTitle = "rc-title";

        /// <summary>Secondary text beside a title (namespaces, counts, states).</summary>
        public const string kSubtle = "rc-subtle";

        /// <summary>Clips overlong single-line text with an ellipsis instead of overflowing.</summary>
        public const string kEllipsis = "rc-ellipsis";

        public const string kRow = "rc-row";
        public const string kColumn = "rc-col";
        public const string kGrow = "rc-grow";
        public const string kFixed = "rc-fixed";

        /// <summary>Pushes whatever follows it to the far end of a row.</summary>
        public const string kSpacer = "rc-spacer";

        public const string kScroll = "rc-scroll";
        public const string kPane = "rc-pane";

        public const string kToolbarLabel = "rc-toolbar-label";
        public const string kToolbarField = "rc-toolbar-field";

        /// <summary>Bordered, faintly filled box that groups one entry of a list.</summary>
        public const string kCard = "rc-card";

        public const string kHelp = "rc-help";
        public const string kIconButton = "rc-icon-button";
        public const string kSeparatorHorizontal = "rc-separator-h";
        public const string kSeparatorVertical = "rc-separator-v";
        public const string kBorderTop = "rc-border-top";
        public const string kBorderBottom = "rc-border-bottom";
        public const string kBorderRight = "rc-border-right";

        public const string kSuccess = "rc-success";
        public const string kWarning = "rc-warning";
        public const string kDanger = "rc-danger";
        public const string kAccent = "rc-accent";

        private const string kPackageRoot = "Packages/jp.lilium.remotecontrol/";
        private const string kSharedStyleSheet = "Editor/Styles/RemoteControlEditor.uss";

        private static readonly Dictionary<string, StyleSheet> _loaded = new Dictionary<string, StyleSheet>();
        private static readonly HashSet<string> _missingReported = new HashSet<string>();

        /// <summary>
        /// Attaches the shared stylesheet to a window root, followed by the window's own sheet
        /// when one is named. Idempotent, so calling it again after a rebuild is harmless.
        /// </summary>
        /// <param name="root">The window's <c>rootVisualElement</c>.</param>
        /// <param name="windowStyleSheet">
        /// Package-relative path of the window's own sheet, e.g.
        /// <c>"Editor/ClassAssetWindow/LiveClassAssetWindow.uss"</c>. Null applies only the
        /// shared sheet.
        /// </param>
        public static void Apply(VisualElement root, string windowStyleSheet = null)
        {
            if (root == null) return;
            _Attach(root, kSharedStyleSheet);
            if (!string.IsNullOrEmpty(windowStyleSheet)) _Attach(root, windowStyleSheet);
        }

        private static void _Attach(VisualElement root, string packageRelativePath)
        {
            // Cached entries are Unity objects, so a reimport can leave a destroyed one behind.
            if (!_loaded.TryGetValue(packageRelativePath, out var sheet) || sheet == null)
            {
                sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(kPackageRoot + packageRelativePath);
                _loaded[packageRelativePath] = sheet;
            }
            if (sheet == null)
            {
                // Without the sheet the window still works, only unstyled - so warn once per
                // path instead of failing the whole editor window.
                if (_missingReported.Add(packageRelativePath))
                {
                    Debug.LogWarning($"[RemoteControl] Style sheet not found at \"{kPackageRoot + packageRelativePath}\".");
                }
                return;
            }
            if (!root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        }
    }
}
