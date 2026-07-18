# Changelog

## [0.25.0] - 2026-07-17
<!-- changelog-sha: c7e861eafdda90dd7e573cda31b4e8ff62b2e076 -->

### Added

- `VirgoMotionSource.ResyncTiming`: an exposed function that clears the frame buffer and the frame offset so the next `Update` re-locks onto the sender's numbering. Sender (Fusion) and receiver (Studio) number frames from their own wall clocks, so a frame offset bridges the two; this also recovers from long stalls or clock jumps.

### Changed

- Fusion now ships as a Player (windowed) build that can embed the capture scene, so batch mode alone no longer identifies it: `FusionApp` always appends the `-fusion-child` / `-hidewindow` launch arguments and `FusionQuitSignalListener` gates on the marker (batch mode is still accepted for older headless builds).
- `BuildPostProcessor` bundles `Tools` only for `StandaloneWindows64`, so iOS and Capture builds no longer get `Tools` copied next to their output.

### Fixed

- The avatar no longer freezes after Fusion restarts. The sender's frame numbering drops to low values on restart while the buffer's `frameCount` only grows, pinning playback to a stale frame; `VirgoMotionSource` now auto-resyncs by clearing the frame buffer when the received numbering jumps far backwards. Frame-gap warnings are also limited to gaps wider than the interpolation search span.
- Studio now retries the `buildavatar` POST to Fusion until it lands, so a Fusion that starts after Studio still receives the avatar skeleton (previously the one-shot POST failed and the avatar had to be re-selected). The transient connection failure is logged once as a warning instead of an error, so launching Studio without Fusion no longer spams the console.

## [0.24.3] - 2026-07-09
<!-- changelog-sha: 66ecbc810ebc2757306d12a4014067d834b7944b -->

### Changed

- `VirgoMotionSource` resync and received-frame-gap messages are now plain logs instead of warnings; they occur under normal timing jitter, so demoting them removes recurring console noise.

### Fixed

- The pose frame's root valid byte now carries separate body (MediaPipe) and face (ARKit) tracking bits, driving the asymmetric avatar-visibility gate in `jp.lilium.livestudio` so the meshes no longer blink as the subject leaves the capture frustum.

## [0.24.2] - 2026-07-05
<!-- changelog-sha: 7a8804f7770e1b90b74b7719ca9885f2926f8d1f -->

### Changed

- `VirgoMotionSource` is now anchored to the capture-camera reference point on warp: on `WarpTo` the avatar is placed at the mark and the source transform is placed at `mark + up * height + forward * distance`, rotated 180° on Y so +Z faces the subject, pinning the shooting camera (cam0) there. With the real-world camera height / distance set, the avatar lands on the mark. `ResetCamera` is now camera-relative.
- The default `_cameraDistance` / `_cameraHeight` on `VirgoMotionSource` are now 0.7 m / 1.3 m.

### Fixed

- Incoming pose frames are now sampled with gap tolerance: the interpolation buffer searches nearby frames instead of requiring exact frame numbers, absorbing the periodic dropped frame caused by wall-clock quantization as well as occasional UDP drops. This removes the recurring motion spikes / stutter seen on the Capture→Fusion→Studio path.
- Capture reset now zeroes yaw correctly, so re-centering no longer leaves the avatar facing a residual direction.

## [0.23.5] - 2026-06-22

### Added

- The Readme inspector now verifies the project settings required for gamepad background input (Player Settings "Run In Background" and Input System "Background Behavior = Ignore Focus"). When either is disabled it shows a warning with a one-click button that enables both, so gamepad input keeps working in builds while the app is not the foreground window.

## [0.23.0] - 2026-06-21

### Changed

- `VirgoMotionSource` now interpolates between the two nearest received 60fps frames inside `Update` (with a configurable `_delaySeconds` lead so the interpolation target is already buffered), so the avatar advances smoothly under variable render fps and SpringBones no longer shiver on held frames. An optional `anchor` transform is used as the placement basis, letting the source live inside an additively loaded set.
- `VRCFTAvatar` expression is now routed through `ExpressionResolver`.

