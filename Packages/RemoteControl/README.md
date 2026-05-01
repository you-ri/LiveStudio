# Lilium Remote Control

Remote-control framework for Unity Editor and runtime over **REST + Server-Sent Events**.

- Mark properties, fields, and methods with `[ExposedProperty]` / `[ExposedFunction]` to expose them to a remote client
- REST API for read / write / invoke; SSE stream for realtime change notifications
- Works in Edit mode and Play mode
- No Virgo Motion dependency — usable as standalone Unity remote-control infrastructure

Pairs with **VirgoMotionRemote** (the desktop / web client) but the protocol is documented and any HTTP client can drive it. Useful for VTuber stage control, virtual-production camera/lighting consoles, and live-show operator panels.

---

## Requirements

- Unity **2022.3** or newer
- The following package is pulled in automatically as a dependency:
  - **Newtonsoft.Json for Unity** (`com.unity.nuget.newtonsoft-json`) `3.2.x`

---

## Installation

This package lives inside the [LiveStudio](https://github.com/you-ri/LiveStudio) monorepo, so installation uses Git URL with a `?path=` query.

In Unity, open `Window > Package Manager > + > Install package from git URL...` and paste:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/RemoteControl
```

Or add to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "jp.lilium.remotecontrol": "https://github.com/you-ri/LiveStudio.git?path=/Packages/RemoteControl"
  }
}
```

To pin a specific version (recommended for production):

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/RemoteControl#v0.19.1
```

To preview the latest unreleased work, point at the `beta` branch:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/RemoteControl#beta
```

> **Versioning note**: every package in the LiveStudio monorepo shares the same `version`. Pinning `#v0.19.1` here also pins every other LiveStudio package you install at that release to a known-compatible set. See [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning).

---

## Quick start

1. **Create an `ExposerAsset`** to hold the registry of exposed objects. In the Project window: right-click → **Create > Virgo Motion > Exposer Asset**.
2. **Add a `RemoteControlProvider` component** to a GameObject in your scene and assign the `ExposerAsset` to it.
3. **Mark a class as exposable** and decorate the members you want to expose:

   ```csharp
   using Lilium.RemoteControl;
   using UnityEngine;

   [ExposedClass]
   public class MySettings : MonoBehaviour
   {
       [ExposedProperty]
       public float volume { get; set; } = 1.0f;

       [ExposedProperty]
       [Slider(0, 100)]
       public int brightness { get; set; } = 50;

       [ExposedFunction]
       public void Reset()
       {
           volume = 1.0f;
           brightness = 50;
       }
   }
   ```

4. Enter Play mode (or use Edit mode — both are supported). The HTTP server starts and accepts requests at the configured port.

For the full attribute list, REST endpoint reference, and SSE stream format, see [`Documentation~/Reference.md`](Documentation~/Reference.md). Translation of labels and help text is documented in [`Documentation~/Localization.md`](Documentation~/Localization.md).

---

## Source generator

A Roslyn source generator ships as a prebuilt DLL under `Plugins/Lilium.RemoteControl.SourceGenerator.dll`. **Consumers do not need to build anything** — it works out of the box.

If you are modifying the generator yourself, the source lives at `SourceGenerator~/Lilium.RemoteControl.SourceGenerator/` and the build script is colocated:

```powershell
./SourceGenerator~/build.ps1
```

Internal details (Roslyn 4.0 constraint, fallback behavior when the generator is disabled, etc.) are in [`Documentation~/SourceGenerator.md`](Documentation~/SourceGenerator.md).

---

## License

MIT — see the [LICENSE](../../LICENSE) at the repository root.

Third-party dependencies and their licenses are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
