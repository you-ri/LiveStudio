using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Lilium.LiveStudio.Virgo.Editor
{
    public class StudioHomeWindow : EditorWindow
    {
        private const string kShowValidatedPrefKey = "StudioHome.ShowValidated";

        private static readonly Color kValidColor = new Color(0.3f, 0.85f, 0.3f);
        private static readonly Color kErrorColor = new Color(0.9f, 0.2f, 0.2f);
        private static readonly Color kWarningColor = new Color(0.9f, 0.6f, 0.0f);
        private static readonly Color kDescriptionColor = new Color(0.67f, 0.67f, 0.67f);

        private Label _projectSettingsTitleLabel;
        private Toggle _showValidatedToggle;
        private VisualElement _inputSystemContainer;
        private Label _inputSystemTitleLabel;
        private Label _inputSystemIndicator;
        private Label _inputSystemStatusLabel;
        private Button _enableInputSystemButton;
        private VisualElement _enterPlayModeContainer;
        private Label _enterPlayModeTitleLabel;
        private Label _enterPlayModeIndicator;
        private Label _enterPlayModeStatusLabel;
        private Button _applyEnterPlayModeButton;
        private VisualElement _validationCard;
        private Label _validationIndicator;
        private DropdownField _buildConfigDropdown;
        private Button _buildStudioButton;

        private bool _isInputSystemValid;
        private bool _isEnterPlayModeValid;

        [MenuItem("Tools/Virgo Motion/Studio Home")]
        public static void ShowWindow()
        {
            var window = GetWindow<StudioHomeWindow>();
            window.Show();
        }

        private void OnEnable()
        {
            // エディター言語設定に応じてタイトルを更新
            titleContent = new GUIContent(StudioStrings.WindowTitle);
        }

        public void CreateGUI()
        {
            // タイトルを設定
            titleContent = new GUIContent(StudioStrings.WindowTitle);

            // UXMLファイルをロード
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/jp.lilium.livestudio.virgo/Editor/StudioHomeWindow/StudioHomeWindow.uxml");

            if (visualTree != null)
            {
                visualTree.CloneTree(rootVisualElement);
                _InitializeUI();
            }
            else
            {
                Debug.LogError("[Studio] Failed to load StudioHomeWindow.uxml");
            }
        }

        private void _InitializeUI()
        {
            // UI要素の参照を取得
            _projectSettingsTitleLabel = rootVisualElement.Q<Label>("project-settings-title");
            _showValidatedToggle = rootVisualElement.Q<Toggle>("show-validated-toggle");
            _inputSystemContainer = rootVisualElement.Q<VisualElement>("input-system-container");
            _inputSystemTitleLabel = rootVisualElement.Q<Label>("input-system-title");
            _inputSystemIndicator = rootVisualElement.Q<Label>("input-system-indicator");
            _inputSystemStatusLabel = rootVisualElement.Q<Label>("input-system-status");
            _enableInputSystemButton = rootVisualElement.Q<Button>("enable-input-system-button");
            _enterPlayModeContainer = rootVisualElement.Q<VisualElement>("enter-play-mode-container");
            _enterPlayModeTitleLabel = rootVisualElement.Q<Label>("enter-play-mode-title");
            _enterPlayModeIndicator = rootVisualElement.Q<Label>("enter-play-mode-indicator");
            _enterPlayModeStatusLabel = rootVisualElement.Q<Label>("enter-play-mode-status");
            _applyEnterPlayModeButton = rootVisualElement.Q<Button>("apply-enter-play-mode-button");
            _validationCard = rootVisualElement.Q<VisualElement>("validation-card");
            _validationIndicator = rootVisualElement.Q<Label>("validation-indicator");

            if (_showValidatedToggle != null)
            {
                _showValidatedToggle.value = EditorPrefs.GetBool(kShowValidatedPrefKey, false);
                _showValidatedToggle.RegisterValueChangedCallback(evt =>
                {
                    EditorPrefs.SetBool(kShowValidatedPrefKey, evt.newValue);
                    _ValidateInputSystem();
                    _ValidateEnterPlayModeSettings();
                    _UpdateSummary();
                });
            }

            if (_enableInputSystemButton != null)
            {
                _enableInputSystemButton.clicked += _EnableInputSystem;
            }

            if (_applyEnterPlayModeButton != null)
            {
                _applyEnterPlayModeButton.clicked += _ApplyEnterPlayModeRecommendedSettings;
            }

            // ビルドUI
            _buildConfigDropdown = rootVisualElement.Q<DropdownField>("build-config-dropdown");
            _buildStudioButton = rootVisualElement.Q<Button>("build-studio-button");

            if (_buildConfigDropdown != null)
            {
                _buildConfigDropdown.choices = new List<string> { "Development", "Release" };
                _buildConfigDropdown.index = 0;
            }

            if (_buildStudioButton != null)
            {
                _buildStudioButton.clicked += _BuildStudioApp;
            }

            // UI更新
            _UpdateUIText();

            // 初回検証
            _ValidateInputSystem();
            _ValidateEnterPlayModeSettings();
            _UpdateSummary();
        }

        private void _UpdateUIText()
        {
            // すべてのテキストを更新（L10n.Tr()により自動翻訳）
            if (_projectSettingsTitleLabel != null)
            {
                _projectSettingsTitleLabel.text = StudioStrings.ProjectSettingsValidation;
            }

            if (_showValidatedToggle != null)
            {
                _showValidatedToggle.label = StudioStrings.ShowValidated;
            }

            if (_inputSystemTitleLabel != null)
            {
                _inputSystemTitleLabel.text = StudioStrings.InputSystemSettings;
            }

            if (_enableInputSystemButton != null)
            {
                _enableInputSystemButton.text = StudioStrings.EnableInputSystem;
            }

            if (_enterPlayModeTitleLabel != null)
            {
                _enterPlayModeTitleLabel.text = StudioStrings.EnterPlayModeSettings;
            }

            if (_applyEnterPlayModeButton != null)
            {
                _applyEnterPlayModeButton.text = StudioStrings.ApplyRecommendedSettings;
            }
        }

        private void _ValidateInputSystem()
        {
            if (_inputSystemStatusLabel == null || _enableInputSystemButton == null)
            {
                return;
            }

            // SerializedObjectを使ってPlayerSettingsの実際の設定値をチェック（Unity 6/2021両対応）
            bool isInputSystemEnabled = false;

            try
            {
                // PlayerSettingsのSerializedObjectを取得
                var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (playerSettings != null && playerSettings.Length > 0)
                {
                    var serializedObject = new UnityEditor.SerializedObject(playerSettings[0]);
                    var activeInputHandlerProperty = serializedObject.FindProperty("activeInputHandler");

                    if (activeInputHandlerProperty != null)
                    {
                        // 0: InputManager (Old), 1: InputSystemPackage (New), 2: Both
                        int handlerValue = activeInputHandlerProperty.intValue;
                        isInputSystemEnabled = handlerValue == 1 || handlerValue == 2;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio] Failed to read Active Input Handler via SerializedObject: {ex.Message}");

                // フォールバック: リフレクションを試す（Unity 2021用）
#if !UNITY_6000_0_OR_NEWER
                try
                {
                    var playerSettingsType = typeof(PlayerSettings);
                    var activeInputHandlerProperty = playerSettingsType.GetProperty("activeInputHandler",
                        BindingFlags.Public | BindingFlags.Static);

                    if (activeInputHandlerProperty != null)
                    {
                        var activeInputHandlerValue = activeInputHandlerProperty.GetValue(null);
                        int handlerValue = (int)activeInputHandlerValue;
                        isInputSystemEnabled = handlerValue == 1 || handlerValue == 2;
                    }
                }
                catch (Exception reflectionEx)
                {
                    Debug.LogWarning($"[Studio] Reflection fallback also failed: {reflectionEx.Message}");
                }
#endif
            }

            // 結果に基づいてUIを更新
            _isInputSystemValid = isInputSystemEnabled;
            bool showValidated = _showValidatedToggle != null && _showValidatedToggle.value;

            if (isInputSystemEnabled)
            {
                // Input Systemが有効な場合（Input System Package または Both）
                if (showValidated)
                {
                    // トグルON: 検証済みステータスを表示
                    if (_inputSystemContainer != null)
                    {
                        _inputSystemContainer.style.display = DisplayStyle.Flex;
                    }

                    if (_inputSystemIndicator != null)
                    {
                        _inputSystemIndicator.style.color = kValidColor;
                        _inputSystemIndicator.style.display = DisplayStyle.Flex;
                    }

                    _inputSystemStatusLabel.text = StudioStrings.InputSystemEnabledDesc;
                    _inputSystemStatusLabel.style.color = kDescriptionColor;
                    _enableInputSystemButton.style.display = DisplayStyle.None;
                }
                else
                {
                    // トグルOFF: コンテナ全体を非表示
                    if (_inputSystemContainer != null)
                    {
                        _inputSystemContainer.style.display = DisplayStyle.None;
                    }
                }
            }
            else
            {
                // Input Systemが無効な場合（Old のみ）（エラー扱い）
                if (_inputSystemContainer != null)
                {
                    _inputSystemContainer.style.display = DisplayStyle.Flex;
                }

                if (_inputSystemIndicator != null)
                {
                    _inputSystemIndicator.style.color = kErrorColor;
                    _inputSystemIndicator.style.display = DisplayStyle.Flex;
                }

                _inputSystemStatusLabel.text = StudioStrings.InputSystemDisabled;
                _inputSystemStatusLabel.style.color = kErrorColor;
                _enableInputSystemButton.style.display = DisplayStyle.Flex;
            }
        }

        private void _EnableInputSystem()
        {
            // SerializedObjectを使ってPlayerSettingsのactiveInputHandlerを設定（Unity 6/2021両対応）
            try
            {
                var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                if (playerSettings != null && playerSettings.Length > 0)
                {
                    var serializedObject = new UnityEditor.SerializedObject(playerSettings[0]);
                    var activeInputHandlerProperty = serializedObject.FindProperty("activeInputHandler");

                    if (activeInputHandlerProperty != null)
                    {
                        // 2: Both に設定
                        activeInputHandlerProperty.intValue = 2;
                        serializedObject.ApplyModifiedProperties();

                        Debug.Log("[Studio] Enabled Input System (Active Input Handling set to 'Both')");

                        // UI再検証
                        _ValidateInputSystem();
                        _UpdateSummary();
                    }
                    else
                    {
                        Debug.LogWarning("[Studio] Could not find activeInputHandler property");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Studio] Failed to enable Input System: {ex.Message}");

                // フォールバック: Project Settingsを開く
                UnityEditor.SettingsService.OpenProjectSettings("Project/Player");
                EditorUtility.DisplayDialog(
                    "Input System Setup Required",
                    "Failed to automatically enable Input System.\n\n" +
                    "Please manually set 'Active Input Handling' to 'Both' in:\n\n" +
                    "Edit > Project Settings > Player > Other Settings > Active Input Handling\n\n" +
                    "The Project Settings window has been opened for you.",
                    "OK"
                );
            }
        }

        private void _ValidateEnterPlayModeSettings()
        {
            if (_enterPlayModeStatusLabel == null || _applyEnterPlayModeButton == null)
            {
                return;
            }

            // 推奨値: enterPlayModeOptionsEnabled = true かつ DisableDomainReload | DisableSceneReload
            const EnterPlayModeOptions kRecommendedOptions =
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;

            bool isEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            EnterPlayModeOptions currentOptions = EditorSettings.enterPlayModeOptions;

            bool isRecommended = isEnabled && (currentOptions == kRecommendedOptions);

            _isEnterPlayModeValid = isRecommended;
            bool showValidated = _showValidatedToggle != null && _showValidatedToggle.value;

            if (isRecommended)
            {
                // 推奨値になっている場合
                if (showValidated)
                {
                    // トグルON: 検証済みステータスを表示
                    if (_enterPlayModeContainer != null)
                    {
                        _enterPlayModeContainer.style.display = DisplayStyle.Flex;
                    }

                    if (_enterPlayModeIndicator != null)
                    {
                        _enterPlayModeIndicator.style.color = kValidColor;
                        _enterPlayModeIndicator.style.display = DisplayStyle.Flex;
                    }

                    _enterPlayModeStatusLabel.text = StudioStrings.EnterPlayModeValidDesc;
                    _enterPlayModeStatusLabel.style.color = kDescriptionColor;
                    _applyEnterPlayModeButton.style.display = DisplayStyle.None;
                }
                else
                {
                    // トグルOFF: コンテナ全体を非表示
                    if (_enterPlayModeContainer != null)
                    {
                        _enterPlayModeContainer.style.display = DisplayStyle.None;
                    }
                }
            }
            else
            {
                // 推奨値以外の場合
                if (_enterPlayModeContainer != null)
                {
                    _enterPlayModeContainer.style.display = DisplayStyle.Flex;
                }

                if (_enterPlayModeIndicator != null)
                {
                    _enterPlayModeIndicator.style.color = kWarningColor;
                    _enterPlayModeIndicator.style.display = DisplayStyle.Flex;
                }

                string currentStatusText = StudioStrings.GetEnterPlayModeStatusText(isEnabled, currentOptions);
                _enterPlayModeStatusLabel.text = currentStatusText;
                _enterPlayModeStatusLabel.style.color = kWarningColor;
                _applyEnterPlayModeButton.style.display = DisplayStyle.Flex;
            }
        }

        private void _ApplyEnterPlayModeRecommendedSettings()
        {
            const EnterPlayModeOptions kRecommendedOptions =
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = kRecommendedOptions;

            Debug.Log("[Studio] Applied recommended Enter Play Mode Settings: DisableDomainReload | DisableSceneReload");

            // UI再検証
            _ValidateEnterPlayModeSettings();
            _UpdateSummary();
        }

        private void _UpdateSummary()
        {
            bool allValid = _isInputSystemValid && _isEnterPlayModeValid;
            bool showValidated = _showValidatedToggle != null && _showValidatedToggle.value;

            // タイトル横のインジケーター表示
            if (_validationIndicator != null)
            {
                if (allValid)
                {
                    _validationIndicator.style.color = kValidColor;
                    _validationIndicator.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _validationIndicator.style.display = DisplayStyle.None;
                }
            }

            // show validated OFF かつ全項目正常 → カード全体を非表示
            if (_validationCard != null)
            {
                _validationCard.style.display = (!showValidated && allValid)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
        }

        private void _BuildStudioApp()
        {
            bool isDevelopment = _buildConfigDropdown != null && _buildConfigDropdown.index == 0;
            string buildType = isDevelopment ? "Development" : "Release";

            if (!EditorUtility.DisplayDialog(
                    "Build Studio App",
                    $"Build Studio App ({buildType})?",
                    "Build",
                    "Cancel"))
            {
                return;
            }

            if (isDevelopment)
            {
                BuildStudioApp.BuildDevelopmentFromEditor();
            }
            else
            {
                BuildStudioApp.BuildFromEditor();
            }
        }

    }
}
