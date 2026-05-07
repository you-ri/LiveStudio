// Copyright (c) You-Ri, 2026
//
// ⚠ 同期注意: このファイルは jp.lilium.virgo.capture と jp.lilium.livestudio.virgo に
//   複製されています。片方を変更したときは必ずもう片方も同じ内容に更新してください。
//   ペア: Packages/jp.lilium.virgo.capture/Runtime/Data/CameraData.cs
//   namespace のみ Lilium.Virgo.Capture / Lilium.LiveStudio.Virgo で異なります。

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Virgo
{
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
            aspect = 1.777777f // 16:9
        };


    }
}
