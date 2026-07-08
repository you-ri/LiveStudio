# Changelog

## [0.24.3] - 2026-07-09
<!-- changelog-sha: 66ecbc810ebc2757306d12a4014067d834b7944b -->

### Added

- `AvatarController` can drive body parts the capture does not track with an override `AnimationClip`. The clip is chosen from Inspector presets or from an external animation bundle (`*.anim.lsb`, which packs several clips) via a unified asset key — a bare GUID for baked assets, `file:<path>#<clip>` for external ones — resolved lazily and re-applied after scene restore and REST writes so the selection survives the bundle's asynchronous load. VRM1 / VRCFT playback is supported.
- A "lock lower body pose" toggle pins the hips and root to the capture anchor with foot IK, holding the lower body in place while the upper body keeps following the capture.
- Per-project session log: the Unity log is mirrored into `{project}/.livestudio/log` as one file per session, alongside the native Player.log. `ProjectPaths` gains `GetLogDir` / `EnsureLogDir` helpers.

### Changed

- Operation set manual hold and manual value now persist across scene reloads (previously both reset). On restore the edge baseline is seeded to the restored hold, so reloading a scene with a set left on no longer manufactures a rising edge that would re-fire the operation.

### Fixed

- Avatar meshes no longer blink while the subject leaves the capture frustum. The avatar root's valid byte is now a body/face tracking bitmask (body = MediaPipe, face = ARKit) and visibility is asymmetric — hidden only when both signals drop, shown on the face rising edge — so the frame-to-frame body-validity beat can no longer flicker the meshes.

## [0.24.2] - 2026-07-05
<!-- changelog-sha: 7a8804f7770e1b90b74b7719ca9885f2926f8d1f -->

### Added

- The editor gains a toolbar button to launch and close the Remote app, matching the runtime RemoteAppHost (same configured path / args and child-process handling).
- `LookAtCameraController` (`ICameraController`) assigns a bone — resolved from a `TransformRef` (target owner name + bone name / path) — to a camera's `LookAt` target, re-resolving only when the reference changes or the avatar is swapped, so a look-at bone survives avatar reloads. Only `LookAt` is touched, leaving the `Tracking` target and any Aim component intact.
- `BoneFollower` component drives its own transform to follow a bone resolved from a `TransformRef`, re-resolving on avatar swap. It bridges a swappable avatar bone to a Cinemachine camera authored in a separate stage / set bundle (outside the LiveStudio camera pipeline), where a direct cross-scene reference to the bone cannot survive the swap.
- `AvatarController` gains an avatar animation layer override, letting a loaded avatar's layer be overridden per scene.
- `ProjectManager` exposes selected / unselected camera-preview polling interval settings (per-project persist scope), controlling how often camera previews are refreshed.

### Changed

- Value-mode Deck sliders now map their drag onto the source property's min/max range instead of always writing the raw normalized 0..1 value, so a slider bound to a property spanning e.g. 0..100 can drive the full range. The normalized value is retained internally so gauges and edge detection are unaffected.
- Loaded prop / avatar objects are now persisted through the live scene's top-level diff as the single source of truth. State whose owning asset has not finished loading is queued and bound once the asset arrives, and any entry that never binds during a session is preserved verbatim across a load→save cycle instead of being warned about and dropped.

### Fixed

- Operation sets authored directly in a scene or prop bundle now have a stable id backfilled on init and restore. Previously they kept the default empty id, so every id-addressed function was a silent no-op and the set was skipped when rebuilding the input map.
- Multiple single-prefab props that shared the same internal AssetBundle CAB could collide when loaded concurrently ("same files already loaded"). Bundle opens are now serialized, and each built bundle is given a unique internal CAB seeded from its source asset GUID so props no longer share one.
- Loaded prop / avatar overrides (e.g. a prop moved from its authored position) are no longer lost when the live scene reloads. The load-complete re-baseline now preserves restore-applied overrides instead of folding the applied value back into the dirty-detection baseline.

## [0.24.1] - 2026-06-30
<!-- changelog-sha: 7c999a055f01c038e5793031dd7b3efcabf17d16 -->

### Added

