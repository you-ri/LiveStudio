// Copyright (c) You-Ri, 2026

using System;
using System.Reflection;
using UnityEngine;

namespace Lilium.RemoteControl.Reflection
{
    /// <summary>
    /// メソッド呼び出しを提供するシステム
    /// </summary>
    public static class MethodInvokeSystem
    {
        private const BindingFlags kDefaultFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        /// <summary>
        /// MethodInvokeDataを使用してメソッドを呼び出す
        /// </summary>
        /// <param name="target">対象オブジェクト（staticの場合はnull可）</param>
        /// <param name="invokeData">呼び出しデータ</param>
        /// <param name="args">引数配列</param>
        /// <returns>戻り値（voidの場合はnull）</returns>
        public static object Invoke(object target, in MethodInvokeData invokeData, object[] args = null)
        {
            if (!invokeData.isValid)
            {
                Debug.LogWarning("[Reflection] Invalid MethodInvokeData");
                return null;
            }

            // デフォルト引数を補完
            args = _ResolveOptionalParameters(invokeData.methodInfo, args);

            // staticメソッドの場合
            if (invokeData.isStatic)
            {
                return _InvokeStatic(invokeData.methodInfo, args);
            }

            // インスタンスメソッドの場合
            if (target == null)
            {
                Debug.LogWarning("[Reflection] Target is null for instance method invocation");
                return null;
            }

            return _InvokeInstance(target, invokeData.methodInfo, args);
        }

        /// <summary>
        /// メソッド名を使用してメソッドを呼び出す
        /// </summary>
        /// <param name="target">対象オブジェクト</param>
        /// <param name="methodName">メソッド名</param>
        /// <param name="args">引数配列</param>
        /// <returns>戻り値（voidの場合はnull）</returns>
        public static object Invoke(object target, string methodName, object[] args = null)
        {
            if (target == null)
            {
                Debug.LogWarning("[Reflection] Target is null");
                return null;
            }

            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogWarning("[Reflection] Method name is null or empty");
                return null;
            }

            var methodInfo = GetMethodInfo(target.GetType(), methodName, args);
            if (methodInfo == null)
            {
                Debug.LogWarning($"[Reflection] Method '{methodName}' not found in type {target.GetType()}");
                return null;
            }

            return _InvokeInstance(target, methodInfo, _ResolveOptionalParameters(methodInfo, args));
        }

        /// <summary>
        /// staticメソッドをメソッド名で呼び出す
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="methodName">メソッド名</param>
        /// <param name="args">引数配列</param>
        /// <returns>戻り値（voidの場合はnull）</returns>
        public static object InvokeStatic(Type type, string methodName, object[] args = null)
        {
            if (type == null)
            {
                Debug.LogWarning("[Reflection] Type is null");
                return null;
            }

            if (string.IsNullOrEmpty(methodName))
            {
                Debug.LogWarning("[Reflection] Method name is null or empty");
                return null;
            }

            var methodInfo = GetMethodInfo(type, methodName, args);
            if (methodInfo == null)
            {
                Debug.LogWarning($"[Reflection] Static method '{methodName}' not found in type {type}");
                return null;
            }

            if (!methodInfo.IsStatic)
            {
                Debug.LogWarning($"[Reflection] Method '{methodName}' is not static");
                return null;
            }

            return _InvokeStatic(methodInfo, _ResolveOptionalParameters(methodInfo, args));
        }

        /// <summary>
        /// MethodInfoからMethodInvokeDataを作成
        /// </summary>
        public static MethodInvokeData CreateInvokeData(MethodInfo methodInfo)
        {
            return MethodInvokeData.FromMethod(methodInfo);
        }

        /// <summary>
        /// 型からメソッド情報を取得（単純な名前検索）
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="methodName">メソッド名</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>MethodInfo（存在しない場合はnull）</returns>
        public static MethodInfo GetMethodInfo(Type type, string methodName, BindingFlags flags = kDefaultFlags)
        {
            return TypeReflectionSystem.GetMethod(type, methodName, flags);
        }

