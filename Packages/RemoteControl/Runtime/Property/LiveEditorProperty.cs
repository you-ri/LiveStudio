// Copyright (c) You-Ri, 2026

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Decides "is this changed" and "revert it" the way the editor itself does, while not playing.
    /// </summary>
    /// <remarks>
    /// The editor's own answer to both questions is the prefab override: a serialized property that
    /// overrides its prefab source shows in bold and offers Revert, and nothing else in the editor has
    /// a per-property notion of a value to go back to. This layer therefore answers only for live
    /// members that reach a <see cref="SerializedProperty"/>, and says "not changed / cannot revert"
    /// for everything else — rather than inventing a rule the editor does not have.
    /// <para/>
    /// What that excludes, deliberately:
    /// <list type="bullet">
    /// <item>members exposed as C# properties with no serialized backing declared</item>
    /// <item>objects that are not a prefab instance (the editor has no source value for them)</item>
    /// <item>live objects that are not a <see cref="UnityEngine.Object"/></item>
    /// </list>
    /// While playing, none of this applies: the session baseline
    /// (<see cref="LiveObjectDefaultRegistry"/>) owns both answers, as it always has.
    /// </remarks>
    public static class LiveEditorProperty
    {
        /// <summary>
        /// Whether the editor's rule is the one in force. False while playing and in a build, where the
        /// session baseline answers instead.
        /// </summary>
        public static bool isEditorRuleActive => LiveEditorSession.isEditorSession;

        /// <summary>
        /// Whether the live member reaches a serialized property at all — that is, whether the editor
        /// is in a position to have any opinion about it. False for members with no serialized backing
        /// declared and for live objects that are not a <see cref="UnityEngine.Object"/>.
        /// </summary>
        public static bool HasSerializedBacking(in LiveProperty property)
        {
#if UNITY_EDITOR
            using var serializedObject = _TryResolve(property, out var serializedProperty);
            return serializedProperty != null;
#else
            return false;
#endif
        }

        /// <summary>
        /// Whether the property overrides its prefab source. False when it does not reach a
        /// serialized property at all. Only call when <see cref="isEditorRuleActive"/>.
        /// </summary>
        public static bool IsChanged(in LiveProperty property)
        {
#if UNITY_EDITOR
            using var serializedObject = _TryResolve(property, out var serializedProperty);
            return serializedProperty != null && serializedProperty.prefabOverride;
#else
            return false;
#endif
        }

        /// <summary>
        /// Reverts the property to its prefab source, exactly as the Inspector's Revert does (undo
        /// included). Returns false when there is nothing the editor can revert — no serialized
        /// property, or no override on it. Only call when <see cref="isEditorRuleActive"/>.
        /// </summary>
        public static bool TryRevert(in LiveProperty property)
        {
#if UNITY_EDITOR
            using var serializedObject = _TryResolve(property, out var serializedProperty);
            if (serializedProperty == null || !serializedProperty.prefabOverride) return false;

            PrefabUtility.RevertPropertyOverride(serializedProperty, InteractionMode.UserAction);
            return true;
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Walks the live path down to a serialized property. Returns the SerializedObject so the
        /// caller can dispose it; the out property is null when any step does not resolve.
        /// </summary>
        private static SerializedObject _TryResolve(in LiveProperty property, out SerializedProperty serializedProperty)
        {
            serializedProperty = null;

            if (!(property.owner.target is UnityEngine.Object unityObject) || unityObject == null) return null;

            var slashPath = property.path.ToSlash();
            if (string.IsNullOrEmpty(slashPath)) return null;
            var segments = slashPath.Split('/');

            // 先頭だけは公開名と保存名が食い違い得る (C# プロパティ公開 / 別名のバッキング)。
            // 綴りの当て推量はせず、公開宣言が持っているフィールドだけを保存先として認める。
            var serializedName = _ResolveSerializedName(property.owner.targetType, segments[0]);
            if (serializedName == null) return null;

            var serializedObject = new SerializedObject(unityObject);
            var current = serializedObject.FindProperty(serializedName);

            for (int i = 1; i < segments.Length && current != null; i++)
            {
                if (int.TryParse(segments[i], out var index))
                {
                    current = current.isArray && index >= 0 && index < current.arraySize
                        ? current.GetArrayElementAtIndex(index)
                        : null;
                    continue;
                }
                current = current.FindPropertyRelative(segments[i]);
            }

            if (current == null)
            {
                serializedObject.Dispose();
                return null;
            }

            serializedProperty = current;
            return serializedObject;
        }

        /// <summary>
        /// 公開メンバーの保存先フィールド名。フィールド公開ならそれ自身、C# プロパティ公開なら
        /// 宣言された shadow field。どちらも無ければ保存先が無い = 対象外。
        /// </summary>
        private static string _ResolveSerializedName(LiveClass liveClass, string liveName)
        {
            var propertyType = liveClass?.FindProperty(liveName);
            if (propertyType == null) return null;

            if (propertyType.fieldInfo != null) return propertyType.fieldInfo.Name;
            if (propertyType.shadowField != null) return propertyType.shadowField.Name;
            return null;
        }
#endif
    }
}
