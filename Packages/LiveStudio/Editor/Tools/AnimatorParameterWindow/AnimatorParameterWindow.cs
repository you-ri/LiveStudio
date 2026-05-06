// Copyright (c) You-Ri, 2026
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    public sealed class AnimatorParameterWindow : EditorWindow
    {
        const double PlayModeRepaintInterval = 0.1;

        [SerializeField] Animator _animator;
        [SerializeField] AnimatorController _controller;
        [SerializeField] bool _lock;
        [SerializeField] string _filter = string.Empty;
        [SerializeField] Vector2 _scroll;

        double _lastRepaintTime;

        [MenuItem("Window/Lilium Live Studio/Animator Parameter")]
        public static void Open()
        {
            var window = GetWindow<AnimatorParameterWindow>();
            window.titleContent = new GUIContent("Animator Params");
            window.minSize = new Vector2(280f, 200f);
            window.Show();
        }

        void OnEnable()
        {
            Selection.selectionChanged += HandleSelectionChanged;
            EditorApplication.update += HandleEditorUpdate;
            HandleSelectionChanged();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= HandleSelectionChanged;
            EditorApplication.update -= HandleEditorUpdate;
        }

        void HandleSelectionChanged()
        {
            if (_lock) return;

            var obj = Selection.activeObject;
            if (obj == null) return;

            if (obj is GameObject go)
            {
                var animator = go.GetComponentInParent<Animator>();
                if (animator != null)
                {
                    _animator = animator;
                    _controller = null;
                    Repaint();
                    return;
                }
            }
            else if (obj is Animator animator)
            {
                _animator = animator;
                _controller = null;
                Repaint();
                return;
            }
            else if (obj is AnimatorController controller)
            {
                _controller = controller;
                _animator = null;
                Repaint();
            }
        }

        void HandleEditorUpdate()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_animator == null) return;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaintTime < PlayModeRepaintInterval) return;
            _lastRepaintTime = now;
            Repaint();
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawTargetFields();

            EditorGUILayout.Space(2f);

            var controller = ResolveController();
            if (controller == null)
            {
                EditorGUILayout.HelpBox("Animator または AnimatorController を選択してください。", MessageType.Info);
                return;
            }

            var parameters = controller.parameters;
            if (parameters == null || parameters.Length == 0)
            {
                EditorGUILayout.HelpBox("パラメータがありません。", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                bool useLive = _animator != null && EditorApplication.isPlaying && _animator.isActiveAndEnabled;
                int visibleIndex = 0;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (!MatchesFilter(p.name)) continue;
                    DrawParameterRow(controller, p, i, useLive, visibleIndex);
                    visibleIndex++;
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _lock = GUILayout.Toggle(_lock, "Lock", EditorStyles.toolbarButton, GUILayout.Width(48f));

                GUILayout.Space(4f);
                GUILayout.Label("Filter", GUILayout.Width(36f));
                _filter = GUILayout.TextField(_filter ?? string.Empty, EditorStyles.toolbarSearchField);

                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    _filter = string.Empty;
                    GUI.FocusControl(null);
                }
            }
        }

        void DrawTargetFields()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    var newAnim = (Animator)EditorGUILayout.ObjectField("Animator", _animator, typeof(Animator), true);
                    if (check.changed)
                    {
                        _animator = newAnim;
                        if (newAnim != null) _controller = null;
                    }
                }

                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    var newCtrl = (AnimatorController)EditorGUILayout.ObjectField("Controller", _controller, typeof(AnimatorController), false);
                    if (check.changed)
                    {
                        _controller = newCtrl;
                        if (newCtrl != null) _animator = null;
                    }
                }
            }
        }

        AnimatorController ResolveController()
        {
            if (_animator != null)
            {
                return ResolveControllerFromRuntime(_animator.runtimeAnimatorController);
            }
            return _controller;
        }

        static AnimatorController ResolveControllerFromRuntime(RuntimeAnimatorController runtime)
        {
            while (runtime is AnimatorOverrideController over)
            {
                runtime = over.runtimeAnimatorController;
                if (runtime == null) return null;
            }
            return runtime as AnimatorController;
        }

        bool MatchesFilter(string name)
        {
            if (string.IsNullOrEmpty(_filter)) return true;
            return name != null && name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static readonly Color RowColorEven = new Color(0f, 0f, 0f, 0.08f);
        static readonly Color RowColorOdd = new Color(1f, 1f, 1f, 0.03f);

        void DrawParameterRow(AnimatorController controller, AnimatorControllerParameter param, int index, bool useLive, int visibleIndex)
        {
            var rowRect = EditorGUILayout.BeginHorizontal();
            if (Event.current.type == EventType.Repaint && rowRect.width > 0f)
            {
                var color = (visibleIndex & 1) == 0 ? RowColorEven : RowColorOdd;
                EditorGUI.DrawRect(rowRect, color);
            }
            try
            {
                bool readOnly = useLive && _animator.IsParameterControlledByCurve(param.nameHash);
                EditorGUILayout.LabelField(param.name, GUILayout.MinWidth(80f));

                using (new EditorGUI.DisabledScope(readOnly))
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            DrawFloat(controller, param, index, useLive);
                            break;
                        case AnimatorControllerParameterType.Int:
                            DrawInt(controller, param, index, useLive);
                            break;
                        case AnimatorControllerParameterType.Bool:
                            DrawBool(controller, param, index, useLive);
                            break;
                        case AnimatorControllerParameterType.Trigger:
                            DrawTrigger(param, useLive && !readOnly);
                            break;
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawFloat(AnimatorController controller, AnimatorControllerParameter param, int index, bool useLive)
        {
            if (useLive)
            {
                float current = _animator.GetFloat(param.nameHash);
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    float next = DrawFloatSliderField(current);
                    if (check.changed) _animator.SetFloat(param.nameHash, next);
                }
            }
            else
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    float next = DrawFloatSliderField(param.defaultFloat);
                    if (check.changed) UpdateDefault(controller, index, p => p.defaultFloat = next);
                }
            }
        }

        static float DrawFloatSliderField(float value)
        {
            using (var sliderCheck = new EditorGUI.ChangeCheckScope())
            {
                float sliderValue = GUILayout.HorizontalSlider(Mathf.Clamp01(value), 0f, 1f, GUILayout.MinWidth(60f));
                if (sliderCheck.changed) value = sliderValue;
            }
            return EditorGUILayout.FloatField(value, GUILayout.Width(60f));
        }

        void DrawInt(AnimatorController controller, AnimatorControllerParameter param, int index, bool useLive)
        {
            if (useLive)
            {
                int current = _animator.GetInteger(param.nameHash);
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    int next = EditorGUILayout.IntField(current);
                    if (check.changed) _animator.SetInteger(param.nameHash, next);
                }
            }
            else
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    int next = EditorGUILayout.IntField(param.defaultInt);
                    if (check.changed) UpdateDefault(controller, index, p => p.defaultInt = next);
                }
            }
        }

        void DrawBool(AnimatorController controller, AnimatorControllerParameter param, int index, bool useLive)
        {
            if (useLive)
            {
                bool current = _animator.GetBool(param.nameHash);
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    bool next = EditorGUILayout.Toggle(current);
                    if (check.changed) _animator.SetBool(param.nameHash, next);
                }
            }
            else
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    bool next = EditorGUILayout.Toggle(param.defaultBool);
                    if (check.changed) UpdateDefault(controller, index, p => p.defaultBool = next);
                }
            }
        }

        void DrawTrigger(AnimatorControllerParameter param, bool useLive)
        {
            using (new EditorGUI.DisabledScope(!useLive))
            {
                if (GUILayout.Button("Set", GUILayout.Width(48f)))
                {
                    _animator.SetTrigger(param.nameHash);
                }
                if (GUILayout.Button("Reset", GUILayout.Width(56f)))
                {
                    _animator.ResetTrigger(param.nameHash);
                }
            }
        }

        static void UpdateDefault(AnimatorController controller, int index, Action<AnimatorControllerParameter> mutate)
        {
            Undo.RecordObject(controller, "Edit Animator Parameter Default");
            var parameters = controller.parameters;
            mutate(parameters[index]);
            controller.parameters = parameters;
            EditorUtility.SetDirty(controller);
        }
    }
}
