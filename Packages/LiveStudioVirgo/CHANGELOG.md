# Changelog

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
