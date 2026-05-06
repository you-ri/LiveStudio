# Lilium LiveStudio

Base package for **VTuber live-streaming** Unity applications. Provides the building blocks for an avatar-driven streaming app, independent of any specific motion capture system.

- **Camera / Lighting / Scene / Screen** — runtime controllers for the on-screen composition.
- **RemoteControl handlers** — Camera / Light / Manipulator API handlers built on `jp.lilium.remotecontrol`.
- **InputAction / KeyBinding** — generic input management on top of the Unity Input System.
- **Build pipeline** — command-line build entry points for Studio-style apps.
- **Localization** — shared key registration for `LocalizationSystem`.

Combine with a motion capture pipeline of your choice (ARKit, MediaPipe, OSC, etc.) to assemble a live-streaming Studio application.

---

## Requirements

- Unity **2022.3** or newer (Unity 6.x verified)
- The following packages will be pulled in automatically as dependencies:
  - `jp.lilium.remotecontrol`
  - `jp.lilium.nativegamepad`
  - `com.unity.cinemachine`

---

## Installation

This package lives inside the [LiveStudio](https://github.com/you-ri/LiveStudio) monorepo, so installation uses Git URL with a `?path=` query.

In Unity, open `Window > Package Manager > + > Install package from git URL...` and paste:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudio
```

Or add to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "jp.lilium.livestudio": "https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudio"
  }
}
```

To pin a specific version (recommended for production):

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudio#v0.19.1
```

To preview the latest unreleased work, point at the `beta` branch:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudio#beta
```

> **Versioning note**: every package in the LiveStudio monorepo shares the same `version`. Pinning `#v0.19.1` here also pins every other LiveStudio package you install at that release to a known-compatible set. See [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning).

---

## Usage

1. Drop the provided prefabs (`Studio System`, `Cameras`, `Screen`, `Skybox Background`, etc.) into your scene, or start from the bundled scene template.
2. Add an avatar source — `PrefabAvatarSource` for a prefab avatar, or `VRMAvatarSource` for runtime VRM loading — and reference it from `AvatarController`.
3. Implement a motion source (`MotionSourceBase` subclass) to feed `HumanoidPoseData` and `ARKitWeightData` into the runtime, or use one of the existing integrations.
4. Hook up RemoteControl by adding the `Remote Control` prefab; this exposes Camera / Light / Manipulator endpoints to any `jp.lilium.remotecontrol` client (REST / SSE).

The runtime types live under the `Lilium.LiveStudio` namespace; editor-only utilities live under `Lilium.LiveStudio.Editor`.

---

## License

MIT — see the [LICENSE](../../LICENSE) at the repository root.
