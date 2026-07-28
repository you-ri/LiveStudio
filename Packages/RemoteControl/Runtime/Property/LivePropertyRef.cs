// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 他の LiveObjectHandle のプロパティへの参照を表す値型。
    /// FusionPage のような「集約ページ」で、実体の LiveProperty を代理露出するために使う。
    ///
    /// `[LiveField] public static readonly LivePropertyRef smoothness = LivePropertyRef.To&lt;AvatarProvider&gt;("_smoothness");`
    /// のように宣言し、取得/設定/dirty/revert のすべてを参照先の LiveProperty に委譲する。
    ///
    /// 参照解決は実行時に LiveObjectRegistry から targetTypeName を引く。対象が未登録の場合は null を返し、
    /// その場合は安全に fallback する (値は null/0、dirty は false)。
    /// </summary>
    public readonly struct LivePropertyRef
    {
        /// <summary>対象 LiveObjectHandle の id (LiveObjectRegistry のキー、通常は LiveClass.typeName)</summary>
        public readonly string targetTypeName;

        /// <summary>対象 LiveObjectHandle 内のプロパティパス (例: "_smoothness")</summary>
        public readonly string propertyPath;

        /// <summary>
        /// 対象プロパティの値型 (例: typeof(float))。
        /// TypeDefinition 出力などで、RemoteApp に「このプロパティは実質 float」と伝えるために使う。
        /// 解決できない場合は null。
        /// </summary>
        public readonly Type targetValueType;

        public LivePropertyRef(string targetTypeName, string propertyPath, Type targetValueType)
        {
            this.targetTypeName = targetTypeName;
            this.propertyPath = propertyPath;
            this.targetValueType = targetValueType;
        }

        public bool isValid => !string.IsNullOrEmpty(targetTypeName) && !string.IsNullOrEmpty(propertyPath);

        /// <summary>
        /// 型 T の LiveClass typeName と指定パスから参照を構築する。
        /// targetValueType は T のメンバ型からリフレクションで解決する。
        /// </summary>
        public static LivePropertyRef To<T>(string propertyPath)
        {
            // 静的初期化順序の影響を受けないよう、未登録でも T のクラス名にフォールバックする
            LiveClass.TryGet(typeof(T), out var liveClass);
            var typeName = liveClass?.typeName ?? typeof(T).Name;
            var memberType = _ResolveMemberType(typeof(T), propertyPath);
            return new LivePropertyRef(typeName, propertyPath, memberType);
        }

        /// <summary>
        /// 明示的に targetTypeName/propertyPath/valueType を指定して参照を構築する。
        /// LiveClass の typeName が既知な場合に使う。
        /// </summary>
        public static LivePropertyRef Create(string targetTypeName, string propertyPath, Type targetValueType)
        {
            return new LivePropertyRef(targetTypeName, propertyPath, targetValueType);
        }

        /// <summary>
        /// 参照先の LiveProperty を解決する。対象が未登録なら null。
        /// 解決順序:
        /// 1. LiveObjectRegistry に id=targetTypeName で登録されているオブジェクト
        /// 2. targetTypeName が static LiveClass なら、GetOrCreate で自動生成
        /// 3. targetTypeName が Component 派生の LiveClass なら、シーン上のインスタンスを検索 (FindFirstObjectByType)。
        ///    既に Registry に登録済みならそれを使い、未登録なら CreateUnregistered でラップする。
        /// </summary>
        public LiveProperty? Resolve()
        {
            if (!isValid) return null;

            // 1. id 登録済み
            var owner = LiveObjectRegistry.FindById(targetTypeName);
            if (owner != null) return owner.Value.FindProperty(propertyPath);

            // 2/3. LiveClass から型を引いて解決
            var liveClass = LiveClass.Find(targetTypeName);
            if (liveClass == null) return null;

            if (liveClass.isStatic)
            {
                var staticOwner = LiveObjectRegistry.GetOrCreate(liveClass.typeName, liveClass, null);
                return staticOwner.FindProperty(propertyPath);
            }

            if (liveClass.type != null && typeof(Component).IsAssignableFrom(liveClass.type))
            {
                var target = UnityEngine.Object.FindFirstObjectByType(liveClass.type, FindObjectsInactive.Include);
                if (target == null) return null;

                // 既存をまず target 参照で探す (他経路で CreateUnregistered 済みのケース)
                var existing = LiveObjectRegistry.FindByTarget(target);
                if (existing != null)
                {
                    // id 未付与なら typeName を後付けで割り当て、FindById でも引けるようにする
                    var resolvedOwner = existing.Value;
                    if (!resolvedOwner.hasId)
                    {
                        resolvedOwner = LiveObjectRegistry.AssignId(resolvedOwner, liveClass.typeName);
                    }
                    return resolvedOwner.FindProperty(propertyPath);
                }

                // target 付きで登録済み LiveObjectHandle を作成 (コンストラクタで default capture も走る)
                var instanceOwner = LiveObjectRegistry.GetOrCreate(liveClass.typeName, liveClass, target);
                return instanceOwner.FindProperty(propertyPath);
            }

            return null;
        }

        /// <summary>
        /// 型 T から指定名のフィールド/プロパティを検索して型を返す。
        /// </summary>
        private static Type _ResolveMemberType(Type declaringType, string memberName)
        {
            if (declaringType == null || string.IsNullOrEmpty(memberName)) return null;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            var fi = declaringType.GetField(memberName, flags);
            if (fi != null) return fi.FieldType;

            var pi = declaringType.GetProperty(memberName, flags);
            if (pi != null) return pi.PropertyType;

            return null;
        }

        public override string ToString() => $"{targetTypeName}/{propertyPath}";
    }
}
