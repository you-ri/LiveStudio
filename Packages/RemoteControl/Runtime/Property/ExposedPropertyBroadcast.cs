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
        /// True when at least one SSE client is connected to any running server. Property
        /// broadcasts serialize the changed value into JSON before fan-out; when nobody is
        /// listening (headless run, or no RemoteApp connected) that work is pure waste, so the
        /// broadcast entry points bail out early. Cheap: reads an int per server (usually one).
        /// </summary>
        private static bool _HasConnectedClients()
        {
            foreach (var instance in RemoteControlServerManager.servers.Values)
            {
                if (instance.server != null && instance.server.GetConnectionCount() > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// ターゲットオブジェクトの指定プロパティをSSEでブロードキャストする。
        /// <paramref name="propertyPath"/> にはトップレベル名 (例: "meshPaths") だけでなく、
        /// DotBracket 形式のネストパス (例: "animationParameterOverrides[0].type") も指定できる。
        /// </summary>
        public static void BroadcastProperty(object target, string propertyPath)
        {
            if (!_HasConnectedClients()) return;
            if (!ExposedObjectRegistry.TryFindByTarget(target, out var exposedObj)) return;
            BroadcastProperty(exposedObj, propertyPath);
        }

        /// <summary>
        /// 未登録の UnityEngine.Object ターゲットのプロパティを instanceID キーで SSE ブロードキャストする。
        /// Registry 検索は行わず、その場で <see cref="ExposedObjectHandle.CreateUnregistered"/> して使う。
        /// RemoteApp 側は selector 配下のインライン要素の <c>@id</c> を instanceID で受信しているため、
        /// 同じ instanceID でルーティングすれば該当要素が更新される。
        /// </summary>
        public static void BroadcastProperty(UnityEngine.Object target, string propertyPath)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath)) return;
            if (!_HasConnectedClients()) return;

            var exposedClass = ExposedClass.Find(target.GetType());
            if (exposedClass == null) return;

            var exposedObject = ExposedObjectHandle.CreateUnregistered(exposedClass, target);
            var property = exposedObject.FindProperty(propertyPath);
            if (property == null) return;

            // ToJson(string) → JObject.Parse の往復を避け、JObject を直接受け取る。
            var jObject = ExposedPropertySerializer.ToJObject(
                property.Value, DefaultExposedObjectResolver.Instance);
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
        public static void BroadcastProperty(ExposedObjectHandle exposedObj, string propertyPath)
        {
            if (exposedObj == null) return;
            if (!_HasConnectedClients()) return;

            var property = exposedObj.FindProperty(propertyPath);
            if (property == null) return;

            // ToJson(string) → JObject.Parse の往復を避け、JObject を直接受け取る。
            var jObject = ExposedPropertySerializer.ToJObject(
                property.Value, DefaultExposedObjectResolver.Instance);
            jObject["type"] = "exposed_object_updated";

            foreach (var instance in RemoteControlServerManager.servers.Values)
            {
                _ = instance.server?.BroadcastMessage(jObject, "exposed_object_updated");
            }
        }

        /// <summary>
        /// Notify connected clients that the ExposedClass / ExposedEnum tables have been
        /// rebuilt and they should refetch /exposed/types and /exposed/enums.
        /// Payload is intentionally empty — the receiver pulls fresh data via REST so that
        /// types and enums stay consistent.
        /// </summary>
        public static void BroadcastTypesUpdate()
        {
            var jObject = new JObject
            {
                ["type"] = "types_update"
            };

            foreach (var instance in RemoteControlServerManager.servers.Values)
            {
                _ = instance.server?.BroadcastMessage(jObject, "types_update");
            }
        }
    }
}
