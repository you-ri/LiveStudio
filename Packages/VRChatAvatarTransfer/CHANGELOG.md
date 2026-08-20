# Changelog

## [0.26.0] - 2026-08-20
<!-- changelog-sha: c639c1ec8e82903d9b2fce176fb5ad194df87332 -->

### Fixed

- The welcome Readme now opens once per project instead of once per editor session. The "already shown" flag lived in `SessionState`, so every editor launch re-selected the Readme and stole the inspector from whatever you were working on; it is now an `EditorPrefs` entry keyed by a hash of the project path, so a different project still gets its own welcome. The flag is only set once the asset was actually found and selected, so a project where the lookup runs before the asset is imported still gets its welcome later.
- Readme lookup no longer fails when another package ships its own `Readme` type. The search required `t:Readme` to match exactly one asset and gave up with a console log otherwise, which is what happened alongside Unity's own project templates. It now loads each candidate and keeps the first one that really is this package's Readme.

## [0.25.3] - 2026-07-22
<!-- changelog-sha: ead5a500c2674f81ae92f66e88e1b3eacac8bd4f -->

### Fixed

- `InMemoryAssetCollector` compiles on Unity 6.5 (6000.5), where `Object.GetInstanceID` and the `EntityId`⇔`int` implicit conversions became Obsolete errors. Its visited-set key now comes from a version-guarded helper and is widened to `long`; only identity within a single collection pass matters, so the change is invisible to callers.

## [0.24.2] - 2026-07-03
<!-- changelog-sha: 378d90c48c438183219cce60ea3f6e38536979f4 -->

### Fixed

- The Prop Builder now strips avatar-follow constraints from a built prop. A hand-held VRChat prop follows the avatar through VRC constraints, which the transfer converts to Unity constraints; on a standalone prop the socket follow is driven by `AvatarItem`'s attachment, so those leftover constraints are redundant and fight it (their avatar-side targets are detached too). Only constraints whose sources point outside the prop hierarchy (i.e. the avatar) are removed; internal part-to-part constraints are kept.

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
