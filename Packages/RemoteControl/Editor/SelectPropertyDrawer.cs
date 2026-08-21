using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    [CustomPropertyDrawer(typeof(SelectAttribute))]
    public class SelectPropertyDrawer : PropertyDrawer
    {
        /// <summary>Instantiable choices for one managed reference field type, plus the popup labels.</summary>
        private sealed class TypeChoices
        {
            public Type[] types;
            public string[] names;
        }

        // Shared across drawer instances on purpose. Unity caches a PropertyHandler - and with it a
        // drawer instance - per property path, so a list of N managed references gets N drawers.
        // Holding the type list per instance made every one of them repeat the same lookup, which is
        // what made expanding a large [SerializeReference, Select] list slow. Static state here is
        // safe because a domain reload (the only thing that can change the type set) clears it.
        private static readonly Dictionary<Type, TypeChoices> _choicesByBaseType = new Dictionary<Type, TypeChoices>();
        private static readonly Dictionary<string, Type> _fieldTypeByTypename = new Dictionary<string, Type>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            // 基底型から派生型を取得 (プロセス内で共有キャッシュ)
            var baseType = GetManagedReferenceFieldType(property);
            var choices = baseType != null ? GetChoices(baseType) : null;
            var derivedTypes = choices != null ? choices.types : Array.Empty<Type>();
            var typeNames = choices != null ? choices.names : Array.Empty<string>();

            EditorGUI.BeginProperty(position, label, property);

            // 現在の型を取得
            var currentType = property.managedReferenceValue?.GetType();
            var currentIndex = currentType != null ? Array.IndexOf(derivedTypes, currentType) + 1 : 0;

            // ドロップダウンの位置を計算
            var dropdownRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            // ドロップダウンを描画
            EditorGUI.BeginChangeCheck();
            var newIndex = EditorGUI.Popup(dropdownRect, label.text, currentIndex, typeNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex)
            {
                if (newIndex == 0)
                {
                    property.managedReferenceValue = null;
                }
                else if (newIndex > 0 && newIndex - 1 < derivedTypes.Length)
                {
                    var newType = derivedTypes[newIndex - 1];
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

        private static Type GetManagedReferenceFieldType(SerializedProperty property)
        {
            var typeName = property.managedReferenceFieldTypename;
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            // 解決できなかった場合も記録する - 走査そのものがコストなので毎回やり直さない
            if (_fieldTypeByTypename.TryGetValue(typeName, out var cached))
            {
                return cached;
            }

            var resolved = ResolveTypename(typeName);
            _fieldTypeByTypename[typeName] = resolved;
            return resolved;
        }

        private static Type ResolveTypename(string typeName)
        {
            // フォーマット: "assemblyName typeName"
            var parts = typeName.Split(' ');
            if (parts.Length < 2)
            {
                return null;
            }

            var assemblyName = parts[0];
            var fullTypeName = parts[1];

            foreach (var assembly in AssemblyUtility.GetLoadedAssemblies())
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

        private static TypeChoices GetChoices(Type baseType)
        {
            if (_choicesByBaseType.TryGetValue(baseType, out var cached))
            {
                return cached;
            }

            var types = new List<Type>();

            // baseType 自身が具象なら候補に含める (旧実装の IsAssignableFrom は自分自身も拾っていた)。
            // TypeCache.GetTypesDerivedFrom は自分自身を返さないため明示的に足す。
            if (IsInstantiable(baseType))
            {
                types.Add(baseType);
            }

            // Unity が事前構築した索引を引く。旧実装はロード済み全アセンブリに GetTypes() を掛けており、
            // このプロジェクトでは 300 前後のアセンブリ・数万個の Type をマテリアライズしていた。
            foreach (var type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (IsInstantiable(type))
                {
                    types.Add(type);
                }
            }

            var choices = new TypeChoices
            {
                types = types.ToArray(),
                names = new string[] { "None" }.Concat(types.Select(t => t.Name)).ToArray(),
            };
            _choicesByBaseType[baseType] = choices;
            return choices;
        }

        private static bool IsInstantiable(Type type)
        {
            return type.IsClass && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null;
        }
    }
}
