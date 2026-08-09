# Changelog

## [Unreleased]

- Added the package: normalized `SocialEvent` schema and the thread-safe `SocialEventHub` intake with frame-stable delivery and a bounded, counted drop policy.
- Added `SocialEventHandler`, the HTTP intake for external feeders: `POST /social/event`, `POST /social/events` and `GET /social/status`, with an optional `X-Social-Token` shared secret (compared in fixed time) and a 64 KB body limit.
- Added the OneComme (わんコメ) plugin under `Bridge~/onecomme-plugin/`: forwards comments and gifts from every platform OneComme supports, with a settings page for the port and token. Not imported by Unity.
- Added `SocialGateway`, the scene-level switch that attaches the intake to the scene's remote-control server and surfaces the port, the intake counters and the optional token in the remote app. It also drives the hub's frame pump, so `onEvent` subscribers work without an input source present.
- Added `SocialEventInputSource`, which fires an operation set from chat: filter by platform, event kind, keyword, tip amount and sender role, with a cooldown and an option to drive a slider tile from the tip amount. Help text ships in English, Japanese and Simplified Chinese.
