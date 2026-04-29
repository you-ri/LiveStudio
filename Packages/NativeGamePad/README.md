# Lilium Native Gamepad

Unity Input System custom devices that wrap **XInput** and **Windows.Gaming.Input** via P/Invoke, so gamepad input keeps coming through even when Unity is not the foreground window.

- **Background input** (`canRunInBackground = true`) — input continues while OBS, a browser, or any other window owns focus.
- **XInput 1.4** path — Xbox / XInput-compatible controllers, indices 0-3.
- **Windows.Gaming.Input** path — extended trigger rumble.
- **Drop-in `Gamepad`** — both devices inherit `UnityEngine.InputSystem.Gamepad`, so existing `InputAction` setups keep working.

Useful for VTuber / live streaming setups where the user controls an avatar while a broadcast or capture tool is in the foreground.

---

## Requirements

- Unity **6.0** or newer
- **OS**: Windows 10 / 11 (this package is Windows-only; other platforms fall back to the standard Input System)
- **Input System** `1.14.2` or newer (`com.unity.inputsystem`)

---

## Installation

This package lives inside the [LiveStudio](https://github.com/you-ri/LiveStudio) monorepo, so installation uses Git URL with a `?path=` query.

In Unity, open `Window > Package Manager > + > Install package from git URL...` and paste:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad
```

Or add to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "jp.lilium.nativegamepad": "https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad"
  }
}
```

To pin a specific version (recommended for production):

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad#v0.19.1
```

To preview the latest unreleased work, point at the `beta` branch:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamePad#beta
```

> **Versioning note**: every package in the LiveStudio monorepo shares the same `version`. Pinning `#v0.19.1` here also pins every other LiveStudio package you install at that release to a known-compatible set. See [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning).

---

## Usage

The package registers two custom Input System devices on Windows:

- `WindowsXInputGamepad` — XInput 1.4 path
- `WindowsGamingInputGamepad` — Windows.Gaming.Input path

Both inherit `UnityEngine.InputSystem.Gamepad` and behave as standard gamepads:

```csharp
using UnityEngine.InputSystem;
using Lilium.NativeGamepad;

var gamepad = InputSystem.GetDevice<WindowsXInputGamepad>();
if (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)
{
    // ...
}

if (gamepad != null && gamepad.SupportsVibration)
{
    gamepad.SetVibration(0.5f, 0.3f);
}
```

In Input Action assets, the device appears as **"Windows XInput Gamepad (Background)"** under the Gamepad category and can be bound like any other `Gamepad`.

For automatic device detection and management, attach `BackgroundGamepadProvider` to a scene GameObject — it scans XInput / WGI devices and exposes them via `GetXInputDevices()` / `GetWGIDevices()`.

> **Important**: this package targets Windows. On other platforms, Unity's standard Input System gamepad implementation is used.

---

## License

MIT — see the [LICENSE](../../LICENSE) at the repository root.
