// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// 収集された型情報を保持するデータクラス
    /// </summary>
    public class TypeReflectionData
    {
        /// <summary>
        /// 対象の型
        /// </summary>
        public readonly Type type;

        /// <summary>
        /// 型名
        /// </summary>
        public readonly string typeName;

        /// <summary>
        /// static型かどうか（abstract && sealed）
        /// </summary>
        public readonly bool isStatic;

        /// <summary>
        /// メンバー（プロパティ/フィールド）情報の配列
        /// </summary>
        public readonly MemberReflectionData[] members;

        /// <summary>
        /// メソッド情報の配列
        /// </summary>
        public readonly MethodReflectionData[] methods;

        public TypeReflectionData(
            Type type,
            string typeName,
            bool isStatic,
            MemberReflectionData[] members,
            MethodReflectionData[] methods)
        {
            this.type = type;
            this.typeName = typeName ?? type?.Name;
            this.isStatic = isStatic;
            this.members = members ?? Array.Empty<MemberReflectionData>();
            this.methods = methods ?? Array.Empty<MethodReflectionData>();
        }
    }

    /// <summary>
    /// メンバー（プロパティ/フィールド）の情報
    /// </summary>
    public class MemberReflectionData
    {
        /// <summary>
        /// メンバー名
        /// </summary>
        public readonly string name;

        /// <summary>
        /// 値の型
        /// </summary>
        public readonly Type valueType;

        /// <summary>
        /// staticメンバーかどうか
        /// </summary>
        public readonly bool isStatic;

        /// <summary>
        /// 読み取り専用かどうか
        /// </summary>
        public readonly bool isReadOnly;

        /// <summary>
        /// プロパティかどうか（falseの場合はフィールド）
        /// </summary>
        public readonly bool isProperty;

        /// <summary>
        /// 内部で使用するMemberInfo
        /// </summary>
        internal readonly MemberInfo memberInfo;

        public MemberReflectionData(
            string name,
            Type valueType,
            bool isStatic,
            bool isReadOnly,
            bool isProperty,
            MemberInfo memberInfo)
        {
            this.name = name;
            this.valueType = valueType;
            this.isStatic = isStatic;
            this.isReadOnly = isReadOnly;
            this.isProperty = isProperty;
            this.memberInfo = memberInfo;
        }

        /// <summary>
        /// PropertyInfoからMemberReflectionDataを作成
        /// </summary>
        public static MemberReflectionData FromProperty(PropertyInfo propertyInfo)
        {
            if (propertyInfo == null) return null;

            var isStatic = propertyInfo.GetMethod?.IsStatic ?? propertyInfo.SetMethod?.IsStatic ?? false;
            var isReadOnly = !propertyInfo.CanWrite;

            return new MemberReflectionData(
                propertyInfo.Name,
                propertyInfo.PropertyType,
                isStatic,
                isReadOnly,
                isProperty: true,
                propertyInfo);
        }

        /// <summary>
        /// FieldInfoからMemberReflectionDataを作成
        /// </summary>
        public static MemberReflectionData FromField(FieldInfo fieldInfo)
        {
            if (fieldInfo == null) return null;

            var isReadOnly = fieldInfo.IsInitOnly || fieldInfo.IsLiteral;

            return new MemberReflectionData(
                fieldInfo.Name,
                fieldInfo.FieldType,
                fieldInfo.IsStatic,
                isReadOnly,
                isProperty: false,
                fieldInfo);
        }
    }

    /// <summary>
    /// メソッドの情報
    /// </summary>
    public class MethodReflectionData
    {
        /// <summary>
        /// メソッド名
        /// </summary>
        public readonly string name;

        /// <summary>
        /// 戻り値の型
        /// </summary>
        public readonly Type returnType;

        /// <summary>
        /// パラメーター情報
        /// </summary>
        public readonly ParameterInfo[] parameters;

        /// <summary>
        /// staticメソッドかどうか
        /// </summary>
        public readonly bool isStatic;

        /// <summary>
        /// 内部で使用するMethodInfo
        /// </summary>
        internal readonly MethodInfo methodInfo;

        public MethodReflectionData(
            string name,
            Type returnType,
            ParameterInfo[] parameters,
            bool isStatic,
            MethodInfo methodInfo)
        {
            this.name = name;
            this.returnType = returnType;
            this.parameters = parameters ?? Array.Empty<ParameterInfo>();
            this.isStatic = isStatic;
            this.methodInfo = methodInfo;
        }

        /// <summary>
        /// MethodInfoからMethodReflectionDataを作成
        /// </summary>
        public static MethodReflectionData FromMethod(MethodInfo methodInfo)
        {
            if (methodInfo == null) return null;

            return new MethodReflectionData(
                methodInfo.Name,
                methodInfo.ReturnType,
                methodInfo.GetParameters(),
                methodInfo.IsStatic,
                methodInfo);
        }
    }
}
