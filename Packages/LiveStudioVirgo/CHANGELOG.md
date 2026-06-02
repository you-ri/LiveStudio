# Changelog

## [0.20.9] - 2026-06-02

### Added

- VirgoMotionSource reception indicator showing the live UDP packet reception state.
- Optional package installer with Install buttons in the Readme inspector.

### Fixed

- Replaced the UDP receive thread busy-wait with `Socket.Poll` to reduce idle CPU usage.

## [0.19.1] - 2026-05-07

### Added

- Initial release. Imported from the parent VirgoMotion repository as `jp.lilium.livestudio.virgo`. Provides the VirgoMotion-specific adapter layer (UDP `VirgoMotionSource`, Fusion REST `FusionRequestSystem`, `AnimationFrameBridge`, Build / Tools menu) on top of `jp.lilium.livestudio`.
