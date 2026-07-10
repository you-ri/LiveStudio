// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Source Generator が ModuleInitializer 経由で登録する、メンバーごとの高速アクセサ
    /// (get/set デリゲート) テーブル。reflection (PropertyInfo/FieldInfo) の invoke を回避する。
    ///
    /// キーは「メンバーを宣言した型 (DeclaringType) + メンバー名」。生成デリゲートは
    /// DeclaringType にキャストして直接アクセスするため、派生インスタンスでもアップキャストで動作する。
    /// 登録されていないメンバー (struct 型 / 非アクセスメンバー / 動的ロード型) は呼び出し側で
    /// reflection にフォールバックする。
    ///
    /// <see cref="ExposedClassDeclarationOrderTable"/> と同じく ExposedClass の cctor を巻き込まない
    /// よう独立クラスに分離している。
    /// </summary>
    public static class ExposedMemberAccessorTable
    {
        private readonly struct Accessor
        {
            public readonly Func<object, object> getter;
            public readonly Action<object, object> setter;

            // 値型メンバー向けの型付きアクセサ (Func<object,T> / Action<object,T>)。
            // object を経由しないため値型の boxing を避けられる。参照型メンバーや未対応メンバーでは null。
            public readonly Delegate typedGetter;
            public readonly Delegate typedSetter;

            public Accessor(Func<object, object> getter, Action<object, object> setter,
                Delegate typedGetter, Delegate typedSetter)
            {
                this.getter = getter;
                this.setter = setter;
                this.typedGetter = typedGetter;
                this.typedSetter = typedSetter;
            }
        }

        // key = DeclaringType, value = メンバー名 → アクセサ。
        private static readonly Dictionary<Type, Dictionary<string, Accessor>> _table
            = new Dictionary<Type, Dictionary<string, Accessor>>();

        /// <summary>
        /// Source Generator から呼ばれる登録 API。setter は read-only メンバーでは null。
        /// </summary>
        public static void Register(Type declaringType, string memberName,
            Func<object, object> getter, Action<object, object> setter)
            => Register(declaringType, memberName, getter, setter, null, null);

        /// <summary>
        /// 型付きアクセサ付きの登録 API (値型メンバー用)。typedGetter/typedSetter は
        /// <c>Func&lt;object,T&gt;</c> / <c>Action&lt;object,T&gt;</c>。未対応側は null。
        /// </summary>
        public static void Register(Type declaringType, string memberName,
            Func<object, object> getter, Action<object, object> setter,
            Delegate typedGetter, Delegate typedSetter)
        {
            if (declaringType == null || string.IsNullOrEmpty(memberName)) return;

            if (!_table.TryGetValue(declaringType, out var map))
            {
                map = new Dictionary<string, Accessor>();
                _table[declaringType] = map;
            }
            map[memberName] = new Accessor(getter, setter, typedGetter, typedSetter);
        }

        /// <summary>
        /// メンバーの get/set デリゲートを取得。未登録なら false を返し、out は null。
        /// </summary>
        public static bool TryGet(Type declaringType, string memberName,
            out Func<object, object> getter, out Action<object, object> setter)
            => TryGet(declaringType, memberName, out getter, out setter, out _, out _);

        /// <summary>
        /// object 版と型付き版のデリゲートをまとめて取得。未登録なら false を返し、out は null。
        /// typedGetter/typedSetter は値型メンバーのみ non-null (<c>Func&lt;object,T&gt;</c> / <c>Action&lt;object,T&gt;</c>)。
        /// </summary>
        public static bool TryGet(Type declaringType, string memberName,
            out Func<object, object> getter, out Action<object, object> setter,
            out Delegate typedGetter, out Delegate typedSetter)
        {
            if (declaringType != null && memberName != null
                && _table.TryGetValue(declaringType, out var map)
                && map.TryGetValue(memberName, out var accessor))
            {
                getter = accessor.getter;
                setter = accessor.setter;
                typedGetter = accessor.typedGetter;
                typedSetter = accessor.typedSetter;
                return true;
            }

            getter = null;
            setter = null;
            typedGetter = null;
            typedSetter = null;
            return false;
        }
    }
}
