# Lilium LiveStudio

Base package for **VTuber live-streaming** Unity applications. Provides the building blocks for an avatar-driven streaming app.

- **Camera / Lighting / Scene / Screen** — runtime controllers for the on-screen composition.
- **RemoteControl handlers** — Camera / Light / Manipulator API handlers built on `jp.lilium.remotecontrol`.
- **InputAction / KeyBinding** — generic input management on top of the Unity Input System.
- **Build pipeline** — command-line build entry points for Studio-style apps.
- **Localization** — shared key registration for `LocalizationSystem`.

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

## License

MIT — see the [LICENSE](../../LICENSE) at the repository root.
