// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Lilium.RemoteControl.Notification;

namespace Lilium.LiveStudio
{
    [Serializable]
    [LiveClass]
    public struct SnapshotInfo
    {
        [LiveField]
        public string name;

        [LiveField]
        public string timestamp;

        [LiveField]
        public bool hasThumbnail;
    }

    /// <summary>
    /// Scene-state snapshots.
    ///
    /// A snapshot is a FULL serialization of the live scene (every exposed value, including
    /// prop/avatar instances), not the delta a scene save writes — so restoring one reproduces
    /// the captured state regardless of what changed since. Files are stored in the open
    /// project's "Snapshots" folder as "&lt;name&gt;.snapshot.json", with an optional camera
    /// screenshot "&lt;name&gt;.snapshot.png" taken from the live camera at capture time
    /// (served to remote apps by <c>SnapshotImageHandler</c>).
    ///
    /// Restoring applies the file on top of the live scene without adopting it as the current
    /// scene file (see <see cref="RemoteControlBehaviour.ApplyExternalData"/>): the restored
    /// values count as unsaved edits until the user saves the scene.
    /// </summary>
    [LiveClass(Icon = "photo_library")]
    public static class SnapshotManager
    {
        public const string kSnapshotDirName = "Snapshots";
        public const string kSnapshotFileExtension = ".snapshot.json";
        public const string kThumbnailFileExtension = ".snapshot.png";

        /// <summary>
        /// Snapshots in the open project's "Snapshots" folder, newest first. Listed by path only
        /// (file contents are not read here).
        /// </summary>
        [LiveProperty, Hide]
        public static SnapshotInfo[] snapshots
        {
            get
            {
                var dir = GetSnapshotDirectory();
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return Array.Empty<SnapshotInfo>();

                var files = Directory.GetFiles(dir, "*" + kSnapshotFileExtension, SearchOption.TopDirectoryOnly);
                var list = new List<SnapshotInfo>(files.Length);
                foreach (var file in files)
                {
                    var name = _SnapshotNameFromPath(file);
                    if (string.IsNullOrEmpty(name)) continue;
                    list.Add(new SnapshotInfo
                    {
                        name = name,
                        timestamp = File.GetLastWriteTime(file).ToString("o"),
                        hasThumbnail = File.Exists(Path.Combine(dir, name + kThumbnailFileExtension)),
                    });
                }
                // Newest first. The timestamp is ISO 8601, so ordinal string order is time order.
                list.Sort((a, b) => string.CompareOrdinal(b.timestamp, a.timestamp));
                return list.ToArray();
            }
        }

        /// <summary>
        /// True when the path names a snapshot file. Path-only (the content is never read), so the
        /// project crawl can classify snapshots like every other asset kind.
        /// </summary>
        public static bool IsSnapshotFile(string filePath)
        {
            return !string.IsNullOrEmpty(filePath) &&
                filePath.EndsWith(kSnapshotFileExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// "{projectPath}/Snapshots", or null when no project is open. Does not create the folder.
        /// </summary>
        public static string GetSnapshotDirectory()
        {
            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath)) return null;
            return Path.Combine(projectPath, kSnapshotDirName);
        }

        [LiveFunction(label = "SNAPSHOT_TAKE"), Hide]
        public static void TakeSnapshot()
        {
            var dir = GetSnapshotDirectory();
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogError("[Studio] TakeSnapshot: no project is open.");
                return;
            }

            var provider = _FindProvider();
            var container = provider != null ? provider.objectContainer : null;
            if (container == null)
            {
                Debug.LogError("[Studio] TakeSnapshot: no RemoteControlBehaviour with a container found.");
                return;
            }

            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Full (non-delta) serialization: unlike a scene save, every exposed value is written,
            // so the file restores the same state even after defaults or the scene file change.
            var allObjects = new List<ILiveObject>(container.EnumerateAllObjects());
            var objects = LiveObjectGraph.ResolveLiveObjects(allObjects, container);
            var json = LiveSceneSerializer.LiveSceneToJson(
                objects, container,
                SerializeMode.Snapshot, ExcludeFilter.None,
                LiveSceneSaveSystem.ResolveBaseSceneName());

            var name = _GenerateSnapshotName(dir);
            File.WriteAllText(Path.Combine(dir, name + kSnapshotFileExtension), json);

            _TryWriteThumbnail(Path.Combine(dir, name + kThumbnailFileExtension));

