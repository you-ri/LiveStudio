// Copyright (c) You-Ri, 2026
using UnityEditor;
using UnityEngine;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// Shared typography for the Live Class Asset windows.
    ///
    /// One font size throughout — hierarchy comes from weight and color only. Unity's mini
    /// styles (<c>miniLabel</c> / <c>miniBoldLabel</c>) render at a different size and baseline
    /// than the rest of an inspector row, which reads as a mismatched font once a few of them
    /// sit next to normal labels, so they are deliberately not used here.
    /// </summary>
    internal static class LiveClassAssetStyles
    {
        private static GUIStyle _paneHeader;
        private static GUIStyle _paneHeaderDetail;
        private static GUIStyle _rowTitle;
        private static GUIStyle _rowMeta;
        private static GUIStyle _memberTitle;

        private static Color kDimText => EditorGUIUtility.isProSkin
            ? new Color(0.62f, 0.62f, 0.62f)
            : new Color(0.35f, 0.35f, 0.35f);

        /// <summary>Title of a pane or popup header bar.</summary>
        public static GUIStyle paneHeader => _paneHeader ??= new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(2, 2, 0, 0),
        };

        /// <summary>Secondary text beside a pane title (namespace, unresolved marker).</summary>
        public static GUIStyle paneHeaderDetail => _paneHeaderDetail ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(2, 2, 0, 0),
            normal = { textColor = kDimText },
        };

        /// <summary>Main text of a list row.</summary>
        public static GUIStyle rowTitle => _rowTitle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
        };

        /// <summary>Trailing, right-aligned annotation of a list row (counts, states).</summary>
        public static GUIStyle rowMeta => _rowMeta ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = kDimText },
        };

        /// <summary>Header of one exposed member in the detail pane.</summary>
        public static GUIStyle memberTitle => _memberTitle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
        };
    }
}
