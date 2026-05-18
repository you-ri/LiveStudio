// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ObjectSelector フィールドの @ref シリアライズ/解決。ExposedPropertySerializer から分離。
    /// コア(ExposedObjectRegistry/ExposedClass/IExposedObjectResolver)への一方向依存のみ。
    /// </summary>
    internal static class ObjectSelectorSerializer
    {
        /// <summary>
        /// ObjectSelector の @ref を rootId と path に分解する。
        /// 例: "guid.components[0]" → rootId="guid", path="components[0]"
        /// </summary>
        private static void _ParseObjectSelectorRef(string refKey, out string rootId, out string path)
        {
            rootId = refKey;
            path = string.Empty;
            if (string.IsNullOrEmpty(refKey)) return;

            int dotIndex = refKey.IndexOf('.');
            int bracketIndex = refKey.IndexOf('[');
            int splitIndex = -1;
            if (dotIndex >= 0 && bracketIndex >= 0) splitIndex = System.Math.Min(dotIndex, bracketIndex);
            else if (dotIndex >= 0) splitIndex = dotIndex;
            else if (bracketIndex >= 0) splitIndex = bracketIndex;

            if (splitIndex < 0) return;
            rootId = refKey.Substring(0, splitIndex);
            path = refKey[splitIndex] == '.' ? refKey.Substring(splitIndex + 1) : refKey.Substring(splitIndex);
        }

        /// <summary>
        /// ObjectSelector の @ref を組み立てる。path が空なら rootId のみ。
        /// </summary>
        internal static string ComposeObjectSelectorRef(string rootId, string path)
        {
            if (string.IsNullOrEmpty(path)) return rootId;
            return path[0] == '[' ? rootId + path : rootId + "." + path;
        }

        /// <summary>
        /// GameObject の components (ExposedClass フィルタ済み) の中で target コンポーネントの index を返す。
        /// ExposedGameObject._components と同じフィルタで計算する (RemoteApp 表示と一致させるため)。
        /// 見つからなければ -1。
        /// </summary>
        private static int _FindFilteredComponentIndex(GameObject gameObject, Component target)
        {
            if (gameObject == null || target == null) return -1;
            var components = gameObject.GetComponents<Component>();
            int index = 0;
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;
                if (!ExposedClass.Has(c.GetType())) continue;
                if (c == target) return index;
                index++;
            }
            return -1;
        }

        /// <summary>
        /// GameObject を包む ExposedObject (ExposedGameObject など) を検索する。
        /// </summary>
        private static ExposedObject _FindGameObjectWrapper(GameObject gameObject)
        {
            if (gameObject == null) return null;
            foreach (var candidate in ExposedObjectRegistry.instances)
            {
                if (candidate == null || !candidate.hasId) continue;
                var wrappedGO = ExposedObjectRegistry.ResolveGameObject(candidate.target);
                if (wrappedGO == gameObject) return candidate;
            }
            return null;
        }

        /// <summary>
        /// ObjectSelector フィールドの値を @ref 形式にシリアライズする。
        /// - 直接登録済み (value 自身が ExposedObject.target) → その id を @ref に使用
        /// - Component かつ所属 GameObject を包む ExposedObject があれば → "rootId.components[N]" 形式で @ref
        /// - wrapper が見つからなければ null (未選択扱い)
        /// </summary>
        internal static JToken SerializeObjectSelectorValue(object value, bool forPersistence)
        {
            if (value == null) return JValue.CreateNull();
            if (value is UnityEngine.Object uo && uo == null) return JValue.CreateNull();

            // 1) 直接登録済み: value 自身が ExposedObject.target なら、その id を @ref に使う
            var direct = ExposedObjectRegistry.FindByTarget(value);
            if (direct != null && direct.hasId)
            {
                var directResult = new JObject
                {
                    ["@type"] = direct.targetTypeName,
                    ["@ref"] = direct.id,
                };
                if (!forPersistence)
                {
                    directResult["@name"] = direct.name;
                    if (value is UnityEngine.Object unityValue && unityValue != null)
                    {
                        directResult["@instanceID"] = unityValue.GetInstanceID().ToString();
                    }
                }
                return directResult;
            }

            // 2) GameObject 経由: components[N] path 付き @ref を生成する
            if (value is Component component)
            {
                var gameObject = component.gameObject;
                var wrapper = _FindGameObjectWrapper(gameObject);
                if (wrapper == null) return JValue.CreateNull();

                int index = _FindFilteredComponentIndex(gameObject, component);
                if (index < 0) return JValue.CreateNull();

                var refKey = ComposeObjectSelectorRef(wrapper.id, $"components[{index}]");
                var componentTypeName = ExposedClass.Find(component.GetType())?.typeName ?? component.GetType().Name;
                var result = new JObject
                {
                    ["@type"] = componentTypeName,
                    ["@ref"] = refKey,
                };
                if (!forPersistence)
                {
                    result["@name"] = component.name;
                    result["@instanceID"] = component.GetInstanceID().ToString();
                }
                return result;
            }

            return JValue.CreateNull();
        }

        /// <summary>
        /// ObjectSelector フィールドへの代入: token (@ref JObject) を解決して fieldType に沿う値を返す。
        /// @ref が path 付き ("rootId.components[N]" など) なら rootExposed.FindProperty(path) で辿る。
        /// 解決値が fieldType に代入不可なら GameObject.GetComponent(fieldType) でフォールバック。
        /// </summary>
        internal static object DeserializeObjectSelectorValue(IExposedObjectResolver resolver, JToken token, Type fieldType)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JObject jObj)) return null;
            var refKey = jObj["@ref"]?.Value<string>();
            if (string.IsNullOrEmpty(refKey)) return null;

            _ParseObjectSelectorRef(refKey, out var rootId, out var path);
            var rootExposed = resolver.FindById(rootId);
            if (rootExposed == null) return null;

            // path 無し: ルート target
            if (string.IsNullOrEmpty(path))
            {
                var rootTarget = rootExposed.target;
                if (rootTarget != null && fieldType.IsAssignableFrom(rootTarget.GetType())) return rootTarget;
                if (typeof(Component).IsAssignableFrom(fieldType))
                {
                    var gameObject = ExposedObjectRegistry.ResolveGameObject(rootTarget);
                    if (gameObject != null) return gameObject.GetComponent(fieldType);
                }
                return null;
            }

            // path 付き: FindProperty で辿る (components[N] 等)
            var property = rootExposed.FindProperty(path);
            if (!property.HasValue) return null;
            var resolved = property.Value.GetValue();
            if (resolved != null && fieldType.IsAssignableFrom(resolved.GetType())) return resolved;
            return null;
        }
    }
}
