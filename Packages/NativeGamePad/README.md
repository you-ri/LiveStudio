# Lilium Native Gamepad

WindowsのXInput APIとWindows.Gaming.Input APIを直接P/Invokeで呼び出し、**バックグラウンドでも継続して入力を受信できる**Unity Input SystemのCustom Deviceパッケージです。

## 🎮 主な機能

- **バックグラウンド入力対応** (`canRunInBackground = true`)
  - アプリがフォーカスを失ってもゲームパッド入力を継続受信
  - OBS等の配信ソフトがフォーカスされていても操作可能
  - 画面共有中も入力が途切れない

- **ネイティブAPI統合**
  - XInput 1.4 完全対応（xinput1_4.dll）
  - Windows Gaming Input API対応
  - P/Invoke経由でのダイレクトアクセス

- **Unity Input System準拠**
  - 標準`Gamepad`クラス継承で既存コードと完全互換
  - `InputAction`で標準的に使用可能
  - 既存`InputProvider`との統合

## 📋 動作環境

- **Unity**: 6.0以降
- **OS**: Windows 10/11
- **依存パッケージ**: Unity Input System 1.14.2以降
- **対応コントローラー**: Xbox Controller、XInput対応ゲームパッド

## 🚀 インストール

このパッケージは [LiveStudio](https://github.com/you-ri/LiveStudio) モノレポに含まれます。UPM Git URL の `?path=` クエリでインストールします。

Unity の `Window > Package Manager > + > Install package from git URL...` に以下を貼り付け：

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad
```

または `Packages/manifest.json` に直接記述：

```json
{
  "dependencies": {
    "jp.lilium.nativegamepad": "https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad"
  }
}
```

特定バージョンに pin する場合（推奨）：

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad#v0.19.1
```

beta プレビュー版：

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad#beta
```

> **Versioning note**: LiveStudio の各パッケージは同一バージョンで揃います。`#v0.19.1` で pin するとモノレポ内の他パッケージとも互換セットになります。詳細は [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning) を参照。

Package Manager で「Lilium Native Gamepad」が表示されることを確認してください。

## 💡 基本的な使用方法

### 標準Gamepadとしての使用

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using Lilium.NativeGamepad;

public class GamepadController : MonoBehaviour
{
    void Update()
    {
        // XInputゲームパッドを取得
        var xinputGamepad = InputSystem.GetDevice<WindowsXInputGamepad>();
        if (xinputGamepad != null)
        {
            // バックグラウンド対応確認
            Debug.Log($"Background support: {xinputGamepad.canRunInBackground}"); // true
            
            // 標準的なGamepad操作
            if (xinputGamepad.buttonSouth.wasPressedThisFrame)
            {
                Debug.Log("A Button pressed!");
            }
            
            var leftStick = xinputGamepad.leftStick.ReadValue();
            var rightTrigger = xinputGamepad.rightTrigger.ReadValue();
            
            // 振動設定
            if (xinputGamepad.SupportsVibration)
            {
                xinputGamepad.SetVibration(0.5f, 0.3f); // 左50%, 右30%
            }
        }
    }
}
```

### InputActionでの使用

Input Action Asset設定で、Gamepadカテゴリから「Windows XInput Gamepad (Background)」を選択できます。

```csharp
public class InputActionExample : MonoBehaviour
{
    [SerializeField] private InputActionReference jumpAction;
    [SerializeField] private InputActionReference moveAction;
    
    void OnEnable()
    {
        jumpAction.action.performed += OnJump;
        moveAction.action.performed += OnMove;
    }
    
    void OnDisable()
    {
        jumpAction.action.performed -= OnJump;
        moveAction.action.performed -= OnMove;
    }
    
    private void OnJump(InputAction.CallbackContext context)
    {
        // アプリがバックグラウンドでも動作
        Debug.Log("Jump! (works in background)");
    }
    
    private void OnMove(InputAction.CallbackContext context)
    {
        Vector2 movement = context.ReadValue<Vector2>();
        // 移動処理
    }
}
```

### BackgroundGamepadProviderの使用

自動でデバイス検出・管理を行う場合：

```csharp
public class GameManager : MonoBehaviour
{
    void Start()
    {
        // BackgroundGamepadProviderを追加
        var provider = gameObject.AddComponent<BackgroundGamepadProvider>();
        
        // デバイス数確認
        Debug.Log($"Connected devices: {provider.GetConnectedDeviceCount()}");
        
        // XInputデバイス一覧
        foreach (var device in provider.GetXInputDevices())
        {
            Debug.Log($"XInput device: {device}");
        }
    }
}
```

## 🔧 バックグラウンド動作の確認

### 動作テスト方法

1. **フォーカステスト**
   ```csharp
   void OnApplicationFocus(bool hasFocus)
   {
       Debug.Log($"App focus: {hasFocus}");
       Debug.Log($"Run in background: {Application.runInBackground}"); // true
       
       var gamepad = InputSystem.GetDevice<WindowsXInputGamepad>();
       if (gamepad != null)
       {
           Debug.Log($"Gamepad background support: {gamepad.canRunInBackground}"); // true
       }
   }
   ```

2. **実用テスト**
   - 他のアプリケーション（ブラウザ、OBS等）にフォーカスを移す
   - ゲームパッドの入力が継続して反応することを確認
   - Unity Consoleでログが出力され続けることを確認

## 📚 API リファレンス

### WindowsXInputGamepad

XInput API経由のゲームパッドデバイス

```csharp
public class WindowsXInputGamepad : Gamepad, IInputUpdateCallbackReceiver
{
    // バックグラウンド対応
    public new bool canRunInBackground => true;
    
    // コントローラーインデックス (0-3)
    public uint ControllerIndex { get; }
    
    // 振動機能
    public bool SupportsVibration { get; }
    public void SetVibration(float leftMotor, float rightMotor);
    
    // 接続状態
    public bool IsConnected();
}
```

### WindowsGamingInputGamepad

Windows Gaming Input API経由のゲームパッドデバイス

```csharp
public class WindowsGamingInputGamepad : Gamepad, IInputUpdateCallbackReceiver
{
    // バックグラウンド対応
    public new bool canRunInBackground => true;
    
    // WGIインデックス
    public int GamepadIndex { get; }
    
    // 高度な振動制御（トリガー振動含む）
    public void SetVibration(float leftMotor, float rightMotor, float leftTrigger, float rightTrigger);
    
    // 利用可能状態
    public bool IsAvailable();
}
```

### BackgroundGamepadProvider

デバイスの自動検出・管理クラス

```csharp
public class BackgroundGamepadProvider : MonoBehaviour, IInputUpdateCallbackReceiver
{
    // バックグラウンド対応
    public bool canRunInBackground => true;
    
    // デバイス情報取得
    public int GetConnectedDeviceCount();
    public IReadOnlyList<WindowsXInputGamepad> GetXInputDevices();
    public IReadOnlyList<WindowsGamingInputGamepad> GetWGIDevices();
    
    // 振動制御
    public void StopAllVibration();
}
```

## ⚠️ トラブルシューティング

### よくある問題

1. **バックグラウンドで入力が反応しない**
   ```csharp
   // Application.runInBackgroundが有効か確認
   Debug.Log($"Run in background: {Application.runInBackground}");
   
   // デバイスのcanRunInBackgroundを確認
   var gamepad = InputSystem.GetDevice<WindowsXInputGamepad>();
   Debug.Log($"Gamepad background: {gamepad?.canRunInBackground}");
   ```

2. **ゲームパッドが認識されない**
   ```csharp
   // 手動でデバイススキャン
   NativeGamepadInitializer.PrintDebugInfo();
   
   // XInputの状態確認
   for (uint i = 0; i < 4; i++)
   {
       bool connected = WindowsXInputAPI.IsControllerConnected(i);
       Debug.Log($"Controller {i}: {connected}");
   }
   ```

3. **振動が動作しない**
   ```csharp
   var gamepad = InputSystem.GetDevice<WindowsXInputGamepad>();
   if (gamepad != null)
   {
       Debug.Log($"Vibration support: {gamepad.SupportsVibration}");
       if (gamepad.SupportsVibration)
       {
           gamepad.SetVibration(1.0f, 1.0f); // 最大振動テスト
       }
   }
   ```

### パフォーマンス最適化

- **フレームレート**: デフォルトで60Hzでポーリング
- **GC回避**: unmanaged構造体使用でメモリ効率化
- **重複チェック**: パケット番号/タイムスタンプで不要な更新を回避

## 🔬 技術的詳細

### P/Invoke実装

```csharp
// XInput API定義例
[DllImport("xinput1_4.dll", CallingConvention = CallingConvention.StdCall)]
public static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

// Windows Gaming Input API定義例
[DllImport("combase.dll", CallingConvention = CallingConvention.StdCall)]
public static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);
```

### アーキテクチャ

```
ネイティブAPI (XInput/WGI)
    ↓ P/Invoke