- Operation sets can now be arranged on a **Deck**: a grid of push-button / toggle / slider controls, freely placed and editable from the remote app. The control type is chosen when a control is placed and is independent of the operation set's input mode.
- Value-mode operation sets gain a manual value override, so a slider control can drive a 0..1 value directly (throttled to the studio).

### Changed

- The action system was renamed to **Operation** throughout (`ActionManager` → `OperationManager`, `ActionSet` → `OperationSet`, `ActionBase` → `OperationBase`, and the concrete `*Action` types to `*Operation`); the remote app's "Actions" page is now "Operation". Existing live scenes and key bindings keep loading via compatibility aliases.
- The operation "panel" concept was renamed to **Deck** throughout, and an operation set's behaviour is now driven by a single control axis. Deck tile width is declared per control kind, missing deck panels are recreated automatically, and tile-layout fields are hidden from the generic remote editor.

### Fixed

- Expression weights can now be read while the avatar is inactive.
- `KeyInputSource`'s control path is stored as a shadow field of its binding, so key bindings persist and resolve correctly.
- The active live-scene path is re-resolved from startup state when entering play mode, so the last-opened scene restores reliably.

## [0.24.0] - 2026-06-26

### Added

- Action set / input binding system (`ActionManager` / `ActionSet` / `InputSource` / `ActionBase`), editable from the remote app: bind a keyboard or gamepad input to an ordered list of actions (set an expression weight, switch avatar / stage, toggle a GameObject). Polymorphic input/actions use `[SerializeReference]` + `[TypeSelector]`; input modes are Button / Toggle / Value. Wired into the `Live Studio System` prefab.
- `InvokeFunctionAction` and `SetPropertyAction` back a generic "bind to key" affordance next to any `[ExposedFunction]` or bool/float control (addressed by stable id), replacing feature-specific bindings.
- Mutually exclusive toggle groups for action sets: sets sharing a non-empty `group` whose input is in Toggle mode behave as a radio group (turning one on clears the others; all-off is still allowed).
- `SwitchStageAction` performs a complete stage switch (`SwitchToSetByName` unloads the other loaded sets and loads the target on demand); the Stage page's `SetActiveSet` keeps its original non-exclusive selection behavior.
- `AvatarChair` component for a chair prop the avatar sits on. It re-parents under the `AvatarController` so avatar root motion does not drag it, then makes its parts track the avatar's pelvis (Hips socket) relative to a recorded rest pose: `swivel` / `recline` rotate a target transform about an authored local axis (hips yaw / pitch), and `lateral` / `height` / `depth` translate one along an axis (hips X / Y / Z). The rest pose is recorded via an `Activate` action. Each axis (a `ChairAxis`) has an operating range (`min`/`max`, `0/0` locks it), a deadzone that is the play/backlash between the source and the follow target (absorbs hips tremor), and damping (`SmoothDampAngle` for rotation).
- Per-project state and project-scoped settings: exposed members gain a persist scope (Scene / Project); project-scoped values are stored in per-class `{Class}.settings.json` (screen output size/fullscreen/Spout and live-scene quality are now Project scope). `StartupStateStore` remembers the last opened live scene per project (`Settings/startup.json`), and `StartupSceneSwitcher` redirects to it before the first scene load.
- Two-tier (memory + file) thumbnail cache stored under a hidden `.livestudio` project folder (`ProjectPaths`).

### Changed

- **Breaking:** the avatar-prop `AvatarProp` component was split into composable siblings: a new shared `Prop` (keeps the exposed name `"Prop"`; owns the socket follow + position/rotation/scale offsets) plus a behavior component. `AvatarProp` was renamed to `AvatarItem` (the avatar→prop parameter bridge + expression driving); a `[MovedFrom]` keeps old type references resolving. Existing `*.prop.lsb` bundles carry only the old single component and therefore **must be re-exported** (a re-exported bundle root carries both `Prop` and `AvatarItem`); the live-scene `"Prop"` state key is unchanged.
- Expression key bindings are now driven generically through the action system: a `SetPropertyAction` writes `expressions[name].weight` (the data-driven expression slot), so the bespoke `SetExpressionAction` / `ExpressionBindingSystem` and the old `/api/expressions` bind routes were removed. Existing expression key bindings must be re-created.
- Shared `FormatHeader` unifies versioning across live-scene / project-settings / preset files (raw-JSON serialization, orphan prune).
- Object presets (`.preset.json`) now capture prop and avatar state as a delta with a lenient reader.
- Button-mode one-shot actions (switch avatar / stage) now commit on key release instead of press (`ActionContext.triggered`).
- The `ActionManager`, `ExternalAssetManager`, and `StageManager` singletons are now hidden from the remote app's generic scene object list (`HideInScene`); they remain reachable through their dedicated pages.

