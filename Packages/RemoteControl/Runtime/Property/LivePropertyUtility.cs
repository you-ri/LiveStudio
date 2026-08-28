// Copyright (c) You-Ri, 2026
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

using UnityEngine;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// LivePropertyのコア操作を担当するユーティリティクラス。
    /// 型判定、プロパティアクセス、デフォルト値管理などの基本操作を提供する。
    /// </summary>
    public static class LivePropertyUtility
    {
        // -------------------------------------------------------
        // Type utilities
        // -------------------------------------------------------

        internal static bool IsArrayType(Type type)
        {
            return GetCollectionElementType(type) != null;
        }

        internal static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType == null) return null;
            if (collectionType.IsArray) return collectionType.GetElementType();
            if (collectionType.IsGenericType)
            {
                var genericDef = collectionType.GetGenericTypeDefinition();
                if (genericDef == typeof(List<>) || genericDef == typeof(IEnumerable<>))
                    return collectionType.GetGenericArguments()[0];
            }
            return null;
        }

        /// <summary>
        /// コレクション（IList, Array, IEnumerable）の要素数を返す。
        /// </summary>
        internal static int GetCollectionLength(object value)
        {
            if (value == null) return 0;
            if (value is System.Collections.IList list) return list.Count;
            if (value is System.Array array) return array.Length;
            if (value is System.Collections.IEnumerable enumerable)
            {
                int count = 0;
                foreach (var _ in enumerable) count++;
                return count;
            }
            return 0;
        }

        /// <summary>
        /// コレクション（IList, Array, IEnumerable）からインデックスで要素を取得する。
        /// </summary>
        internal static object GetCollectionElement(object value, int index)
        {
            if (value == null || index < 0) return null;
            if (value is System.Collections.IList list)
                return index < list.Count ? list[index] : null;
            if (value is System.Array array)
                return index < array.Length ? array.GetValue(index) : null;
            if (value is System.Collections.IEnumerable enumerable)
            {
                int idx = 0;
                foreach (var item in enumerable)
                {
                    if (idx == index) return item;
                    idx++;
                }
            }
            return null;
        }

        public static Guid GetGuidFromPropertyName(PropertyName propertyName)
        {
            var nameStr = propertyName.ToString();
            if (string.IsNullOrEmpty(nameStr))
            {
                return Guid.Empty;
            }

            var guidPart = nameStr.Split(':')[0];
            if (Guid.TryParse(guidPart, out var guid))
            {
                return guid;
            }

            Debug.LogWarning($"[RemoteControl] Invalid property name format: {nameStr}");
            return Guid.Empty;
        }

        internal static LivePropertyType GetPropertyType(LiveObjectHandle liveObject, string propertyName)
        {
            if (liveObject == null) throw new ArgumentNullException(nameof(liveObject));
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));

            if (string.IsNullOrEmpty(propertyName)) return null;

            return liveObject.propertyTypes.FirstOrDefault(p => p.properyInfo != null && p.properyInfo.Name == propertyName);
        }

        internal static LivePropertyType[] MakePropertyTypes(Type type, LivePropertyDefine[] defines)
        {
            if (defines == null) throw new ArgumentNullException(nameof(defines));

            return defines.Select(e =>
            {
                var memberInfo = MemberAccessSystem.GetMemberInfo(type, e.path);
                if (memberInfo != null)
                {
                    FieldInfo shadowField = null;
                    if (!string.IsNullOrEmpty(e.shadowFieldPath))
                    {
                        // Type.GetField は基底クラスの private フィールドを返さないため、
                        // 階層を遡って DeclaredOnly で検索する。
                        // (LiveUnityObjectProxy._name など、基底に shadow field を置く構造に対応)
                        for (var t = type; t != null && shadowField == null; t = t.BaseType)
                        {
                            shadowField = t.GetField(e.shadowFieldPath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                        }
                        if (shadowField == null)
                        {
                            Debug.LogWarning($"[RemoteControl] Shadow field '{e.shadowFieldPath}' not found on {type.Name}; falling back to direct property access for '{e.name}'.");
                        }
                    }
                    return new LivePropertyType(e.name, memberInfo, e.isPersistable, shadowField, e.persistScope,
                        controlOverride: e.control, labelOverride: e.label, helpOverride: e.help, sectionOverride: e.section,
                        readOnlyOverride: e.isReadOnly, lane: e.lane);
                }

                Debug.LogError($"[RemoteControl] Member not found for {type.Name}.{e.path}");
                return null;

            }).Where(e => e != null).ToArray();
        }

        /// <summary>
        /// <see cref="MakePropertyTypes"/> の関数版。Define の path でメソッドを解決し、
        /// 属性なしで <see cref="LiveFunctionType"/> を構築する (メタデータは Define 側の値を優先)。
        /// オーバーロードは非対応で、同名メソッドは最初に見つかったものを使う。
        /// </summary>
        internal static LiveFunctionType[] MakeFunctionTypes(Type type, LiveFunctionDefine[] defines)
        {
            if (defines == null || defines.Length == 0) return null;

            return defines.Select(e =>
            {
                // Type.GetMethod(name) はオーバーロードが存在すると AmbiguousMatchException を投げるため、
                // 列挙して同名メソッドのうち引数が最少のものを選ぶ (RemoteApp のボタンは主に引数なし想定)。
                MethodInfo methodInfo = null;
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (m.Name != e.path || m.IsGenericMethodDefinition) continue;
                    if (methodInfo == null || m.GetParameters().Length < methodInfo.GetParameters().Length)
                    {
                        methodInfo = m;
                    }
                }
                if (methodInfo != null)
                {
                    return new LiveFunctionType(e.name ?? methodInfo.Name, methodInfo,
                        labelOverride: e.label, iconOverride: e.icon, helpOverride: e.help, sectionOverride: e.section);
                }

                Debug.LogError($"[RemoteControl] Method not found for {type.Name}.{e.path}");
                return null;

            }).Where(e => e != null).ToArray();
        }

        // -------------------------------------------------------
        // Property access
        // -------------------------------------------------------

        /// <summary>
        /// プロパティアクセスの共通バリデーション。
        /// </summary>
        /// <returns>アクセス可能ならtrue</returns>
        private static bool _ValidatePropertyAccess(object obj, in LivePropertyType propertyType, bool throwOnNull)
        {
            // staticの場合はobjがnullでも許可
            if (!propertyType.isStatic && obj == null)
            {
                if (throwOnNull) throw new ArgumentNullException(nameof(obj));
                return false;
            }
            // 破棄済みUnityオブジェクトへのアクセスを防止
            if (!propertyType.isStatic && obj is UnityEngine.Object unityObj && unityObj == null)
                return false;
            if (propertyType == null) throw new ArgumentNullException(nameof(propertyType));
            if (!propertyType.isValid) throw new ArgumentException("Property must have either PropertyInfo or FieldInfo");

            // ポリモーフィック型不一致を検出した場合は黙って skip する。
            // (BCL の ArgumentException 防止が目的。警告は呼び出し側 (例: _FromJsonProperty)
            //  で論理操作 1 回につき 1 度だけ出すため、ここではログを出さない)
            if (!IsInstanceCompatible(obj, propertyType))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// <see cref="_ValidatePropertyAccess"/> の例外を投げない版。Try 系 API (TryGetValue/TrySetValue) 用。
        /// null / 破棄済み UnityObject / 型非互換 / 無効な propertyType のいずれでも false を返す。
        /// </summary>
        internal static bool CanAccess(object obj, in LivePropertyType propertyType)
        {
            if (propertyType == null || !propertyType.isValid) return false;
            if (!propertyType.isStatic)
            {
                if (obj == null) return false;
                if (obj is UnityEngine.Object unityObj && unityObj == null) return false;
            }
            return IsInstanceCompatible(obj, propertyType);
        }

        /// <summary>
        /// 指定された obj が propertyType の宣言型と互換性があるかを返す (ログ出力なし)。
        /// 静的メンバー / 配列要素 / null obj はチェック対象外として true 扱い。
        /// </summary>
        internal static bool IsInstanceCompatible(object obj, in LivePropertyType propertyType)
        {
            if (propertyType == null || !propertyType.isValid) return true;
            if (propertyType.isStatic || propertyType.isArrayElement) return true;
            if (obj == null) return true;

            var declaringType = propertyType.properyInfo?.DeclaringType ?? propertyType.fieldInfo?.DeclaringType;
            if (declaringType == null) return true;
            return declaringType.IsAssignableFrom(obj.GetType());
        }

        /// <summary>
        /// IsInstanceCompatible が false のときに警告ログを 1 回出す。互換ならログを出さず false を返す。
        /// 論理操作 1 回 (例: シリアライザのプロパティ適用) の入口で呼び、後続の Get/Set 経路を短絡させる用途。
        /// </summary>
        /// <returns>型不一致を検出したら true。</returns>
        internal static bool WarnIfInstanceMismatch(object obj, in LivePropertyType propertyType)
        {
            if (IsInstanceCompatible(obj, propertyType)) return false;

            var declaringType = propertyType.properyInfo?.DeclaringType ?? propertyType.fieldInfo?.DeclaringType;
            var memberName = propertyType.properyInfo?.Name ?? propertyType.fieldInfo?.Name;
            Debug.LogWarning($"[RemoteControl] Property '{declaringType?.Name}.{memberName}' is not defined on actual instance of type '{obj.GetType().Name}'. Skipping (likely polymorphic type mismatch on load).");
            return true;
        }

        internal static bool SetValueRaw(object obj, in LivePropertyType propertyType, object value)
        {
            if (!_ValidatePropertyAccess(obj, propertyType, throwOnNull: true)) return false;

            if (propertyType.isReadOnly) return false;

            // 配列要素の場合
            if (propertyType.isArrayElement)
            {
                if (obj is System.Collections.IList list)
                {
                    if (propertyType.arrayIndex >= 0 && propertyType.arrayIndex < list.Count)
                        list[propertyType.arrayIndex] = value;
                }
                else if (obj != null && obj.GetType().IsArray)
                {
                    var array = (Array)obj;
                    if (propertyType.arrayIndex >= 0 && propertyType.arrayIndex < array.Length)
                        array.SetValue(value, propertyType.arrayIndex);
                }
            }
            else if (propertyType.properyInfo != null)
            {
                if (value != null && !propertyType.properyInfo.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    Debug.LogWarning($"[RemoteControl] Type mismatch: cannot assign {value.GetType().Name} to {propertyType.properyInfo.PropertyType.Name}");
                    return false;
                }
                // staticの場合はobjにnullを渡す。Source Generator の高速 setter があれば reflection を回避する。
                if (propertyType.setter != null)
                    propertyType.setter(propertyType.isStatic ? null : obj, value);
                else
                    propertyType.properyInfo.SetValue(propertyType.isStatic ? null : obj, value);
            }
            else if (propertyType.fieldInfo != null)
            {
                if (value != null && !propertyType.fieldInfo.FieldType.IsAssignableFrom(value.GetType()))
                {
                    Debug.LogWarning($"[RemoteControl] Type mismatch: cannot assign {value.GetType().Name} to {propertyType.fieldInfo.FieldType.Name}");
                    return false;
                }
                // staticの場合はobjにnullを渡す。Source Generator の高速 setter があれば reflection を回避する。
                if (propertyType.setter != null)
                    propertyType.setter(propertyType.isStatic ? null : obj, value);
                else
                    propertyType.fieldInfo.SetValue(propertyType.isStatic ? null : obj, value);
            }
            else
            {
                return false;
            }

            return true;
        }

        internal static object GetValueRaw(object obj, in LivePropertyType propertyType)
        {
            if (!_ValidatePropertyAccess(obj, propertyType, throwOnNull: true)) return null;

            // 配列要素の場合
            if (propertyType.isArrayElement)
            {
                return GetCollectionElement(obj, propertyType.arrayIndex);
            }

            // Source Generator の高速 getter があれば reflection を回避する。
            if (propertyType.getter != null)
            {
                return propertyType.getter(propertyType.isStatic ? null : obj);
            }

            if (propertyType.properyInfo != null)
            {
                // staticの場合はobjにnullを渡す
                return propertyType.properyInfo.GetValue(propertyType.isStatic ? null : obj);
            }
            else if (propertyType.fieldInfo != null)
            {
                // staticの場合はobjにnullを渡す
                return propertyType.fieldInfo.GetValue(propertyType.isStatic ? null : obj);
            }

            return null;
        }

        // -------------------------------------------------------
        // Reset / Default
        // -------------------------------------------------------

        public static bool ResetValue(LiveObjectHandle liveObject, in LiveProperty property)
        {
            if (liveObject == null) throw new ArgumentNullException(nameof(liveObject));

            property.RevertValue();

            return true;
        }

        public static void SetDefault(LiveObjectHandle liveObject)
        {
            if (liveObject == null) throw new ArgumentNullException(nameof(liveObject));

            // 全プロパティの現在値をJObjectとしてスナップショット保存
            LiveObjectDefaultRegistry.CaptureDefaults(liveObject, DefaultLiveObjectResolver.Instance);
        }

        internal static object CreateDefaultElement(Type elementType)
        {
            // [LiveDefault]が付与されたstaticプロパティを検索
            var properties = elementType.GetProperties(BindingFlags.Public | BindingFlags.Static);
            PropertyInfo defaultProperty = null;
            foreach (var prop in properties)
            {
                if (TypeReflectionSystem.GetCustomAttribute<LiveDefaultAttribute>(prop) != null)
                {
                    defaultProperty = prop;
                    break;
                }
            }
            if (defaultProperty != null)
            {
                return defaultProperty.GetValue(null);
            }

            // 抽象クラス / インターフェース (多態 SerializeReference 配列の要素型) は直接生成できない。
            // 登録済みの具象 [LiveClass] 派生型の先頭にフォールバックし、generic な「要素追加」が
            // 有効なデフォルト要素を生成できるようにする (型は @type で後から変更できる)。
            if (elementType.IsAbstract || elementType.IsInterface)
            {
                foreach (var derived in TypeReflectionSystem.FindDerivedTypes(elementType))
                {
                    if (!derived.IsAbstract && LiveClass.Find(derived) != null)
                    {
                        return CreateDefaultElement(derived);
                    }
                }
                Debug.LogWarning($"[RemoteControl] No concrete [LiveClass] subtype found for abstract element type '{elementType.Name}'.");
                return null;
            }

            // LiveClassが登録されているか確認
            var liveClass = LiveClass.Find(elementType);
            if (liveClass != null && liveClass.propertyTypes != null)
            {
                // MonoBehaviour/ScriptableObjectはActivator.CreateInstanceで生成できない
                if (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(elementType) ||
                    typeof(UnityEngine.ScriptableObject).IsAssignableFrom(elementType))
                {
                    Debug.LogWarning($"[RemoteControl] Cannot create default instance of UnityEngine.Object derived type '{elementType.Name}' via Activator. Skipping.");
                    return null;
                }

                // インスタンスを作成
                var instance = Activator.CreateInstance(elementType);

                // 各プロパティにデフォルト値を設定
                foreach (var propType in liveClass.propertyTypes)
                {
                    if (propType.defaultValue != null)
                    {
                        SetValueRaw(instance, propType, propType.defaultValue);
                    }
                }

                return instance;
            }

            // LiveClassがない場合は通常のデフォルト値
            if (elementType.IsValueType)
            {
                return Activator.CreateInstance(elementType);
            }

            return null;
        }

        /// <summary>
        /// 渡されたオブジェクトの特定プロパティのデフォルト値をキャプチャ
        /// </summary>
        public static bool EnsurePropertyDefaultCaptured(object target, string propertyPath)
        {
            var liveObject = LiveObjectRegistry.FindByTarget(target);
            if (liveObject != null)
            {
                liveObject.Value.EnsureDefaultCaptured(propertyPath);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 後方互換性のためのエイリアス
        /// </summary>
        [Obsolete("Use EnsurePropertyDefaultCaptured instead")]
        public static bool SetPropertyDirty(object target, string propertyPath)
        {
            return EnsurePropertyDefaultCaptured(target, propertyPath);
        }

        // -------------------------------------------------------
        // Property count
        // -------------------------------------------------------

        internal static int GetPropertyCount(LiveObjectHandle liveObject, bool isDirtyOnly = false, bool forPersistence = false)
        {
            Debug.Assert(liveObject != null, "LiveObjectHandle cannot be null");

            var properties = liveObject.propertyTypes;

            if (properties == null || properties.Length == 0)
            {
                return 0;
            }

            int count = 0;
            foreach (var prop in properties)
            {
                if (!prop.isValid) continue;

                // forPersistence が true の場合、isPersistable なプロパティのみ含める
                if (forPersistence && !prop.isPersistable) continue;

                // isDirtyOnly の場合、isDirty なプロパティのみ含める（子プロパティも考慮）
                if (isDirtyOnly && !liveObject.HasDirtyChildProperty(prop.name)) continue;

                count++;
            }
            return count;
        }
    }
}
