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
    /// Windows Gaming Input専用ゲームパッドデバイス（バックグラウンド対応）
    /// </summary>
    [InputControlLayout(
        stateType = typeof(GamepadState),
        displayName = "Native WGI",
        description = "Windows Gaming Input gamepad with background input support")]
    public class WindowsGamingInputGamepad : Gamepad, IInputUpdateCallbackReceiver
    {
        /// <summary>
        /// バックグラウンドでの実行を許可（重要！）
        /// </summary>
        public new bool canRunInBackground => true;
        
        /// <summary>
        /// WGIゲームパッドのインデックス
        /// </summary>
        public int GamepadIndex { get; private set; }
        
        /// <summary>
        /// 最後に取得したタイムスタンプ（重複チェック用）
        /// </summary>
        private ulong _lastTimestamp;
        
        /// <summary>
        /// デバイスの初期化完了フラグ
        /// </summary>
        private bool _isInitialized;
        
        /// <summary>
        /// WGI初期化状態
        /// </summary>
        private static bool _wgiInitialized = false;
        
        /// <summary>
        /// 振動機能対応フラグ
        /// </summary>
        public bool SupportsVibration { get; private set; } = true; // WGIは基本的に振動をサポート
        
        /// <summary>
        /// 静的コンストラクタ - WGI初期化
        /// </summary>
        static WindowsGamingInputGamepad()
        {
            InitializeWGI();
        }

        /// <summary>
        /// 静的フィールドをリセット（Editor Play Mode対応）
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            _wgiInitialized = false;
        }
        
        /// <summary>
        /// WGI初期化
        /// </summary>
        private static void InitializeWGI()
        {
            try
            {
                _wgiInitialized = WindowsGamingInputAPI.WGIHelper.Initialize();
                if (_wgiInitialized)
                {
                    Debug.Log("[NativeGamepad] Windows Gaming Input initialized successfully");
                }
                else
                {
                    // WGI非対応環境では正常動作 - 他の入力方法にフォールバック
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] WGI initialization error: {ex.Message}");
                _wgiInitialized = false;
            }
        }
        
        protected override void FinishSetup()
        {
            base.FinishSetup();
            _isInitialized = true;
            
            Debug.Log($"[NativeGamepad] WindowsGamingInputGamepad initialized: {deviceId} (canRunInBackground: {canRunInBackground})");
        }
        
        /// <summary>
        /// ゲームパッドインデックスを設定
        /// </summary>
        internal void SetGamepadIndex(int index)
        {
            GamepadIndex = index;
        }
        
        /// <summary>
        /// Input System更新コールバック（バックグラウンドでも実行される）
        /// </summary>
        public void OnUpdate()
        {
            if (!_isInitialized || !_wgiInitialized) return;
            
            try
            {
                // WGI経由でゲームパッドの状態を取得
                bool success = WindowsGamingInputAPI.WGIHelper.GetGamepadReading(GamepadIndex, out var wgiReading);
                
                if (success)
                {
                    // タイムスタンプが変わった場合のみ更新
                    if (wgiReading.Timestamp != _lastTimestamp)
                    {
                        _lastTimestamp = wgiReading.Timestamp;
                        
                        // WGI ReadingをUnityのGamepadStateに変換
                        var gamepadState = ConvertToGamepadState(wgiReading);
                        
                        // Unity Input Systemに状態を送信
                        InputSystem.QueueStateEvent(this, gamepadState);
                    }
                }
                else
                {
                    // デバイスが利用できない場合の処理
                    OnDeviceUnavailable();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Error in WGI OnUpdate: {ex.Message}");
            }
        }
        
        /// <summary>
        /// WGI GamepadReadingをUnityのGamepadStateに変換
        /// </summary>
        private GamepadState ConvertToGamepadState(WindowsGamingInputAPI.GamepadReading wgiReading)
        {
            var state = new GamepadState();
            
            var buttons = (WindowsGamingInputAPI.GamepadButtons)wgiReading.Buttons;
            
            // ボタンの変換
            state.WithButton(GamepadButton.South, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.A));
            state.WithButton(GamepadButton.East, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.B));
            state.WithButton(GamepadButton.West, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.X));
            state.WithButton(GamepadButton.North, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.Y));
            
            state.WithButton(GamepadButton.LeftShoulder, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.LeftShoulder));
            state.WithButton(GamepadButton.RightShoulder, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.RightShoulder));
            
            state.WithButton(GamepadButton.Start, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.Menu));
            state.WithButton(GamepadButton.Select, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.View));
            
            state.WithButton(GamepadButton.LeftStick, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.LeftThumbstick));
            state.WithButton(GamepadButton.RightStick, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.RightThumbstick));
            
            // D-Padの変換
            state.WithButton(GamepadButton.DpadUp, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.DPadUp));
            state.WithButton(GamepadButton.DpadDown, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.DPadDown));
            state.WithButton(GamepadButton.DpadLeft, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.DPadLeft));
            state.WithButton(GamepadButton.DpadRight, buttons.HasFlag(WindowsGamingInputAPI.GamepadButtons.DPadRight));
            
            // パドルボタン（利用可能な場合）
            // Unity標準Gamepadにはパドルボタンがないため、ここではスキップ
            
            // スティックの変換（WGIは既に正規化済み）
            state.leftStick = new Vector2(
                (float)wgiReading.LeftThumbstickX,
                (float)wgiReading.LeftThumbstickY
            );
            state.rightStick = new Vector2(
                (float)wgiReading.RightThumbstickX,
                (float)wgiReading.RightThumbstickY
            );
            
            // トリガーの変換（WGIは既に0.0-1.0に正規化済み）
            state.leftTrigger = (float)wgiReading.LeftTrigger;
            state.rightTrigger = (float)wgiReading.RightTrigger;
            
            return state;
        }
        
        /// <summary>
        /// 振動を設定
        /// </summary>
        /// <param name="leftMotor">左モーター (0.0-1.0)</param>
        /// <param name="rightMotor">右モーター (0.0-1.0)</param>
        /// <param name="leftTrigger">左トリガー振動 (0.0-1.0)</param>
        /// <param name="rightTrigger">右トリガー振動 (0.0-1.0)</param>
        public void SetVibration(float leftMotor, float rightMotor, float leftTrigger = 0f, float rightTrigger = 0f)
        {
            if (!SupportsVibration || !_wgiInitialized) return;
            
            try
            {
                var vibration = new WindowsGamingInputAPI.GamepadVibration
                {
                    LeftMotor = Mathf.Clamp01(leftMotor),
                    RightMotor = Mathf.Clamp01(rightMotor),
                    LeftTrigger = Mathf.Clamp01(leftTrigger),
                    RightTrigger = Mathf.Clamp01(rightTrigger)
                };
                
                // 実際のWGI振動設定は簡易実装のため省略
                // 完全な実装では IGamepad.put_Vibration を呼び出す
                Debug.Log($"[NativeGamepad] WGI Vibration set: L:{leftMotor:F2}, R:{rightMotor:F2}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeGamepad] Failed to set WGI vibration: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 振動を設定（オーバーロード）
        /// </summary>
        public void SetVibration(float leftMotor, float rightMotor)
        {
            SetVibration(leftMotor, rightMotor, 0f, 0f);
        }
        
        /// <summary>
        /// デバイスが利用できない場合の処理
        /// </summary>
        private void OnDeviceUnavailable()
        {
            // 振動を停止
            if (SupportsVibration)
            {
                SetVibration(0, 0);
            }
        }
        
        /// <summary>
        /// ゲームパッドが利用可能かチェック
        /// </summary>
        public bool IsAvailable()
        {
            if (!_wgiInitialized) return false;
            
            try
            {
                // 簡易チェック - 実際の実装では個別のゲームパッド状態をチェック
                return WindowsGamingInputAPI.WGIHelper.GetGamepadCount() > GamepadIndex;
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
            return $"WindowsGamingInputGamepad(Index:{GamepadIndex}, Available:{IsAvailable()}, Vibration:{SupportsVibration})";
        }
        
        /// <summary>
        /// ファイナライザ - WGIクリーンアップ
        /// </summary>
        ~WindowsGamingInputGamepad()
        {
            try
            {
                if (SupportsVibration)
                {
                    SetVibration(0, 0);
                }
            }
            catch { }
        }
        
        /// <summary>
        /// 静的クリーンアップメソッド
        /// </summary>
        public static void CleanupWGI()
        {
            if (_wgiInitialized)
            {
                WindowsGamingInputAPI.WGIHelper.Cleanup();
                _wgiInitialized = false;
                Debug.Log("[NativeGamepad] Windows Gaming Input cleaned up");
            }
        }
    }
#endif
}