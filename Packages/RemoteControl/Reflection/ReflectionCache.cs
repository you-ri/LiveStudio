// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Properties;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// リフレクション結果のキャッシュ（GC抑制用）
    /// </summary>
    internal static class ReflectionCache
    {
        // MemberInfo キャッシュ (Type, name) -> MemberInfo
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> _memberCache
            = new Dictionary<Type, Dictionary<string, MemberInfo>>();

        // MethodInfo キャッシュ (Type, name) -> MethodInfo[]
        private static readonly Dictionary<Type, Dictionary<string, MethodInfo[]>> _methodCache
            = new Dictionary<Type, Dictionary<string, MethodInfo[]>>();

        // Attribute キャッシュ (MemberInfo) -> (attrType -> Attribute)
        private static readonly Dictionary<MemberInfo, Dictionary<Type, Attribute>> _attributeCache
            = new Dictionary<MemberInfo, Dictionary<Type, Attribute>>();

        // Type Attribute キャッシュ (Type) -> (attrType -> Attribute)
        private static readonly Dictionary<Type, Dictionary<Type, Attribute>> _typeAttributeCache
            = new Dictionary<Type, Dictionary<Type, Attribute>>();

        // Unity.Properties.PropertyPath キャッシュ
        private static readonly Dictionary<string, Unity.Properties.PropertyPath> _unityPathCache
            = new Dictionary<string, Unity.Properties.PropertyPath>();

        // 無効なパス（Unity.Properties で解析できない）を記録
        private static readonly HashSet<string> _invalidUnityPaths = new HashSet<string>();

        #region Member Cache

        /// <summary>
        /// MemberInfoをキャッシュから取得
        /// </summary>
        public static bool TryGetMember(Type type, string name, out MemberInfo member)
        {
            member = null;
            if (type == null || string.IsNullOrEmpty(name))
                return false;

            if (_memberCache.TryGetValue(type, out var members))
            {
                return members.TryGetValue(name, out member);
            }
            return false;
        }

        /// <summary>
        /// MemberInfoをキャッシュに設定
        /// </summary>
        public static void SetMember(Type type, string name, MemberInfo member)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return;

            if (!_memberCache.TryGetValue(type, out var members))
            {
                members = new Dictionary<string, MemberInfo>();
                _memberCache[type] = members;
            }
            members[name] = member;
        }

        #endregion

        #region Method Cache

        /// <summary>
        /// MethodInfo配列をキャッシュから取得
        /// </summary>
        public static bool TryGetMethod(Type type, string name, out MethodInfo[] methods)
        {
            methods = null;
            if (type == null || string.IsNullOrEmpty(name))
                return false;

            if (_methodCache.TryGetValue(type, out var methodDict))
            {
                return methodDict.TryGetValue(name, out methods);
            }
            return false;
        }

        /// <summary>
        /// MethodInfo配列をキャッシュに設定
        /// </summary>
        public static void SetMethod(Type type, string name, MethodInfo[] methods)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return;

            if (!_methodCache.TryGetValue(type, out var methodDict))
            {
                methodDict = new Dictionary<string, MethodInfo[]>();
                _methodCache[type] = methodDict;
            }
            methodDict[name] = methods;
        }

        #endregion

        #region Attribute Cache

        /// <summary>
        /// MemberInfoの属性をキャッシュから取得
        /// </summary>
        public static bool TryGetAttribute<T>(MemberInfo member, out T attribute) where T : Attribute
        {
            attribute = null;
            if (member == null)
                return false;

            if (_attributeCache.TryGetValue(member, out var attrs))
            {
                if (attrs.TryGetValue(typeof(T), out var attr))
                {
                    attribute = attr as T;
                    return attribute != null;
                }
            }
            return false;
        }

        /// <summary>
        /// MemberInfoの属性をキャッシュに設定
        /// </summary>
        public static void SetAttribute(MemberInfo member, Attribute attribute)
        {
            if (member == null || attribute == null)
                return;

            if (!_attributeCache.TryGetValue(member, out var attrs))
            {
                attrs = new Dictionary<Type, Attribute>();
                _attributeCache[member] = attrs;
            }
            attrs[attribute.GetType()] = attribute;
        }

        /// <summary>
        /// MemberInfoの属性がキャッシュに存在しないことを記録（null属性として）
        /// </summary>
        public static void SetAttributeNotFound<T>(MemberInfo member) where T : Attribute
        {
            if (member == null)
                return;

            if (!_attributeCache.TryGetValue(member, out var attrs))
            {
                attrs = new Dictionary<Type, Attribute>();
                _attributeCache[member] = attrs;
            }
            // nullを設定して「存在しない」を記録
            attrs[typeof(T)] = null;
        }

        /// <summary>
        /// MemberInfoの属性がキャッシュにあるか（存在しないことが記録されている場合も含む）
        /// </summary>
        public static bool HasAttributeCached<T>(MemberInfo member) where T : Attribute
        {
            if (member == null)
                return false;

            if (_attributeCache.TryGetValue(member, out var attrs))
            {
                return attrs.ContainsKey(typeof(T));
            }
            return false;
        }

        /// <summary>
        /// Typeの属性をキャッシュから取得
        /// </summary>
        public static bool TryGetTypeAttribute<T>(Type type, out T attribute) where T : Attribute
        {
            attribute = null;
            if (type == null)
                return false;

            if (_typeAttributeCache.TryGetValue(type, out var attrs))
            {
                if (attrs.TryGetValue(typeof(T), out var attr))
                {
                    attribute = attr as T;
                    return attribute != null;
                }
            }
            return false;
        }

        /// <summary>
        /// Typeの属性をキャッシュに設定
        /// </summary>
        public static void SetTypeAttribute(Type type, Attribute attribute)
        {
            if (type == null || attribute == null)
                return;

            if (!_typeAttributeCache.TryGetValue(type, out var attrs))
            {
                attrs = new Dictionary<Type, Attribute>();
                _typeAttributeCache[type] = attrs;
            }
            attrs[attribute.GetType()] = attribute;
        }

        /// <summary>
        /// Typeの属性が存在しないことをキャッシュに記録
        /// </summary>
        public static void SetTypeAttributeNotFound<T>(Type type) where T : Attribute
        {
            if (type == null)
                return;

            if (!_typeAttributeCache.TryGetValue(type, out var attrs))
            {
                attrs = new Dictionary<Type, Attribute>();
                _typeAttributeCache[type] = attrs;
            }
            attrs[typeof(T)] = null;
        }

        /// <summary>
        /// Typeの属性がキャッシュにあるか（存在しないことが記録されている場合も含む）
        /// </summary>
        public static bool HasTypeAttributeCached<T>(Type type) where T : Attribute
        {
            if (type == null)
                return false;

            if (_typeAttributeCache.TryGetValue(type, out var attrs))
            {
                return attrs.ContainsKey(typeof(T));
            }
            return false;
        }

        #endregion

        #region Unity PropertyPath Cache

        /// <summary>
        /// Unity.Properties.PropertyPathをキャッシュから取得
        /// </summary>
        /// <param name="path">パス文字列</param>
        /// <param name="unityPath">取得したPropertyPath</param>
        /// <returns>有効なパスの場合はtrue</returns>
        public static bool TryGetUnityPath(string path, out Unity.Properties.PropertyPath unityPath)
        {
            unityPath = default;

            if (string.IsNullOrEmpty(path))
                return false;

            // 既に無効と判明しているパスはスキップ
            if (_invalidUnityPaths.Contains(path))
                return false;

            if (_unityPathCache.TryGetValue(path, out unityPath))
                return true;

            // 新しいパスを作成（例外が発生する可能性あり）
            try
            {
                unityPath = new Unity.Properties.PropertyPath(path);
                _unityPathCache[path] = unityPath;
                return true;
            }
            catch (ArgumentException)
            {
                // 負のインデックスなど、Unity.Properties でサポートされていないパス形式
                _invalidUnityPaths.Add(path);
                return false;
            }
        }

        /// <summary>
        /// パスが無効かどうかを確認
        /// </summary>
        public static bool IsInvalidUnityPath(string path)
        {
            return !string.IsNullOrEmpty(path) && _invalidUnityPaths.Contains(path);
        }

        #endregion

        /// <summary>
        /// すべてのキャッシュをクリア
        /// </summary>
        public static void Clear()
        {
            _memberCache.Clear();
            _methodCache.Clear();
            _attributeCache.Clear();
            _typeAttributeCache.Clear();
            _unityPathCache.Clear();
            _invalidUnityPaths.Clear();
        }
    }
}
