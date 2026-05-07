using UnityEditor;

namespace Lilium.LiveStudio.Virgo.Editor
{
    /// <summary>
    /// Studio Editor用の多言語対応文字列定義
    /// Unity L10n.Tr()を使用してUnityエディターの言語設定に追従
    /// </summary>
    public static class StudioStrings
    {
        // ウィンドウタイトル
        public static readonly string WindowTitle = L10n.Tr("Studio Home");

        // プロジェクト設定検証
        public static readonly string ProjectSettingsValidation = L10n.Tr("Project Settings Validation");

        // Enter Play Mode Settings
        public static readonly string EnterPlayModeSettings = L10n.Tr("Enter Play Mode Settings");

        // Input System Settings
        public static readonly string InputSystemSettings = L10n.Tr("Input System");

        // Show Validated トグル
        public static readonly string ShowValidated = L10n.Tr("Show Validated");

        // 検証済みステータス
        public static readonly string ValidatedStatus = L10n.Tr("✓ Validated (Do not reload Domain or Scene)");
        public static readonly string InputSystemValidated = L10n.Tr("✓ Validated (Input System is enabled)");

        // 検証済み説明テキスト（インジケータードット横の説明）
        public static readonly string InputSystemEnabledDesc = L10n.Tr("Input System is enabled");
        public static readonly string EnterPlayModeValidDesc = L10n.Tr("Domain and Scene reload are disabled");

        // 警告メッセージフォーマット（{0}には現在のステータステキストが入る）
        public static readonly string WarningFormat = L10n.Tr("⚠ Warning: {0}\nRecommended: Enable \"Do not reload Domain or Scene\"");

        // エラーメッセージフォーマット（{0}には現在のステータステキストが入る）
        public static readonly string ErrorFormat = L10n.Tr("✗ Error: {0}\nInput System must be enabled for this project");

        // 推奨設定を適用ボタン
        public static readonly string ApplyRecommendedSettings = L10n.Tr("Apply Recommended Settings");

        // Input System有効化ボタン
        public static readonly string EnableInputSystem = L10n.Tr("Enable Input System");

        // 全検証済みサマリー
        public static readonly string AllSettingsValidated = L10n.Tr("✓ All settings are correctly configured.");

        // ステータスメッセージ
        public static readonly string EnterPlayModeDisabled = L10n.Tr("Enter Play Mode Options is disabled");
        public static readonly string BothReloadEnabled = L10n.Tr("Both Domain and Scene reload are enabled");
        public static readonly string DomainReloadEnabled = L10n.Tr("Domain reload is enabled");
        public static readonly string SceneReloadEnabled = L10n.Tr("Scene reload is enabled");
        public static readonly string UnknownConfiguration = L10n.Tr("Unknown configuration");
        public static readonly string InputSystemDisabled = L10n.Tr("Input System is disabled");

        /// <summary>
        /// Enter Play Mode の現在のステータステキストを取得
        /// </summary>
        public static string GetEnterPlayModeStatusText(bool isEnabled, EnterPlayModeOptions options)
        {
            if (!isEnabled)
            {
                return EnterPlayModeDisabled;
            }

            bool hasDomainReload = (options & EnterPlayModeOptions.DisableDomainReload) != 0;
            bool hasSceneReload = (options & EnterPlayModeOptions.DisableSceneReload) != 0;

            if (!hasDomainReload && !hasSceneReload)
            {
                return BothReloadEnabled;
            }
            else if (!hasDomainReload)
            {
                return DomainReloadEnabled;
            }
            else if (!hasSceneReload)
            {
                return SceneReloadEnabled;
            }

            return UnknownConfiguration;
        }
    }
}
