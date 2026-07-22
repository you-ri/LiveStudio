# Changelog

## [Unreleased]

### Added

- `RemoteConfirmSystem` raises a confirmation on every surface at once — the OS dialog on the machine running the app and a modal in each connected remote app — and resolves it from whichever answers first, dismissing the rest. An operator working from a phone could not answer a prompt that only existed on the PC, and someone at the PC could not answer one that only existed in the remote app. Prompts travel as localization keys with the app's translation attached as a fallback, so each remote app renders them in ITS language. Answers come back over `POST /api/confirm` (a default route, since the framework itself raises prompts).
- `NativeConfirmDialog` shows the OS dialog on its own thread and can be closed from another one. The blocking `ConfirmDialog` cannot back a mirrored prompt: while it is up the app cannot serve the REST call carrying the remote answer, let alone act on it. `ConfirmDialog` stays for terminal, single-surface paths.
- `RemoteControlBehaviour.TrySaveOrPrompt` saves to the current path or opens the platform's "Save As" picker, picking the Editor or player dialog itself.
- `QuitApiHandler.onQuitRequesting` vetoes a quit asked for over REST. A player already has `Application.wantsToQuit` for this, but in the Editor a remote quit forces play mode off and the resulting `ExitingPlayMode` cannot be aborted — so without a veto here the Editor tore down before an asynchronous unsaved-changes prompt could be answered, and the prompt never reached the remote apps at all. Pressing Stop in the Editor still uses the local, un-mirrored dialog: nothing can hold that path open.
- `LiveSceneSaveSystem.ResetAllToDefault` reverts every contained object to its captured defaults regardless of dirty state. `RevertAllToDefault` only touches objects reporting dirty properties, and a load re-baselines everything as clean, so right after one it reverts nothing — a caller that has to discard the current state (switching to another project) needs the unconditional form.
- `LoadCurrentData` / `LoadCurrentDataFrom` take `forceBaseSceneReload`, which reloads the base scene even when the file already targets the active one. Without it a load into the same base scene deserializes on top of the live state, so every value the (delta) file omits keeps whatever was there before.

### Changed

- The unsaved-changes prompt shown when quitting now goes through `RemoteConfirmSystem`, so it appears in the remote apps as well as on the machine. It no longer needs the deferring coroutine either: the prompt never blocks `wantsToQuit`, which simply declines the quit and lets the answer start a new one.

## [0.25.2] - 2026-07-21
<!-- changelog-sha: 9a672726afe9b37983b2e8e941a5380792866d98 -->

### Added

- `ExposedObjectHandle.MarkClean` / `ExposedObjectDefaultRegistry.MarkClean` adopt an object's current state as the user-change baseline, so nothing on it counts as an unsaved edit until the next write. Only the user-change baseline moves; the serialization baseline keeps the captured defaults, so a delta save that follows still writes the full diff. Call it wherever state arrives from disk.

### Fixed

- Unsaved-change detection no longer reports a scene as dirty on every launch, which blocked quitting a Studio that had opened an existing project. The check serialized the whole scene and string-compared it against the file, so it could not tell "the user edited something" apart from "this build serializes the scene differently than the build that wrote the file" — any project whose `live.json` came from an older version failed it forever. It now asks the per-object dirty tracking, re-baselined wherever state arrives from disk (scene load, save, deferred pending entries, asset load tails).
- `IsDirty` and `GetDirtyProperties` compare in persistence shape (`forPersistence: true`), matching `CaptureDefaults` and the per-property baseline. Dirty must mean "a save would write something different", so read-only and non-persistable members — exactly what async loading churns after a restore: the crawl-built `ExternalAssetManager.assets` catalog, rebuilt `StageManager.sets`, `TransformRef.availableOwnerNames` — no longer count.

## [0.25.0] - 2026-07-17
<!-- changelog-sha: e35e6b466c57bae26ebaa8ecb4e4c73921db66bb -->

### Added

- Invoking exposed functions with arguments and through property paths: `ExposedPropertySerializer.BuildInvokeArguments` builds a typed positional argument array from a JSON array (shared by the REST invoke path and callers that replay a stored call), and `ExposedObjectHandle.ResolveFunction` resolves a function reached through a property path (a nested function on a member), mirroring how the REST layer resolves nested members.
- `ExposedProperty.TryGetValue<T>` / `TrySetValue<T>`: read and write value-type members without boxing, through typed delegates (`Func<object,T>` / `Action<object,T>`) emitted by the Source Generator.
- `ShowIf` / `HideIf` accept multiple conditions and can be placed on methods. Conditions serialize as a `visibilityConditions` array and are AND-evaluated on the client, so a member — or a function button — can be gated by several conditions at once. A single condition still emits the legacy `visibility` field, keeping the type output byte-compatible.
- `ImagePreviewAttribute`: a read-only control that polls a server-relative image URL and renders it (backs the Capture camera preview).
- `ExposedObjectRegistry.keyedCollectionVersion` and `NotifyKeyedCollectionChanged()`, so consumers can invalidate cached property resolutions when a keyed collection's elements are rebuilt and stop writes landing on stale keys.