            Debug.Log($"[Studio] Snapshot saved: '{Path.Combine(dir, name + kSnapshotFileExtension)}'");
            LivePropertyBroadcast.BroadcastStaticProperty(typeof(SnapshotManager), nameof(snapshots));
            // The file is a project asset too, so the project listing has to see it appear.
            ProjectManager.RecrawlProject();
            RemoteNotificationSystem.Show(
                LocalizationSystem.Translate("NOTIFY_SNAPSHOT_SAVED"),
                RemoteNotificationSystem.Type.Success,
                icon: "photo_camera");
        }

        [LiveFunction(label = "SNAPSHOT_RESTORE"), Hide]
        public static void RestoreSnapshot(string name)
        {
            var filePath = _ResolveSnapshotFile(name, kSnapshotFileExtension);
            if (filePath == null) return;
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[Studio] RestoreSnapshot: snapshot not found: '{filePath}'");
                return;
            }

            var provider = _FindProvider();
            if (provider == null)
            {
                Debug.LogError("[Studio] RestoreSnapshot: no RemoteControlBehaviour found.");
                return;
            }

            provider.ApplyExternalData(filePath);

            Debug.Log($"[Studio] Snapshot restored: '{filePath}'");
            RemoteNotificationSystem.Show(
                LocalizationSystem.Translate("NOTIFY_SNAPSHOT_RESTORED"),
                RemoteNotificationSystem.Type.Success,
                icon: "history");
        }

        [LiveFunction(label = "SNAPSHOT_DELETE"), Hide]
        public static void DeleteSnapshot(string name)
        {
            var filePath = _ResolveSnapshotFile(name, kSnapshotFileExtension);
            if (filePath == null) return;

            if (File.Exists(filePath)) File.Delete(filePath);
            var thumbnailPath = _ResolveSnapshotFile(name, kThumbnailFileExtension);
            if (thumbnailPath != null && File.Exists(thumbnailPath)) File.Delete(thumbnailPath);

            Debug.Log($"[Studio] Snapshot deleted: '{filePath}'");
            LivePropertyBroadcast.BroadcastStaticProperty(typeof(SnapshotManager), nameof(snapshots));
            ProjectManager.RecrawlProject();
        }

        /// <summary>
        /// Resolves a snapshot name to "{Snapshots}/{name}{extension}", or null when no project is
        /// open or the name is not a plain file name (path separators / ".." are rejected — names
        /// arrive from remote apps, so they must never escape the snapshot folder).
        /// </summary>
        internal static string _ResolveSnapshotFile(string name, string extension)
        {
            var dir = GetSnapshotDirectory();
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogError("[Studio] Snapshot: no project is open.");
                return null;
            }
            if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name.Contains(".."))
            {
                Debug.LogError($"[Studio] Snapshot: invalid snapshot name: '{name}'");
                return null;
            }
            return Path.Combine(dir, name + extension);
        }

        // "<dir>/Foo.snapshot.json" -> "Foo" (null when the file name does not carry the extension).
        private static string _SnapshotNameFromPath(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            if (!fileName.EndsWith(kSnapshotFileExtension, StringComparison.OrdinalIgnoreCase)) return null;
            return fileName.Substring(0, fileName.Length - kSnapshotFileExtension.Length);
        }

        // "Snapshot_20260729_153012", suffixed "_1", "_2", ... when taken within the same second.
        private static string _GenerateSnapshotName(string dir)
        {
            var baseName = "Snapshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var name = baseName;
            int suffix = 1;
            while (File.Exists(Path.Combine(dir, name + kSnapshotFileExtension)))
            {
                name = baseName + "_" + suffix++;
            }
            return name;
        }

        // Renders the live camera into its (synchronously captured) preview texture and writes it
        // as the snapshot thumbnail. Skipped silently when no live camera exists — the snapshot
        // itself is still valid, the card just shows a placeholder icon.
        private static void _TryWriteThumbnail(string thumbnailPath)
        {
            ILiveCamera liveCamera = null;
            var cameras = CameraService.cameras;
            if (cameras != null)
            {
                foreach (var camera in cameras)
                {
                    if (camera != null && camera.isLive) { liveCamera = camera; break; }
                }
            }
            if (liveCamera == null) return;

            liveCamera.RequestCameraImage();
            var image = liveCamera.image;
            if (image == null) return;

            File.WriteAllBytes(thumbnailPath, image.EncodeToPNG());
        }

#if UNITY_2022_3_OR_NEWER
        private static RemoteControlBehaviour _FindProvider()
            => UnityEngine.Object.FindFirstObjectByType<RemoteControlBehaviour>();
#else
        private static RemoteControlBehaviour _FindProvider()
            => UnityEngine.Object.FindObjectOfType<RemoteControlBehaviour>();
#endif
    }
}
