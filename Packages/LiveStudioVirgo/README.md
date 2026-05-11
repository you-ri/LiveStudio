# Lilium LiveStudio Virgo Extension

VirgoMotion adapter package for [`jp.lilium.livestudio`](../LiveStudio). Plugs the [VirgoMotion](https://github.com/you-ri) capture pipeline (Capture → Fusion) into LiveStudio's avatar runtime.

- **`VirgoMotionSource`** — `MotionSourceBase` implementation that receives `AnimationFrameData` packets over UDP and feeds them into the LiveStudio avatar animation system. Drop one onto a scene, set the listen `port`, and you're streaming.
- **`FusionRequestSystem`** — Posts the active avatar's `AvatarBuildData` to the Fusion process so it can rebuild its skeleton in lockstep with the studio.
- **`AnimationFrameBridge`** — Zero-allocation conversion between the VirgoMotion wire format (`Lilium.LiveStudio.Virgo.AnimationFrameData`) and LiveStudio's `AvatarAnimationData`.
- **Build / Tools menu** — Editor entry points for building the Studio / Fusion player apps and launching the companion Remote app from inside Unity.

This package is intentionally a thin VirgoMotion-specific adapter on top of LiveStudio. Anything that isn't tied to the VirgoMotion capture protocol — generic camera / lighting / scene / RemoteControl plumbing — lives in [`jp.lilium.livestudio`](../LiveStudio) instead.

---

## Requirements

- Unity **2022.3** or newer (Unity 6.x verified)
- The following packages are pulled in automatically as dependencies:
  - `jp.lilium.livestudio`
  - `jp.lilium.remotecontrol`
  - `com.unity.cinemachine`
- VirgoMotion runtime side (Capture iOS app + Fusion process) is required for end-to-end use, but this package alone is enough to build a Studio that consumes the protocol.

---

## Installation

This package lives inside the [LiveStudio](https://github.com/you-ri/LiveStudio) monorepo, so installation uses Git URL with a `?path=` query.

In Unity, open `Window > Package Manager > + > Install package from git URL...` and paste:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudioVirgo
```

Or add to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "jp.lilium.livestudio.virgo": "https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudioVirgo"
  }
}
```

To pin a specific version (recommended for production):

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudioVirgo#v0.19.1
```

To preview the latest unreleased work, point at the `beta` branch:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudioVirgo#beta
```

> **Versioning note**: every package in the LiveStudio monorepo shares the same `version`. Pinning `#v0.19.1` here also pins every other LiveStudio package you install at that release to a known-compatible set. See [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning).

---

## License

Apache License 2.0 — see the [LICENSE](../../LICENSE) at the repository root.
