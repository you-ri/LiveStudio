using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Lilium.LiveStudio.Editor
{
    [CustomEditor(typeof(AvatarExpression))]
    public class AvatarExpressionEditor : UnityEditor.Editor
    {
        private AvatarExpression _target;
        private bool _showExpressionList = true;
        private Vector2 _expressionScrollPosition;

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
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Expression Preview", EditorStyles.boldLabel);

            // キー割り当ては ActionManager (ActionSet -> SetExpressionAction) で管理する。
            // このインスペクターは利用可能な表情の確認とウェイトのプレビューのみを行う。
            EditorGUILayout.HelpBox(
                "Key bindings are managed by the Action system (Actions page / ActionManager).",
                MessageType.Info);

            if (_target == null) return;

            DrawExpressionListSection();
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

            // 現在のウェイト値を表示（常に表示してレイアウト固定）
            float weight = Application.isPlaying ? GetCurrentWeight(expression) : 0f;
            EditorGUILayout.LabelField($"Weight: {weight:F3}", GUILayout.Width(80));

            // プログレスバーでウェイト値を視覚化（常に表示）
            Rect progressRect = GUILayoutUtility.GetRect(60, 16);
            EditorGUI.ProgressBar(progressRect, weight, "");

            // ON/OFFボタン（辞書の状態を参照）
            bool editorState = GetEditorExpressionState(expression);
            string buttonText = editorState ? "Reset" : "ON";

            string tooltip = Application.isPlaying
                ? (editorState ? "Reset expression to allow runtime control" : "Force expression ON")
                : (editorState ? "Turn expression OFF" : "Turn expression ON");

            if (GUILayout.Button(new GUIContent(buttonText, tooltip), GUILayout.Width(40)))
            {
                ToggleExpression(expression);
            }

            EditorGUILayout.EndHorizontal();
        }

        private FacialKey[] GetAvailableExpressions()
        {
            if (_target == null) return new FacialKey[0];
            var expressions = _target.GetAvailableExpressions();
            return expressions ?? new FacialKey[0];
        }

        private float GetCurrentWeight(FacialKey expression)
        {
            if (_target == null) return 0f;
            return _target.GetExpressionWeight(expression);
        }

        private bool GetEditorExpressionState(FacialKey expression)
        {
            return _editorExpressionStates.ContainsKey(expression) && _editorExpressionStates[expression];
        }

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
                _target.SetExpressionWeight(expression, 0f);
            }

            if (!Application.isPlaying)
            {
                EditorApplication.delayCall += () => Repaint();
            }
        }
    }
}
