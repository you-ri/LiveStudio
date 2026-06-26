# Changelog

## [0.24.0] - 2026-06-26

### Changed

- The VRChat Prop Builder follows the prop-component split: a built prop now carries the shared `Prop` (socket follow) plus the `AvatarItem` behavior component instead of the old single `AvatarProp`. Existing built props should be rebuilt.

## [0.23.6] - 2026-06-23

### Added

- The VRChat Prop Builder now ports facial expressions onto the built prop: gesture toggles derived from the prop controller's transitions, plus the avatar's `ExpressionsMenu` expressions filtered to the parameters the prop's own controller declares (overrides that the prop cannot drive are dropped). Standalone prop prefabs still get their gesture expressions.

### Changed

- `VRCExpressionsConverter.Collect` was split out from `Convert` so the avatar and prop-builder paths share the same `VRCExpressionsMenu` walk.

## [0.23.0] - 2026-06-21

### Added

- VRChat Prop Builder: a new editor window/tool that builds a runtime avatar prop (`.prop.lsb`) from a VRChat avatar so it can be attached to avatars in the Studio app.
- Hand-gesture expressions are now generated automatically during conversion (`GestureExpressionBuilder`), sharing the VRChat expression driver.
- PhysBone → VRM SpringBone conversion exposes gravity and stiffness coefficient inputs to tune the converted springs.
- Exported avatar bundles embed a thumbnail, used by the remote app's avatar cards.

### Fixed

- VRChat `StateMachineBehaviour`s are now stripped from the converted avatar's controllers (`VrcStateMachineBehaviourStripper`), avoiding missing-script references in the exported `.avatar.lsb`.

## [0.22.0] - 2026-06-14

### Changed

- Avatar bundle export now produces a `.avatar.lsb` file (was `.lsavatar`), built through the shared `BundleBuildUtility`, unifying the extension with LiveStudio's scene bundles. A new "Export Avatar Bundle (.avatar.lsb)" Assets context menu exports the selected prefab directly and guarantees the compound suffix.
- The export buttons were relabeled to spell out their extensions ("Export as Unity Package (.unitypackage)").

### Fixed

- The FX `AnimatorController` is now always deep-cloned through the standard AnimatorController API during conversion, avoiding "Broken text PPtr" warnings from dangling references in the source and the transition loss that `CopyAsset` could cause for sub-asset controllers.

## [0.21.2] - 2026-06-11

### Changed

- Renamed the export buttons to "Export as UnityPackage" and "Export as LSAvatar" for clearer labels.

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
