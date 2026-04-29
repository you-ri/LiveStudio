# LiveStudio

Open-source Unity packages for VTuber / avatar live-streaming pipelines, maintained by [You-Ri / Lilium](https://github.com/you-ri).

This is a **package monorepo**. Each subdirectory under `Packages/` is an independent Unity Package Manager (UPM) package and can be installed via Git URL with a `?path=` query.

---

## Packages

| Package | Folder | Description |
|---|---|---|
| `jp.lilium.vrchatavatartransfer` | [`Packages/VRChatAvatarTransfer`](./Packages/VRChatAvatarTransfer) | Editor tools to bring VRChat avatars into non-VRChat environments (PhysBone → VRM SpringBone, VRC Constraint → Unity Constraint). |

More packages will be added here over time.

---

## Installation

Each package is installed individually using the UPM Git URL `?path=` syntax. Example for `VRChatAvatarTransfer`:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer
```

Pin to a specific tag:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/VRChatAvatarTransfer#v0.19.1
```

See each package's own README for required SDKs and Unity version.

---

## License

MIT — see [LICENSE](LICENSE). Copyright (c) You-Ri, 2026.
