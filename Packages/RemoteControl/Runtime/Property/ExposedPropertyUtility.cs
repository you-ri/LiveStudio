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
    /// ExposedPropertyのコア操作を担当するユーティリティクラス。
    /// 型判定、プロパティアクセス、デフォルト値管理などの基本操作を提供する。
    /// </summary>
    public static class ExposedPropertyUtility
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

        internal static ExposedPropertyType GetPropertyType(ExposedObject exposedObject, string propertyName)
        {
            if (exposedObject == null) throw new ArgumentNullException(nameof(exposedObject));
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));

            if (string.IsNullOrEmpty(propertyName)) return null;

            return exposedObject.propertyTypes.FirstOrDefault(p => p.properyInfo != null && p.properyInfo.Name == propertyName);
        }

        internal static ExposedPropertyType[] MakePropertyTypes(Type type, ExposedPropertyDefine[] defines)
        {
            if (defines == null) throw new ArgumentNullException(nameof(defines));

            return defines.Select(e =>
            {
                var memberInfo = MemberAccessSystem.GetMemberInfo(type, e.path);
                if (memberInfo != null)
                {
                    return new ExposedPropertyType(e.name, memberInfo, e.isPersistable);
                }

                Debug.LogError($"[RemoteControl] Member not found for {type.Name}.{e.path}");
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
        private static bool _ValidatePropertyAccess(object obj, in ExposedPropertyType propertyType, bool throwOnNull)
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
        /// 指定された obj が propertyType の宣言型と互換性があるかを返す (ログ出力なし)。
        /// 静的メンバー / 配列要素 / null obj はチェック対象外として true 扱い。
        /// </summary>
        internal static bool IsInstanceCompatible(object obj, in ExposedPropertyType propertyType)
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
        internal static bool WarnIfInstanceMismatch(object obj, in ExposedPropertyType propertyType)
        {
            if (IsInstanceCompatible(obj, propertyType)) return false;

            var declaringType = propertyType.properyInfo?.DeclaringType ?? propertyType.fieldInfo?.DeclaringType;
            var memberName = propertyType.properyInfo?.Name ?? propertyType.fieldInfo?.Name;
            Debug.LogWarning($"[RemoteControl] Property '{declaringType?.Name}.{memberName}' is not defined on actual instance of type '{obj.GetType().Name}'. Skipping (likely polymorphic type mismatch on load).");
            return true;
        }

        internal static bool SetValueRaw(object obj, in ExposedPropertyType propertyType, object value)
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
                // staticの場合はobjにnullを渡す
                propertyType.properyInfo.SetValue(propertyType.isStatic ? null : obj, value);
            }
            else if (propertyType.fieldInfo != null)
            {
                if (value != null && !propertyType.fieldInfo.FieldType.IsAssignableFrom(value.GetType()))
                {
                    Debug.LogWarning($"[RemoteControl] Type mismatch: cannot assign {value.GetType().Name} to {propertyType.fieldInfo.FieldType.Name}");
                    return false;
                }
                // staticの場合はobjにnullを渡す
                propertyType.fieldInfo.SetValue(propertyType.isStatic ? null : obj, value);
            }
            else
            {
                return false;
            }

            return true;
        }

        internal static object GetValueRaw(object obj, in ExposedPropertyType propertyType)
        {
            if (!_ValidatePropertyAccess(obj, propertyType, throwOnNull: true)) return null;

            // 配列要素の場合
            if (propertyType.isArrayElement)
            {
                return GetCollectionElement(obj, propertyType.arrayIndex);
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

        public static bool ResetValue(ExposedObject exposedObject, in ExposedProperty property)
        {
            if (exposedObject == null) throw new ArgumentNullException(nameof(exposedObject));

            property.RevertValue();

            return true;
        }

        public static void SetDefault(ExposedObject exposedObject)
        {
            if (exposedObject == null) throw new ArgumentNullException(nameof(exposedObject));

            // 全プロパティの現在値をJObjectとしてスナップショット保存
            ExposedObjectDefaultRegistry.CaptureDefaults(exposedObject, DefaultExposedObjectResolver.Instance);
        }

        internal static object CreateDefaultElement(Type elementType)
        {
            // [ExposedDefault]が付与されたstaticプロパティを検索
            var properties = elementType.GetProperties(BindingFlags.Public | BindingFlags.Static);
            PropertyInfo defaultProperty = null;
            foreach (var prop in properties)
            {
                if (TypeReflectionSystem.GetCustomAttribute<ExposedDefaultAttribute>(prop) != null)
                {
                    defaultProperty = prop;
                    break;
                }
            }
            if (defaultProperty != null)
            {
                return defaultProperty.GetValue(null);
            }

            // ExposedClassが登録されているか確認
            var exposedClass = ExposedClass.Find(elementType);
            if (exposedClass != null && exposedClass.propertyTypes != null)
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
                foreach (var propType in exposedClass.propertyTypes)
                {
                    if (propType.defaultValue != null)
                    {
                        SetValueRaw(instance, propType, propType.defaultValue);
                    }
                }

                return instance;
            }

            // ExposedClassがない場合は通常のデフォルト値
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
            var exposedObject = ExposedObjectRegistry.FindByTarget(target);
            if (exposedObject != null)
            {
                exposedObject.EnsureDefaultCaptured(propertyPath);
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

        internal static int GetPropertyCount(ExposedObject exposedObject, bool isDirtyOnly = false, bool forPersistence = false)
        {
            Debug.Assert(exposedObject != null, "ExposedObject cannot be null");

            var properties = exposedObject.propertyTypes;

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
                if (isDirtyOnly && !exposedObject.HasDirtyChildProperty(prop.name)) continue;

                count++;
            }
            return count;
        }
    }
}
