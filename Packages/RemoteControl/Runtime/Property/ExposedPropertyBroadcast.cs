// Copyright (c) You-Ri, 2026
using Newtonsoft.Json.Linq;
using Lilium.RemoteControl.Server;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// ExposedObjectのプロパティ変更をSSE経由で全クライアントに通知するユーティリティ
    /// </summary>
    public static class ExposedPropertyBroadcast
    {
        /// <summary>
        /// ターゲットオブジェクトの指定プロパティをSSEでブロードキャストする。
        /// <paramref name="propertyPath"/> にはトップレベル名 (例: "meshPaths") だけでなく、
        /// DotBracket 形式のネストパス (例: "animationParameterOverrides[0].type") も指定できる。
        /// </summary>
        public static void BroadcastProperty(object target, string propertyPath)
        {
            if (!ExposedObjectRegistry.TryFindByTarget(target, out var exposedObj)) return;
            BroadcastProperty(exposedObj, propertyPath);
        }

        /// <summary>
        /// 未登録の UnityEngine.Object ターゲットのプロパティを instanceID キーで SSE ブロードキャストする。
        /// Registry 検索は行わず、その場で <see cref="ExposedObject.CreateUnregistered"/> して使う。
        /// RemoteApp 側は selector 配下のインライン要素の <c>@id</c> を instanceID で受信しているため、
        /// 同じ instanceID でルーティングすれば該当要素が更新される。
        /// </summary>
        public static void BroadcastProperty(UnityEngine.Object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath)) return;

            var exposedClass = ExposedClass.Find(target.GetType());
            if (exposedClass == null) return;

            var exposedObject = ExposedObject.CreateUnregistered(exposedClass, target);
            var property = exposedObject.FindProperty(propertyPath);
            if (property == null) return;

            var json = ExposedPropertySerializer.ToJson(
                property.Value, DefaultExposedObjectResolver.Instance);
            var jObject = JObject.Parse(json);
            jObject["type"] = "exposed_object_updated";
            jObject["id"] = target.GetInstanceID().ToString();

            foreach (var instance in RemoteControlServerManager.servers.Values)
            {
                _ = instance.server?.BroadcastMessage(jObject, "exposed_object_updated");
            }
        }

        /// <summary>
        /// ExposedObjectの指定プロパティをSSEでブロードキャストする。
        /// <paramref name="propertyPath"/> にはトップレベル名だけでなく、DotBracket 形式の
        /// ネストパス (例: "animationParameterOverrides[0].type") も指定できる。
        /// </summary>
        public static void BroadcastProperty(ExposedObject exposedObj, string propertyPath)
        {
            if (exposedObj == null) return;

            var property = exposedObj.FindProperty(propertyPath);
            if (property == null) return;

            var json = ExposedPropertySerializer.ToJson(
                property.Value, DefaultExposedObjectResolver.Instance);

            var jObject = JObject.Parse(json);
            jObject["type"] = "exposed_object_updated";

            foreach (var instance in RemoteControlServerManager.servers.Values)
            {
                _ = instance.server?.BroadcastMessage(jObject, "exposed_object_updated");
            }
        }
    }
}