Custom InputDevice (WindowsXInputGamepad)
    ↓ IInputUpdateCallbackReceiver
Unity Input System
    ↓ 標準Gamepad互換
アプリケーション
```

## 📈 使用例：ライブ配信対応

VTuberアプリケーションでの典型的な使用例：

```csharp
public class VTuberController : MonoBehaviour
{
    [SerializeField] private Animator avatarAnimator;
    
    void Update()
    {
        var gamepad = InputSystem.GetDevice<WindowsXInputGamepad>();
        if (gamepad != null)
        {
            // OBSがフォーカスされていてもアバター制御継続
            HandleEmotions(gamepad);
            HandleGestures(gamepad);
        }
    }
    
    private void HandleEmotions(WindowsXInputGamepad gamepad)
    {
        // 表情制御（バックグラウンドでも動作）
        if (gamepad.buttonNorth.wasPressedThisFrame) // Y button
            avatarAnimator.SetTrigger("Happy");
        
        if (gamepad.buttonWest.wasPressedThisFrame) // X button
            avatarAnimator.SetTrigger("Surprised");
    }
    
    private void HandleGestures(WindowsXInputGamepad gamepad)
    {
        // ジェスチャー制御（バックグラウンドでも動作）
        var leftStick = gamepad.leftStick.ReadValue();
        avatarAnimator.SetFloat("GestureX", leftStick.x);
        avatarAnimator.SetFloat("GestureY", leftStick.y);
    }
}
```

## 📄 ライセンス

MIT — リポジトリルートの [LICENSE](../../LICENSE) を参照。

## 🛠️ 開発・貢献

バグ報告や機能要望は、プロジェクトのIssueトラッカーまでお願いします。

---

**重要**: このパッケージはWindows専用です。他のプラットフォームでは標準のInput Systemが使用されます。