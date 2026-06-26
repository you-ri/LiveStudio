# Changelog

## [0.24.0] - 2026-06-26

### Added

- `POST /exposed/batch` endpoint applies multiple object / property / function operations in a single request with per-item continue-on-error (each item's status and body are echoed back in order). The exposed REST API is now documented in `Documentation~/openapi.yml`.
- Per-member persist scope: `[ExposedField]` / `[ExposedProperty]` gain a `persistScope` (Scene default / Project), so the serializer can split live-scene state from per-class project settings.
- Exposed key-path addressing: a property path can target an array element by a stable `[ExposedKey]` value (e.g. `expressions[Joy].weight`), which backs the generic `SetPropertyAction` "bind to key" flow.
- `[Collapsed]` attribute: a hint that makes the remote app render an array or nested struct collapsed by default (the expand toggle remains). Emitted as a standalone `collapsed` flag, independent of the property controller.
- `elementTypeOptions` is emitted for polymorphic array properties so the remote app can offer "add an element of type …".

### Fixed

- `ExposedPropertyUtility.CreateDefaultElement` falls back to the first concrete `[ExposedClass]` subtype for abstract / interface element types, so adding an element to a polymorphic `[SerializeReference]` array no longer throws `MissingMethodException`.

## [0.23.6] - 2026-06-23

### Changed

- `[StringSelector]` can now annotate method parameters in addition to properties and fields. An `ExposedFunction` argument so marked is rendered as a dropdown whose choices come from the owning object's `sourcePropertyName` property, letting a function take a constrained string argument (e.g. selecting a named stage mark).

## [0.23.4] - 2026-06-22

### Fixed

- A `?type=X` query no longer drops a first-class `[ExposedClass]` component (e.g. `AvatarController` exposed as "Avatar") when its GameObject is also surfaced through a generic wrapper handle. The component and the wrapper are distinct exposed identities; the previous GameObject-identity de-duplication made RemoteApp pages report "No avatars available" once the avatar was exposed as an `ExposedGameObjectWithTransform`. Genuine same-target duplicates are still collapsed via `FindByTarget`.
- Live scene serialization now skips only a real-but-destroyed (fake-null) Unity reference during a base-scene reload, while still serializing a pure proxy that legitimately never had a backing reference.

## [0.23.2] - 2026-06-21

### Fixed

- `FrameRate.AsFrameNumber` now computes in `double` instead of casting through `float`. Past 2^24 (~3.2 days at 60fps) `float` lost integer precision, collapsing consecutive frame numbers onto the same value and stuttering the Studio playback buffer routing; `double` is exact for frame integers up to 2^53.

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