### Fixed

- `ResetCamera` now reads the capture-camera channel (device/ARKit yaw) instead of the pose-driven avatar root, which latched onto a jittery pose snapshot and made every reset land slightly differently; repeated resets are now stable. A new `_cameraHeight` field (default 1.5 m) lifts the virtual camera to eye level instead of dropping it to the floor.

## [0.22.0] - 2026-06-14

### Added

- The `AnimationFrameData` wire frame now carries a per-bone tracking presence array (`bonePresences`, 0..1), bridged into `HumanoidPoseData.bonePresences` so the Studio avatar can blend the mocap pose with its own animation per bone. `AnimationSystem.MakeAnimationFrameData` currently fills every tracked bone with presence 1 (full-body tracking); the synthesis side can lower individual bones to let their animation flow through.

### Changed

- The `AnimationFrameData` wire frame now carries two capture-camera channels (`cameras[2]`, one `CameraData` each) instead of a single camera, so the worldA / worldB cameras are bridged independently into the avatar pipeline.
- **Wire format change**: `AnimationFrameData` grew by the per-bone presence array and the second capture-camera channel, so its serialized size changed. The Fusion app and Studio validate frames by exact struct size, so the Fusion executable must be rebuilt for Studio to accept frames again (an older Fusion build is rejected as an invalid size).

## [0.21.2] - 2026-06-11

### Changed

- `FusionApp` now guarantees the Fusion process is terminated on shutdown: when the PID-keyed quit signal cannot be delivered (startup race or an orphan from a previous run), it hard-kills the process instead of leaving it lingering. Studio no longer blocks waiting for Fusion to exit.
- `BuildStudioApp` derives the build output folder and exe name from the project folder (VirgoMotionStudio / VirgoMotionStudioVRC), dropping the dedicated VRC build entry point and profile path. In batchmode it opens the first build scene to avoid lilToon recompiling all shader variants.

## [0.21.0] - 2026-06-08

### Changed

- The Fusion process is now hosted by a `FusionApp` component instead of the static `FusionAppHost`, making Fusion hosting configurable per scene.

## [0.20.11] - 2026-06-03

### Added

- Fusion is now asked to quit gracefully on Studio shutdown via a PID-keyed quit signal (`FusionQuitSignalListener`), so the windowless Fusion process saves its state instead of being hard-killed.

### Changed

- Studio no longer blocks on Fusion's exit when stopping it; the quit signal is sent and Studio returns immediately.

### Fixed

- The Readme inspector buttons are now clickable even when LiveStudioVirgo is consumed as an immutable git/registry dependency.

## [0.20.10] - 2026-06-02

### Added

- The capture UDP ports are now allowed through Windows Firewall automatically before Fusion launches (one-time UAC prompt). New `registerFusionFirewallRule` / `fusionCapturePorts` project settings control this.
- "Allow Capture Ports through Firewall" button in the Readme inspector for recovering when motion is not received, plus a description line above the optional packages list.

### Changed

- The "Allow Capture Ports through Firewall" action now reports its result (already allowed / added / failed) via a dialog instead of silently doing nothing when the rule already exists.

## [0.20.9] - 2026-06-02

### Added

- VirgoMotionSource reception indicator showing the live UDP packet reception state.
- Optional package installer with Install buttons in the Readme inspector.

### Fixed

- Replaced the UDP receive thread busy-wait with `Socket.Poll` to reduce idle CPU usage.

## [0.19.1] - 2026-05-07

### Added

- Initial release. Imported from the parent VirgoMotion repository as `jp.lilium.livestudio.virgo`. Provides the VirgoMotion-specific adapter layer (UDP `VirgoMotionSource`, Fusion REST `FusionRequestSystem`, `AnimationFrameBridge`, Build / Tools menu) on top of `jp.lilium.livestudio`.
