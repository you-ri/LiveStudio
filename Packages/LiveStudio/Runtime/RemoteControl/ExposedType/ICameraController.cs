using System;
using UnityEngine;
using Unity.Cinemachine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    [ExposedEnum]
    public enum LookAt
    {
        Face,

        Free,
    }


    [ExposedEnum]
    public enum CameraControlType
    {
        OrbitalFollow,

        Free,
    }

    [Serializable]
    [ExposedClass]
    public abstract class ICameraController
    {
        public abstract void Setup(CinemachineCamera camera);

        public abstract void Teardown(CinemachineCamera camera);

        public abstract void Update(CinemachineCamera camera);
    }
}
