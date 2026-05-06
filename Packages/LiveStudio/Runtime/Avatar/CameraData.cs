// Copyright (c) You-Ri, 2026

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Snapshot of a camera state used for avatar control. Independent from any
    /// motion-capture transport format.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct CameraData
    {
        static CameraData() => CompilerUtility.CheckUnmanaged<CameraData>();

        public Vector3 position;

        public Quaternion rotation;

        public float fieldOfView;

        public float nearClipPlane;

        public float farClipPlane;

        public float aspect;

        public static CameraData From(Camera camera)
        {
            return new CameraData
            {
                position = camera.transform.localPosition,
                rotation = camera.transform.localRotation,
                fieldOfView = camera.fieldOfView,
                nearClipPlane = camera.nearClipPlane,
                farClipPlane = camera.farClipPlane,
                aspect = camera.aspect,
            };
        }

        public void To(Camera camera)
        {
            camera.transform.localPosition = position;
            camera.transform.localRotation = rotation;
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = nearClipPlane;
            camera.farClipPlane = farClipPlane;
            camera.aspect = aspect;
        }

        public static CameraData Default => new CameraData
        {
            position = Vector3.zero,
            rotation = Quaternion.identity,
            fieldOfView = 60.0f,
            nearClipPlane = 0.1f,
            farClipPlane = 1000.0f,
            aspect = 1.777777f, // 16:9
        };
    }
}
