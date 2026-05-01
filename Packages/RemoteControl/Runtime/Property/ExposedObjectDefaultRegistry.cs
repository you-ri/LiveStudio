// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;

using UnityEngine;
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl.Reflection;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedObjectのデフォルト値をJObjectとして一元管理し、dirty判定・revert機能を提供する。
    ///
    /// キーはtargetオブジェクト（インスタンス型）またはExposedClass.type（static型）を使用し、
    /// ExposedObjectインスタンスへの辞書キー依存を排除している。
    ///
    /// 2つのJObjectを管理:
    /// - _serializationBaseline: CaptureDefaultsで設定、不変。GetDefaults（delta serialization）とGetDefaultValue（配列長）とRevertで使用。
    /// - _userChangeBaseline: ClearPropertyDirtyで更新される可変コピー。dirty判定で使用。
    /// </summary>
    public static class ExposedObjectDefaultRegistry
    {
        // delta serialization / Revert / 配列長判定の基準。CaptureDefaultsで設定、ClearPropertyDirtyでは変更しない
        private static Dictionary<object, JObject> _serializationBaseline
            = new Dictionary<object, JObject>(ExposedObjectRegistry.ReferenceEqualityComparer.Instance);

        // dirty判定の基準。ClearPropertyDirtyで遅延作成・更新。未登録エントリ = _serializationBaselineを使用
        private static Dictionary<object, JObject> _userChangeBaseline
            = new Dictionary<object, JObject>(ExposedObjectRegistry.ReferenceEqualityComparer.Instance);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _Initialize()
        {
            _serializationBaseline = new Dictionary<object, JObject>(ExposedObjectRegistry.ReferenceEqualityComparer.Instance);
            _userChangeBaseline = new Dictionary<object, JObject>(ExposedObjectRegistry.ReferenceEqualityComparer.Instance);
        }

        /// <summary>
        /// ExposedObjectからディクショナリキーを取得する。
        /// インスタンス型: target（参照等価）
        /// static型: ExposedClass.type（Type参照）
        /// </summary>
        private static object _GetKey(ExposedObject obj)
        {
            if (obj.target != null) return obj.target;
            if (obj.targetType?.type != null) return obj.targetType.type;
            return obj; // フォールバック: ExposedObjectインスタンス自体
        }

        // -------------------------------------------------------
        // Core API
        // -------------------------------------------------------

        /// <summary>
        /// ExposedObjectの現在の状態をデフォルトJSONとしてキャプチャする。
        /// _serializationBaselineを設定し、_userChangeBaselineをリセットする。
        /// </summary>
        public static void CaptureDefaults(ExposedObject obj, IExposedObjectResolver resolver)
        {
            if (obj == null) return;
            var key = _GetKey(obj);
            // forPersistence: true でシリアライズし、Delta比較時のcurrent（同じくforPersistence: true）と
            // 同一条件で比較できるようにする。read-onlyプロパティは永続化不要なので除外する。
            var json = ExposedPropertySerializer.SerializeFullToJObject(obj, resolver, forPersistence: true);
            _serializationBaseline[key] = json;
            _userChangeBaseline.Remove(key);
        }

        /// <summary>
        /// 指定ExposedObjectのデフォルトを削除する。
        /// </summary>
        public static void Remove(ExposedObject obj)
        {
            if (obj == null) return;
            var key = _GetKey(obj);
            _serializationBaseline.Remove(key);
            _userChangeBaseline.Remove(key);
        }

        /// <summary>
        /// すべてのデフォルトをクリアする。
        /// </summary>
        public static void ClearAll()
        {
            _serializationBaseline.Clear();
            _userChangeBaseline.Clear();
        }

        /// <summary>
        /// _serializationBaselineのJObjectを取得する（delta serialization用）。
        /// 未登録の場合はnull。
        /// </summary>
        public static JObject GetDefaults(ExposedObject obj)
        {
            if (obj == null) return null;
            var key = _GetKey(obj);
            _serializationBaseline.TryGetValue(key, out var result);
            return result;
        }

        // -------------------------------------------------------
        // Dirty Detection
        // -------------------------------------------------------

        /// <summary>
        /// dirty判定に使用する基準JObjectを返す（userChangeBaseline優先、未登録なら serializationBaseline）。
        /// </summary>
        private static JObject _GetUserChangeBaseline(object key)
        {
            if (_userChangeBaseline.TryGetValue(key, out var userBaseline)) return userBaseline;
            _serializationBaseline.TryGetValue(key, out var serialBaseline);
            return serialBaseline;
        }

        /// <summary>
        /// いずれかのプロパティがデフォルトから変更されているか。
        /// </summary>
        public static bool IsDirty(ExposedObject obj, IExposedObjectResolver resolver)
        {
            if (obj == null) return false;
            var key = _GetKey(obj);
            var defaultJson = _GetUserChangeBaseline(key);
            if (defaultJson == null) return false;

            // ExposedPropertyRef は baseline に含まれないため、current 側でも除外して比較する
            var currentJson = ExposedPropertySerializer.SerializeFullToJObject(obj, resolver, forPersistence: false, skipPropertyRef: true);
            var diff = ExposedPropertySerializer.JsonDiff(defaultJson, currentJson);
            if (diff != null) return true;

            // PropertyRef については参照先の dirty を個別に集計する
            if (obj.propertyTypes != null)
            {
                foreach (var propType in obj.propertyTypes)
                {
                    if (propType != null && propType.isExposedPropertyReference)
                    {
                        var property = obj.FindProperty(propType.name);
                        if (property.HasValue && property.Value.isDirty) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 指定プロパティパスがデフォルトから変更されているか。
        /// </summary>
        public static bool IsPropertyDirty(ExposedObject obj, string propertyPath, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(propertyPath) || obj == null) return false;
            var key = _GetKey(obj);
            var defaultJson = _GetUserChangeBaseline(key);
            if (defaultJson == null) return false;

            var defaultToken = _ResolveJsonPath(defaultJson, propertyPath);

            // パスが解決できない場合（親コンテナが存在しない等）は未追跡 → not dirty
            // 旧システムの_propertyStates.TryGetValueがfalseを返す場合に相当
            if (defaultToken == null) return false;

            // 現在値をシリアライズして比較
            var currentToken = _SerializePropertyValue(obj, propertyPath, resolver);
            if (currentToken == null) return true;

            return !JToken.DeepEquals(defaultToken, currentToken);
        }

        /// <summary>
        /// 指定パスまたはその子プロパティがdirtyかチェック。
        /// </summary>
        public static bool HasDirtyChildProperty(ExposedObject obj, string propertyPath, IExposedObjectResolver resolver)
        {
            if (obj == null) return false;
            if (string.IsNullOrEmpty(propertyPath)) return IsDirty(obj, resolver);
            var key = _GetKey(obj);
            var defaultJson = _GetUserChangeBaseline(key);
            if (defaultJson == null) return false;

            // ルートプロパティ名を取得
            var rootName = PropertyPathParser.GetRootName(propertyPath).ToString();

            var defaultRootToken = defaultJson[rootName];
            var currentRootToken = _SerializeRootPropertyValue(obj, rootName, resolver);

            if (defaultRootToken == null && currentRootToken == null) return false;
            if (defaultRootToken == null || currentRootToken == null) return true;

            // パスがルートと同じ場合はサブツリー全体を比較
            if (rootName == propertyPath)
            {
                return ExposedPropertySerializer.JsonDiff(defaultRootToken, currentRootToken) != null;
            }

            // 深いパスの場合、ルートのJTokenからサブパスを辿って比較
            var subPath = propertyPath.Substring(rootName.Length);
            var defaultSubToken = _ResolveSubPath(defaultRootToken, subPath);
            var currentSubToken = _ResolveSubPath(currentRootToken, subPath);

            if (defaultSubToken == null && currentSubToken == null) return false;
            if (defaultSubToken == null || currentSubToken == null) return true;

            return ExposedPropertySerializer.JsonDiff(defaultSubToken, currentSubToken) != null;
        }

        /// <summary>
        /// dirtyなルートプロパティ名のリストを返す。
        /// </summary>
        public static IReadOnlyCollection<string> GetDirtyProperties(ExposedObject obj, IExposedObjectResolver resolver)
        {
            if (obj == null) return Array.Empty<string>();
            var key = _GetKey(obj);
            var defaultJson = _GetUserChangeBaseline(key);
            if (defaultJson == null) return Array.Empty<string>();

            var result = new List<string>();
            // PropertyRef は currentJson から除外し、参照先の dirty を別途判定する
            var currentJson = ExposedPropertySerializer.SerializeFullToJObject(obj, resolver, forPersistence: false, skipPropertyRef: true);

            foreach (var propType in obj.propertyTypes)
            {
                if (!propType.isValid) continue;
                var name = propType.name;

                // ExposedPropertyRef は自身の baseline を持たない。参照先の dirty を判定する。
                if (propType.isExposedPropertyReference)
                {
                    var property = obj.FindProperty(name);
                    if (property.HasValue && property.Value.isDirty)
                    {
                        result.Add(name);
                    }
                    continue;
                }

                var defaultToken = defaultJson[name];
                var currentToken = currentJson[name];

                if (defaultToken == null && currentToken == null) continue;
                if (defaultToken == null || currentToken == null)
                {
                    result.Add(name);
                    continue;
                }

                if (!JToken.DeepEquals(defaultToken, currentToken))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        // -------------------------------------------------------
        // Clear / Revert
        // -------------------------------------------------------

        /// <summary>
        /// 全プロパティのdirty状態を解消する（現在値をデフォルトとして再キャプチャ）。
        /// _serializationBaselineを再設定し、_userChangeBaselineをリセット。
        /// </summary>
        public static void ClearDirty(ExposedObject obj, IExposedObjectResolver resolver)
        {
            CaptureDefaults(obj, resolver);
        }

        /// <summary>
        /// 指定プロパティのdirty状態を解消する。
        /// _userChangeBaselineのルートプロパティを再シリアライズして更新する。
        /// _serializationBaselineは変更しない（配列長やdelta serialization用に保持）。
        /// </summary>
        public static void ClearPropertyDirty(ExposedObject obj, string propertyPath, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(propertyPath) || obj == null) return;
            var key = _GetKey(obj);
            if (!_serializationBaseline.TryGetValue(key, out var serialBaseline))
            {
                // まだデフォルトがない場合はフルキャプチャ
                CaptureDefaults(obj, resolver);
                return;
            }

            // _userChangeBaselineが未作成なら、_serializationBaselineからクローンして作成
            if (!_userChangeBaseline.TryGetValue(key, out var userBaseline))
            {
                userBaseline = serialBaseline.DeepClone() as JObject;
                _userChangeBaseline[key] = userBaseline;
            }

            // ルートプロパティ名を取得して全体を再シリアライズ
            var rootName = PropertyPathParser.GetRootName(propertyPath).ToString();
            var propertyType = obj.targetType?.FindProperty(rootName);
            if (propertyType == null) return;

            var value = ExposedPropertyUtility.GetValueRaw(obj.target, propertyType);
            var currentToken = _SerializeWithControlAttribute(resolver, value, propertyType);
            userBaseline[rootName] = currentToken?.DeepClone();
        }

        /// <summary>
        /// 指定プロパティをデフォルト値に戻す。
        /// _serializationBaselineから値を取得する。
        /// </summary>
        public static bool Revert(ExposedObject obj, string propertyPath, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(propertyPath) || obj == null) return false;
            var key = _GetKey(obj);

            // _serializationBaselineを優先して使用する。
            // _userChangeBaselineはLoad中にEnsurePropertyDefaultCapturedで更新されるため、
            // ルートプロパティ（特に配列）のRevert時にロード後の値に戻してしまう問題がある。
            // ただし、新規追加された配列要素など_serializationBaselineに存在しないパスは
            // _userChangeBaselineにフォールバックする。
            _serializationBaseline.TryGetValue(key, out var serialBaseline);
            var defaultJson = serialBaseline;
            if (defaultJson == null)
            {
                defaultJson = _GetUserChangeBaseline(key);
                if (defaultJson == null) return false;
            }

            var defaultToken = _ResolveJsonPath(defaultJson, propertyPath);

            // _serializationBaselineにパスが存在しない場合（新規配列要素など）、
            // _userChangeBaselineにフォールバック
            if (defaultToken == null && serialBaseline != null)
            {
                var userBaseline = _GetUserChangeBaseline(key);
                if (userBaseline != null)
                    defaultToken = _ResolveJsonPath(userBaseline, propertyPath);
            }
            if (defaultToken == null) return false;

            var property = obj.FindProperty(propertyPath);
            if (property == null) return false;

            object defaultValue;
            var valueType = property.Value.type.valueType;
            // ObjectSelector フィールドは @ref に "rootId.components[N]" 形式のパスが含まれるため、
            // 通常の DeserializeUnityType では resolver.FindById が失敗し null になる。
            // 専用のパス解決ロジックを使う。
            if (property.Value.type.controlAttribute is ObjectSelectorAttribute
                && valueType != null
                && typeof(UnityEngine.Object).IsAssignableFrom(valueType))
            {
                defaultValue = ExposedPropertySerializer.DeserializeObjectSelectorValue(
                    resolver, defaultToken, valueType);
            }
            else
            {
                defaultValue = ExposedPropertySerializer.DeserializeUnityType(
                    resolver, defaultToken, valueType);
            }
            property.Value.SetValue(defaultValue, captureDefault: false);
            return true;
        }

        // -------------------------------------------------------
        // Utility
        // -------------------------------------------------------

        /// <summary>
        /// デフォルトがまだキャプチャされていなければキャプチャする。
        /// </summary>
        public static void EnsureDefaultsCaptured(ExposedObject obj, IExposedObjectResolver resolver)
        {
            if (obj == null) return;
            var key = _GetKey(obj);
            if (!_serializationBaseline.ContainsKey(key))
            {
                CaptureDefaults(obj, resolver);
            }
        }

        /// <summary>
        /// 指定パスのデフォルト値が_userChangeBaselineに存在しなければキャプチャする。
        /// 新規配列要素など、CaptureDefaults時に存在しなかったパスを遅延登録する。
        /// SetValue呼び出し前に実行されることで、変更前の値がデフォルトとして記録される。
        /// </summary>
        public static void EnsurePropertyDefaultCaptured(ExposedObject obj, string propertyPath, IExposedObjectResolver resolver)
        {
            if (string.IsNullOrEmpty(propertyPath) || obj == null) return;

            var key = _GetKey(obj);
            if (!_serializationBaseline.ContainsKey(key))
            {
                CaptureDefaults(obj, resolver);
                return;
            }

            // _userChangeBaselineでパスが解決できるか確認
            var userBaseline = _GetUserChangeBaseline(key);
            if (userBaseline != null)
            {
                var existingToken = _ResolveJsonPath(userBaseline, propertyPath);
                if (existingToken != null) return; // 既に追跡済み
            }

            // パスが存在しない → ルートプロパティを再シリアライズして_userChangeBaselineに登録
            ClearPropertyDirty(obj, propertyPath, resolver);
        }

        /// <summary>
        /// 指定パスのデフォルトJTokenを取得する（_serializationBaselineから）。
        /// 配列長チェック等、初期状態に基づく判定に使用。
        /// </summary>
        public static JToken GetDefaultToken(ExposedObject obj, string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath) || obj == null) return null;
            var key = _GetKey(obj);
            if (!_serializationBaseline.TryGetValue(key, out var defaultJson)) return null;
            return _ResolveJsonPath(defaultJson, propertyPath);
        }

        // -------------------------------------------------------
        // JSON Path Resolution (private)
        // -------------------------------------------------------

        /// <summary>
        /// JObject内のプロパティパスを辿ってJTokenを取得する。
        /// 例: "nested.name" → root["nested"]["name"]
        /// 例: "items[0].name" → root["items"][0]["name"]
        /// </summary>
        private static JToken _ResolveJsonPath(JObject root, string propertyPath)
        {
            if (root == null || string.IsNullOrEmpty(propertyPath)) return null;

            JToken current = root;
            foreach (var segment in PropertyPathParser.Parse(propertyPath))
            {
                if (segment.isError || current == null) return null;

                if (segment.isIndexed)
                {
                    if (current is JArray arr && segment.index >= 0 && segment.index < arr.Count)
                        current = arr[segment.index];
                    else
                        return null;
                }
                else
                {
                    if (current is JObject obj)
                        current = obj[segment.name.ToString()];
                    else
                        return null;
                }
            }

            return current;
        }

        /// <summary>
        /// JToken内のサブパスを辿る。サブパスは ".name" や "[0].name" の形式。
        /// </summary>
        private static JToken _ResolveSubPath(JToken root, string subPath)
        {
            if (root == null || string.IsNullOrEmpty(subPath)) return root;

            JToken current = root;
            foreach (var segment in PropertyPathParser.Parse(subPath))
            {
                if (segment.isError || current == null) return null;

                if (segment.isIndexed)
                {
                    if (current is JArray arr && segment.index >= 0 && segment.index < arr.Count)
                        current = arr[segment.index];
                    else
                        return null;
                }
                else
                {
                    if (current is JObject obj)
                        current = obj[segment.name.ToString()];
                    else
                        return null;
                }
            }

            return current;
        }

        // -------------------------------------------------------
        // Serialization Helpers (private)
        // -------------------------------------------------------

        /// <summary>
        /// 指定プロパティパスの現在値をJTokenにシリアライズする。
        /// </summary>
        private static JToken _SerializePropertyValue(ExposedObject obj, string propertyPath, IExposedObjectResolver resolver)
        {
            var property = obj.FindProperty(propertyPath);
            if (property == null) return null;

            var value = property.Value.GetValue();
            return _SerializeWithControlAttribute(resolver, value, property.Value.type);
        }

        /// <summary>
        /// ルートプロパティの現在値をJTokenにシリアライズする。
        /// </summary>
        private static JToken _SerializeRootPropertyValue(ExposedObject obj, string rootName, IExposedObjectResolver resolver)
        {
            var propertyType = obj.targetType?.FindProperty(rootName);
            if (propertyType == null) return null;

            var value = ExposedPropertyUtility.GetValueRaw(obj.target, propertyType);
            return _SerializeWithControlAttribute(resolver, value, propertyType);
        }

        /// <summary>
        /// dirty check 用のシリアライズ。CaptureDefaults は forPersistence:true で保存しているため、
        /// 比較対象となる現在値も同じ形式でシリアライズしないと @name/@instanceID 等のメタ差分で
        /// 常に dirty と判定されてしまう。
        /// また ObjectSelector 属性のフィールドは "rootId.components[N]" 形式の @ref を使うため、
        /// 通常の SerializeUnityType ではなく ObjectSelector 専用シリアライザを使う。
        /// </summary>
        private static JToken _SerializeWithControlAttribute(
            IExposedObjectResolver resolver, object value, ExposedPropertyType propertyType)
        {
            if (propertyType.controlAttribute is ObjectSelectorAttribute)
            {
                return ExposedPropertySerializer.SerializeObjectSelectorValue(value, forPersistence: true);
            }
            return ExposedPropertySerializer.SerializeUnityType(resolver, value, propertyType.forceValue, forPersistence: true);
        }
    }
}
