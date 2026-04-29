using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

namespace Lilium.NativeGamepad
{
#if UNITY_STANDALONE_WIN
    /// <summary>
    /// Native Gamepadパッケージの初期化処理
    /// Unity起動時にデバイスレイアウトを自動登録
    /// </summary>
    public static class NativeGamepadInitializer
    {
        /// <summary>
        /// 初期化完了フラグ
        /// </summary>
        private static bool _initialized = false;
        
        /// <summary>
        /// Unity起動時の自動初期化
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (_initialized) return;
            
            try
            {
                // アプリケーション全体でバックグラウンド実行を有効化
                Application.runInBackground = true;
                
                // デバイスレイアウトを登録
                RegisterDeviceLayouts();
                
                // デバイス変更イベントのハンドリングを設定
                SetupDeviceChangeHandling();
                
                _initialized = true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Failed to initialize Native Gamepad system: {ex.Message}");
                _initialized = false;
            }
        }
        
        /// <summary>
        /// デバイスレイアウトの登録
        /// </summary>
        private static void RegisterDeviceLayouts()
        {
            // WindowsXInputGamepadの登録
            InputSystem.RegisterLayout<WindowsXInputGamepad>(
                name: "WindowsXInputGamepad",
                matches: new InputDeviceMatcher()
                    .WithInterface("NativeXInput")
                    .WithManufacturer("Microsoft")
                    .WithProduct("XInput")
            );
            
            // WindowsGamingInputGamepadの登録
            InputSystem.RegisterLayout<WindowsGamingInputGamepad>(
                name: "WindowsGamingInputGamepad", 
                matches: new InputDeviceMatcher()
                    .WithInterface("NativeWGI")
                    .WithManufacturer("Microsoft")
                    .WithProduct("WGI")
            );
        }
        
        /// <summary>
        /// デバイス変更イベントのハンドリング設定
        /// </summary>
        private static void SetupDeviceChangeHandling()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
        }
        
        /// <summary>
        /// デバイス変更イベントのハンドラ
        /// </summary>
        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            try
            {
                switch (change)
                {
                    case InputDeviceChange.Added:
                        OnDeviceAdded(device);
                        break;
                        
                    case InputDeviceChange.Removed:
                        OnDeviceRemoved(device);
                        break;
                        
                    case InputDeviceChange.Disconnected:
                        OnDeviceDisconnected(device);
                        break;
                        
                    case InputDeviceChange.Reconnected:
                        OnDeviceReconnected(device);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Error handling device change: {ex.Message}");
            }
        }
        
        /// <summary>
        /// デバイス追加時の処理
        /// </summary>
        private static void OnDeviceAdded(InputDevice device)
        {
            if (device is WindowsXInputGamepad xinputGamepad)
            {
                //Debug.Log($"[NativeGamepad] XInput gamepad added: {xinputGamepad} (Background: {xinputGamepad.canRunInBackground})");
            }
            else if (device is WindowsGamingInputGamepad wgiGamepad)
            {
                //Debug.Log($"[NativeGamepad] WGI gamepad added: {wgiGamepad} (Background: {wgiGamepad.canRunInBackground})");
            }
        }
        
        /// <summary>
        /// デバイス削除時の処理
        /// </summary>
        private static void OnDeviceRemoved(InputDevice device)
        {
            if (device is WindowsXInputGamepad xinputGamepad)
            {
                //Debug.Log($"[NativeGamepad] XInput gamepad removed: {xinputGamepad}");
                // 振動を停止
                xinputGamepad.SetVibration(0, 0);
            }
            else if (device is WindowsGamingInputGamepad wgiGamepad)
            {
                //Debug.Log($"[NativeGamepad] WGI gamepad removed: {wgiGamepad}");
                // 振動を停止
                wgiGamepad.SetVibration(0, 0);
            }
        }
        
        /// <summary>
        /// デバイス切断時の処理
        /// </summary>
        private static void OnDeviceDisconnected(InputDevice device)
        {
            if (device is WindowsXInputGamepad xinputGamepad)
            {
                //Debug.Log($"[NativeGamepad] XInput gamepad disconnected: {xinputGamepad}");
            }
            else if (device is WindowsGamingInputGamepad wgiGamepad)
            {
                //Debug.Log($"[NativeGamepad] WGI gamepad disconnected: {wgiGamepad}");
            }
        }
        
        /// <summary>
        /// デバイス再接続時の処理
        /// </summary>
        private static void OnDeviceReconnected(InputDevice device)
        {
            if (device is WindowsXInputGamepad xinputGamepad)
            {
                //Debug.Log($"[NativeGamepad] XInput gamepad reconnected: {xinputGamepad}");
            }
            else if (device is WindowsGamingInputGamepad wgiGamepad)
            {
                //Debug.Log($"[NativeGamepad] WGI gamepad reconnected: {wgiGamepad}");
            }
        }
        
        /// <summary>
        /// システムのクリーンアップ（アプリケーション終了時）
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void SetupCleanup()
        {
            Application.quitting += OnApplicationQuitting;
        }
        
        /// <summary>
        /// アプリケーション終了時のクリーンアップ
        /// </summary>
        private static void OnApplicationQuitting()
        {
            try
            {
                // すべてのネイティブゲームパッドの振動を停止
                foreach (var device in InputSystem.devices)
                {
                    if (device is WindowsXInputGamepad xinputGamepad)
                    {
                        xinputGamepad.SetVibration(0, 0);
                    }
                    else if (device is WindowsGamingInputGamepad wgiGamepad)
                    {
                        wgiGamepad.SetVibration(0, 0);
                    }
                }
                
                // XInputを無効化
                WindowsXInputAPI.XInputEnable(false);
                
                // WGIクリーンアップ
                WindowsGamingInputGamepad.CleanupWGI();
                
                // イベントハンドラを削除
                InputSystem.onDeviceChange -= OnDeviceChange;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Error during cleanup: {ex.Message}");
            }
        }
        
        /// <summary>
        /// システムの初期化状態を取得
        /// </summary>
        public static bool IsInitialized => _initialized;
        
        /// <summary>
        /// 手動でシステムを初期化（必要に応じて）
        /// </summary>
        public static void ForceInitialize()
        {
            _initialized = false;
            Initialize();
        }
        
        /// <summary>
        /// 現在接続されているネイティブゲームパッドの数を取得
        /// </summary>
        public static int GetNativeGamepadCount()
        {
            int count = 0;
            foreach (var device in InputSystem.devices)
            {
                if (device is WindowsXInputGamepad || device is WindowsGamingInputGamepad)
                {
                    count++;
                }
            }
            return count;
        }
        
        /// <summary>
        /// デバッグ情報を出力
        /// </summary>
        public static void PrintDebugInfo()
        {
            Debug.Log($"[NativeGamepad] System initialized: {_initialized}");
            Debug.Log($"[NativeGamepad] Native gamepad count: {GetNativeGamepadCount()}");
            Debug.Log($"[NativeGamepad] Run in background: {Application.runInBackground}");
            
            foreach (var device in InputSystem.devices)
            {
                if (device is WindowsXInputGamepad xinputGamepad)
                {
                    Debug.Log($"[NativeGamepad] XInput: {xinputGamepad}");
                }
                else if (device is WindowsGamingInputGamepad wgiGamepad)
                {
                    Debug.Log($"[NativeGamepad] WGI: {wgiGamepad}");
                }
            }
        }
    }
#endif
}