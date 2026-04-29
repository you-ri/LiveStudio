# VRChat Avatar Transfer

Editor tools to bring **VRChat avatars** into non-VRChat Unity environments.

- **PhysBone → VRM SpringBone** conversion
- **VRC Constraint → Unity Constraint** conversion

Useful when you want to reuse a VRChat-prepared avatar in projects that target VRM, generic Unity, or other VTuber pipelines.

---

## Requirements

- Unity **2022.3** or newer
- The following packages must already be installed in the consuming project (this package does **not** pull them in automatically):
  - **VRChat SDK** (`com.vrchat.base`, `com.vrchat.avatars`) — install via [VRChat Creator Companion (VCC) / VPM](https://vcc.docs.vrchat.com/)
  - **UniVRM** (`com.vrmc.gltf`, `com.vrmc.vrm`) `0.130.x` — install via UPM Git URL or OpenUPM

> The `dependencies` field in `package.json` lists VRChat SDK packages, but the VRChat SDK is distributed through VPM, not the standard Unity Package Registry. UPM cannot resolve them on its own — install them in the host project first.

---

## Installation

This package lives inside the [LiveStudio](https://github.com/you-ri/LiveStudio) monorepo, so installation uses Git URL with a `?path=` query.

In Unity, open `Window > Package Manager > + > Install package from git URL...` and paste:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer
```

Or add to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "jp.lilium.vrchatavatartransfer": "https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer"
  }
}
```

To pin a specific version (recommended for production):

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer#v0.19.1
```

To preview the latest unreleased work, point at the `beta` branch:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer#beta
```

> **Versioning note**: every package in the LiveStudio monorepo shares the same `version`. Pinning `#v0.19.1` here also pins every other LiveStudio package you install at that release to a known-compatible set. See [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning).

---

## Usage

1. Place a VRChat avatar (with `VRCAvatarDescriptor`) into the scene and select its root GameObject.
2. From the menu bar, choose one of:
   - `Tools/Virgo Motion/VRChat Avatar Transfer/Convert PhysBone to VRM SpringBone (Selected)`
   - `Tools/Virgo Motion/VRChat Avatar Transfer/Convert VRC Constraint to Unity Constraint (Selected)`
   - `Tools/Virgo Motion/VRChat Avatar Transfer/Convert All (VRM SpringBone) (Selected)`
3. The converters operate in-place on the selected avatar(s).

Multiple avatars can be selected and processed at once.

---

## License

MIT — see the [LICENSE](../../LICENSE) at the repository root.
