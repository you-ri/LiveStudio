// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// メソッド呼び出しに必要な情報
    /// </summary>
    public readonly struct MethodInvokeData
    {
        /// <summary>
        /// メソッド情報
        /// </summary>
        public readonly MethodInfo methodInfo;

        /// <summary>
        /// staticメソッドかどうか
        /// </summary>
        public readonly bool isStatic;

        /// <summary>
        /// 戻り値の型
        /// </summary>
        public readonly Type returnType;

        /// <summary>
        /// パラメーター情報
        /// </summary>
        public readonly ParameterInfo[] parameters;

        /// <summary>
        /// 有効な呼び出しデータかどうか
        /// </summary>
        public bool isValid => methodInfo != null;

        /// <summary>
        /// パラメーターの数
        /// </summary>
        public int parameterCount => parameters?.Length ?? 0;

        /// <summary>
        /// パラメーターがないかどうか
        /// </summary>
        public bool hasNoParameters => parameterCount == 0;

        /// <summary>
        /// 戻り値がvoidかどうか
        /// </summary>
        public bool isVoid => returnType == typeof(void);

        /// <summary>
        /// MethodInvokeDataを作成
        /// </summary>
        public MethodInvokeData(MethodInfo methodInfo, bool isStatic, Type returnType, ParameterInfo[] parameters)
        {
            this.methodInfo = methodInfo;
            this.isStatic = isStatic;
            this.returnType = returnType;
            this.parameters = parameters ?? Array.Empty<ParameterInfo>();
        }

        /// <summary>
        /// MethodInfoからMethodInvokeDataを作成
        /// </summary>
        public static MethodInvokeData FromMethod(MethodInfo methodInfo)
        {
            if (methodInfo == null)
                return default;

            return new MethodInvokeData(
                methodInfo,
                methodInfo.IsStatic,
                methodInfo.ReturnType,
                methodInfo.GetParameters());
        }

        /// <summary>
        /// メソッド名を取得
        /// </summary>
        public string name => methodInfo?.Name;
    }
}
