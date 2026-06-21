# Changelog

## [0.23.0] - 2026-06-21

### Added

- `[ExposedExternalEnum(typeof(T))]` assembly attribute registers external or built-in enums (e.g. `HumanBodyBones`) as remote-app dropdowns without modifying the type; supports `excludeNames` and the registration survives `Reset()`.
- `ExposedObjectSnapshot` can now capture an exposed object's default state and compute the delta against it (`CaptureDefaults` / `CaptureDelta`), the basis for prop presets.
- The remote app is now notified when a live scene is saved.

### Fixed

- Saved live-scene entries that reference an unresolved `@type` now deserialize away cleanly instead of leaving a null hole.
- Live-scene restore now reapplies scene visibility and active state correctly on startup.

## [0.22.1] - 2026-06-14

### Fixed

- Live scene saves now record only a build-settings scene as `baseSceneName`. Saving while an additive `.scene.lsb` world (buildIndex -1) was the active scene previously wrote the world's name, so loading that file in a build without the world dirtied the active scene and blocked graceful quit with an unsaved-changes dialog. The name is now resolved to the active scene only when it is a build scene, otherwise the first loaded build scene (worlds are calibrated on top of it), applied to both the save and the `HasUnsavedChanges` baseline.

## [0.21.2] - 2026-06-11

### Fixed

- `ExposedObjectUtility.InstanceIDToObject` now calls the public `Resources.InstanceIDToObject` instead of reflecting the internal `UnityEngine.Object.FindObjectFromInstanceID`, which broke on Unity 6.1 (6000.3) where its argument changed from `int` to an `EntityId` struct. The public API keeps an `int` argument across Unity 2021.3–6000.3.

## [0.21.0] - 2026-06-08

### Changed

- Member access is now backed by source-generated accessors, removing the runtime dependency on `Unity.Properties` and reducing reflection overhead.
- `ExposedObject` is now a readonly struct and was renamed to `ExposedObjectHandle`, eliminating transient view allocations during property access.

### Fixed

- Static `[ExposedClass]` types no longer disappear from the exposed set when their static constructor runs before all game assemblies are loaded; attributes are re-scanned after assembly load.
- Inherited `[ExposedClass]` members are now registered in declaration order.

## [0.20.12] - 2026-06-04

### Added

- `switchSceneOnLoad` option for LiveScene loading, controlling whether loading a scene also switches to it.

## [0.20.11] - 2026-06-03

### Added

- Log the live scene file path when a scene is saved.

## [0.20.10] - 2026-06-02

- No functional changes (version synchronized with the monorepo release).

## [0.20.9] - 2026-06-02

### Added

- `allowExternalConnections` option to bind the server to all network interfaces.

### Changed

- Handlers are now registered per-instance with their routes declared in the constructor.
