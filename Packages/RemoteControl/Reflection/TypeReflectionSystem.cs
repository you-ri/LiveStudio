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
        /// 特定の属性を持つすべての型を検索する。<see cref="FindTypesWithAttribute{T}"/> と違い、
        /// エディタではまだ Mono にロードされていないアセンブリの型も見つける。
        ///
        /// ロード済みアセンブリだけを走査する版は、走査した瞬間にたまたま何がロードされていたかで
        /// 結果が変わる。ドメインリロード直後にこれをやると型テーブルが歯抜けのまま配られるため、
        /// 起動時の一括登録はこちらを使う。
        ///
        /// エディタでは <c>UnityEditor.TypeCache</c> を引くので、**メインスレッドから呼ぶこと**
        /// (静的コンストラクタ経由でワーカースレッドから走りうる箇所では使わない)。
        /// </summary>
        public static IEnumerable<Type> FindAllTypesWithAttribute<T>() where T : Attribute
        {
#if UNITY_EDITOR
            // TypeCache は属性が直接付いた型しか返さない。一方リフレクション走査は属性の継承
            // (基底に付いた [LiveClass] を派生が引き継ぐ) も拾うので、置き換えず和集合にする。
            var seen = new HashSet<Type>();
            foreach (var type in UnityEditor.TypeCache.GetTypesWithAttribute<T>())
            {
                if (type == null || !seen.Add(type)) continue;
                yield return type;
            }
            foreach (var type in FindTypesWithAttribute<T>())
            {
                if (type == null || !seen.Add(type)) continue;
                yield return type;
            }
#else
            // プレイヤーでは全スクリプトアセンブリが起動時にロード済みなので、走査で足りる。
            foreach (var type in FindTypesWithAttribute<T>())
            {
                if (type == null) continue;
                yield return type;
            }
#endif
        }

        /// <summary>
        /// 特定の属性を持つすべての型を検索 (ロード済みアセンブリのみ)
        /// </summary>
        /// <typeparam name="T">検索する属性の型</typeparam>
        /// <returns>属性を持つ型のコレクション</returns>
        public static IEnumerable<Type> FindTypesWithAttribute<T>() where T : Attribute
        {
            var assemblies = AssemblyUtility.GetLoadedAssemblies();
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
            // prefix "p:" でフィールドのキャッシュエントリと区別する
            return _GetCachedMember(type, name, "p:", _lookupProperty, flags);
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
            // prefix "f:" でプロパティのキャッシュエントリと区別する
            return _GetCachedMember(type, name, "f:", _lookupField, flags);
        }

        // GetProperty / GetField 共通のキャッシュ定型: null/空チェック → prefix 付き cacheKey で
        // ReflectionCache を引き、ミスなら reflection で取得してキャッシュする。delegate は
        // static readonly で1度だけ確保し per-call の GC を避ける。
        // Type.GetProperty / GetField は基底クラスの private メンバーを返さないため、
        // 見つからなければ階層を遡って DeclaredOnly で探し直す。
        //
        // 基底に private フィールドを置き、派生型で公開する構造 (このリポジトリの
        // shadow field 慣習そのもの) が、shadow field 経路でだけ通って通常メンバー経路では
        // "Member not found" になっていた。遡りは shadow field 側に既にあったものを揃えた形で、
        // 影響するのは今まで null を返していた (= エラーになっていた) 場合だけ。
        private static readonly Func<Type, string, BindingFlags, PropertyInfo> _lookupProperty =
            (t, n, f) => t.GetProperty(n, f) ?? _FindDeclaredInBases(t, b => b.GetProperty(n, f | BindingFlags.DeclaredOnly));
        private static readonly Func<Type, string, BindingFlags, FieldInfo> _lookupField =
            (t, n, f) => t.GetField(n, f) ?? _FindDeclaredInBases(t, b => b.GetField(n, f | BindingFlags.DeclaredOnly));

        /// <summary>継承チェーンを遡って、各階層で宣言されたメンバーだけを探す。</summary>
        private static T _FindDeclaredInBases<T>(Type type, Func<Type, T> lookup) where T : MemberInfo
        {
            for (var b = type?.BaseType; b != null; b = b.BaseType)
            {
                var found = lookup(b);
                if (found != null) return found;
            }

            return null;
        }

        private static T _GetCachedMember<T>(Type type, string name, string prefix,
            Func<Type, string, BindingFlags, T> lookup, BindingFlags flags) where T : MemberInfo
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;

            var cacheKey = prefix + name;
            if (ReflectionCache.TryGetMember(type, cacheKey, out var cached))
            {
                return cached as T;
            }

            var member = lookup(type, name, flags);
            if (member != null)
            {
                ReflectionCache.SetMember(type, cacheKey, member);
            }

            return member;
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
            var assemblies = AssemblyUtility.GetLoadedAssemblies();
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
