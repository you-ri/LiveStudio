using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Lilium.RemoteControl
{
    [CustomPropertyDrawer(typeof(SelectAttribute))]
    public class SelectPropertyDrawer : PropertyDrawer
    {
        private Type[] _derivedTypes;
        private string[] _typeNames;
        private Type _baseType;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            // 基底型から派生型を取得
            if (_derivedTypes == null)
            {
                _baseType = GetManagedReferenceFieldType(property);
                if (_baseType != null)
                {
                    _derivedTypes = GetDerivedTypes(_baseType);
                    _typeNames = new string[] { "None" }.Concat(_derivedTypes.Select(t => t.Name)).ToArray();
                }
                else
                {
                    _derivedTypes = Array.Empty<Type>();
                    _typeNames = Array.Empty<string>();
                }
            }

            EditorGUI.BeginProperty(position, label, property);

            // 現在の型を取得
            var currentType = property.managedReferenceValue?.GetType();
            var currentIndex = currentType != null ? Array.IndexOf(_derivedTypes, currentType) + 1 : 0;

            // ドロップダウンの位置を計算
            var dropdownRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // ドロップダウンを描画
            EditorGUI.BeginChangeCheck();
            var newIndex = EditorGUI.Popup(dropdownRect, label.text, currentIndex, _typeNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex)
            {
                if (newIndex == 0)
                {
                    property.managedReferenceValue = null;
                }
                else if (newIndex > 0 && newIndex - 1 < _derivedTypes.Length)
                {
                    var newType = _derivedTypes[newIndex - 1];
                    property.managedReferenceValue = Activator.CreateInstance(newType);
                }
            }

            // プロパティを展開表示
            if (property.managedReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                var childRect = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing, position.width, position.height - EditorGUIUtility.singleLineHeight);

                // 子プロパティを描画
                var iterator = property.Copy();
                var endProperty = property.GetEndProperty();
                bool enterChildren = true;

                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
                {
                    enterChildren = false;
                    childRect.height = EditorGUI.GetPropertyHeight(iterator, true);
                    EditorGUI.PropertyField(childRect, iterator, true);
                    childRect.y += childRect.height + EditorGUIUtility.standardVerticalSpacing;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            float height = EditorGUIUtility.singleLineHeight;

            if (property.managedReferenceValue != null)
            {
                var iterator = property.Copy();
                var endProperty = property.GetEndProperty();
                bool enterChildren = true;

                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
                {
                    enterChildren = false;
                    height += EditorGUI.GetPropertyHeight(iterator, true) + EditorGUIUtility.standardVerticalSpacing;
                }
            }

            return height;
        }

        private Type GetManagedReferenceFieldType(SerializedProperty property)
        {
            var typeName = property.managedReferenceFieldTypename;
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            // フォーマット: "assemblyName typeName"
            var parts = typeName.Split(' ');
            if (parts.Length < 2)
            {
                return null;
            }

            var assemblyName = parts[0];
            var fullTypeName = parts[1];

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == assemblyName)
                {
                    var type = assembly.GetType(fullTypeName);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private Type[] GetDerivedTypes(Type baseType)
        {
            var types = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsClass && !type.IsAbstract && baseType.IsAssignableFrom(type)
                            && type.GetConstructor(Type.EmptyTypes) != null)
                        {
                            types.Add(type);
                        }
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    // アセンブリの読み込みエラーは無視
                }
            }

            return types.ToArray();
        }
    }
}
