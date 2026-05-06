using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;

namespace Lilium.LiveStudio.Editor
{
    [CustomEditor(typeof(AvatarExpression))]
    public class AvatarExpressionEditor : UnityEditor.Editor
    {
        private AvatarExpression _target;
        private bool _showExpressionList = true;
        private Vector2 _expressionScrollPosition;

        // 簡易バインド追加用の状態変数
        private bool _isRebindingInProgress = false;
        private string _rebindingExpressionName = "";

        // Editor側での表情ON/OFF状態管理用辞書
        private Dictionary<FacialKey, bool> _editorExpressionStates = new Dictionary<FacialKey, bool>();

        // Repaint最適化用
        private double _lastRepaintTime = 0;
        private const double kRepaintInterval = 0.1; // 0.1秒間隔でRepaint
        
        void OnEnable()
        {
            _target = (AvatarExpression)target;
            EditorApplication.update += OnEditorUpdate;
        }
        
        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }
        
        private void OnEditorUpdate()
        {
            if (_target == null) return;

            // 一定間隔でのみRepaintを実行(UI再構築タイミングの競合を回避)
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastRepaintTime >= kRepaintInterval)
            {
                _lastRepaintTime = currentTime;
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            // デフォルトのインスペクターを描画
            DrawDefaultInspector();
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Expression Binding Control", EditorStyles.boldLabel);
            
            if (_target == null) return;
            
            DrawExpressionListSection();
            EditorGUILayout.Space();

            DrawControlButtons();
        }
        
