// Copyright (c) You-Ri, 2026

using UnityEngine;
using UnityEngine.Serialization;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// ソケット
    /// </summary>
    [ExecuteAlways]
    public class Socket : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("socketName")]
        private string _socketName;

        private string _registeredName;

        public string socketName
        {
            get => _socketName;
            set
            {
                if (_socketName == value) return;
                _socketName = value;
                if (isActiveAndEnabled) _UpdateRegistration();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetStatics()
        {
            SelectableService<Socket>.Initialize();
        }

        void OnEnable()
        {
            _UpdateRegistration();
        }

        void OnDisable()
        {
            _ClearRegistration();
        }

        void OnValidate()
        {
            if (isActiveAndEnabled) _UpdateRegistration();
        }

        void _UpdateRegistration()
        {
            if (_registeredName == _socketName) return;

            _ClearRegistration();

            if (string.IsNullOrEmpty(_socketName)) return;

            SelectableService<Socket>.Register(_socketName, this);
            _registeredName = _socketName;
        }

        void _ClearRegistration()
        {
            if (_registeredName == null) return;

            SelectableService<Socket>.Unregister(_registeredName, this);
            _registeredName = null;
        }

        /// <summary>
        /// 指定Transformの子としてSocketを作成する。
        /// referenceWorldRotation を親の標準的なワールド座標系として指定することで、
        /// スケルトンの座標系が異なるキャラクターでも生成された子Transformの向きを統一する。
        /// </summary>
        /// <param name="parent">親Transform（通常はボーン）。null不可。</param>
        /// <param name="socketName">Socket名。SelectableService への登録キーになる。</param>
        /// <param name="referenceWorldRotation">親の基準となるワールド回転。子の初期ワールド回転がこの値に揃う。</param>
        public static Socket CreateSocket(Transform parent, string socketName, Quaternion referenceWorldRotation)
        {
            if (parent == null)
            {
                Debug.LogError("[Studio] Parent transform is null.");
                return null;
            }

            var go = new GameObject(socketName);
            var socketTransform = go.transform;
            socketTransform.SetParent(parent, worldPositionStays: false);
            socketTransform.rotation = referenceWorldRotation;

            var socket = go.AddComponent<Socket>();
            socket.socketName = socketName;
            return socket;
        }
    }
}
