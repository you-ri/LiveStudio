# Changelog

## [0.20.10] - 2026-06-02

### Added

- `WindowsFirewall` helper that registers inbound UDP allow rules via `netsh`. Rules are keyed by the listening port (not the program path), so a single rule survives the tool being relocated, reinstalled, or shipped from a different package-cache path. Elevation is requested only when the rule is missing.

## [0.20.9] - 2026-06-02

### Changed

- RemoteControl handlers are now registered per-instance with their routes declared in the constructor.

## [0.19.1] - 2026-04-29

### Added

- Initial release. Split out from `jp.lilium.virgo.studio` to separate VTuber-app generic infrastructure (Camera / Lighting / Scene / Build / RemoteControl base / shared Localization) from VirgoMotion-specific motion reception and avatar control.
