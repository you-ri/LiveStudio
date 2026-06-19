// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Process-wide cache of bundle thumbnails keyed by bundle file path.
    ///
    /// A LiveStudio bundle's <see cref="BundleThumbnail"/> is read once, while the bundle is already
    /// open during load (see the bundle loaders), and stored here. The REST handler then serves the
    /// stored bytes without re-opening the bundle — opening an AssetBundle inside a request handler is
    /// unsafe (it can stall the server and conflict with the live bundle). As a result, bundle previews
    /// become available after the avatar/prop has been loaded at least once, which matches the intended
    /// "read the thumbnail after load" behavior.
    /// </summary>
    public static class BundleThumbnailCache
    {
        private static readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        private readonly struct Entry
        {
            public readonly byte[] bytes;
            public readonly string mimeType;
            public Entry(byte[] bytes, string mimeType)
            {
                this.bytes = bytes;
                this.mimeType = mimeType;
            }
        }

        /// <summary>Stores a thumbnail for <paramref name="filePath"/>. No-op for empty input.</summary>
        public static void Store(string filePath, byte[] bytes, string mimeType)
        {
            if (string.IsNullOrEmpty(filePath) || bytes == null || bytes.Length == 0)
            {
                return;
            }
            _entries[_Normalize(filePath)] = new Entry(
                bytes, string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType);
        }

        /// <summary>Returns a previously stored thumbnail, or false when none is cached.</summary>
        public static bool TryGet(string filePath, out byte[] bytes, out string mimeType)
        {
            bytes = null;
            mimeType = null;
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }
            if (_entries.TryGetValue(_Normalize(filePath), out var entry))
            {
                bytes = entry.bytes;
                mimeType = entry.mimeType;
                return true;
            }
            return false;
        }

        private static string _Normalize(string path) => path.Replace('\\', '/');
    }
}
