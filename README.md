# LiveStudio

Open-source Unity packages for VTuber / avatar live-streaming pipelines.

This is a **package monorepo**. Each subdirectory under `Packages/` is an independent Unity Package Manager (UPM) package and can be installed via Git URL with a `?path=` query.

> [!WARNING]
> **Under active development.** All packages in this repository are pre-1.0 and ship with a UPM lifecycle suffix:
> - `-exp.N` (Experimental) — APIs and behavior may change at any time without notice. Currently: `jp.lilium.livestudio`, `jp.lilium.livestudio.virgo`, `jp.lilium.remotecontrol`, `jp.lilium.vrchatavatartransfer`.
> - `-pre.N` (Pre-release) — Approaching a stable release; surface area is largely settled but may still change. Currently: `jp.lilium.nativegamepad`.
>
> Unity Package Manager hides these versions unless **Show Preview Packages** is enabled in *Edit > Project Settings > Package Manager*. Pin to a specific tag (`#vX.Y.Z`) if you need a stable target, and review release notes before upgrading.

---

## Packages

| Package | Folder | Stage | Description |
|---|---|---|---|
| `jp.lilium.livestudio` | [`Packages/LiveStudio`](./Packages/LiveStudio) | Experimental (`-exp`) | Base package for VTuber live-streaming apps: Camera / Lighting / Scene / Build / RemoteControl base / shared Localization, independent of any specific motion capture system. |
| `jp.lilium.livestudio.virgo` | [`Packages/LiveStudioVirgo`](./Packages/LiveStudioVirgo) | Experimental (`-exp`) | VirgoMotion adapter for `jp.lilium.livestudio`: UDP `VirgoMotionSource`, Fusion REST `FusionRequestSystem`, `AnimationFrameBridge`, Build / Tools menu. |
| `jp.lilium.vrchatavatartransfer` | [`Packages/VRChatAvatarTransfer`](./Packages/VRChatAvatarTransfer) | Experimental (`-exp`) | Editor tools to bring VRChat avatars into non-VRChat environments (PhysBone → VRM SpringBone, VRC Constraint → Unity Constraint). |
| `jp.lilium.nativegamepad` | [`Packages/NativeGamepad`](./Packages/NativeGamepad) | Pre-release (`-pre`) | Native Windows gamepad support with background input (XInput + Windows.Gaming.Input). |
| `jp.lilium.remotecontrol` | [`Packages/RemoteControl`](./Packages/RemoteControl) | Experimental (`-exp`) | REST API server with reflection-based remote control for Unity Editor and runtime. SSE-based realtime updates and `[ExposedProperty]`-driven UI generation. |

---

## Installation

Each package is installed individually using the UPM Git URL `?path=` syntax.

```jsonc
// Packages/manifest.json
{
  "dependencies": {
    // Stable (latest release on main)
    "jp.lilium.vrchatavatartransfer": "https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer",

    // Pinned to a specific release (recommended for production)
    "jp.lilium.vrchatavatartransfer": "https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer#v0.19.2",

    // Latest beta (preview, may break)
    "jp.lilium.vrchatavatartransfer": "https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer#beta"
  }
}
```

See each package's own README for required SDKs (e.g. VRChat SDK, UniVRM) and Unity version.

---

## License

Apache License 2.0 — see [LICENSE](LICENSE). Copyright (c) You-Ri, 2026.
