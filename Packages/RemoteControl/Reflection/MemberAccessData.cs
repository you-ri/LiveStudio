// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// メンバーアクセスに必要な情報
    /// </summary>
    public readonly struct MemberAccessData
    {
        /// <summary>
        /// メンバー情報（PropertyInfoまたはFieldInfo）
        /// </summary>
        public readonly MemberInfo memberInfo;

        /// <summary>
        /// staticメンバーかどうか
        /// </summary>
        public readonly bool isStatic;

        /// <summary>
        /// 読み取り専用かどうか
        /// </summary>
        public readonly bool isReadOnly;

        /// <summary>
        /// 値の型
        /// </summary>
        public readonly Type valueType;

        /// <summary>
        /// プロパティかどうか（falseの場合はフィールド）
        /// </summary>
        public readonly bool isProperty;

        /// <summary>
        /// 有効なアクセスデータかどうか
        /// </summary>
        public bool isValid => memberInfo != null;

        /// <summary>
        /// MemberAccessDataを作成
        /// </summary>
        public MemberAccessData(MemberInfo memberInfo, bool isStatic, bool isReadOnly, Type valueType, bool isProperty)
        {
            this.memberInfo = memberInfo;
            this.isStatic = isStatic;
            this.isReadOnly = isReadOnly;
            this.valueType = valueType;
            this.isProperty = isProperty;
        }

        /// <summary>
        /// PropertyInfoからMemberAccessDataを作成
        /// </summary>
        public static MemberAccessData FromProperty(PropertyInfo propertyInfo)
        {
            if (propertyInfo == null)
                return default;

            var isStatic = propertyInfo.GetMethod?.IsStatic ?? propertyInfo.SetMethod?.IsStatic ?? false;
            var isReadOnly = !propertyInfo.CanWrite;

            return new MemberAccessData(
                propertyInfo,
                isStatic,
                isReadOnly,
                propertyInfo.PropertyType,
                isProperty: true);
        }

        /// <summary>
        /// FieldInfoからMemberAccessDataを作成
        /// </summary>
        public static MemberAccessData FromField(FieldInfo fieldInfo)
        {
            if (fieldInfo == null)
                return default;

            var isReadOnly = fieldInfo.IsInitOnly || fieldInfo.IsLiteral;

            return new MemberAccessData(
                fieldInfo,
                fieldInfo.IsStatic,
                isReadOnly,
                fieldInfo.FieldType,
                isProperty: false);
        }

        /// <summary>
        /// MemberInfoからMemberAccessDataを作成
        /// </summary>
        public static MemberAccessData FromMember(MemberInfo memberInfo)
        {
            if (memberInfo == null)
                return default;

            if (memberInfo is PropertyInfo propInfo)
                return FromProperty(propInfo);

            if (memberInfo is FieldInfo fieldInfo)
                return FromField(fieldInfo);

            return default;
        }

        /// <summary>
        /// 内部のPropertyInfoを取得（プロパティの場合のみ）
        /// </summary>
        public PropertyInfo AsPropertyInfo()
        {
            return memberInfo as PropertyInfo;
        }

        /// <summary>
        /// 内部のFieldInfoを取得（フィールドの場合のみ）
        /// </summary>
        public FieldInfo AsFieldInfo()
        {
            return memberInfo as FieldInfo;
        }
    }
}