        private void DrawExpressionListSection()
        {
            _showExpressionList = EditorGUILayout.Foldout(_showExpressionList, "Available Expressions", true);
            if (!_showExpressionList) return;
            
            EditorGUI.indentLevel++;
            
            var expressions = GetAvailableExpressions();
            if (expressions == null || expressions.Length == 0)
            {
                EditorGUI.indentLevel--;
                return;
            }
            
            // スクロールビューで表情リストを表示
            _expressionScrollPosition = EditorGUILayout.BeginScrollView(_expressionScrollPosition, GUILayout.MaxHeight(200));
            
            foreach (var expression in expressions)
            {
                DrawExpressionItem(expression);
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUI.indentLevel--;
        }
        
        private void DrawExpressionItem(FacialKey expression)
        {
            EditorGUILayout.BeginHorizontal();
            
            // 表情名を表示
            EditorGUILayout.LabelField(expression.name, GUILayout.Width(120));
            
            // バインディング数を表示（リアルタイム更新）
            var bindings = GetBindingsForExpression(expression);
            string bindingCountText = $"Bindings: {bindings.Count}";
            if (bindings.Count > 0)
            {
                // バインディングが存在する場合は緑色で強調表示
                var originalColor = GUI.color;
                GUI.color = Color.green;
                EditorGUILayout.LabelField(bindingCountText, GUILayout.Width(80));
                GUI.color = originalColor;
            }
            else
            {
                EditorGUILayout.LabelField(bindingCountText, GUILayout.Width(80));
            }
            
            // 現在のウェイト値を表示（常に表示してレイアウト固定）
            float weight = Application.isPlaying ? GetCurrentWeight(expression) : 0f;
            EditorGUILayout.LabelField($"Weight: {weight:F3}", GUILayout.Width(80));
            
            // プログレスバーでウェイト値を視覚化（常に表示）
            Rect progressRect = GUILayoutUtility.GetRect(60, 16);
            EditorGUI.ProgressBar(progressRect, weight, "");
            
            // ON/OFFボタン（辞書の状態を参照）
            bool editorState = GetEditorExpressionState(expression);
            string buttonText = editorState ? "Reset" : "ON";
            
            // PlayMode中は動作が異なることを示すツールチップ
            string tooltip = Application.isPlaying 
                ? (editorState ? "Reset expression to allow runtime control" : "Force expression ON")
                : (editorState ? "Turn expression OFF" : "Turn expression ON");
            
            if (GUILayout.Button(new GUIContent(buttonText, tooltip), GUILayout.Width(40)))
            {
                ToggleExpression(expression);
            }
            
            // Add Bindボタン（Editor/PlayMode両対応）
            bool isThisRebinding = _isRebindingInProgress && _rebindingExpressionName == expression.name;
            GUI.enabled = !_isRebindingInProgress || isThisRebinding;
            
            string bindButtonText = isThisRebinding ? "Binding..." : "Add Bind";
            string bindButtonTooltip = Application.isPlaying 
                ? "Add key binding (PlayMode - low-level input detection)"
                : "Add key binding (EditorMode - direct input detection)";
            
            if (GUILayout.Button(new GUIContent(bindButtonText, bindButtonTooltip), GUILayout.Width(70)))
            {
                if (!isThisRebinding)
                {
                    StartInteractiveRebinding(expression);
                }
            }
            
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            // バインディング情報を表示
            if (bindings.Count > 0)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < bindings.Count; i++)
                {
                    DrawBindingInfo(bindings[i], i, expression);
                }
                EditorGUI.indentLevel--;
            }
        }
        
        private void DrawBindingInfo(InputBinding binding, int bindingIndex, FacialKey expression)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(20, false);
            
            // 入力デバイス情報
            string displayName = InputControlPath.ToHumanReadableString(binding.effectivePath, InputControlPath.HumanReadableStringOptions.UseShortNames);
            if (string.IsNullOrEmpty(displayName))
                displayName = binding.effectivePath;
            EditorGUILayout.LabelField($"• {displayName}", GUILayout.Width(150));
            
            // PlayMode中は現在の入力値も表示
            if (Application.isPlaying)
            {
                // AvatarInputからInputActionMapを取得して入力値を確認
                var inputActionsController = _target.GetComponent<AvatarInput>();
                if (inputActionsController?.inputActionMap != null)
                {
                    string actionName = "Expression." + expression.name;
                    var action = inputActionsController.inputActionMap.FindAction(actionName);
                    if (action != null)
                    {
                        float inputValue = action.ReadValue<float>();
                        bool isActive = inputValue > 0.5f;
                        EditorGUILayout.LabelField($"Input: {inputValue:F3}", GUILayout.Width(80));
                        EditorGUILayout.LabelField($"Active: {(isActive ? "Yes" : "No")}", GUILayout.Width(60));
                    }
                }
            }
            
            // 削除ボタン
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                RemoveBinding(expression.name, bindingIndex);
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        
        private void DrawControlButtons()
        {
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Show All Bindings"))
            {
                _target.ShowExpressionBindings();
            }

            
            EditorGUILayout.EndHorizontal();

            // 簡易バインド追加UI
            DrawQuickBindUI();
        }
        
        private FacialKey[] GetAvailableExpressions()
        {
            if (_target == null) return new FacialKey[0];
            var expressions = _target.GetAvailableExpressions();
            return expressions ?? new FacialKey[0];
        }
        
        private List<InputBinding> GetBindingsForExpression(FacialKey expression)
        {
            if (_target == null) return new List<InputBinding>();
            
            // AvatarInputからInputActionMapを取得
            var inputActionsController = _target.GetComponent<AvatarInput>();
            if (inputActionsController?.inputActionMap == null) return new List<InputBinding>();
            
            // Expression.{expressionName}の形式でアクションを検索
            string actionName = "Expression." + expression.name;
            var action = inputActionsController.inputActionMap.FindAction(actionName);
            
            if (action != null)
            {
                return action.bindings.ToList();
            }
            
            return new List<InputBinding>();
        }
        
        private float GetCurrentWeight(FacialKey expression)
        {
            if (_target == null) return 0f;
            return _target.GetExpressionWeight(expression);
        }
        
        /// <summary>
        /// Editor側での表情状態を取得
        /// </summary>
        private bool GetEditorExpressionState(FacialKey expression)
        {
            return _editorExpressionStates.ContainsKey(expression) && _editorExpressionStates[expression];
        }
        
        /// <summary>
        /// Editor側での表情状態を設定
        /// </summary>
        private void SetEditorExpressionState(FacialKey expression, bool state)
        {
            _editorExpressionStates[expression] = state;
        }
        
        private void ToggleExpression(FacialKey expression)
        {
            if (_target == null) return;
            
            bool currentEditorState = GetEditorExpressionState(expression);
            
            if (!currentEditorState) // Editor状態: OFF -> ON
            {
                SetEditorExpressionState(expression, true);
                _target.SetExpressionWeight(expression, 1f);
            }
            else // Editor状態: ON -> Reset (OFF)
            {
                SetEditorExpressionState(expression, false);
                // PlayMode、EditMode問わず重みを0にリセット
                _target.SetExpressionWeight(expression, 0f);
            }

            // UI更新(遅延実行で競合回避)
            if (!Application.isPlaying)
            {
                EditorApplication.delayCall += () => Repaint();
            }
        }
        
        
        private void RemoveBinding(string expressionName, int bindingIndex)
        {
            if (_target == null) return;
            
            // AvatarInputからInputActionMapを取得
            var inputActionsController = _target.GetComponent<AvatarInput>();
            if (inputActionsController?.inputActionMap == null) return;
            
            string actionName = "Expression." + expressionName;
            var action = inputActionsController.inputActionMap.FindAction(actionName);
            
            if (action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count)
            {
                
                // バインディングを正しい方法で削除
                action.ChangeBinding(bindingIndex).Erase();
                
                // InputActionMapの変更を強制更新
                bool wasEnabled = inputActionsController.inputActionMap.enabled;
                if (wasEnabled)
                {
                    inputActionsController.inputActionMap.Disable();
                    inputActionsController.inputActionMap.Enable();
                }
                
                // Unity Editorでの永続化のためにSetDirtyを呼び出し
#if UNITY_EDITOR
                if (inputActionsController.inputActionMap.asset != null)
                {
                    EditorUtility.SetDirty(inputActionsController.inputActionMap.asset);
                }
#endif
                

                // UI更新(遅延実行で競合回避)
                EditorApplication.delayCall += () => Repaint();
            }
            else
            {
                Debug.LogWarning($"[LiveStudio] Cannot remove binding: action='{actionName}', bindingIndex={bindingIndex}, bindingCount={action?.bindings.Count ?? -1}");
                EditorUtility.DisplayDialog("削除エラー", $"バインディングが見つかりません: {expressionName}[{bindingIndex}]", "OK");
            }
        }
        
        /// <summary>
        /// インタラクティブリバインドを開始（Editor/PlayMode両対応）
        /// </summary>
        private async void StartInteractiveRebinding(FacialKey expression)
        {
            if (_target == null)
            {
                Debug.LogError("[LiveStudio] AvatarExpression target is null!");
                return;
            }
            
            _isRebindingInProgress = true;
            _rebindingExpressionName = expression.name;
            
            try
            {
                bool success = await _target.StartExpressionBindingAsync(expression.name);

#if UNITY_EDITOR
                if (success)
                {
                    EditorUtility.SetDirty(_target);
                }
#endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LiveStudio] Error during PlayMode rebinding: {e.Message}");
            }

            _isRebindingInProgress = false;
            _rebindingExpressionName = "";

            // UI再構築タイミングの競合を回避するため遅延実行
            EditorApplication.delayCall += () => Repaint();
        }

        
        /// <summary>
        /// リバインド中のUIを描画
        /// </summary>
        private void DrawQuickBindUI()
        {
            if (!_isRebindingInProgress) return;
            
            EditorGUILayout.Space();
            string modeText = Application.isPlaying ? "PlayMode" : "EditorMode";
            EditorGUILayout.LabelField($"Interactive Rebinding in Progress ({modeText})", EditorStyles.boldLabel);
            
            EditorGUI.indentLevel++;
            
            // 対象の表情を表示
            EditorGUILayout.LabelField($"Target Expression: {_rebindingExpressionName}", EditorStyles.helpBox);
            
            // モード別のメッセージ
            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "PlayMode - Low-level input detection active\n" +
                    "• Press any key/button (ESC to cancel)\n" +
                    "• Works with keyboard and gamepad\n" +
                    "• Mouse input is automatically excluded", 
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "EditorMode - Direct input detection active\n" +
                    "• Press any key/button in Unity Editor window\n" +
                    "• Press ESC to cancel\n" +
                    "• Make sure Unity Editor window is focused", 
                    MessageType.Info);
            }
            
            // プログレス表示（視覚的フィードバック）
            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.ProgressBar(rect, Mathf.PingPong(Time.realtimeSinceStartup * 2f, 1f), "Waiting for input...");
            
            EditorGUI.indentLevel--;
        }
        
    }
}