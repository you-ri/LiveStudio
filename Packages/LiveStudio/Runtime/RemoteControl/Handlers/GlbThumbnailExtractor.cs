// Copyright (c) You-Ri, 2026

using System;
using System.IO;

using Newtonsoft.Json.Linq;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Extracts the embedded VRM thumbnail image from a .vrm / .glb file without depending on
    /// UniVRM: it parses the glTF binary (glb) container directly and slices the thumbnail image
    /// bytes out of the BIN chunk. Works for both VRM 0.x (<c>extensions.VRM.meta.texture</c>) and
    /// VRM 1.0 (<c>extensions.VRMC_vrm.meta.thumbnailImage</c>).
    ///
    /// Pure System.IO + Newtonsoft, so it can run off the main thread. Returns the raw PNG/JPEG
    /// bytes exactly as stored in the file (no Texture2D round-trip / re-encode).
    /// </summary>
    public static class GlbThumbnailExtractor
    {
        // glb little-endian magic / chunk type tags.
        private const uint kGlbMagic = 0x46546C67; // "glTF"
        private const uint kChunkJson = 0x4E4F534A; // "JSON"
        private const uint kChunkBin = 0x004E4942;  // "BIN\0"

        /// <summary>
        /// Reads <paramref name="filePath"/> and, if it contains a VRM thumbnail, returns its bytes
        /// and mime type. Returns false (with null outputs) for any non-glb / no-thumbnail / error case.
        /// </summary>
        public static bool TryExtract(string filePath, out byte[] bytes, out string mimeType)
        {
            bytes = null;
            mimeType = null;

            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return false;
                }

                byte[] file = File.ReadAllBytes(filePath);
                if (!TryReadGlbChunks(file, out JObject gltf, out int binOffset, out int binLength))
                {
                    return false;
                }

                int imageIndex = ResolveThumbnailImageIndex(gltf);
                if (imageIndex < 0)
                {
                    return false;
                }

                var images = gltf["images"] as JArray;
                if (images == null || imageIndex >= images.Count)
                {
                    return false;
                }

                var image = images[imageIndex] as JObject;
                if (image == null)
                {
                    return false;
                }

                mimeType = (string)image["mimeType"];

                // glb-embedded image: bufferView into the BIN chunk.
                var bufferViewToken = image["bufferView"];
                if (bufferViewToken != null && bufferViewToken.Type == JTokenType.Integer)
                {
                    if (!TrySliceBufferView(gltf, (int)bufferViewToken, file, binOffset, binLength, out bytes))
                    {
                        return false;
                    }
                }
                else if (image["uri"] is JValue uri && uri.Type == JTokenType.String)
                {
                    // External / data-uri image (rare for glb). Only handle inline base64 data URIs.
                    if (!TryDecodeDataUri((string)uri.Value, ref mimeType, out bytes))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(mimeType))
                {
                    mimeType = SniffMimeType(bytes);
                }

                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[LiveStudio] Failed to extract VRM thumbnail from '{filePath}': {e.Message}");
                bytes = null;
                mimeType = null;
                return false;
            }
        }

        /// <summary>
        /// Parses the 12-byte glb header and the JSON / BIN chunks. Returns the parsed glTF JSON and
        /// the BIN chunk's byte range within <paramref name="file"/>.
        /// </summary>
        private static bool TryReadGlbChunks(byte[] file, out JObject gltf, out int binOffset, out int binLength)
        {
            gltf = null;
            binOffset = 0;
            binLength = 0;

            if (file.Length < 12)
            {
                return false;
            }

            if (BitConverter.ToUInt32(file, 0) != kGlbMagic)
            {
                return false; // Not a binary glTF (e.g. a text .gltf).
            }

            // file[4] = version (expect 2), file[8] = total length (ignored; we use chunk lengths).
            int pos = 12;
            while (pos + 8 <= file.Length)
            {
                int chunkLength = (int)BitConverter.ToUInt32(file, pos);
                uint chunkType = BitConverter.ToUInt32(file, pos + 4);
                int dataStart = pos + 8;
                if (dataStart + chunkLength > file.Length)
                {
                    break; // Truncated / malformed.
                }

                if (chunkType == kChunkJson)
                {
                    string json = System.Text.Encoding.UTF8.GetString(file, dataStart, chunkLength);
                    gltf = JObject.Parse(json);
                }
                else if (chunkType == kChunkBin)
                {
                    binOffset = dataStart;
                    binLength = chunkLength;
                }

                // Chunks are 4-byte aligned, but chunkLength already includes padding in glb.
                pos = dataStart + chunkLength;
            }

            return gltf != null;
        }

        /// <summary>
        /// Returns the glTF image index of the VRM thumbnail, or -1 when absent.
        /// VRM 1.0 stores an image index directly; VRM 0.x stores a texture index that must be
        /// dereferenced through <c>textures[t].source</c>.
        /// </summary>
        private static int ResolveThumbnailImageIndex(JObject gltf)
        {
            var extensions = gltf["extensions"] as JObject;
            if (extensions == null)
            {
                return -1;
            }

            // VRM 1.0. Cast the JToken (not JValue.Value): Newtonsoft parses JSON integers as
            // boxed long, so (int)token converts correctly whereas (int)token.Value would throw.
            var vrm1Meta = extensions["VRMC_vrm"]?["meta"];
            if (vrm1Meta?["thumbnailImage"] is JValue thumb && thumb.Type == JTokenType.Integer)
            {
                return (int)thumb;
            }

            // VRM 0.x: meta.texture is an index into glTF.textures.
            var vrm0Meta = extensions["VRM"]?["meta"];
            if (vrm0Meta?["texture"] is JValue tex && tex.Type == JTokenType.Integer)
            {
                int textureIndex = (int)tex;
                if (textureIndex < 0)
                {
                    return -1;
                }

                var textures = gltf["textures"] as JArray;
                if (textures != null && textureIndex < textures.Count)
                {
                    var source = textures[textureIndex]?["source"];
                    if (source != null && source.Type == JTokenType.Integer)
                    {
                        return (int)source;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Slices the bytes referenced by <paramref name="bufferViewIndex"/> out of the BIN chunk.
        /// Only buffer 0 (the glb BIN chunk) is supported.
        /// </summary>
        private static bool TrySliceBufferView(JObject gltf, int bufferViewIndex, byte[] file,
            int binOffset, int binLength, out byte[] bytes)
        {
            bytes = null;

            var bufferViews = gltf["bufferViews"] as JArray;
            if (bufferViews == null || bufferViewIndex < 0 || bufferViewIndex >= bufferViews.Count)
            {
                return false;
            }

            var bufferView = bufferViews[bufferViewIndex] as JObject;
            if (bufferView == null)
            {
                return false;
            }

            int buffer = bufferView["buffer"]?.Type == JTokenType.Integer ? (int)bufferView["buffer"] : 0;
            if (buffer != 0)
            {
                return false; // Thumbnails live in the embedded BIN chunk (buffer 0) for glb.
            }

            int byteOffset = bufferView["byteOffset"]?.Type == JTokenType.Integer ? (int)bufferView["byteOffset"] : 0;
            int byteLength = bufferView["byteLength"]?.Type == JTokenType.Integer ? (int)bufferView["byteLength"] : 0;
            if (byteLength <= 0 || byteOffset < 0 || byteOffset + byteLength > binLength)
            {
                return false;
            }

            bytes = new byte[byteLength];
            Array.Copy(file, binOffset + byteOffset, bytes, 0, byteLength);
            return true;
        }

        private static bool TryDecodeDataUri(string uri, ref string mimeType, out byte[] bytes)
        {
            bytes = null;
            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int comma = uri.IndexOf(',');
            if (comma < 0)
            {
                return false;
            }

            string header = uri.Substring(5, comma - 5); // between "data:" and ","
            if (!header.Contains("base64"))
            {
                return false;
            }

            if (string.IsNullOrEmpty(mimeType))
            {
                int semicolon = header.IndexOf(';');
                mimeType = semicolon > 0 ? header.Substring(0, semicolon) : header;
            }

            bytes = Convert.FromBase64String(uri.Substring(comma + 1));
            return true;
        }

        /// <summary>Detects png / jpeg from magic bytes; falls back to image/png.</summary>
        private static string SniffMimeType(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return "image/jpeg";
            }
            return "image/png";
        }
    }
}
