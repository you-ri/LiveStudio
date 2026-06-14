# Changelog

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
