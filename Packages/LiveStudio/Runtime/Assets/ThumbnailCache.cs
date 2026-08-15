// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Two-tier cache of asset preview thumbnails keyed by the asset file path.
    ///
    /// L1 is a process-wide in-memory map (avoids disk I/O per HTTP request); L2 is a file cache under
    /// the open project's hidden ".livestudio/cache/thumbnails" folder (see <see cref="ProjectPaths"/>),
    /// so previews survive restarts and bundle previews become available before the asset is loaded again.
    ///
    /// Thumbnails come from two sources, both routed here: a bundle's packed <see cref="BundleThumbnail"/>
    /// (stored by the bundle loaders while the bundle is open) and a VRM/glb's embedded thumbnail
    /// (extracted and stored by <see cref="AssetThumbnailProvider"/> on first request). Disk entries are invalidated by
    /// modification time: a source file newer than its cached thumbnail is treated as a miss (and the
    /// stale file removed), so re-exported bundles / replaced VRM self-heal.
    ///
    /// Thread-safety: <see cref="Store"/> runs on the main thread (loaders) while <see cref="TryGet"/>
    /// runs on request worker threads, so the in-memory map is guarded by a lock.
    /// </summary>
    public static class ThumbnailCache
    {
        private static readonly object _gate = new object();

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

        /// <summary>
        /// Stores a thumbnail for <paramref name="filePath"/> in memory and, when a project is open, on
        /// disk. No-op for empty input.
        /// </summary>
        public static void Store(string filePath, byte[] bytes, string mimeType)
        {
            if (string.IsNullOrEmpty(filePath) || bytes == null || bytes.Length == 0)
            {
                return;
            }

            var mime = string.IsNullOrEmpty(mimeType) ? "image/png" : mimeType;
            lock (_gate)
            {
                _entries[_Normalize(filePath)] = new Entry(bytes, mime);
            }
            _WriteDisk(filePath, bytes, mime);
        }

        /// <summary>
        /// Returns a cached thumbnail: from memory if present, otherwise from the disk cache (which also
        /// re-populates memory). Returns false when none is cached or the cached copy is stale.
        /// </summary>
        public static bool TryGet(string filePath, out byte[] bytes, out string mimeType)
        {
            bytes = null;
            mimeType = null;
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            var key = _Normalize(filePath);
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var entry))
                {
                    bytes = entry.bytes;
                    mimeType = entry.mimeType;
                    return true;
                }
            }

            if (!_TryReadDisk(filePath, out bytes, out mimeType))
            {
                return false;
            }

            lock (_gate)
            {
                _entries[key] = new Entry(bytes, mimeType);
            }
            return true;
        }

        // ---- disk (L2) ----

        private static void _WriteDisk(string filePath, byte[] bytes, string mime)
        {
            var dir = ProjectPaths.EnsureThumbnailCacheDir();
            if (string.IsNullOrEmpty(dir)) return; // no project open → memory only

            try
            {
                var path = Path.Combine(dir, _CacheFileName(filePath, mime));
                File.WriteAllBytes(path, bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio] Failed to write thumbnail cache for '{filePath}': {ex.Message}");
            }
        }

        private static bool _TryReadDisk(string filePath, out byte[] bytes, out string mimeType)
        {
            bytes = null;
            mimeType = null;

            var dir = ProjectPaths.GetThumbnailCacheDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            var hash = _Hash(filePath);
            // Probe the two stored encodings; mime is derived from the extension.
            if (_TryReadEncoded(dir, hash, ".png", "image/png", filePath, out bytes, out mimeType)) return true;
            if (_TryReadEncoded(dir, hash, ".jpg", "image/jpeg", filePath, out bytes, out mimeType)) return true;
            return false;
        }

        private static bool _TryReadEncoded(
            string dir, string hash, string ext, string mime, string sourcePath,
            out byte[] bytes, out string mimeType)
        {
            bytes = null;
            mimeType = null;

            var path = Path.Combine(dir, hash + ext);
            if (!File.Exists(path)) return false;

            try
            {
                // Invalidate when the source asset is newer than its cached thumbnail.
                if (File.Exists(sourcePath)
                    && File.GetLastWriteTimeUtc(sourcePath) > File.GetLastWriteTimeUtc(path))
                {
                    File.Delete(path);
                    return false;
                }

                var data = File.ReadAllBytes(path);
                if (data.Length == 0) return false;
                bytes = data;
                mimeType = mime;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Studio] Failed to read thumbnail cache '{path}': {ex.Message}");
                return false;
            }
        }

        private static string _CacheFileName(string filePath, string mime)
        {
            var ext = mime == "image/jpeg" ? ".jpg" : ".png";
            return _Hash(filePath) + ext;
        }

        // Stable cache key: the asset path relative to the open project (forward slashes) so the cache
        // survives moving the project folder; falls back to the absolute path when outside the project.
        private static string _Hash(string filePath)
        {
            var key = _RelativeToProject(filePath);
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string _RelativeToProject(string filePath)
        {
            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath)) return _Normalize(filePath);

            try
            {
                // Unity 2021 has no Path.GetRelativePath; relativize by prefix match instead.
                var projectFull = Path.GetFullPath(projectPath).TrimEnd('/', '\\');
                var fileFull = Path.GetFullPath(filePath);
                var prefix = projectFull + Path.DirectorySeparatorChar;
                if (fileFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return fileFull.Substring(prefix.Length).Replace('\\', '/');
                return fileFull.Replace('\\', '/');
            }
            catch
            {
                return _Normalize(filePath);
            }
        }

        private static string _Normalize(string path) => path.Replace('\\', '/');

#if UNITY_EDITOR
        // With Domain Reload disabled the in-memory map would otherwise persist across editor play
        // sessions; clear it so each session re-reads from the disk cache (which still persists).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetState()
        {
            lock (_gate) _entries.Clear();
        }
#endif
    }
}
