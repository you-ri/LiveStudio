using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Lilium.LiveStudio
{

    public enum CameraRotationType
    {
        Default,
        UpsideDown,
        LeftSideDown,
        RightSideDown
    }

    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(200)]
    public class CameraRotater : MonoBehaviour
    {
        [SerializeField]
        CameraRotationType _rotationType = CameraRotationType.Default;

        Camera _camera;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void LateUpdate()
        {
            if (_rotationType == CameraRotationType.UpsideDown)
            {
                _camera.transform.rotation = _camera.transform.rotation * Quaternion.Euler(0, 0, 180);
            }
            else if (_rotationType == CameraRotationType.LeftSideDown)
            {
                _camera.transform.rotation = _camera.transform.rotation * Quaternion.Euler(0, 0, 90);
            }
            else if (_rotationType == CameraRotationType.RightSideDown)
            {
                _camera.transform.rotation = _camera.transform.rotation * Quaternion.Euler(0, 0, -90);
            }
            else
            {
            }
            
        }
    }


}