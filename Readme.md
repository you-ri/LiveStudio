# LiveStudio

Open-source Unity packages for VTuber / avatar live-streaming pipelines.

This is a **package monorepo**. Each subdirectory under `Packages/` is an independent Unity Package Manager (UPM) package and can be installed via Git URL with a `?path=` query.

> [!WARNING]
> **Under active development.** All packages in this repository are pre-1.0 and ship with a UPM lifecycle suffix:
> - `-exp.N` (Experimental) — APIs and behavior may change at any time without notice. Currently: `jp.lilium.remotecontrol`, `jp.lilium.vrchatavatartransfer`.
> - `-pre.N` (Pre-release) — Approaching a stable release; surface area is largely settled but may still change. Currently: `jp.lilium.nativegamepad`.
>
> Unity Package Manager hides these versions unless **Show Preview Packages** is enabled in *Edit > Project Settings > Package Manager*. Pin to a specific tag (`#vX.Y.Z`) if you need a stable target, and review release notes before upgrading.

---

## Packages

| Package | Folder | Stage | Description |
|---|---|---|---|
| `jp.lilium.vrchatavatartransfer` | [`Packages/VRChatAvatarTransfer`](./Packages/VRChatAvatarTransfer) | Experimental (`-exp`) | Editor tools to bring VRChat avatars into non-VRChat environments (PhysBone → VRM SpringBone, VRC Constraint → Unity Constraint). |
| `jp.lilium.nativegamepad` | [`Packages/NativeGamepad`](./Packages/NativeGamepad) | Pre-release (`-pre`) | Native Windows gamepad support with background input (XInput + Windows.Gaming.Input). |
| `jp.lilium.remotecontrol` | [`Packages/RemoteControl`](./Packages/RemoteControl) | Experimental (`-exp`) | REST API server with reflection-based remote control for Unity Editor and runtime. SSE-based realtime updates and `[ExposedProperty]`-driven UI generation. |

More packages will be added here over time.

---

## Versioning

LiveStudio uses **monorepo-wide synchronized SemVer**: every package under `Packages/` shares the same `X.Y.Z` core in its `package.json`. A release bumps the SemVer of all packages together and tags the repository as `v<X.Y.Z>`. This trades strict per-package SemVer for low operational overhead as the package count grows.

Each package carries its own UPM lifecycle suffix (`-exp.N`, `-pre.N`, or none), reflecting maturity independently. The release workflow preserves each package's stage and resets the suffix counter to `1` on every SemVer bump (so `0.19.1-exp.3` becomes `0.19.2-exp.1` after a `patch` bump).

Tag → package mapping is consistent: pinning `#v0.19.2` works for any package and gives you a compatible set as of that release. Tags do not include the suffix.

---

## Branches

Promotion flow: **`dev` → `beta` → `main`**.

| Branch | Role |
|---|---|
| `main` | **Stable**. Tags (`vX.Y.Z`) are cut here. Default branch for installs that don't specify `#fragment`. |
| `beta` | Pre-release / release-candidate. Releases are cut from here (the Release workflow runs on `beta`, which then fast-forwards `main` and tags). |
| `dev` | **Active development**. Day-to-day commits land here, either directly or via short-lived feature branches that merge in and are then deleted. |

Long-lived branches are limited to these three. Force-pushing to any of them is forbidden; everything else is allowed for low-friction solo development. The same protection rules can later be tightened to require pull requests if more contributors join.

When a batch of work on `dev` is ready to release, fast-forward `beta` to `dev` (`git push origin dev:beta`) and run the Release workflow.

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

MIT — see [LICENSE](LICENSE). Copyright (c) You-Ri, 2026.
