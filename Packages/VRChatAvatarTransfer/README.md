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

### Option A — VPM (VCC / ALCOM, recommended)

1. In VCC, open *Settings → Packages → Add Repository* and add:
   ```
   https://you-ri.github.io/LiveStudio/vpm.json
   ```
2. Pick your project, enable **Show Pre-release Packages** (this package ships with `-exp.N` suffix), then add **Lilium VRChat Avatar Transfer**.
3. VCC pulls in `jp.lilium.livestudio` and `jp.lilium.remotecontrol` automatically via `vpmDependencies`, and resolves `com.vrchat.base` / `com.vrchat.avatars` from the VRChat VPM you have already configured.
4. **UniVRM is not on VPM** — install `com.vrmc.gltf` / `com.vrmc.vrm` separately via UPM Git URL:
   ```json
   {
     "dependencies": {
       "com.vrmc.gltf": "https://github.com/vrm-c/UniVRM.git?path=/Packages/UniGLTF#v0.131.0",
       "com.vrmc.vrm":  "https://github.com/vrm-c/UniVRM.git?path=/Packages/VRM10#v0.131.0"
     }
   }
   ```

### Option B — UPM Git URL

If you prefer not to use VCC, install everything via UPM Git URL. Pin to the LiveStudio tag that matches the version you want — every package in the LiveStudio monorepo shares the same `version`:

```json
{
  "dependencies": {
    "jp.lilium.livestudio":           "https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudio#v0.20.2",
    "jp.lilium.remotecontrol":        "https://github.com/you-ri/LiveStudio.git?path=/Packages/RemoteControl#v0.20.2",
    "jp.lilium.nativegamepad":        "https://github.com/you-ri/LiveStudio.git?path=/Packages/NativeGamepad#v0.20.2",
    "jp.lilium.vrchatavatartransfer": "https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer#v0.20.2",
    "com.vrmc.gltf":                  "https://github.com/vrm-c/UniVRM.git?path=/Packages/UniGLTF#v0.131.0",
    "com.vrmc.vrm":                   "https://github.com/vrm-c/UniVRM.git?path=/Packages/VRM10#v0.131.0"
  }
}
```

VRChat SDK packages (`com.vrchat.base`, `com.vrchat.avatars`) must still be installed via [VRChat Creator Companion (VCC)](https://vcc.docs.vrchat.com/) — UPM cannot resolve VPM-only packages on its own.

To preview the latest unreleased work, point any of these LiveStudio URLs at the `beta` branch instead of a tag:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer#beta
```

> **Versioning note**: every package in the LiveStudio monorepo shares the same `version`. Pinning `#v0.20.2` here also pins every other LiveStudio package you install at that release to a known-compatible set. See [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning).

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
3. Converted prefabs are written to `Assets/VRCAT_GeneratedAssets/{originalName}.prefab`. Existing files at the destination are overwritten without confirmation. The original prefab assets are not modified.

> **VRCAvatarParameterDriver**: the avatar's FX AnimatorController is duplicated to `Assets/VRCAT_GeneratedAssets/{originalName}.FX.controller`, its `VRCAvatarParameterDriver` state behaviours are replaced with `AvatarParameterDriver`, and the copy is assigned to the Animator. The original FX controller asset is left untouched. This conversion runs only in the prefab-asset pipeline — the `(Selected)` hierarchy menus do not convert parameter drivers, because that would mutate the shared source controller asset in place.

### From the transfer window

1. Open `Window/VRChat Avatar Transfer/Transfer` (or `Tools/VRChat Avatar Transfer/Transfer`).
2. Drop a VRChat avatar prefab into the **VRChat Avatar Prefab** field.
3. The window verifies the prefab against the prerequisites:
   - It must be a prefab asset.
   - The root must have `VRCAvatarDescriptor`.
   - The root must have an `Animator` configured as Humanoid.

   It also reports informational counts (PhysBone components, PhysBone colliders, VRC Constraints) and whether a custom FX AnimatorController is set on the avatar descriptor.
4. The **Convert** button is enabled only when all required checks pass. Pressing it writes the converted prefab to `Assets/VRCAT_GeneratedAssets/{originalName}.prefab` (overwrites without confirmation).

---

## License

Apache License 2.0 — see the [LICENSE](../../LICENSE) at the repository root.
