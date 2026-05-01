// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// 型情報の収集と分析を行うシステム
    /// </summary>
    public static class TypeReflectionSystem
    {
        private const BindingFlags kDefaultFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        /// <summary>
        /// 型から詳細なリフレクション情報を収集
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>型情報データ</returns>
        public static TypeReflectionData Collect(Type type, BindingFlags flags = kDefaultFlags)
        {
            if (type == null)
            {
                Debug.LogError("[Reflection] Type cannot be null");
                return null;
            }

            var isStatic = type.IsAbstract && type.IsSealed;
            var members = new List<MemberReflectionData>();
            var methods = new List<MethodReflectionData>();

            // プロパティを収集
            var properties = type.GetProperties(flags);
            foreach (var prop in properties)
            {
                var memberData = MemberReflectionData.FromProperty(prop);
                if (memberData != null)
                {
                    members.Add(memberData);
                }
            }

            // フィールドを収集
            var fields = type.GetFields(flags);
            foreach (var field in fields)
            {
                var memberData = MemberReflectionData.FromField(field);
                if (memberData != null)
                {
                    members.Add(memberData);
                }
            }

            // メソッドを収集（プロパティのgetter/setterは除外）
            var methodInfos = type.GetMethods(flags);
            foreach (var method in methodInfos)
            {
                // 特殊メソッド（getter/setter/event）をスキップ
                if (method.IsSpecialName)
                    continue;

                var methodData = MethodReflectionData.FromMethod(method);
                if (methodData != null)
                {
                    methods.Add(methodData);
                }
            }

            return new TypeReflectionData(
                type,
                type.Name,
                isStatic,
                members.ToArray(),
                methods.ToArray());
        }

        /// <summary>
        /// 特定の属性を持つすべての型を検索
        /// </summary>
        /// <typeparam name="T">検索する属性の型</typeparam>
        /// <returns>属性を持つ型のコレクション</returns>
        public static IEnumerable<Type> FindTypesWithAttribute<T>() where T : Attribute
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 読み込めた型のみを使用
                    types = ex.Types;
                    Debug.LogWarning($"[Reflection] Failed to load some types from assembly: {assembly.FullName}");
                }

                foreach (var type in types)
                {
                    if (type == null) continue;

                    // try-catch内でyieldは使えないため、フラグで制御
                    bool hasAttribute = false;
                    try
                    {
                        var attr = GetCustomAttribute<T>(type);
                        hasAttribute = attr != null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Reflection] Failed to get attribute from type {type?.FullName}: {ex.Message}");
                    }

                    if (hasAttribute)
                    {
                        yield return type;
                    }
                }
            }
        }

        /// <summary>
        /// 型からカスタム属性を取得（キャッシュ使用）
        /// </summary>
        /// <typeparam name="T">属性の型</typeparam>
        /// <param name="type">対象の型</param>
        /// <returns>属性（存在しない場合はnull）</returns>
        public static T GetCustomAttribute<T>(Type type) where T : Attribute
        {
            if (type == null) return null;

            // キャッシュを確認
            if (ReflectionCache.HasTypeAttributeCached<T>(type))
            {
                ReflectionCache.TryGetTypeAttribute<T>(type, out var cachedAttr);
                return cachedAttr;
            }

            // リフレクションで取得
            var attr = type.GetCustomAttribute<T>();
            if (attr != null)
            {
                ReflectionCache.SetTypeAttribute(type, attr);
            }
            else
            {
                ReflectionCache.SetTypeAttributeNotFound<T>(type);
            }

            return attr;
        }

        /// <summary>
        /// メンバーからカスタム属性を取得（キャッシュ使用）
        /// </summary>
        /// <typeparam name="T">属性の型</typeparam>
        /// <param name="member">対象のメンバー</param>
        /// <returns>属性（存在しない場合はnull）</returns>
        public static T GetCustomAttribute<T>(MemberInfo member) where T : Attribute
        {
            if (member == null) return null;

            // キャッシュを確認
            if (ReflectionCache.HasAttributeCached<T>(member))
            {
                ReflectionCache.TryGetAttribute<T>(member, out var cachedAttr);
                return cachedAttr;
            }

            // リフレクションで取得
            var attr = member.GetCustomAttribute<T>();
            if (attr != null)
            {
                ReflectionCache.SetAttribute(member, attr);
            }
            else
            {
                ReflectionCache.SetAttributeNotFound<T>(member);
            }

            return attr;
        }

        /// <summary>
        /// 型からプロパティを取得（キャッシュ使用）
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="name">プロパティ名</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>PropertyInfo（存在しない場合はnull）</returns>
        public static PropertyInfo GetProperty(Type type, string name, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;

            // キャッシュキーにはprefixを付けてプロパティとフィールドを区別
            var cacheKey = "p:" + name;

            if (ReflectionCache.TryGetMember(type, cacheKey, out var cached))
            {
                return cached as PropertyInfo;
            }

            var prop = type.GetProperty(name, flags);
            if (prop != null)
            {
                ReflectionCache.SetMember(type, cacheKey, prop);
            }

            return prop;
        }

        /// <summary>
        /// 型からフィールドを取得（キャッシュ使用）
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="name">フィールド名</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>FieldInfo（存在しない場合はnull）</returns>
        public static FieldInfo GetField(Type type, string name, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;

            // キャッシュキーにはprefixを付けてプロパティとフィールドを区別
            var cacheKey = "f:" + name;

            if (ReflectionCache.TryGetMember(type, cacheKey, out var cached))
            {
                return cached as FieldInfo;
            }

            var field = type.GetField(name, flags);
            if (field != null)
            {
                ReflectionCache.SetMember(type, cacheKey, field);
            }

            return field;
        }

        /// <summary>
        /// 型からメソッドを取得（キャッシュ使用）
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="name">メソッド名</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>MethodInfo（存在しない場合はnull）</returns>
        public static MethodInfo GetMethod(Type type, string name, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;

            // キャッシュを確認（最初のメソッドを返す）
            if (ReflectionCache.TryGetMethod(type, name, out var cachedMethods))
            {
                return cachedMethods.Length > 0 ? cachedMethods[0] : null;
            }

            var method = type.GetMethod(name, flags);
            if (method != null)
            {
                ReflectionCache.SetMethod(type, name, new[] { method });
            }

            return method;
        }

        /// <summary>
        /// 型から同名のすべてのメソッドを取得（オーバーロード対応、キャッシュ使用）
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="name">メソッド名</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>MethodInfo配列</returns>
        public static MethodInfo[] GetMethods(Type type, string name, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(name)) return Array.Empty<MethodInfo>();

            // キャッシュを確認
            if (ReflectionCache.TryGetMethod(type, name, out var cachedMethods))
            {
                return cachedMethods;
            }

            // 全メソッドから名前が一致するものを抽出
            var allMethods = type.GetMethods(flags);
            var matchingMethods = new List<MethodInfo>();
            foreach (var method in allMethods)
            {
                if (method.Name == name)
                {
                    matchingMethods.Add(method);
                }
            }

            var result = matchingMethods.ToArray();
            ReflectionCache.SetMethod(type, name, result);

            return result;
        }

        /// <summary>
        /// 指定された基底型から派生する具象クラスを検索（キャッシュ使用）
        /// </summary>
        /// <param name="baseType">基底型（インターフェースまたは抽象クラス）</param>
        /// <returns>派生型のリスト</returns>
        public static List<Type> FindDerivedTypes(Type baseType)
        {
            if (baseType == null)
            {
                Debug.LogError("[Reflection] Base type cannot be null");
                return new List<Type>();
            }

            if (_derivedTypesCache.TryGetValue(baseType, out var cached))
            {
                return cached;
            }

            var result = new List<Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (!type.IsClass) continue;
                    if (type.IsAbstract) continue;
                    if (!baseType.IsAssignableFrom(type)) continue;

                    result.Add(type);
                }
            }

            _derivedTypesCache[baseType] = result;
            return result;
        }

        private static readonly Dictionary<Type, List<Type>> _derivedTypesCache = new Dictionary<Type, List<Type>>();

        /// <summary>
        /// 型からメンバー（プロパティまたはフィールド）を取得
        /// プロパティを優先して検索する
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="name">メンバー名</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>MemberInfo（存在しない場合はnull）</returns>
        public static MemberInfo GetMember(Type type, string name, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;

            // まずプロパティを検索
            var prop = GetProperty(type, name, flags);
            if (prop != null) return prop;

            // 次にフィールドを検索
            var field = GetField(type, name, flags);
            return field;
        }
    }
}
