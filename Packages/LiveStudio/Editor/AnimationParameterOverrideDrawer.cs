// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lilium.LiveStudio.Editor
{
    [CustomPropertyDrawer(typeof(AnimationParameterOverride))]
    public sealed class AnimationParameterOverrideDrawer : PropertyDrawer
    {
        const string kNoneLabel = "(none)";
        const float kNameValueGap = 4f;
        const float kNameWidthRatio = 0.6f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var nameProp = property.FindPropertyRelative("name");
            var typeProp = property.FindPropertyRelative("type");
            var floatProp = property.FindPropertyRelative("floatValue");
            var intProp = property.FindPropertyRelative("intValue");
            var boolProp = property.FindPropertyRelative("boolValue");

            if (nameProp == null || typeProp == null || floatProp == null || intProp == null || boolProp == null)
            {
                EditorGUI.PropertyField(position, property, label, includeChildren: true);
                return;
            }

            using (new EditorGUI.PropertyScope(position, GUIContent.none, property))
            {
                var animator = ResolveAnimator(property);
                var parameters = CollectParameters(animator);

                var nameWidth = (position.width - kNameValueGap) * kNameWidthRatio;
                var valueWidth = position.width - kNameValueGap - nameWidth;
                var nameRect = new Rect(position.x, position.y, nameWidth, position.height);
                var valueRect = new Rect(position.x + nameWidth + kNameValueGap, position.y, valueWidth, position.height);

                DrawNameField(nameRect, nameProp, typeProp, parameters);
                DrawValueField(valueRect, nameProp, typeProp, floatProp, intProp, boolProp, parameters);
            }
        }

        static void DrawNameField(
            Rect rect,
            SerializedProperty nameProp,
            SerializedProperty typeProp,
            List<AnimatorControllerParameter> parameters)
        {
            // パラメータ列挙が空 (Animator 未解決 / Controller 未割当) の時はテキスト直接編集。
            // タイプミス防止より「とにかく編集できる」を優先する。
            if (parameters.Count == 0)
            {
                EditorGUI.PropertyField(rect, nameProp, GUIContent.none);
                return;
            }

            var current = nameProp.stringValue ?? string.Empty;
            var options = new List<string>(parameters.Count + 2) { kNoneLabel };
            int selectedIndex = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                options.Add(parameters[i].name);
                if (parameters[i].name == current) selectedIndex = i + 1;
            }
            bool isMissing = !string.IsNullOrEmpty(current) && selectedIndex == 0;
            if (isMissing)
            {
                options.Add($"(missing: {current})");
                selectedIndex = options.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUI.Popup(rect, selectedIndex, options.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                if (newIndex == 0)
                {
                    nameProp.stringValue = string.Empty;
                }
                else if (newIndex >= 1 && newIndex <= parameters.Count)
                {
                    var picked = parameters[newIndex - 1];
                    nameProp.stringValue = picked.name;
                    typeProp.enumValueIndex = (int)picked.type;
                }
                // missing 項目を再選択した場合は何もしない (値据え置き)
            }
        }

        static void DrawValueField(
            Rect rect,
            SerializedProperty nameProp,
            SerializedProperty typeProp,
            SerializedProperty floatProp,
            SerializedProperty intProp,
            SerializedProperty boolProp,
            List<AnimatorControllerParameter> parameters)
        {
            var resolvedType = ResolveType(nameProp.stringValue, parameters);
            if (resolvedType.HasValue)
            {
                if (typeProp.enumValueIndex != (int)resolvedType.Value)
                {
                    typeProp.enumValueIndex = (int)resolvedType.Value;
                }
            }

            // 編集対象の型: Animator から解決できればそれを、できなければ serialize された type を信用する。
            var effectiveType = resolvedType ?? (AnimatorControllerParameterType)typeProp.enumValueIndex;

            if (string.IsNullOrEmpty(nameProp.stringValue))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.LabelField(rect, "-");
                }
                return;
            }

            switch (effectiveType)
            {
                case AnimatorControllerParameterType.Float:
                    EditorGUI.PropertyField(rect, floatProp, GUIContent.none);
                    break;
                case AnimatorControllerParameterType.Int:
                    EditorGUI.PropertyField(rect, intProp, GUIContent.none);
                    break;
                case AnimatorControllerParameterType.Bool:
                    EditorGUI.PropertyField(rect, boolProp, GUIContent.none);
                    break;
                default:
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.LabelField(rect, "-");
                    }
                    break;
            }
        }

        static AnimatorControllerParameterType? ResolveType(string name, List<AnimatorControllerParameter> parameters)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].name == name) return parameters[i].type;
            }
            return null;
        }

        static Animator ResolveAnimator(SerializedProperty property)
        {
            var owner = property.serializedObject.targetObject as Component;
            if (owner == null) return null;

            // RequireComponent(typeof(Animator)) が付くケース (VRCAvatar 等) を先に拾い、
            // 無ければ子孫から探す (AvatarController は子に Animator がある構成)。
            var animator = owner.GetComponent<Animator>();
            if (animator != null) return animator;
            return owner.GetComponentInChildren<Animator>(includeInactive: true);
        }

        static List<AnimatorControllerParameter> CollectParameters(Animator animator)
        {
            var list = new List<AnimatorControllerParameter>();
            if (animator == null || animator.runtimeAnimatorController == null) return list;

            var parameters = animator.parameters;
            if (parameters == null) return list;

            bool canFilterCurves = animator.isInitialized;
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.type == AnimatorControllerParameterType.Trigger) continue;
                if (canFilterCurves && animator.IsParameterControlledByCurve(p.nameHash)) continue;
                list.Add(p);
            }
            return list;
        }
    }
}
