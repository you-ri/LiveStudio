using System;
using System.Runtime.InteropServices;

namespace Lilium.NativeGamepad
{
#if UNITY_STANDALONE_WIN
    /// <summary>
    /// Windows Gaming Input (WGI) API P/Invoke 定義
    /// </summary>
    public static class WindowsGamingInputAPI
    {
        // Windows Runtime APIを使用するためのCOM関連定義
        private const string KERNEL32_DLL = "kernel32.dll";
        private const string COMBASE_DLL = "combase.dll";
        
        // Windows Gaming Input GUID
        public static readonly Guid IID_IGamepadStatics = new Guid("8BBCE529-D49C-39E9-9560-E47DDE96B7C8");
        public static readonly Guid IID_IGamepad = new Guid("BC7BB43C-0A69-3903-9E9D-A50F86A45DE5");
        
        /// <summary>
        /// Windows Runtime の初期化
        /// </summary>
        [DllImport(COMBASE_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int RoInitialize(uint initType);
        
        /// <summary>
        /// Windows Runtime のクリーンアップ
        /// </summary>
        [DllImport(COMBASE_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern void RoUninitialize();
        
        /// <summary>
        /// WinRT アクティベーションファクトリの取得
        /// </summary>
        [DllImport(COMBASE_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int RoGetActivationFactory(
            IntPtr activatableClassId,
            [In] ref Guid iid,
            out IntPtr factory);
        
        /// <summary>
        /// HSTRING の作成
        /// </summary>
        [DllImport(COMBASE_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            uint length,
            out IntPtr hstring);
        
        /// <summary>
        /// HSTRING の削除
        /// </summary>
        [DllImport(COMBASE_DLL, CallingConvention = CallingConvention.StdCall)]
        public static extern int WindowsDeleteString(IntPtr hstring);
        
        /// <summary>
        /// GamepadReading 構造体 - ゲームパッドの読み取り値
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GamepadReading
        {
            public ulong Timestamp;
            public uint Buttons;
            public double LeftTrigger;
            public double RightTrigger;
            public double LeftThumbstickX;
            public double LeftThumbstickY;
            public double RightThumbstickX;
            public double RightThumbstickY;
        }
        
        /// <summary>
        /// GamepadVibration 構造体 - ゲームパッド振動設定
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GamepadVibration
        {
            public double LeftMotor;
            public double RightMotor;
            public double LeftTrigger;
            public double RightTrigger;
        }
        
        /// <summary>
        /// GamepadButtons 列挙型のビット定義
        /// </summary>
        [Flags]
        public enum GamepadButtons : uint
        {
            None = 0,
            Menu = 1,
            View = 2,
            A = 4,
            B = 8,
            X = 16,
            Y = 32,
            DPadUp = 64,
            DPadDown = 128,
            DPadLeft = 256,
            DPadRight = 512,
            LeftShoulder = 1024,
            RightShoulder = 2048,
            LeftThumbstick = 4096,
            RightThumbstick = 8192,
            Paddle1 = 16384,
            Paddle2 = 32768,
            Paddle3 = 65536,
            Paddle4 = 131072
        }
        
        /// <summary>
        /// IGamepadStatics インターフェース
        /// </summary>
        [ComImport]
        [Guid("8BBCE529-D49C-39E9-9560-E47DDE96B7C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IGamepadStatics
        {
            [PreserveSig]
            int add_GamepadAdded(IntPtr handler, out long token);
            
            [PreserveSig]
            int remove_GamepadAdded(long token);
            
            [PreserveSig]
            int add_GamepadRemoved(IntPtr handler, out long token);
            
            [PreserveSig]
            int remove_GamepadRemoved(long token);
            
            [PreserveSig]
            int get_Gamepads(out IntPtr value);
        }
        
        /// <summary>
        /// IGamepad インターフェース
        /// </summary>
        [ComImport]
        [Guid("BC7BB43C-0A69-3903-9E9D-A50F86A45DE5")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IGamepad
        {
            [PreserveSig]
            int get_Vibration(out GamepadVibration value);
            
            [PreserveSig]
            int put_Vibration(GamepadVibration value);
            
            [PreserveSig]
            int GetCurrentReading(out GamepadReading value);
        }
        
        /// <summary>
        /// シンプルなWGI初期化クラス
        /// </summary>
        public static class WGIHelper
        {
            private static bool _initialized = false;
            private static IntPtr _gamepadStatics = IntPtr.Zero;
            
            /// <summary>
            /// WGI を初期化
            /// </summary>
            public static bool Initialize()
            {
                if (_initialized) return true;
                
                try
                {
                    // Windows Runtime を初期化
                    int hr = RoInitialize(1); // RO_INIT_MULTITHREADED
                    if (hr < 0) return false;
                    
                    // GamepadStatics のアクティベーションファクトリを取得
                    IntPtr className = IntPtr.Zero;
                    hr = WindowsCreateString("Windows.Gaming.Input.Gamepad", 31, out className);
                    if (hr < 0) return false;
                    
                    try
                    {
                        var iid = IID_IGamepadStatics;
                        hr = RoGetActivationFactory(className, ref iid, out _gamepadStatics);
                        if (hr < 0) return false;
                        
                        _initialized = true;
                        return true;
                    }
                    finally
                    {
                        WindowsDeleteString(className);
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
            
            /// <summary>
            /// WGI をクリーンアップ
            /// </summary>
            public static void Cleanup()
            {
                if (!_initialized) return;
                
                if (_gamepadStatics != IntPtr.Zero)
                {
                    Marshal.Release(_gamepadStatics);
                    _gamepadStatics = IntPtr.Zero;
                }
                
                RoUninitialize();
                _initialized = false;
            }
            
            /// <summary>
            /// 接続されているゲームパッドの数を取得
            /// </summary>
            public static int GetGamepadCount()
            {
                if (!_initialized || _gamepadStatics == IntPtr.Zero) return 0;
                
                try
                {
                    var gamepadStatics = Marshal.GetObjectForIUnknown(_gamepadStatics) as IGamepadStatics;
                    if (gamepadStatics == null) return 0;
                    
                    int hr = gamepadStatics.get_Gamepads(out IntPtr gamepadsPtr);
                    if (hr < 0) return 0;
                    
                    // IVectorView の Count を取得するための簡易実装
                    // 実際の実装では IVectorView インターフェースを使用する必要がある
                    return 1; // 簡易実装: 最低1つとして返す
                }
                catch (Exception)
                {
                    return 0;
                }
            }
            
            /// <summary>
            /// ゲームパッドの状態を取得 (簡易実装)
            /// </summary>
            public static bool GetGamepadReading(int index, out GamepadReading reading)
            {
                reading = default;
                
                if (!_initialized || _gamepadStatics == IntPtr.Zero) return false;
                
                try
                {
                    // 実際の実装では IVectorView からゲームパッドを取得し、
                    // IGamepad.GetCurrentReading を呼び出す
                    // ここでは簡易実装のため false を返す
                    return false;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
#endif
}