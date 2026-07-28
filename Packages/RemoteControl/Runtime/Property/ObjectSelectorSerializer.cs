// Copyright (c) You-Ri, 2026
using System;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ObjectSelector フィールドの @ref シリアライズ/解決。LivePropertySerializer から分離。
    /// コア(LiveObjectRegistry/LiveClass/ILiveObjectResolver)への一方向依存のみ。
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
        /// GameObject の components (LiveClass フィルタ済み) の中で target コンポーネントの index を返す。
        /// LiveGameObject._components と同じフィルタで計算する (RemoteApp 表示と一致させるため)。
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
                if (!LiveClass.Has(c.GetType())) continue;
                if (c == target) return index;
                index++;
            }
            return -1;
        }

        /// <summary>
        /// GameObject を包む LiveObjectHandle (LiveGameObject など) を検索する。
        /// </summary>
        private static LiveObjectHandle? _FindGameObjectWrapper(GameObject gameObject)
        {
            if (gameObject == null) return null;
            foreach (var candidate in LiveObjectRegistry.instances)
            {
                if (!candidate.hasId) continue;
                var wrappedGO = LiveObjectRegistry.ResolveGameObject(candidate.target);
                if (wrappedGO == gameObject) return candidate;
            }
            return null;
        }

        /// <summary>
        /// ObjectSelector フィールドの値を @ref 形式にシリアライズする。
        /// - 直接登録済み (value 自身が LiveObjectHandle.target) → その id を @ref に使用
        /// - Component かつ所属 GameObject を包む LiveObjectHandle があれば → "rootId.components[N]" 形式で @ref
        /// - wrapper が見つからなければ null (未選択扱い)
        /// </summary>
        internal static JToken SerializeObjectSelectorValue(object value, bool forPersistence)
        {
            if (value == null) return JValue.CreateNull();
            if (value is UnityEngine.Object uo && uo == null) return JValue.CreateNull();

            // 1) 直接登録済み: value 自身が LiveObjectHandle.target なら、その id を @ref に使う
            var direct = LiveObjectRegistry.FindByTarget(value);
            if (direct != null && direct.Value.hasId)
            {
                var directObj = direct.Value;
                var directResult = new JObject
                {
                    ["@type"] = directObj.targetTypeName,
                    ["@ref"] = directObj.id,
                };
                if (!forPersistence)
                {
                    directResult["@name"] = directObj.name;
                    if (value is UnityEngine.Object unityValue && unityValue != null)
                    {
                        directResult["@instanceID"] = LiveObjectUtility.GetInstanceID(unityValue).ToString();
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

                var refKey = ComposeObjectSelectorRef(wrapper.Value.id, $"components[{index}]");
                var componentTypeName = LiveClass.Find(component.GetType())?.typeName ?? component.GetType().Name;
                var result = new JObject
                {
                    ["@type"] = componentTypeName,
                    ["@ref"] = refKey,
                };
                if (!forPersistence)
                {
                    result["@name"] = component.name;
                    result["@instanceID"] = LiveObjectUtility.GetInstanceID(component).ToString();
                }
                return result;
            }

            return JValue.CreateNull();
        }

        /// <summary>
        /// ObjectSelector フィールドへの代入: token (@ref JObject) を解決して fieldType に沿う値を返す。
        /// @ref が path 付き ("rootId.components[N]" など) なら rootLive.FindProperty(path) で辿る。
        /// 解決値が fieldType に代入不可なら GameObject.GetComponent(fieldType) でフォールバック。
        /// </summary>
        internal static object DeserializeObjectSelectorValue(ILiveObjectResolver resolver, JToken token, Type fieldType)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JObject jObj)) return null;
            var refKey = jObj["@ref"]?.Value<string>();
            if (string.IsNullOrEmpty(refKey)) return null;

            _ParseObjectSelectorRef(refKey, out var rootId, out var path);
            var rootLive = resolver.FindById(rootId);
            if (rootLive == null) return null;
            var rootLiveObj = rootLive.Value;

            // path 無し: ルート target
            if (string.IsNullOrEmpty(path))
            {
                var rootTarget = rootLiveObj.target;
                if (rootTarget != null && fieldType.IsAssignableFrom(rootTarget.GetType())) return rootTarget;
                if (typeof(Component).IsAssignableFrom(fieldType))
                {
                    var gameObject = LiveObjectRegistry.ResolveGameObject(rootTarget);
                    if (gameObject != null) return gameObject.GetComponent(fieldType);
                }
                return null;
            }

            // path 付き: FindProperty で辿る (components[N] 等)
            var property = rootLiveObj.FindProperty(path);
            if (!property.HasValue) return null;
            var resolved = property.Value.GetValue();
            if (resolved != null && fieldType.IsAssignableFrom(resolved.GetType())) return resolved;
            return null;
        }
    }
}
