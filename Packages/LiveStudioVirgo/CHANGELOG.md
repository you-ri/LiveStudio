# Changelog

## [Unreleased]

### Added

- The `AnimationFrameData` wire frame now carries a per-bone tracking presence array (`bonePresences`, 0..1), bridged into `HumanoidPoseData.bonePresences` so the Studio avatar can blend the mocap pose with its own animation per bone. `AnimationSystem.MakeAnimationFrameData` currently fills every tracked bone with presence 1 (full-body tracking); the synthesis side can lower individual bones to let their animation flow through.

### Changed

- **Wire format change**: `AnimationFrameData` grew by the per-bone presence array, so its serialized size changed. The Fusion app and Studio validate frames by exact struct size, so the Fusion executable must be rebuilt for Studio to accept frames again (an older Fusion build is rejected as an invalid size).

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
