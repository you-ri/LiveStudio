# Changelog

## [0.21.1] - 2026-06-09

### Added

- "Export as *.lsavatar" — exports a converted avatar as an AssetBundle that the Studio app can import at runtime.

### Fixed

- Shader variants (e.g. lilToon) are no longer stripped from the exported AssetBundle during build, preventing magenta materials when the avatar is loaded.

## [0.20.12] - 2026-06-04

### Added

- VRM10 `LookAt` is now configured during conversion so the transferred avatar's eye gaze can be delegated to LookAt.

## [0.20.11] - 2026-06-03

### Added

- Editor-only components implementing `VRC.SDKBase.IEditorOnly` (NDMF / Modular Avatar, etc.) are now stripped during both Convert and Make Baked Prefab.

### Fixed

- The Readme inspector buttons are now clickable even when the package is consumed as an immutable git/registry dependency.

## [0.20.10] - 2026-06-02

- No functional changes (version synchronized with the monorepo release).

## [0.20.9] - 2026-06-02

### Added

- Optional package install button in the Readme inspector.

### Fixed

- PhysBone to SpringBone conversion now uses a 1.0 gravity power scale instead of 20.0, which previously over-amplified gravity.
