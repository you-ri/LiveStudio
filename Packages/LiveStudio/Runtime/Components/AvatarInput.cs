using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.Scripting;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// 入力デバイスの情報を保持するためのコンポーネント
    /// </summary>
    [ExposedClass("InputActions", Category = "Input", Icon = "keyboard", HideInScene = true)]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class AvatarInput : MonoBehaviour, IInputActionProvider,
        IExposedSerializeCallback, IExposedDeserializeCallback
    {
        /// <summary>
        /// デバイスフィルタ
        /// </summary>
        [SerializeField, ExposedField, Hide]
        [FormerlyExposedAs("deviceName")]
        internal string _deviceName;

        [NonSerialized] private InputUser _inputUser;

        public InputActionMap inputActionMap => _inputActionMap;

        [SerializeField]
        private InputActionMap _inputActionMap;

        // --- Exposed Properties ---

        [ExposedProperty]
        public string deviceName
        {
            get => _deviceName;
            set
            {
                _deviceName = value;
                if (!string.IsNullOrEmpty(value))
                    PairDevice(value);
                else
                    UnpairDevice();
            }
        }

        // Shadow Field: serialization buffer for the InputActionMap state.
        // Refreshed from the live InputActionMap in OnBeforeExposedSerialize and
        // applied back to the InputActionMap in OnAfterExposedDeserialize. The
        // property getter intentionally returns a fresh snapshot rather than
        // this field so live API queries reflect the current map.
        [SerializeField, HideInInspector, ExposedField, Hide]
        [FormerlyExposedAs("settings")]
        private AvatarInputSettings _settings;

        /// <summary>
        /// InputActionMapの設定をシリアライズ可能な形で公開する。
        /// SceneToJson/SceneFromJson経由で保存/復元される。
        /// </summary>
        [ExposedProperty, Hide]
        public AvatarInputSettings settings
        {
            get => AvatarInputSettingsUtils.CreateSettingsFromAvatarInput(this);
            set
            {
                _settings = value;
                if (value != null)
                {
                    AvatarInputSettingsUtils.ApplySettingsToAvatarInput(this, value);
                }
            }
        }

        public void OnBeforeExposedSerialize()
        {
            // shadow field を最新 InputActionMap state から refresh してから JSON 化する
            _settings = AvatarInputSettingsUtils.CreateSettingsFromAvatarInput(this);
        }

        public void OnAfterExposedDeserialize()
        {
            // SetValueRaw は Property setter をバイパスするので、shadow field に書かれた
            // _settings を InputActionMap に反映するためここで apply する
            if (_settings != null)
            {
                AvatarInputSettingsUtils.ApplySettingsToAvatarInput(this, _settings);
            }
        }

        [ExposedProperty]
        public IEnumerable<string> actionNames
        {
            get
            {
                if (_inputActionMap == null) return Enumerable.Empty<string>();
                return _inputActionMap.actions.Select(a => a.name);
            }
        }


        /// <summary>
        /// 表情に対応する入力アクションの取得
        /// </summary>
        /// <param name="face"></param>
        /// <returns></returns>
        public InputAction FindInputAction(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException(
                    $"Input Key '{name}' does not defined");

            return _inputActionMap.FindAction(name);
        }

        /// <summary>
        /// 表情に対応する入力アクションの追加
        /// </summary>
        /// <param name="face"></param>
        public void AddInputAction (string name, string controlLayout = null)
        {
            if (string.IsNullOrEmpty(name)) 
                throw new ArgumentException(
                    $"Input Key '{name}' does not defined");

            InputActionMapUtils.SafeCreateAction(_inputActionMap, name, controlLayout);

        }

        /// <summary>
        /// 表情に対応する入力アクションの削除
        /// </summary>
        /// <param name="face"></param>
        public void RemoveInputAction (string name)
        {
            InputActionMapUtils.SafeRemoveAction(_inputActionMap, name);

        }


        private void OnEnable ()
        {
            Service<IInputActionProvider>.Register(this);
            _inputActionMap.Enable();

        }

        private void OnDisable()
        {
            _inputActionMap.Disable();
            Service<IInputActionProvider>.Unregister(this);
        }

        void Start ()
        {
            if (!string.IsNullOrEmpty (_deviceName)) {
                PairDevice (_deviceName);
            }
        }

        private void OnDestroy()
        {
            UnpairDevice();
        }

        public void PairDevice (string deviceName)
        {
            var device = InputSystem.GetDevice (deviceName);
            if (device != null) {
                PairDevice (device);
            }
        }

        /// <summary>
        /// 入力デバイスとペアリング設定
        /// </summary>
        /// <param name="device"></param>
        public void PairDevice (InputDevice device)
        {
            UnpairDevice ();
            _deviceName = device.name;

            if (!Application.isPlaying) return;

            //if (actionMap == null) {
            //    _inputUser = new InputUser ();
            //    return;
            //}

            _inputUser = InputUser.PerformPairingWithDevice (device, _inputUser);

            // If we don't have a valid user at this point, we don't have any paired devices.
            if (_inputUser.valid) {
                _inputUser.AssociateActionsWithUser (_inputActionMap);
            }

        }

        /// <summary>
        /// 入力デバイスとペアリング解除
        /// </summary>
        /// <param name="device"></param>
        public void UnpairDevice ()
        {
            _deviceName = "";
            if (!Application.isPlaying) return;

            if (_inputUser.valid) {
                _inputUser.UnpairDevices ();
            }
        }

        /// <summary>
        /// すべてのバインディングをリセット
        /// </summary>
        [Preserve]
        [ExposedFunction(label = "INPUTACTIONS_RESETBINDINGS")]
        public void ResetAllBindings()
        {
            if (_inputActionMap != null)
            {
                foreach (var action in _inputActionMap.actions)
                {
                    action.RemoveAllBindingOverrides();
                }
                InputActionMapUtils.RefreshAndMarkDirty(_inputActionMap);

                Debug.Log("[LiveStudio] All bindings reset and saved");
            }
        }
 
    }

}

