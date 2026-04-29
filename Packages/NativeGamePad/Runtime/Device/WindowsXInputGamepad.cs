using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace Lilium.NativeGamepad
{
#if UNITY_STANDALONE_WIN
    /// <summary>
    /// Windows XInput専用ゲームパッドデバイス（バックグラウンド対応）
    /// </summary>
    [InputControlLayout(
        stateType = typeof(GamepadState),
        displayName = "Native XInput",
        description = "XInput gamepad with background input support")]
    public class WindowsXInputGamepad : Gamepad, IInputUpdateCallbackReceiver
    {
        /// <summary>
        /// バックグラウンドでの実行を許可（重要！）
        /// </summary>
        public new bool canRunInBackground => true;
        
        /// <summary>
        /// XInputコントローラーインデックス (0-3)
        /// </summary>
        public uint ControllerIndex { get; private set; }
        
        /// <summary>
        /// 最後に取得したパケット番号（重複チェック用）
        /// </summary>
        private uint _lastPacketNumber;
        
        /// <summary>
        /// デバイスの初期化完了フラグ
        /// </summary>
        private bool _isInitialized;
        
        /// <summary>
        /// 振動機能対応フラグ
        /// </summary>
        public bool SupportsVibration { get; private set; }
        
        protected override void FinishSetup()
        {
            base.FinishSetup();
            _isInitialized = true;
        }
        
        /// <summary>
        /// コントローラーインデックスを設定
        /// </summary>
        internal void SetControllerIndex(uint index)
        {
            ControllerIndex = index;
            
            // 振動機能の対応を確認
            CheckVibrationSupport();
        }
        
        /// <summary>
        /// 振動機能の対応をチェック
        /// </summary>
        private void CheckVibrationSupport()
        {
            try
            {
                uint result = WindowsXInputAPI.XInputGetCapabilities(ControllerIndex, 0, out var capabilities);
                SupportsVibration = (result == WindowsXInputAPI.ERROR_SUCCESS);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeGamepad] Failed to check vibration support: {ex.Message}");
                SupportsVibration = false;
            }
        }
        
        /// <summary>
        /// Input System更新コールバック（バックグラウンドでも実行される）
        /// </summary>
        public void OnUpdate()
        {
            if (!_isInitialized) return;
            
            try
            {
                // P/Invoke経由でXInputからデータを取得
                uint result = WindowsXInputAPI.XInputGetState(ControllerIndex, out var xinputState);
                
                if (result == WindowsXInputAPI.ERROR_SUCCESS)
                {
                    // パケット番号が変わった場合のみ更新
                    if (xinputState.dwPacketNumber != _lastPacketNumber)
                    {
                        _lastPacketNumber = xinputState.dwPacketNumber;
                        
                        // XInputStateをUnityのGamepadStateに変換
                        var gamepadState = ConvertToGamepadState(xinputState.Gamepad);
                        
                        // Unity Input Systemに状態を送信
                        InputSystem.QueueStateEvent(this, gamepadState);
                    }
                }
                else if (result == WindowsXInputAPI.ERROR_DEVICE_NOT_CONNECTED)
                {
                    // デバイスが切断された場合
                    OnDeviceDisconnected();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Error in OnUpdate: {ex.Message}");
            }
        }
        
        /// <summary>
        /// XInputStateをUnityのGamepadStateに変換
        /// </summary>
        private GamepadState ConvertToGamepadState(WindowsXInputAPI.XINPUT_GAMEPAD xinputGamepad)
        {
            var state = new GamepadState();
            
            // ボタンの変換
            state.WithButton(GamepadButton.South, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_A) != 0);
            state.WithButton(GamepadButton.East, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_B) != 0);
            state.WithButton(GamepadButton.West, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_X) != 0);
            state.WithButton(GamepadButton.North, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_Y) != 0);
            
            state.WithButton(GamepadButton.LeftShoulder, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0);
            state.WithButton(GamepadButton.RightShoulder, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0);
            
            state.WithButton(GamepadButton.Start, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_START) != 0);
            state.WithButton(GamepadButton.Select, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_BACK) != 0);
            
            state.WithButton(GamepadButton.LeftStick, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_LEFT_THUMB) != 0);
            state.WithButton(GamepadButton.RightStick, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_RIGHT_THUMB) != 0);
            
            // D-Padの変換
            state.WithButton(GamepadButton.DpadUp, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_DPAD_UP) != 0);
            state.WithButton(GamepadButton.DpadDown, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_DPAD_DOWN) != 0);
            state.WithButton(GamepadButton.DpadLeft, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_DPAD_LEFT) != 0);
            state.WithButton(GamepadButton.DpadRight, (xinputGamepad.wButtons & WindowsXInputAPI.XINPUT_GAMEPAD_DPAD_RIGHT) != 0);
            
            // スティックの変換（デッドゾーン適用）
            var leftStick = WindowsXInputAPI.ApplyDeadzone(
                xinputGamepad.sThumbLX, xinputGamepad.sThumbLY,
                WindowsXInputAPI.XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE);
            var rightStick = WindowsXInputAPI.ApplyDeadzone(
                xinputGamepad.sThumbRX, xinputGamepad.sThumbRY,
                WindowsXInputAPI.XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE);
            
            state.leftStick = leftStick;
            state.rightStick = rightStick;
            
            // トリガーの変換
            state.leftTrigger = WindowsXInputAPI.NormalizeTrigger(xinputGamepad.bLeftTrigger);
            state.rightTrigger = WindowsXInputAPI.NormalizeTrigger(xinputGamepad.bRightTrigger);
            
            return state;
        }
        
        /// <summary>
        /// 振動を設定
        /// </summary>
        /// <param name="leftMotor">左モーター (0.0-1.0)</param>
        /// <param name="rightMotor">右モーター (0.0-1.0)</param>
        public void SetVibration(float leftMotor, float rightMotor)
        {
            if (!SupportsVibration) return;
            
            try
            {
                var vibration = new WindowsXInputAPI.XINPUT_VIBRATION
                {
                    wLeftMotorSpeed = (ushort)(Mathf.Clamp01(leftMotor) * 65535),
                    wRightMotorSpeed = (ushort)(Mathf.Clamp01(rightMotor) * 65535)
                };
                
                WindowsXInputAPI.XInputSetState(ControllerIndex, ref vibration);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Failed to set vibration: {ex.Message}");
            }
        }
        
        /// <summary>
        /// デバイス切断時の処理
        /// </summary>
        private void OnDeviceDisconnected()
        {
            // 振動を停止
            if (SupportsVibration)
            {
                SetVibration(0, 0);
            }
        }
        
        /// <summary>
        /// コントローラーが接続されているかチェック
        /// </summary>
        public bool IsConnected()
        {
            try
            {
                return WindowsXInputAPI.IsControllerConnected(ControllerIndex);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// デバイス情報の文字列表現
        /// </summary>
        public override string ToString()
        {
            return $"WindowsXInputGamepad(Index:{ControllerIndex}, Connected:{IsConnected()}, Vibration:{SupportsVibration})";
        }
    }
#endif
}