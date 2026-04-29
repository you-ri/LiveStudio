using System;
using System.Runtime.InteropServices;

namespace Lilium.NativeGamepad
{
#if UNITY_STANDALONE_WIN
    /// <summary>
    /// Windows XInput API P/Invoke 定義
    /// </summary>
    public static class WindowsXInputAPI
    {
        private const string DLL_NAME = "xinput1_4.dll";
        
        // XInput 関数の戻り値
        public const uint ERROR_SUCCESS = 0;
        public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;
        
        // XInput ボタン定数
        public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        public const ushort XINPUT_GAMEPAD_START = 0x0010;
        public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        public const ushort XINPUT_GAMEPAD_A = 0x1000;
        public const ushort XINPUT_GAMEPAD_B = 0x2000;
        public const ushort XINPUT_GAMEPAD_X = 0x4000;
        public const ushort XINPUT_GAMEPAD_Y = 0x8000;
        
        // デッドゾーン定数
        public const short XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE = 7849;
        public const short XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE = 8689;
        public const byte XINPUT_GAMEPAD_TRIGGER_THRESHOLD = 30;
        
        /// <summary>
        /// XInputGetState - コントローラーの状態を取得
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        public static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);
        
        /// <summary>
        /// XInputSetState - コントローラーの振動を設定
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        public static extern uint XInputSetState(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);
        
        /// <summary>
        /// XInputGetCapabilities - コントローラーの機能を取得
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        public static extern uint XInputGetCapabilities(uint dwUserIndex, uint dwFlags, out XINPUT_CAPABILITIES pCapabilities);
        
        /// <summary>
        /// XInputEnable - XInputの有効/無効を設定
        /// </summary>
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        public static extern void XInputEnable(bool enable);
        
        /// <summary>
        /// XINPUT_STATE構造体 - コントローラーの状態
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }
        
        /// <summary>
        /// XINPUT_GAMEPAD構造体 - ゲームパッドの入力状態
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }
        
        /// <summary>
        /// XINPUT_VIBRATION構造体 - 振動設定
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }
        
        /// <summary>
        /// XINPUT_CAPABILITIES構造体 - コントローラー機能
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_CAPABILITIES
        {
            public byte Type;
            public byte SubType;
            public ushort Flags;
            public XINPUT_GAMEPAD Gamepad;
            public XINPUT_VIBRATION Vibration;
        }
        
        /// <summary>
        /// XInputの状態をチェックしてコントローラーが接続されているかを確認
        /// </summary>
        /// <param name="userIndex">ユーザーインデックス (0-3)</param>
        /// <returns>接続されている場合true</returns>
        public static bool IsControllerConnected(uint userIndex)
        {
            return XInputGetState(userIndex, out _) == ERROR_SUCCESS;
        }
        
        /// <summary>
        /// デッドゾーンを適用してスティック値を正規化
        /// </summary>
        /// <param name="thumbX">X軸の値</param>
        /// <param name="thumbY">Y軸の値</param>
        /// <param name="deadzone">デッドゾーン値</param>
        /// <returns>正規化されたベクトル</returns>
        public static UnityEngine.Vector2 ApplyDeadzone(short thumbX, short thumbY, short deadzone)
        {
            float x = thumbX;
            float y = thumbY;
            
            // デッドゾーンの範囲内かチェック
            float magnitude = UnityEngine.Mathf.Sqrt(x * x + y * y);
            
            if (magnitude < deadzone)
            {
                return UnityEngine.Vector2.zero;
            }
            
            // 正規化 (-1.0 to 1.0)
            x = x / 32767.0f;
            y = y / 32767.0f;
            
            // デッドゾーンを考慮した再正規化
            if (magnitude > 0)
            {
                float normalizedMagnitude = (magnitude - deadzone) / (32767.0f - deadzone);
                normalizedMagnitude = UnityEngine.Mathf.Clamp01(normalizedMagnitude);
                
                x = x * normalizedMagnitude / (magnitude / 32767.0f);
                y = y * normalizedMagnitude / (magnitude / 32767.0f);
            }
            
            return new UnityEngine.Vector2(x, y);
        }
        
        /// <summary>
        /// トリガー値を正規化 (0.0 to 1.0)
        /// </summary>
        /// <param name="triggerValue">トリガー値 (0-255)</param>
        /// <returns>正規化された値</returns>
        public static float NormalizeTrigger(byte triggerValue)
        {
            if (triggerValue < XINPUT_GAMEPAD_TRIGGER_THRESHOLD)
                return 0.0f;
            
            return triggerValue / 255.0f;
        }
    }
#endif
}