// Copyright (c) You-Ri, 2026
using UnityEngine.UIElements;

namespace Lilium.RemoteControl.Editor
{
    /// <summary>
    /// USS class names the Live Class Asset windows apply, and the entry point that attaches
    /// their stylesheets. The names live here rather than as literals at each call site so a
    /// rename stays a single edit across the three windows.
    ///
    /// Names that resolve to a <see cref="RemoteControlEditorStyles"/> constant are the shared
    /// vocabulary; the rest are declared in LiveClassAssetWindow.uss and mean nothing outside
    /// these windows.
    /// </summary>
    internal static class LiveClassAssetStyles
    {
        private const string kStyleSheet = "Editor/ClassAssetWindow/LiveClassAssetWindow.uss";

        // --- Shared vocabulary ---

        public const string kToolbarField = RemoteControlEditorStyles.kToolbarField;
        public const string kSpacer = RemoteControlEditorStyles.kSpacer;
        public const string kScroll = RemoteControlEditorStyles.kScroll;
        public const string kHelp = RemoteControlEditorStyles.kHelp;
        public const string kIconButton = RemoteControlEditorStyles.kIconButton;
        public const string kSeparator = RemoteControlEditorStyles.kSeparatorHorizontal;
        public const string kMember = RemoteControlEditorStyles.kCard;
        public const string kSubtle = RemoteControlEditorStyles.kSubtle;
        public const string kWarning = RemoteControlEditorStyles.kWarning;
        public const string kAccent = RemoteControlEditorStyles.kAccent;

        // --- Live Class Asset only ---

        public const string kHeader = "lca-header";
        public const string kHeaderRow = "lca-header-row";
        public const string kHeaderRowField = "lca-header-row__field";
        public const string kHeaderRowAction = "lca-header-row__action";
        public const string kPane = "lca-pane";
        public const string kPaneDivided = "lca-pane--divided";
        public const string kPaneHeader = "lca-pane-title";
        public const string kPaneHeaderDetail = "lca-pane-header-detail";
        public const string kEmpty = "lca-empty";
        public const string kDetail = "lca-detail";
        public const string kRowButton = "lca-row-button";
        public const string kClassRow = "lca-class-row";
        public const string kClassRowTitle = "lca-class-row__title";
        public const string kClassRowTitleUnresolved = "lca-class-row__title--unresolved";
        public const string kClassRowCount = "lca-class-row__count";
        public const string kMemberHeader = "lca-member__header";
        public const string kMemberTitle = "lca-member__title";
        public const string kMemberLane = "lca-member__lane";
        public const string kStateBudget = "lca-state-budget";
        public const string kMemberSectionDetail = "lca-member__section-detail";
        public const string kFooter = "lca-footer";
        public const string kFooterTitle = "lca-footer__title";
        public const string kBindingRow = "lca-binding-row";
        public const string kBindingRowField = "lca-binding-row__field";
        public const string kBindingRowState = "lca-binding-row__state";
        public const string kPopupRow = "lca-popup-row";
        public const string kPopupRowToggle = "lca-popup-row__toggle";
        public const string kPopupRowMeta = "lca-popup-row__meta";
        public const string kPopupRowTitle = "lca-popup-row__title";
        public const string kPopupRowTitleAdded = "lca-popup-row__title--added";
        public const string kPopupRowButton = "lca-popup-row__button";

        /// <summary>Attaches the shared and the window stylesheet to a window root (idempotent).</summary>
        public static void Apply(VisualElement root)
        {
            RemoteControlEditorStyles.Apply(root, kStyleSheet);
        }
    }
}
