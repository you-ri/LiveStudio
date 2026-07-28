// Copyright (c) You-Ri, 2026

using System;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// GUID-based serialization for AssetSelector fields. The value is written as
    /// <c>{ "@type", "@guid" }</c> (plus <c>"@name"</c> outside persistence, so clients can
    /// label the current value without an extra request) and resolved back through
    /// <see cref="AssetRegistry"/>. Mirrors <see cref="ObjectSelectorSerializer"/>, but
    /// identifies the target by asset GUID instead of an LiveObject id.
    /// </summary>
    internal static class AssetSelectorSerializer
    {
        /// <summary>
        /// Serialize an asset reference as its GUID. Unselected (null) or unregistered
        /// assets serialize to null.
        /// </summary>
        internal static JToken SerializeAssetSelectorValue(object value, bool forPersistence)
        {
            if (!(value is UnityEngine.Object asset) || asset == null) return JValue.CreateNull();

            if (!AssetRegistry.TryFindGuid(asset, out var guid))
            {
                // Unregistered asset: it cannot be expressed as a GUID. Treat as unselected.
                return JValue.CreateNull();
            }

            var result = new JObject
            {
                ["@type"] = asset.GetType().Name,
                ["@guid"] = guid,
            };
            if (!forPersistence)
            {
                result["@name"] = asset.name;
            }
            return result;
        }

        /// <summary>
        /// Resolve a GUID token (either a <c>{ "@guid": ... }</c> JObject or a plain GUID
        /// string) to the registered asset. Returns null when the GUID is empty, unknown,
        /// or the asset is not assignable to <paramref name="fieldType"/>.
        /// </summary>
        internal static object DeserializeAssetSelectorValue(JToken token, Type fieldType)
        {
            if (token == null || token.Type == JTokenType.Null) return null;

            string guid = null;
            if (token is JObject jObj) guid = jObj["@guid"]?.Value<string>();
            else if (token.Type == JTokenType.String) guid = token.Value<string>();
            if (string.IsNullOrEmpty(guid)) return null;

            if (!AssetRegistry.TryFind(guid, out var asset)) return null;
            if (fieldType != null && !fieldType.IsAssignableFrom(asset.GetType())) return null;
            return asset;
        }
    }
}
