// Copyright (c) You-Ri, 2026

using System.IO;

using UnityEngine;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Writes what the live camera sees to a PNG beside a file the app has just written.
    ///
    /// Shared by the two things this app records: a snapshot -- one frame of live data, written as
    /// JSON -- and a take, a run of it. Both are listed on the same page and both are recognized by
    /// their picture, so how that picture is taken is answered once rather than per kind.
    /// </summary>
    public static class LiveCameraThumbnail
    {
        /// <summary>
        /// Renders the live camera into its (synchronously captured) preview texture and writes it
        /// as a PNG at <paramref name="thumbnailPath"/>.
        ///
        /// False when there is no live camera to render, or when it has no image yet -- the file the
        /// picture belongs to is still valid, the card just shows a placeholder icon. So the caller
        /// has nothing to undo on failure, and none of this is worth failing a capture over.
        /// </summary>
        public static bool TryWrite(string thumbnailPath)
        {
            if (string.IsNullOrEmpty(thumbnailPath)) return false;

            ILiveCamera liveCamera = null;
            var cameras = CameraService.cameras;
            if (cameras != null)
            {
                foreach (var camera in cameras)
                {
                    if (camera != null && camera.isLive) { liveCamera = camera; break; }
                }
            }
            if (liveCamera == null) return false;

            liveCamera.RequestCameraImage();
            var image = liveCamera.image;
            if (image == null) return false;

            File.WriteAllBytes(thumbnailPath, image.EncodeToPNG());
            return true;
        }
    }
}