## [0.23.6] - 2026-06-23

### Added

- `AvatarProp` now exposes a `scaleOffset` (Vector3, default `1,1,1`) that multiplies the prop's authored local scale, so an avatar-attached prop's size can be tuned from the remote app / inspector. It is applied every frame and persists across an unload/reload like the position/rotation offsets.

### Changed

- Stage marks now warp the avatar through `StageManager` instead of the standalone `AvatarStageController`. Each `SetBundleEntry` exposes the `StageMark` labels in its own set scene (the new `marks` field, scoped per set), and `StageManager.WarpTo(setId, markLabel)` moves the avatar's anchor to the named mark within that set's scene (an empty label warps to the origin).

### Removed

- **Breaking:** `AvatarStageController` (the "Avatar Placement" exposed object with the `place` / `availablePlaces` dropdown of all loaded marks) was removed; warping is now per-set on `StageManager.WarpTo` (see above). It is also dropped from the `Live Studio System` prefab.

## [0.23.5] - 2026-06-22

### Changed

- Object preset files (`*.preset.json`) now use the `jp.lilium.remotecontrol.preset` format identifier with a `formatVersion` field, aligned with the live-scene file format. The redundant `sourceKind` hint was dropped (it is derived from the source path).

### Removed

- Backward-compatible reading of the legacy `prop.preset` preset format; presets saved before this change must be re-saved.

## [0.23.1] - 2026-06-21

### Fixed

- The Studio no longer reports unsaved changes on every launch (which blocked quit with an unsaved-changes dialog when a project folder contained files). Persisting the `ExternalAssetManager` asset array directly let the deferred project-folder crawl populate it after the live-scene save baseline was captured; the in-use-only persistence shadow is restored so the crawl-built catalog stays out of the saved/dirty state.

## [0.23.0] - 2026-06-21

### Added

- `ExternalAssetManager`: a single runtime catalog that loads and unloads props, avatars and set bundles uniformly (replacing the old `PropManager`). Avatar selection, prop loading and set-bundle management all flow through it.
- Runtime avatar props: a `.prop.lsb` bundle can be attached to an avatar at runtime, following a bone/socket through `AvatarProp`. Socket attachment is now robust to the avatar's rig and scale, and a prop's exposed-object parameters and id persist across unload/reload.
- Prop presets: a loaded prop's tweaked parameters can be saved as a `.preset.json` (`SaveAsPreset`) and re-loaded as a new prop, storing only the delta from the prop's defaults.
- Project asset catalog with live-scene assets and bundle thumbnails; exported `.prop.lsb` / `.avatar.lsb` bundles embed a thumbnail used by the remote app's asset cards.
- Avatar VRM thumbnail served over `/api/avatar/image?id=`, extracted from the VRM's embedded thumbnail without a UniVRM dependency.
- Cross-scene remote control: additively loaded set bundles expose their objects to the single persistent host server. New stage marks (`StageMark` / `StageMarkRegistry` / `AvatarStageController`) warp the avatar to named marks (plus a built-in Zero origin) from the remote app, and the saved active scene is restored on load.
- Adding an asset now imports it into the project: `AddAsset` copies the picked file into a kind-specific subfolder of the open project folder (`Avatars` / `Props` / `Sets`, resolved per asset kind, colliding names get a ` (n)` suffix) and registers the in-project copy. A picked file already inside the project folder is registered in place without copying. Only the single picked file is copied for now.
- A project folder is the home for all saved data: on first launch `Documents/<brand>/<project>` is created and opened, it is used as the default Save As location for live scenes, and "Open Save Folder" opens it.

### Changed

