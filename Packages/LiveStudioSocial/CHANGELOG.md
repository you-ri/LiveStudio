# Changelog

## [Unreleased]

- Added the package: normalized `SocialEvent` schema and the thread-safe `SocialEventHub` intake with frame-stable delivery and a bounded, counted drop policy.
- Added `SocialEventHandler`, the HTTP intake for external feeders: `POST /social/event`, `POST /social/events` and `GET /social/status`, with an optional `X-Social-Token` shared secret (compared in fixed time) and a 64 KB body limit.
- Added the OneComme (わんコメ) plugin under `Bridge~/onecomme-plugin/`: forwards comments and gifts from every platform OneComme supports, with a settings page for the port and token. Not imported by Unity.
- Added `SocialGateway`, the scene-level switch that attaches the intake to the scene's remote-control server and surfaces the port, the intake counters and the optional token in the remote app. It also drives the hub's frame pump, so `onEvent` subscribers work without an input source present.
- Added `SocialEventInputSource`, which fires an operation set from chat: filter by platform, event kind, keyword, tip amount and sender role, with a cooldown and an option to drive a slider tile from the tip amount. Help text ships in English, Japanese and Simplified Chinese.
- Added `Documentation~/SocialEventAPI.md`, the wire contract external feeders are written against: schema, endpoints, status codes, intake guarantees, limits, and the additive-only stability policy.
- `POST /social/events` now converts each entry on its own instead of deserializing the array in one call. One entry of the wrong shape — a number, or a field carrying the wrong JSON type — used to throw and fail the whole request, which is the opposite of what the endpoint promises; it is now counted in `rejected` and its well-formed siblings are accepted.
- An explicit JSON `null` is now read as an omitted field on every optional field, `amount` and the role flags included. Serializing unset members as `null` is common enough that rejecting it made "optional" untrue in practice. The reader's settings are also spelled out rather than inherited from `JsonConvert.DefaultSettings`, a process-global a consuming project could otherwise change the wire contract through.
- Added `Documentation~/StreamerBot.md`: connecting Streamer.bot as a feeder — its built-in `Fetch URL` is `GET`-only, so both the imported curl-utility route and a C# `HttpClient` recipe are given — plus its trigger-variable mapping and the composite-logic delegation pattern.
