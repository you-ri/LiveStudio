# VRChat Avatar Transfer

Editor tools to bring **VRChat avatars** into non-VRChat Unity environments.

- **PhysBone → VRM SpringBone** conversion
- **VRC Constraint → Unity Constraint** conversion
- **VRCAvatarParameterDriver → AvatarParameterDriver** conversion (clean-room, VRCSDK-independent state behaviour from `jp.lilium.livestudio`)

Useful when you want to reuse a VRChat-prepared avatar in projects that target VRM, generic Unity, or other VTuber pipelines.

---

## Requirements

- Unity **2022.3** or newer
- The following packages must already be installed in the consuming project (this package does **not** pull them in automatically):
  - **VRChat SDK** (`com.vrchat.base`, `com.vrchat.avatars`) — install via [VRChat Creator Companion (VCC) / VPM](https://vcc.docs.vrchat.com/)
  - **UniVRM** (`com.vrmc.gltf`, `com.vrmc.vrm`) `0.130.x` — install via UPM Git URL or OpenUPM
- **Lilium Live Studio** (`jp.lilium.livestudio`) is resolved automatically as a package dependency; it provides the runtime `AvatarParameterDriver` state behaviour the converted controllers reference.

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

### From a Hierarchy GameObject

1. Place a VRChat avatar (with `VRCAvatarDescriptor`) into the scene and select its root GameObject.
2. From the menu bar, choose:
   - `Tools/VRChat Avatar Transfer/Convert All (VRM SpringBone) (Selected)`
3. The converters operate in-place on the selected avatar(s).

Multiple avatars can be selected and processed at once.

### From a prefab asset (Project window)

1. Select one or more VRChat avatar prefab assets in the Project window.
2. From the menu bar, choose:
   - `Tools/VRChat Avatar Transfer/Convert All (VRM SpringBone) (Prefab Asset)`
3. Converted prefabs are written to `Assets/VRChatAvatarTransfer/{originalName}.prefab`. Existing files at the destination are overwritten without confirmation. The original prefab assets are not modified.

> **VRCAvatarParameterDriver**: the avatar's FX AnimatorController is duplicated to `Assets/VRChatAvatarTransfer/{originalName}.FX.controller`, its `VRCAvatarParameterDriver` state behaviours are replaced with `AvatarParameterDriver`, and the copy is assigned to the Animator. The original FX controller asset is left untouched. This conversion runs only in the prefab-asset pipeline — the `(Selected)` hierarchy menus do not convert parameter drivers, because that would mutate the shared source controller asset in place.

### From the transfer window

1. Open `Window/VRChat Avatar Transfer/Transfer` (or `Tools/VRChat Avatar Transfer/Transfer`).
2. Drop a VRChat avatar prefab into the **VRChat Avatar Prefab** field.
3. The window verifies the prefab against the prerequisites:
   - It must be a prefab asset.
   - The root must have `VRCAvatarDescriptor`.
   - The root must have an `Animator` configured as Humanoid.

   It also reports informational counts (PhysBone components, PhysBone colliders, VRC Constraints) and whether a custom FX AnimatorController is set on the avatar descriptor.
4. The **Convert** button is enabled only when all required checks pass. Pressing it writes the converted prefab to `Assets/VRChatAvatarTransfer/{originalName}.prefab` (overwrites without confirmation).

---

## License

Apache License 2.0 — see the [LICENSE](../../LICENSE) at the repository root.