- The remote-control host and its managers now stay alive across base-scene reloads instead of being torn down and recreated, so remote connections and manager state survive scene switches; the Studio prefabs and template were reworked around this persistent host.
- `VRCFTAvatar` expression now flows through `ExpressionResolver`, and converted avatars auto-gain hand-gesture expressions.
- Received poses are now time-interpolated between the two nearest received 60fps frames during the variable-fps render update (`AvatarAnimationSystem.Lerp`), so the avatar advances smoothly and SpringBones no longer shiver on held frames.
- **Breaking:** Renamed the scene-bundle concept to "set" and the world concept to "stage", aligning the vocabulary with virtual-production terms (a stage holds sets, props and marks). Set bundles are now exported as `.set.lsb`; `SceneBundleAsset` → `SetBundleAsset`, `SceneBundleLoader`/`LoadedSceneBundle` → `SetBundleLoader`/`LoadedSetBundle`, `SceneBundleExporter` → `SetBundleExporter` (menu "Export Set Bundle (.set.lsb)"), `LiveStudioBundle.SceneExtension`/`IsSceneBundle` → `SetExtension`/`IsSetBundle`, and `WorldManager` → `StageManager` (exposed functions `AddSet`/`RemoveSet`/`SetActiveSet`, exposed property `sets`). The legacy `.scene.lsb` extension is still accepted on input (loaded as a `SetBundleAsset`), no longer produced on export. Saved live scenes referencing the old `@type` names (e.g. `SceneBundleAsset`/`WorldManager`) are not migrated and must be re-saved; such unresolved entries now deserialize away instead of leaving a null hole. `StageManager` carries `[MovedFrom("WorldManager")]` so in-scene `[SerializeReference]` wiring migrates automatically.
- `ExternalAssetManager` now persists its asset array directly to the live scene; the project-folder crawl rebuilds the disabled catalog on load, so the separate persistence shadow was removed.
- `SavedPaths` derives its base directory from the configured brand name and organizes saved data into per-project folders, replacing the single `Scene` subfolder and the standalone `LiveStudioPathsInitializer` (folder setup now lives in `ProjectManager`).

### Fixed

- `ResetCamera` now derives its yaw/position offset from the capture-camera channel instead of the pose-driven avatar root, so repeated resets are stable; a configurable camera height (default 1.5 m) keeps the virtual camera at eye level rather than dropping to the floor.
- Arms Post Rig is now added only for `VRM1Avatar`.
- Stage/scene visibility and active state are restored correctly on startup, and the persistent active entry settles at restore so the save baseline matches the post-restore state (no false unsaved-changes prompt at quit).
- A runtime-loaded avatar re-applies its animation parameter overrides one frame after its `PlayableGraph` builds, instead of reverting to the controller defaults; `VRMAvatarSetupSystem` skips adding a duplicate facial controller when an `IAvatar` already exists.

## [0.22.0] - 2026-06-14

### Added

- Scene bundle import: a `.scene.lsb` AssetBundle can now be loaded additively at runtime. New `RuntimeSceneManager` and `SceneBundleLoader` components plus a shared `BundleBuildUtility`, with `LiveStudioBundle` detecting the compound extension so `.scene.lsb` (scene) and `.avatar.lsb` (avatar) bundles share one pipeline.
- `CaptureCameraController` exposes a `channelIndex` property (with `CAMERA_CHANNELINDEX` localization), so a `CaptureCameraTracker` can choose which capture-camera channel (0 or 1) it follows.
- `HumanoidPoseData` now carries a per-bone tracking presence (`bonePresences`, 0..1). `AvatarBodyDriver`'s pose job blends the mocap pose over the avatar's animation per bone by this weight: a bone with presence 1 takes the mocap rotation fully, presence 0 leaves the animation flowing through, and values in between slerp between the two. This lets untracked body parts keep playing their `AnimatorController` animation while tracked parts follow the mocap.

### Changed

- `ExternalAvatarSource`'s file selector now filters the compound `.avatar.lsb` extension (was `.lsb`) to distinguish avatar bundles from `.scene.lsb`; the legacy `.lsavatar` extension is still accepted.
- Avatar frames now carry multiple capture-camera channels per frame (one `CameraData` per channel), so the worldA / worldB cameras are conveyed independently.
- `VRM1Avatar` and `VRCFTAvatar` now drive body animation through a `PlayableGraph` (new shared `AvatarBodyDriver`) instead of writing the humanoid bone transforms directly every frame. An optional `AnimatorController` on the avatar is wrapped into the graph: its animation (e.g. an idle or range-of-motion clip) plays through while tracking is lost and is overwritten by the mocap pose while tracking. Shared tracking-state handling, mesh visibility, and the avatar-build boilerplate were extracted into `AvatarBodyDriver` and `AvatarBuildNotifier.BuildAndNotify`.

