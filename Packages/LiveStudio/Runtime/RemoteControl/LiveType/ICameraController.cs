using System;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [LiveEnum]
    public enum LookAt
    {
        Face,

        Free,
    }


    [LiveEnum]
    public enum CameraControlType
    {
        OrbitalFollow,

        Free,
    }

    [Serializable]
    [LiveClass]
    public abstract class ICameraController
    {
        public abstract void Setup(CinemachineCamera camera);

        public abstract void Teardown(CinemachineCamera camera);

        public abstract void Update(CinemachineCamera camera);
    }
}
