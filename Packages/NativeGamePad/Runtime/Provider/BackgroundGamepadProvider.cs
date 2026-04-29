using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Lilium.NativeGamepad
{
#if UNITY_STANDALONE_WIN
    /// <summary>
    /// バックグラウンド対応ゲームパッドプロバイダー
    /// XInputとWindows Gaming Inputデバイスを管理
    /// </summary>
    public class BackgroundGamepadProvider : MonoBehaviour, IInputUpdateCallbackReceiver
    {
        /// <summary>
        /// バックグラウンドでの実行を許可
        /// </summary>
        public bool canRunInBackground => true;

        /// <summary>
        /// デバイススキャン間隔（秒）
        /// </summary>
        [SerializeField] private float _deviceScanInterval = 2.0f;

        /// <summary>
        /// XInputデバイスを優先するかどうか
        /// </summary>
        [SerializeField] private bool _preferXInput = true;

        /// <summary>
        /// 接続されたXInputデバイス
        /// </summary>
        private readonly List<WindowsXInputGamepad> _xinputDevices = new List<WindowsXInputGamepad>();

        /// <summary>
        /// 接続されたWGIデバイス
        /// </summary>
        private readonly List<WindowsGamingInputGamepad> _wgiDevices = new List<WindowsGamingInputGamepad>();

        /// <summary>
        /// 最後にデバイススキャンを実行した時間
        /// </summary>
        private float _lastScanTime = 0f;

        /// <summary>
        /// プロバイダーが初期化されているかどうか
        /// </summary>
        private bool _initialized = false;

        /// <summary>
        /// XInputが有効かどうか
        /// </summary>
        private bool _xinputEnabled = true;

        /// <summary>
        /// WGIが有効かどうか
        /// </summary>
        private bool _wgiEnabled = true;

        void Start()
        {
            InitializeProvider();
        }

        /// <summary>
        /// プロバイダーの初期化
        /// </summary>
        private void InitializeProvider()
        {
            try
            {
                // アプリケーション全体でバックグラウンド実行を有効化
                Application.runInBackground = true;

                // XInputを有効化
                WindowsXInputAPI.XInputEnable(true);

                // 初回デバイススキャンを実行
                ScanForDevices();

                _initialized = true;

                //Debug.Log($"[NativeGamepad] BackgroundGamepadProvider initialized (XInput: {_xinputEnabled}, WGI: {_wgiEnabled})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Failed to initialize BackgroundGamepadProvider: {ex.Message}");
                _initialized = false;
            }
        }

        /// <summary>
        /// Input System更新コールバック
        /// </summary>
        public void OnUpdate()
        {
            if (!_initialized) return;

            // 定期的なデバイススキャン
            if (Time.unscaledTime - _lastScanTime > _deviceScanInterval)
            {
                ScanForDevices();
                _lastScanTime = Time.unscaledTime;
            }

            // 切断されたデバイスをクリーンアップ
            CleanupDisconnectedDevices();
        }

        /// <summary>
        /// デバイスをスキャンして新しいデバイスを検出
        /// </summary>
        private void ScanForDevices()
        {
            try
            {
                if (_xinputEnabled)
                {
                    ScanXInputDevices();
                }

                if (_wgiEnabled && !_preferXInput)
                {
                    ScanWGIDevices();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Error during device scan: {ex.Message}");
            }
        }

        /// <summary>
        /// XInputデバイスをスキャン
        /// </summary>
        private void ScanXInputDevices()
        {
            for (uint i = 0; i < 4; i++) // XInputは最大4つのコントローラーをサポート
            {
                try
                {
                    bool isConnected = WindowsXInputAPI.IsControllerConnected(i);
                    bool alreadyAdded = _xinputDevices.FindIndex(d => d != null && d.ControllerIndex == i) >= 0;

                    if (isConnected && !alreadyAdded)
                    {
                        // 新しいXInputデバイスを追加
                        AddXInputDevice(i);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NativeGamepad] Error scanning XInput controller {i}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// WGIデバイスをスキャン
        /// </summary>
        private void ScanWGIDevices()
        {
            try
            {
                int gamepadCount = WindowsGamingInputAPI.WGIHelper.GetGamepadCount();

                for (int i = 0; i < gamepadCount; i++)
                {
                    bool alreadyAdded = _wgiDevices.FindIndex(d => d != null && d.GamepadIndex == i) >= 0;

                    if (!alreadyAdded)
                    {
                        // 新しいWGIデバイスを追加
                        AddWGIDevice(i);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeGamepad] Error scanning WGI devices: {ex.Message}");
            }
        }

        /// <summary>
        /// XInputデバイスを追加
        /// </summary>
        private void AddXInputDevice(uint controllerIndex)
        {
            try
            {
                var device = InputSystem.AddDevice<WindowsXInputGamepad>();
                device.SetControllerIndex(controllerIndex);

                _xinputDevices.Add(device);

            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Failed to add XInput device {controllerIndex}: {ex.Message}");
            }
        }

        /// <summary>
        /// WGIデバイスを追加
        /// </summary>
        private void AddWGIDevice(int gamepadIndex)
        {
            try
            {
                var device = InputSystem.AddDevice<WindowsGamingInputGamepad>();
                device.SetGamepadIndex(gamepadIndex);

                _wgiDevices.Add(device);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Failed to add WGI device {gamepadIndex}: {ex.Message}");
            }
        }

        /// <summary>
        /// 切断されたデバイスをクリーンアップ
        /// </summary>
        private void CleanupDisconnectedDevices()
        {
            // XInputデバイスのクリーンアップ
            for (int i = _xinputDevices.Count - 1; i >= 0; i--)
            {
                var device = _xinputDevices[i];
                if (device == null || !device.IsConnected())
                {
                    if (device != null)
                    {
                        Debug.Log($"[NativeGamepad] Removing disconnected XInput device: {device.ControllerIndex}");
                        InputSystem.RemoveDevice(device);
                    }
                    _xinputDevices.RemoveAt(i);
                }
            }

            // WGIデバイスのクリーンアップ
            for (int i = _wgiDevices.Count - 1; i >= 0; i--)
            {
                var device = _wgiDevices[i];
                if (device == null || !device.IsAvailable())
                {
                    if (device != null)
                    {
                        Debug.Log($"[NativeGamepad] Removing unavailable WGI device: {device.GamepadIndex}");
                        InputSystem.RemoveDevice(device);
                    }
                    _wgiDevices.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 接続されているデバイス数を取得
        /// </summary>
        public int GetConnectedDeviceCount()
        {
            return _xinputDevices.Count + _wgiDevices.Count;
        }

        /// <summary>
        /// XInputデバイスの一覧を取得
        /// </summary>
        public IReadOnlyList<WindowsXInputGamepad> GetXInputDevices()
        {
            return _xinputDevices.AsReadOnly();
        }

        /// <summary>
        /// WGIデバイスの一覧を取得
        /// </summary>
        public IReadOnlyList<WindowsGamingInputGamepad> GetWGIDevices()
        {
            return _wgiDevices.AsReadOnly();
        }

        /// <summary>
        /// すべてのデバイスの振動を停止
        /// </summary>
        public void StopAllVibration()
        {
            foreach (var device in _xinputDevices)
            {
                device?.SetVibration(0, 0);
            }

            foreach (var device in _wgiDevices)
            {
                device?.SetVibration(0, 0);
            }
        }


        void OnDestroy()
        {
            // 振動を停止
            StopAllVibration();

            // デバイスを削除
            foreach (var device in _xinputDevices)
            {
                if (device != null)
                {
                    InputSystem.RemoveDevice(device);
                }
            }

            foreach (var device in _wgiDevices)
            {
                if (device != null)
                {
                    InputSystem.RemoveDevice(device);
                }
            }

            // XInputを無効化
            try
            {
                WindowsXInputAPI.XInputEnable(false);
            }
            catch { }

            // WGIクリーンアップ
            WindowsGamingInputGamepad.CleanupWGI();

            _xinputDevices.Clear();
            _wgiDevices.Clear();
        }

#if false
        /// <summary>
        /// デバッグ情報の表示
        /// </summary>
        void OnGUI()
        {
            if (!_initialized) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 400, 200));
            GUILayout.Label($"Native Gamepad Provider (Background: {canRunInBackground})");
            GUILayout.Label($"XInput Devices: {_xinputDevices.Count}");
            GUILayout.Label($"WGI Devices: {_wgiDevices.Count}");
            GUILayout.Label($"App Focus: {Application.isFocused}");
            GUILayout.Label($"Run in Background: {Application.runInBackground}");
            GUILayout.EndArea();
        }
#endif

    }
#endif
}