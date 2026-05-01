// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// 他の ExposedObject のプロパティへの参照を表す値型。
    /// FusionPage のような「集約ページ」で、実体の ExposedProperty を代理露出するために使う。
    ///
    /// `[ExposedField] public static readonly ExposedPropertyRef smoothness = ExposedPropertyRef.To&lt;AvatarProvider&gt;("_smoothness");`
    /// のように宣言し、取得/設定/dirty/revert のすべてを参照先の ExposedProperty に委譲する。
    ///
    /// 参照解決は実行時に ExposedObjectRegistry から targetTypeName を引く。対象が未登録の場合は null を返し、
    /// その場合は安全に fallback する (値は null/0、dirty は false)。
    /// </summary>
    public readonly struct ExposedPropertyRef
    {
        /// <summary>対象 ExposedObject の id (ExposedObjectRegistry のキー、通常は ExposedClass.typeName)</summary>
        public readonly string targetTypeName;

        /// <summary>対象 ExposedObject 内のプロパティパス (例: "_smoothness")</summary>
        public readonly string propertyPath;

        /// <summary>
        /// 対象プロパティの値型 (例: typeof(float))。
        /// TypeDefinition 出力などで、RemoteApp に「このプロパティは実質 float」と伝えるために使う。
        /// 解決できない場合は null。
        /// </summary>
        public readonly Type targetValueType;

        public ExposedPropertyRef(string targetTypeName, string propertyPath, Type targetValueType)
        {
            this.targetTypeName = targetTypeName;
            this.propertyPath = propertyPath;
            this.targetValueType = targetValueType;
        }

        public bool isValid => !string.IsNullOrEmpty(targetTypeName) && !string.IsNullOrEmpty(propertyPath);

        /// <summary>
        /// 型 T の ExposedClass typeName と指定パスから参照を構築する。
        /// targetValueType は T のメンバ型からリフレクションで解決する。
        /// </summary>
        public static ExposedPropertyRef To<T>(string propertyPath)
        {
            // 静的初期化順序の影響を受けないよう、未登録でも T のクラス名にフォールバックする
            ExposedClass.TryGet(typeof(T), out var exposedClass);
            var typeName = exposedClass?.typeName ?? typeof(T).Name;
            var memberType = _ResolveMemberType(typeof(T), propertyPath);
            return new ExposedPropertyRef(typeName, propertyPath, memberType);
        }

        /// <summary>
        /// 明示的に targetTypeName/propertyPath/valueType を指定して参照を構築する。
        /// ExposedClass の typeName が既知な場合に使う。
        /// </summary>
        public static ExposedPropertyRef Create(string targetTypeName, string propertyPath, Type targetValueType)
        {
            return new ExposedPropertyRef(targetTypeName, propertyPath, targetValueType);
        }

        /// <summary>
        /// 参照先の ExposedProperty を解決する。対象が未登録なら null。
        /// 解決順序:
        /// 1. ExposedObjectRegistry に id=targetTypeName で登録されているオブジェクト
        /// 2. targetTypeName が static ExposedClass なら、GetOrCreate で自動生成
        /// 3. targetTypeName が Component 派生の ExposedClass なら、シーン上のインスタンスを検索 (FindFirstObjectByType)。
        ///    既に Registry に登録済みならそれを使い、未登録なら CreateUnregistered でラップする。
        /// </summary>
        public ExposedProperty? Resolve()
        {
            if (!isValid) return null;

            // 1. id 登録済み
            var owner = ExposedObjectRegistry.FindById(targetTypeName);
            if (owner != null) return owner.FindProperty(propertyPath);

            // 2/3. ExposedClass から型を引いて解決
            var exposedClass = ExposedClass.Find(targetTypeName);
            if (exposedClass == null) return null;

            if (exposedClass.isStatic)
            {
                var staticOwner = ExposedObjectRegistry.GetOrCreate(exposedClass.typeName, exposedClass, null);
                return staticOwner?.FindProperty(propertyPath);
            }

            if (exposedClass.type != null && typeof(Component).IsAssignableFrom(exposedClass.type))
            {
                var target = UnityEngine.Object.FindFirstObjectByType(exposedClass.type, FindObjectsInactive.Include);
                if (target == null) return null;

                // 既存をまず target 参照で探す (他経路で CreateUnregistered 済みのケース)
                var existing = ExposedObjectRegistry.FindByTarget(target);
                if (existing != null)
                {
                    // id 未付与なら typeName を後付けで割り当て、FindById でも引けるようにする
                    if (!existing.hasId)
                    {
                        existing.AssignId(exposedClass.typeName);
                    }
                    return existing.FindProperty(propertyPath);
                }

                // target 付きで登録済み ExposedObject を作成 (コンストラクタで default capture も走る)
                var instanceOwner = ExposedObjectRegistry.GetOrCreate(exposedClass.typeName, exposedClass, target);
                return instanceOwner?.FindProperty(propertyPath);
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