### Changed

- Cut GC from the exposed property pipeline. `bool` / `enum` getters return canonical boxed instances via `BoxedValues`; `oldValue` reads and property-change events are skipped when nothing is subscribed; `FindProperty` / `GetProperty` are span-based (no `name.ToString()`); array-element descriptors are cached (`ExposedPropertyType.GetArrayElement`) and element lookup reads the collection once, building only the matched element; `PropertyPath` slash conversion and `AppendIndex` are allocation-free; `ExposedUnityObject.components` replaces its LINQ filter with a two-pass hand roll. REST responses and `scene.json` are unchanged.

## [0.24.3] - 2026-07-09
<!-- changelog-sha: 66ecbc810ebc2757306d12a4014067d834b7944b -->

### Added

- `AssetSelector` control: asset-reference fields serialize as asset GUIDs (`AssetRegistry`, baked in `OnValidate` and registered at runtime). `GET /api/asset?guid=` resolves a GUID to the asset's name and type so clients can label the choices. Sub-assets use a `guid:localId` composite key, and an optional `refProperty` carries scheme-prefixed external keys (e.g. `file:<path>#<sub>`) whose target may not be loaded yet; `AssetRegistry` gains name-fallback and display-name resolution so those unloaded keys can still be labelled.
- `GET /exposed/orphans` and `POST /exposed/orphans/remove`: root references a saved scene carries but whose object is absent this session are surfaced as removable "missing" items (path-carrying child overrides are excluded — they still bind once their asset loads asynchronously). Removing one drops every queued entry rooted at that id, so the next scene save stops re-emitting it.
- `RemoteErrorNotifier` forwards Unity errors (Error / Exception / Assert) to every connected remote app as an error notification. The broadcast is marshalled to the main thread (the server registry is mutated only there), identical messages are de-duplicated and bursts are capped so a per-frame exception cannot flood the app. Enabled by default (`RemoteErrorNotifier.enabled`).

### Changed

- The live-scene serializer no longer logs an ambiguous-restore warning for persisted-file entries. They carry no `@name` and cannot be disambiguated by type, so they are left unresolved for the remote app to present and delete instead of emitting a warning the user cannot act on.

## [0.24.2] - 2026-07-05
<!-- changelog-sha: 7a8804f7770e1b90b74b7719ca9885f2926f8d1f -->

### Changed

- **Breaking:** `GET /exposed/objects` and `GET /exposed/object/{id}` now return objects at **depth 1** by default. A nested inline (unregistered) composite child is emitted as a truncation stub `{ "@type": ..., "@truncated": true }` instead of being fully expanded, keeping the payload small and scalable as the object graph grows. Arrays do not consume depth, so element count and per-element type stay visible, and registered children keep their `@ref` form. Pass `?nested` (or `?nested=true`) to restore the previous unbounded expansion. Property reads (`GET /exposed/object/{id}/{path}`), SSE broadcasts, PUT responses and persistence (scene / project / preset) are unchanged — always fully expanded, so saved files stay byte-identical. Clients that walked nested values from the object list must either pass `?nested` or lazily fetch each truncated child via the property GET.

### Added

- `POST /exposed/batch` endpoint applies multiple object / property / function operations in a single request with per-item continue-on-error (each item's status and body are echoed back in order). The exposed REST API is now documented in `Documentation~/openapi.yml`.
- Per-member persist scope: `[ExposedField]` / `[ExposedProperty]` gain a `persistScope` (Scene default / Project), so the serializer can split live-scene state from per-class project settings.
- Exposed key-path addressing: a property path can target an array element by a stable `[ExposedKey]` value (e.g. `expressions[Joy].weight`), which backs the generic `SetPropertyAction` "bind to key" flow.
- `[Collapsed]` attribute: a hint that makes the remote app render an array or nested struct collapsed by default (the expand toggle remains). Emitted as a standalone `collapsed` flag, independent of the property controller.
- `elementTypeOptions` is emitted for polymorphic array properties so the remote app can offer "add an element of type …".
- A dedicated `GET /api/performance` endpoint reports the app's current FPS and process memory (sampled from the player loop via `PerformanceMonitor`; memory is read through the native `ProcessMemoryReader` on Windows), so the remote app can poll it only while an overlay is showing instead of piggybacking on other requests.

### Fixed

- `ExposedPropertyUtility.CreateDefaultElement` falls back to the first concrete `[ExposedClass]` subtype for abstract / interface element types, so adding an element to a polymorphic `[SerializeReference]` array no longer throws `MissingMethodException`.
- Constructing an `ExposedObjectHandle` no longer bakes an already-applied override value into its dirty-detection baseline. The constructor now uses `EnsureDefaultsCaptured` (capture only when unset, preserve an existing baseline) instead of an unconditional `SetDefault`, so an override captured by live-scene restore before the handle was registered is no longer overwritten with the current (applied) value and then lost on save.

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
