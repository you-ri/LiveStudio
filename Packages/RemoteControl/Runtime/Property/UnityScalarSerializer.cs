// Copyright (c) You-Ri, 2026
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl
{
    /// <summary>
    /// Unity 値型(Color/Vector/Quaternion/Rect/TransformValue/Texture2D)を JToken へ
    /// 変換する純粋なリーフ直列化。LivePropertySerializer から分離(自己完結・依存なし)。
    /// </summary>
    internal static class UnityScalarSerializer
    {
        // JObject.FromObject は呼び出しごとに JsonSerializer + JTokenWriter + 匿名型インスタンスを
        // 確保する。ここは full serialize (GET / シーン保存 / dirty 判定) の全経路を通るため、
        // JObject を直接組んで確保を避ける。float → JValue の暗黙変換は FromObject 内部の
        // WriteValue(float) と同じ JValue(float) を作るので出力 byte は不変。

        internal static JToken SerializeColor(Color c)
            => new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };

        internal static JToken SerializeVector3(Vector3 v)
            => new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };

        internal static JToken SerializeVector2(Vector2 v)
            => new JObject { ["x"] = v.x, ["y"] = v.y };

        internal static JToken SerializeQuaternion(Quaternion q)
            => new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };

        internal static JToken SerializeRect(Rect r)
            => new JObject { ["x"] = r.x, ["y"] = r.y, ["width"] = r.width, ["height"] = r.height };

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
                    return _BuildTextureJson(tex.width, tex.height, tex.format.ToString(), "");
                }

                // PNG形式でエンコード
                byte[] pngData = tex.EncodeToPNG();
                if (pngData == null || pngData.Length == 0)
                {
                    Debug.LogWarning($"[RemoteControl] Failed to encode Texture2D '{tex.name}' to PNG");
                    return _BuildTextureJson(tex.width, tex.height, tex.format.ToString(), "");
                }

                // Base64エンコード
                string base64Image = System.Convert.ToBase64String(pngData);

                return _BuildTextureJson(tex.width, tex.height, tex.format.ToString(), base64Image);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RemoteControl] Error serializing Texture2D '{tex.name}': {ex.Message}");
                return _BuildTextureJson(0, 0, "Unknown", "");
            }
        }

        private static JObject _BuildTextureJson(int width, int height, string format, string image)
            => new JObject
            {
                ["width"] = width,
                ["height"] = height,
                ["format"] = format,
                ["image"] = image,
            };
    }
}
