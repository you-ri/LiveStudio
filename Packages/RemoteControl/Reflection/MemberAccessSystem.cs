// Copyright (c) You-Ri, 2026

using System;
using System.Collections;
using System.Reflection;
using Unity.Properties;
using UnityEngine;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// メンバーへのアクセス（get/set）を提供するシステム
    /// static member: System.Reflection を使用
    /// instance member: Unity.Properties 優先、フォールバックで Reflection
    /// </summary>
    public static class MemberAccessSystem
    {
        private const BindingFlags kDefaultFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        #region Public API

        /// <summary>
        /// MemberAccessDataを使用して値を取得
        /// </summary>
        /// <param name="target">対象オブジェクト（staticの場合はnull可）</param>
        /// <param name="accessData">アクセスデータ</param>
        /// <returns>取得した値</returns>
        public static object GetValue(object target, in MemberAccessData accessData)
        {
            if (!accessData.isValid)
            {
                Debug.LogWarning("[Reflection] Invalid MemberAccessData");
                return null;
            }

            // staticの場合
            if (accessData.isStatic)
            {
                return _GetStaticValue(accessData.memberInfo);
            }

            // インスタンスの場合
            if (target == null)
            {
                Debug.LogWarning("[Reflection] Target is null for instance member access");
                return null;
            }

            return _GetInstanceValue(target, accessData);
        }

        /// <summary>
        /// プロパティパスを使用して値を取得
        /// Unity.Properties を優先し、失敗時は Reflection にフォールバック
        /// </summary>
        /// <param name="target">対象オブジェクト</param>
        /// <param name="propertyPath">プロパティパス（例: "components[0].value"）</param>
        /// <returns>取得した値</returns>
        public static object GetValue(object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
                return null;

            // Unity.Properties を試みる
            var result = _GetValueWithUnityProperties(target, propertyPath);
            if (result != null)
                return result;

            // Reflection にフォールバック
            return _GetValueWithReflection(target, propertyPath);
        }

        /// <summary>
        /// MemberAccessDataを使用して値を設定
        /// </summary>
        /// <param name="target">対象オブジェクト（staticの場合はnull可）</param>
        /// <param name="accessData">アクセスデータ</param>
        /// <param name="value">設定する値</param>
        /// <returns>成功した場合true</returns>
        public static bool SetValue(object target, in MemberAccessData accessData, object value)
        {
            if (!accessData.isValid)
            {
                Debug.LogWarning("[Reflection] Invalid MemberAccessData");
                return false;
            }

            if (accessData.isReadOnly)
            {
                Debug.LogWarning($"[Reflection] Member '{accessData.memberInfo.Name}' is read-only");
                return false;
            }

            // staticの場合
            if (accessData.isStatic)
            {
                return _SetStaticValue(accessData.memberInfo, value);
            }

            // インスタンスの場合
            if (target == null)
            {
                Debug.LogWarning("[Reflection] Target is null for instance member access");
                return false;
            }

            return _SetInstanceValue(target, accessData, value);
        }

        /// <summary>
        /// プロパティパスを使用して値を設定
        /// Unity.Properties を優先し、失敗時は Reflection にフォールバック
        /// </summary>
        /// <param name="target">対象オブジェクト</param>
        /// <param name="propertyPath">プロパティパス（例: "components[0].value"）</param>
        /// <param name="value">設定する値</param>
        /// <returns>成功した場合true</returns>
        public static bool SetValue(object target, string propertyPath, object value)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
                return false;

            // Unity.Properties を試みる
            if (_SetValueWithUnityProperties(target, propertyPath, value))
                return true;

            // Reflection にフォールバック
            return _SetValueWithReflection(target, propertyPath, value);
        }

        /// <summary>
        /// MemberInfoからMemberAccessDataを作成
        /// </summary>
        public static MemberAccessData CreateAccessData(MemberInfo memberInfo)
        {
            return MemberAccessData.FromMember(memberInfo);
        }

        /// <summary>
        /// プロパティパスからMemberInfoを取得（単純なパスのみ対応）
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="propertyPath">プロパティパス</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>MemberInfo（存在しない場合はnull）</returns>
        public static MemberInfo GetMemberInfo(Type type, string propertyPath, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(propertyPath))
                return null;

            // 単純なパス（配列アクセスやドット区切りなし）の場合
            if (!propertyPath.Contains('.') && !propertyPath.Contains('['))
            {
                return TypeReflectionSystem.GetMember(type, propertyPath, flags);
            }

            // 複雑なパスの場合はTraverseで最終メンバーを取得
            return _TraversePathForMemberInfo(type, propertyPath, flags);
        }

        /// <summary>
        /// 対象オブジェクトとプロパティパスからMemberInfoを取得
        /// </summary>
        public static MemberInfo GetMemberInfo(object target, string propertyPath, BindingFlags flags = kDefaultFlags)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
                return null;

            return GetMemberInfo(target.GetType(), propertyPath, flags);
        }

        #endregion

        #region Internal - Unity.Properties

        /// <summary>
        /// Unity.Properties を使用して値を取得
        /// </summary>
        internal static object _GetValueWithUnityProperties(object target, string path)
        {
            if (!ReflectionCache.TryGetUnityPath(path, out var unityPath))
            {
                // 無効なパス（負のインデックスなど）
                return null;
            }

            if (PropertyContainer.TryGetValue(ref target, unityPath, out object value))
            {
                return value;
            }

            return null;
        }

        /// <summary>
        /// Unity.Properties を使用して値を設定
        /// </summary>
        internal static bool _SetValueWithUnityProperties(object target, string path, object value)
        {
            if (!ReflectionCache.TryGetUnityPath(path, out var unityPath))
            {
                // 無効なパス
                return false;
            }

            return PropertyContainer.TrySetValue(ref target, unityPath, value);
        }

        #endregion

        #region Internal - Reflection

        /// <summary>
        /// Reflection を使用して値を取得
        /// </summary>
        internal static object _GetValueWithReflection(object target, string path)
        {
            object current = target;

            foreach (var segment in PropertyPathParser.Parse(path))
            {
                if (segment.isError || current == null)
                    return null;

                if (segment.isIndexed)
                {
                    current = _GetIndexedValue(current, segment.index);
                }
                else
                {
                    var memberInfo = TypeReflectionSystem.GetMember(
                        current.GetType(),
                        segment.name.ToString(),
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

                    if (memberInfo == null)
                    {
                        Debug.LogWarning($"[Reflection] Member '{segment.name.ToString()}' not found in type {current.GetType()}");
                        return null;
                    }

                    current = _GetMemberValue(current, memberInfo);
                }
            }

            return current;
        }

        /// <summary>
        /// Reflection を使用して値を設定
        /// </summary>
        internal static bool _SetValueWithReflection(object target, string path, object value)
        {
            // 最後のセグメント以外を辿って親オブジェクトを取得
            // ref structはListに格納できないため、前のセグメントを保持しながら辿る
            object current = target;
            string lastSegmentName = null;
            int lastSegmentIndex = 0;
            bool lastSegmentIsIndexed = false;
            int segmentCount = 0;

            foreach (var segment in PropertyPathParser.Parse(path))
            {
                if (segment.isError)
                    return false;

                // 前のセグメントがあれば、そのセグメントで辿る
                if (segmentCount > 0)
                {
                    if (current == null)
                        return false;

                    if (lastSegmentIsIndexed)
                    {
                        current = _GetIndexedValue(current, lastSegmentIndex);
                    }
                    else
                    {
                        var memberInfo = TypeReflectionSystem.GetMember(
                            current.GetType(),
                            lastSegmentName,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

                        if (memberInfo == null)
                            return false;

                        current = _GetMemberValue(current, memberInfo);
                    }
                }

                // 現在のセグメントを保持（最後のセグメントになる可能性がある）
                lastSegmentName = segment.isIndexed ? null : segment.name.ToString();
                lastSegmentIndex = segment.index;
                lastSegmentIsIndexed = segment.isIndexed;
                segmentCount++;
            }

            if (segmentCount == 0 || current == null)
                return false;

            // 最後のセグメントに値を設定
            if (lastSegmentIsIndexed)
            {
                return _SetIndexedValue(current, lastSegmentIndex, value);
            }
            else
            {
                var memberInfo = TypeReflectionSystem.GetMember(
                    current.GetType(),
                    lastSegmentName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

                if (memberInfo == null)
                    return false;

                return _SetMemberValue(current, memberInfo, value);
            }
        }

        /// <summary>
        /// staticメンバーの値を取得
        /// </summary>
        internal static object _GetStaticValue(MemberInfo memberInfo)
        {
            if (memberInfo is PropertyInfo propInfo)
            {
                return propInfo.GetValue(null);
            }
            else if (memberInfo is FieldInfo fieldInfo)
            {
                return fieldInfo.GetValue(null);
            }
            return null;
        }

        /// <summary>
        /// staticメンバーに値を設定
        /// </summary>
        internal static bool _SetStaticValue(MemberInfo memberInfo, object value)
        {
            try
            {
                if (memberInfo is PropertyInfo propInfo)
                {
                    if (!propInfo.CanWrite)
                    {
                        Debug.LogWarning($"[Reflection] Property '{propInfo.Name}' is read-only");
                        return false;
                    }
                    propInfo.SetValue(null, value);
                    return true;
                }
                else if (memberInfo is FieldInfo fieldInfo)
                {
                    if (fieldInfo.IsInitOnly || fieldInfo.IsLiteral)
                    {
                        Debug.LogWarning($"[Reflection] Field '{fieldInfo.Name}' is read-only");
                        return false;
                    }
                    fieldInfo.SetValue(null, value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reflection] Failed to set static value: {ex.Message}");
            }
            return false;
        }

        #endregion

        #region Private Helpers

        private static object _GetInstanceValue(object target, in MemberAccessData accessData)
        {
            if (accessData.isProperty)
            {
                var propInfo = accessData.AsPropertyInfo();
                return propInfo?.GetValue(target);
            }
            else
            {
                var fieldInfo = accessData.AsFieldInfo();
                return fieldInfo?.GetValue(target);
            }
        }

        private static bool _SetInstanceValue(object target, in MemberAccessData accessData, object value)
        {
            try
            {
                if (accessData.isProperty)
                {
                    var propInfo = accessData.AsPropertyInfo();
                    propInfo?.SetValue(target, value);
                    return true;
                }
                else
                {
                    var fieldInfo = accessData.AsFieldInfo();
                    fieldInfo?.SetValue(target, value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reflection] Failed to set instance value: {ex.Message}");
                return false;
            }
        }

        private static object _GetMemberValue(object target, MemberInfo memberInfo)
        {
            if (memberInfo is PropertyInfo propInfo)
            {
                return propInfo.GetValue(target);
            }
            else if (memberInfo is FieldInfo fieldInfo)
            {
                return fieldInfo.GetValue(target);
            }
            return null;
        }

        private static bool _SetMemberValue(object target, MemberInfo memberInfo, object value)
        {
            try
            {
                if (memberInfo is PropertyInfo propInfo)
                {
                    if (!propInfo.CanWrite)
                    {
                        Debug.LogWarning($"[Reflection] Property '{propInfo.Name}' is read-only");
                        return false;
                    }
                    propInfo.SetValue(target, value);
                    return true;
                }
                else if (memberInfo is FieldInfo fieldInfo)
                {
                    if (fieldInfo.IsInitOnly || fieldInfo.IsLiteral)
                    {
                        Debug.LogWarning($"[Reflection] Field '{fieldInfo.Name}' is read-only");
                        return false;
                    }
                    fieldInfo.SetValue(target, value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reflection] Failed to set member value: {ex.Message}");
            }
            return false;
        }

        private static object _GetIndexedValue(object target, int index)
        {
            if (target == null) return null;

            if (target is IList list)
            {
                if (index < 0 || index >= list.Count)
                {
                    Debug.LogWarning($"[Reflection] Index {index} out of range for list of size {list.Count}");
                    return null;
                }
                return list[index];
            }
            else if (target.GetType().IsArray)
            {
                var array = (Array)target;
                if (index < 0 || index >= array.Length)
                {
                    Debug.LogWarning($"[Reflection] Index {index} out of range for array of size {array.Length}");
                    return null;
                }
                return array.GetValue(index);
            }
            else
            {
                Debug.LogWarning($"[Reflection] Object of type {target.GetType()} is not indexable");
                return null;
            }
        }

        private static bool _SetIndexedValue(object target, int index, object value)
        {
            if (target == null) return false;

            try
            {
                if (target is IList list && !list.IsReadOnly)
                {
                    if (index < 0 || index >= list.Count) return false;
                    list[index] = value;
                    return true;
                }
                else if (target.GetType().IsArray)
                {
                    var array = (Array)target;
                    if (index < 0 || index >= array.Length) return false;
                    array.SetValue(value, index);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reflection] Failed to set indexed value: {ex.Message}");
            }

            return false;
        }

        private static MemberInfo _TraversePathForMemberInfo(Type type, string path, BindingFlags flags)
        {
            Type currentType = type;
            MemberInfo lastMember = null;

            foreach (var segment in PropertyPathParser.Parse(path))
            {
                if (segment.isError || currentType == null)
                    return null;

                if (segment.isIndexed)
                {
                    // 配列/リストの要素型を取得
                    if (currentType.IsArray)
                    {
                        currentType = currentType.GetElementType();
                    }
                    else if (currentType.IsGenericType)
                    {
                        var genericArgs = currentType.GetGenericArguments();
                        if (genericArgs.Length > 0)
                        {
                            currentType = genericArgs[0];
                        }
                    }
                    lastMember = null;
                }
                else
                {
                    lastMember = TypeReflectionSystem.GetMember(currentType, segment.name.ToString(), flags);
                    if (lastMember == null)
                        return null;

                    // 次の型を取得
                    if (lastMember is PropertyInfo propInfo)
                    {
                        currentType = propInfo.PropertyType;
                    }
                    else if (lastMember is FieldInfo fieldInfo)
                    {
                        currentType = fieldInfo.FieldType;
                    }
                }
            }

            return lastMember;
        }

        #endregion
    }
}
