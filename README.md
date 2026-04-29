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

## Versioning

LiveStudio uses **monorepo-wide synchronized versioning**: every package under `Packages/` carries the **same `version`** in its `package.json`. A release bumps all packages together and tags the repository as `v<X.Y.Z>`. This trades strict per-package SemVer for low operational overhead as the package count grows.

Tag → package mapping is consistent: pinning `#v0.19.2` works for any package and gives you a compatible set as of that release.

---

## Branches

| Branch | Role |
|---|---|
| `main` | **Stable**. Tags (`vX.Y.Z`) are cut here. Default branch for installs that don't specify `#fragment`. |
| `beta` | Active development. Day-to-day commits land here, either directly or via short-lived feature branches that merge in and are then deleted. |

Long-lived branches are limited to these two. Force-pushing to either is forbidden; everything else is allowed for low-friction solo development. The same protection rules can later be tightened to require pull requests if more contributors join.

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

## Release workflow

Releases are produced by the `Release` GitHub Actions workflow (`.github/workflows/release.yml`). It runs on the `beta` branch via `workflow_dispatch` and:

1. Verifies that every `Packages/*/package.json` has the same `version`.
2. Bumps that version (`patch` / `minor` / `major`) and commits to `beta`.
3. Fast-forwards `main` to `beta` (`git push origin beta:main`).
4. Tags `main` as `v<X.Y.Z>`.
5. Creates a GitHub Release with auto-generated notes.

To cut a release: open the **Actions** tab, choose **Release**, pick a `version_bump`, and run it on `beta`. No manual tag pushes needed.

---

## License

MIT — see [LICENSE](LICENSE). Copyright (c) You-Ri, 2026.
