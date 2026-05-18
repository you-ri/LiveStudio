// Copyright (c) You-Ri, 2026
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Unity 値型(Color/Vector/Quaternion/Rect/TransformValue/Texture2D)を JToken へ
    /// 変換する純粋なリーフ直列化。ExposedPropertySerializer から分離(自己完結・依存なし)。
    /// </summary>
    internal static class UnityScalarSerializer
    {
        internal static JToken SerializeColor(Color c)
            => JObject.FromObject(new { r = c.r, g = c.g, b = c.b, a = c.a });

        internal static JToken SerializeVector3(Vector3 v)
            => JObject.FromObject(new { x = v.x, y = v.y, z = v.z });

        internal static JToken SerializeVector2(Vector2 v)
            => JObject.FromObject(new { x = v.x, y = v.y });

        internal static JToken SerializeQuaternion(Quaternion q)
            => JObject.FromObject(new { x = q.x, y = q.y, z = q.z, w = q.w });

        internal static JToken SerializeRect(Rect r)
            => JObject.FromObject(new { x = r.x, y = r.y, width = r.width, height = r.height });

        internal static JToken SerializeTransformValue(TransformValue t)
            => new JObject
            {
                ["position"] = SerializeVector3(t.position),
                ["rotation"] = SerializeQuaternion(t.rotation),
                ["scale"] = SerializeVector3(t.scale),
            };

        internal static JToken SerializeTexture2D(Texture2D tex)
        {
            if (tex == null)
                return JValue.CreateNull();

            try
            {
                // 読み取り可能かチェック
                if (!tex.isReadable)
                {
                    Debug.LogWarning($"[RemoteControl] Texture2D '{tex.name}' is not readable and cannot be serialized");
                    return JObject.FromObject(new { width = tex.width, height = tex.height, format = tex.format.ToString(), image = "" });
                }

                // PNG形式でエンコード
                byte[] pngData = tex.EncodeToPNG();
                if (pngData == null || pngData.Length == 0)
                {
                    Debug.LogWarning($"[RemoteControl] Failed to encode Texture2D '{tex.name}' to PNG");
                    return JObject.FromObject(new { width = tex.width, height = tex.height, format = tex.format.ToString(), image = "" });
                }

                // Base64エンコード
                string base64Image = System.Convert.ToBase64String(pngData);

                return JObject.FromObject(new
                {
                    width = tex.width,
                    height = tex.height,
                    format = tex.format.ToString(),
                    image = base64Image
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RemoteControl] Error serializing Texture2D '{tex.name}': {ex.Message}");
                return JObject.FromObject(new { width = 0, height = 0, format = "Unknown", image = "" });
            }
        }
    }
}
