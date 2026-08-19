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
    /// One action of an <see cref="AvatarInput"/> map, exposed as a bindable slot.
    ///
    /// The element count is dynamic (per input map) while the element schema stays static, so the
    /// generic object surface can list every action and offer a rebind button per row without a
    /// dedicated route. Rebinding needs no argument beyond the row itself, which is the whole point:
    /// the client picks a row and presses a key.
    /// </summary>
    [LiveClass("InputActionBinding")]
    public class InputActionEntry
    {
        // The map this action lives in. Null on the default-constructed entry that the serializer
        // builds as an array-diff template, so every member has to tolerate it.
        private readonly InputActionMap _map;
        private readonly string _name = string.Empty;

        public InputActionEntry() { }

        internal InputActionEntry(InputActionMap map, string name)
        {
            _map = map;
            _name = name ?? string.Empty;
        }

        [LiveProperty, LiveKey]
        public string name => _name;

        /// <summary>The bound key as a person would read it ("A", "Left Button"), empty when unbound.</summary>
        [LiveProperty(label = "INPUT_ACTION_BINDING")]
        public string binding
        {
            get
            {
                var action = _Action();
                if (action == null || action.bindings.Count == 0) return string.Empty;
                return InputControlPath.ToHumanReadableString(
                    action.bindings[0].effectivePath,
                    InputControlPath.HumanReadableStringOptions.UseShortNames);
            }
        }

        [LiveProperty(label = "INPUT_ACTION_ENABLED")]
        public bool enabled => _Action()?.enabled ?? false;

        /// <summary>
        /// Listens for the next key or button on the Studio machine and binds it to this action.
        /// Same path as expression and operation rebinding (<see cref="RuntimeKeyBindingSystem"/>),
        /// which detects the key through the global InputSystem event stream, so the map does not
        /// need to be enabled.
        ///
        /// Returns as soon as listening starts — invocation results are not awaited, so there is
        /// nothing to report back. A client sees the outcome in <see cref="binding"/>, which its
        /// property polling picks up once the key lands.
        /// </summary>
        [LiveFunction(label = "INPUT_ACTION_REBIND", icon = "keyboard")]
        [Help("INPUT_ACTION_REBIND_HELP")]
        public void Rebind() => _RebindAsync();

        private async void _RebindAsync()
        {
            var action = _Action();
            if (action == null) return;

            await RuntimeKeyBindingSystem.StartBindingAsync(
                new RuntimeKeyBindingData(), _map, _name, 0);
        }

        private InputAction _Action()
        {
            if (_map == null || string.IsNullOrEmpty(_name)) return null;
            return _map.FindAction(_name);
        }
    }

    /// <summary>
    /// 入力デバイスの情報を保持するためのコンポーネント
    /// </summary>
    [LiveClass("InputActions", Category = "Input", Icon = "keyboard", HideInScene = true)]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class AvatarInput : MonoBehaviour, IInputActionProvider,
        ILiveSerializeCallback, ILiveDeserializeCallback
    {
        /// <summary>
        /// デバイスフィルタ
        /// </summary>
        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("deviceName")]
        internal string _deviceName;

        [NonSerialized] private InputUser _inputUser;

        public InputActionMap inputActionMap => _inputActionMap;

        [SerializeField]
        private InputActionMap _inputActionMap;

        // --- Live Properties ---

        [LiveProperty]
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
        // Refreshed from the live InputActionMap in OnBeforeLiveSerialize and
        // applied back to the InputActionMap in OnAfterLiveDeserialize. The
        // property getter intentionally returns a fresh snapshot rather than
        // this field so live API queries reflect the current map.
        [SerializeField, HideInInspector, LiveField, Hide]
        [FormerlyNamedAs("settings")]
        private AvatarInputSettings _settings;

        /// <summary>
        /// InputActionMapの設定をシリアライズ可能な形で公開する。
        /// LiveSceneToJson/LiveSceneFromJson経由で保存/復元される。
        /// </summary>
        [LiveProperty, Hide]
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

        public void OnBeforeLiveSerialize()
        {
            // shadow field を最新 InputActionMap state から refresh してから JSON 化する
            _settings = AvatarInputSettingsUtils.CreateSettingsFromAvatarInput(this);
        }

        public void OnAfterLiveDeserialize()
        {
            // SetValueRaw は Property setter をバイパスするので、shadow field に書かれた
            // _settings を InputActionMap に反映するためここで apply する
            if (_settings != null)
            {
                AvatarInputSettingsUtils.ApplySettingsToAvatarInput(this, _settings);
            }
        }

        [LiveProperty]
        public IEnumerable<string> actionNames
        {
            get
            {
                if (_inputActionMap == null) return Enumerable.Empty<string>();
                return _inputActionMap.actions.Select(a => a.name);
            }
        }

        /// <summary>
        /// Every action of the map as a bindable slot, rebuilt on each read so it follows the live
        /// map. Read-only and not persisted (the bindings themselves ride in <see cref="settings"/>);
        /// this exists so a client can list the actions and rebind one through the ordinary object
        /// surface, which is why there is no route for it.
        /// </summary>
        [LiveProperty, Collapsed]
        public InputActionEntry[] actions
        {
            get
            {
                if (_inputActionMap == null) return Array.Empty<InputActionEntry>();
                return _inputActionMap.actions
                    .Select(a => new InputActionEntry(_inputActionMap, a.name))
                    .ToArray();
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
        [LiveFunction(label = "INPUTACTIONS_RESETBINDINGS")]
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