### Fixed

- `VRM1Avatar` / `VRCFTAvatar` now resolve their `Animator` lazily in `BuildAvatar`, which can run before `Start` during a synchronous `.lsavatar` load and previously left the animator null and logged an error.
- `AvatarBodyDriver` now disables `applyRootMotion` on the wrapped `Animator`, so a controller's root motion no longer sinks the whole avatar to the floor (VRCFT avatars).

## [0.21.3] - 2026-06-12

### Changed

- The exposed `Light`'s shadow toggle now enables soft shadows (`LightShadows.Soft`) instead of hard shadows, both in the property setter and on deserialize, for smoother shadow edges.

## [0.21.2] - 2026-06-11

### Added

- `ChildProcessHost.RequestCloseAndRelease` posts WM_CLOSE to a windowed child without a quit signal and releases it immediately, without waiting for exit.

### Changed

- Quitting Studio no longer blocks waiting for child processes to exit. The Fusion process is signaled to quit and released immediately (`RequestStopAndRelease`), and the Remote app is asked to close (WM_CLOSE) and released immediately (`RequestCloseAndRelease`), instead of each blocking up to 5 seconds on `WaitForExit`.
- The Studio build output folder and exe name are now derived from the project folder name, dropping the dedicated VRC build entry point and the App VRC build profile path.

### Fixed

- `SkyboxBackground` now logs an error instead of propagating a null shader when the Skybox/Cubemap shader is stripped from a player build (it runs during `ExposedObjectContainer.Initialize` and must not throw).

## [0.21.1] - 2026-06-09

### Added

- `ExternalAvatarSource` can now import avatars from AssetBundle files (`.lsavatar`) in addition to VRM, chosen automatically by file extension.

### Changed

- Renamed `VRMAvatarSource` to `ExternalAvatarSource` (the former exposed name is preserved for existing scenes). The avatar load API `RequestLoadVRM` was renamed to `RequestLoad` across `IAvatarService`, `AvatarController`, and `AvatarService`.
- Generalized the model file field label and scoped its help text to VRM-only guidance.

## [0.21.0] - 2026-06-08

### Fixed

- Windows builds could leave a lingering background process after quitting, caused by a native shutdown hang related to Windows.Gaming.Input. A watchdog (`QuitTerminationGuard`) now force-terminates the process if graceful shutdown stalls.

## [0.20.12] - 2026-06-04

### Added

- Eye gaze is now delegated to the VRM10 `LookAt` component, so the avatar's eyes follow the configured look-at target instead of being driven directly.

## [0.20.11] - 2026-06-03

### Added

- `ChildProcessQuitSignal`: signals a specific child process (keyed by its PID) to quit gracefully via a Windows named event, so a windowless child can run its own save-on-quit instead of being hard-killed.
- "Assets/Lilium Live Studio/Remove Missing Scripts" context-menu command that removes missing scripts from the selected prefab assets.

### Changed

- Stopping a windowless child process now signals it to quit and returns immediately without waiting for it to exit (`ChildProcessHost.RequestStopAndRelease`).

### Fixed

- The package Readme inspector buttons (language toggle, section actions, Install) are now clickable even when the package is consumed as an immutable git/registry dependency.

## [0.20.10] - 2026-06-02

### Added

- `WindowsFirewall` helper that registers inbound UDP allow rules via `netsh`. Rules are keyed by the listening port (not the program path), so a single rule survives the tool being relocated, reinstalled, or shipped from a different package-cache path. Elevation is requested only when the rule is missing.

## [0.20.9] - 2026-06-02

### Changed

- RemoteControl handlers are now registered per-instance with their routes declared in the constructor.

## [0.19.1] - 2026-04-29

### Added

- Initial release. Split out from `jp.lilium.virgo.studio` to separate VTuber-app generic infrastructure (Camera / Lighting / Scene / Build / RemoteControl base / shared Localization) from VirgoMotion-specific motion reception and avatar control.