        /// <summary>
        /// 型からメソッド情報を取得（引数の型に基づいてオーバーロードを解決）
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="methodName">メソッド名</param>
        /// <param name="args">引数配列（オーバーロード解決に使用）</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>MethodInfo（存在しない場合はnull）</returns>
        public static MethodInfo GetMethodInfo(Type type, string methodName, object[] args, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return null;

            // 引数がない場合はシンプルな検索
            if (args == null || args.Length == 0)
            {
                return TypeReflectionSystem.GetMethod(type, methodName, flags);
            }

            // 同名のすべてのメソッドを取得
            var methods = TypeReflectionSystem.GetMethods(type, methodName, flags);
            if (methods == null || methods.Length == 0)
                return null;

            // 引数の数と型が一致するメソッドを検索
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                    continue;

                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var argValue = args[i];

                    // nullは参照型に対応
                    if (argValue == null)
                    {
                        if (paramType.IsValueType && Nullable.GetUnderlyingType(paramType) == null)
                        {
                            matches = false;
                            break;
                        }
                    }
                    else if (!paramType.IsAssignableFrom(argValue.GetType()))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return method;
            }

            // 完全一致が見つからない場合は最初のメソッドを返す（互換性のため）
            return methods.Length > 0 ? methods[0] : null;
        }

        /// <summary>
        /// 型から引数の型を指定してメソッド情報を取得
        /// </summary>
        /// <param name="type">対象の型</param>
        /// <param name="methodName">メソッド名</param>
        /// <param name="parameterTypes">パラメーターの型配列</param>
        /// <param name="flags">バインディングフラグ</param>
        /// <returns>MethodInfo（存在しない場合はnull）</returns>
        public static MethodInfo GetMethodInfo(Type type, string methodName, Type[] parameterTypes, BindingFlags flags = kDefaultFlags)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
                return null;

            // パラメーター型が指定されていない場合はシンプルな検索
            if (parameterTypes == null || parameterTypes.Length == 0)
            {
                return TypeReflectionSystem.GetMethod(type, methodName, flags);
            }

            // 型配列を使用して直接検索
            return type.GetMethod(methodName, flags, null, parameterTypes, null);
        }

        #region Private Methods

        /// <summary>
        /// 引数配列をメソッドのパラメータ数に合わせて補完する。
        /// デフォルト値を持つoptionalパラメータにはType.Missingを設定する。
        /// </summary>
        private static object[] _ResolveOptionalParameters(MethodInfo methodInfo, object[] args)
        {
            var parameters = methodInfo.GetParameters();
            int paramCount = parameters.Length;
            int argCount = args?.Length ?? 0;

            // 引数が足りている場合はそのまま返す
            if (argCount >= paramCount)
                return args;

            // 不足分がすべてoptionalパラメータか確認
            for (int i = argCount; i < paramCount; i++)
            {
                if (!parameters[i].IsOptional)
                {
                    // 必須パラメータが不足している場合は補完せずそのまま返す（Invokeでエラーになる）
                    return args;
                }
            }

            // 新しい引数配列を作成し、不足分をType.Missingで埋める
            var resolved = new object[paramCount];
            for (int i = 0; i < argCount; i++)
            {
                resolved[i] = args[i];
            }
            for (int i = argCount; i < paramCount; i++)
            {
                resolved[i] = Type.Missing;
            }
            return resolved;
        }

        private static object _InvokeStatic(MethodInfo methodInfo, object[] args)
        {
            try
            {
                return methodInfo.Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError($"[Reflection] Method invocation failed: {ex.InnerException?.Message ?? ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reflection] Method invocation failed: {ex.Message}");
                return null;
            }
        }

        private static object _InvokeInstance(object target, MethodInfo methodInfo, object[] args)
        {
            try
            {
                return methodInfo.Invoke(target, args);
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError($"[Reflection] Method invocation failed: {ex.InnerException?.Message ?? ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Reflection] Method invocation failed: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}
