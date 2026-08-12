# Lilium Live Studio Social

Streaming-platform viewer events — chat, superchat, membership, follow, subscribe, raid — delivered into a [LiveStudio](https://github.com/you-ri/LiveStudio) app so they can drive operations.

This package is **receive-side only**. It never talks to YouTube, Twitch or any other platform: it defines a normalized event schema and an intake hub, and external feeders (OneComme, Streamer.bot, your own tool) push events in. No platform SDK, no third-party DLL, nothing that breaks when a platform changes its API.

- **`SocialEvent`** — one platform-neutral event. Open vocabulary for `source` / `type`, so an unknown platform still round-trips.
- **`SocialEventHub`** — thread-safe intake. Feeders enqueue from any thread; consumers read a list that is stable for the whole frame, so every consumer in a frame sees exactly the same events.
- **`SocialGateway`** — the scene-level on switch. Adds the HTTP endpoint to the scene's remote-control server and surfaces the port, the counters and an optional shared secret in the remote app.
- **`SocialEventInputSource`** — fires a deck operation from chat, configured entirely from the remote app: platform, event kind, keyword, tip amount, sender role, cooldown.
- **Bounded** — the queue holds 256 events. A flood drops the oldest and counts what was dropped; nothing is silently lost.

---

## Requirements

- Unity **2022.3** or newer
- `jp.lilium.livestudio`
- `jp.lilium.remotecontrol`

---

## Installation

This package lives inside the [LiveStudio](https://github.com/you-ri/LiveStudio) monorepo, so installation uses a Git URL with a `?path=` query.

In Unity, open `Window > Package Manager > + > Install package from git URL...` and paste:

```
https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudioSocial
```

Or add it to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "jp.lilium.livestudio.social": "https://github.com/you-ri/LiveStudio.git?path=/Packages/LiveStudioSocial"
  }
}
```

> **Versioning note**: every package in the LiveStudio monorepo shares the same `version`. Pinning `#v0.25.3` here also pins every other LiveStudio package you install at that release to a known-compatible set. See [LiveStudio README](https://github.com/you-ri/LiveStudio#versioning).

---

## Usage

### Turning intake on

Add a `SocialGateway` to the object list of the scene's `RemoteControlBehaviour`. It finds the server that component is already running, adds the routes below to it, and shows up in the remote app under **Social** with the port to point your feeder at, the counters, and the optional token. Remove it and the routes go away. There is no port to configure — a second copy of a number the scene already knows is a number that can disagree.

### Feeding events over HTTP

With a gateway in the scene, external tools can post events to it. Three routes:

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/social/event` | one event object | `{"accepted":true}` |
| POST | `/social/events` | array of events | `{"accepted":N,"rejected":M}` |
| GET | `/social/status` | – | `{"formatVersion":1,"totalReceived":n,"totalDropped":n,"queueCapacity":256}` |

```sh
curl -X POST http://127.0.0.1:3003/social/event \
  -H "Content-Type: application/json" \
  -d '{"source":"test","type":"chat","user":{"name":"tester"},"message":"!confetti"}'
```

Only `source` and `type` are required; everything else is optional. Unknown fields are ignored, so a feeder may send its own extras. Bodies are read as UTF-8. Errors are `400` (malformed JSON, or a missing `source` / `type`), `401` (bad token) and `413` (body over 64 KB). In a batch, a malformed entry is counted in `rejected` rather than failing the whole request — one bad comment should not cost a feeder the other 49.

`rejected` counts entries that never reached the hub. `totalDropped` on `/social/status` counts something different: events that did arrive and were later displaced by newer traffic in a full queue.

The full wire contract — every field, every status code, the guarantees a feeder can rely on and the policy that keeps it from breaking — is in [Documentation~/SocialEventAPI.md](Documentation~/SocialEventAPI.md). Read it before writing a feeder.

### Firing an operation from chat

Add a `SocialEventInputSource` to an operation set and configure it from the remote app — it shows up beside the key-binding input source with no extra setup. The filters are plain fields and all have to pass: platform, event kind, a keyword the message must contain, a minimum tip amount, and who may fire it (anyone / members / moderators). `Cooldown Seconds` ignores repeats for a while after firing, which is the answer to a viewer spamming a command.

What happens when it matches is decided by the tile the operation set carries:

| Tile | Behaviour |
|---|---|
| Button | The operation runs once per burst of matching chat. |
| Toggle | Each burst flips the set on/off. |
| Slider | With `Value From Amount`, the tip becomes the slider position and holds until the next event. `Amount Range` is the tip that counts as full. |

"Burst", not "event": a match makes the input read as pressed for that frame, and the operation fires on the edges — so matching comments on back-to-back frames read as one long press and fire once, the same way holding a key does. In ordinary chat that is one comment, one firing; in a flood it collapses, which is usually what you want from a confetti cannon. Set `Cooldown Seconds` when you want an explicit rate instead.

Leave `Value From Amount` off for button and toggle tiles — a held value reads as a key that is never released, so the tile would fire once and stay stuck on.

Anything compound — AND/OR, counting, raffles — belongs in the feeder. Streamer.bot and the OneComme plugin are logic engines already; let them decide and post the verdict as a `custom` event with a name your keyword catches.

### Connecting OneComme (わんコメ)

`Bridge~/onecomme-plugin/` is a ready-made [OneComme](https://onecomme.com/) plugin — copy the folder into OneComme's plugins directory, enable it, and point it at the port the gateway reports. One plugin covers every platform OneComme supports, because it reads OneComme's already normalized comment stream rather than talking to any platform directly. See its [README](Bridge~/onecomme-plugin/README.md) for the install steps and the event mapping.

`Bridge~` is not imported by Unity, so the plugin ships with the package without ending up in your build.

Set `Auth Token` on the gateway to require an `X-Social-Token` header on every route. It is empty by default, which is safe while the server is bound to loopback; set it before allowing external connections.

### Connecting Streamer.bot

[Streamer.bot](https://streamer.bot/) needs no plugin from us — it posts to the endpoint above with a sub-action you configure. Its built-in `Fetch URL` only does `GET`, so posting takes either a community curl action you import or a short C# block; both are written out in [Documentation~/StreamerBot.md](Documentation~/StreamerBot.md), along with which of its trigger variables map to which event fields.

That page is also where the delegation pattern is spelled out with worked examples — put the AND/OR, the counters and the raffles in Streamer.bot, and post the verdict as one `custom` event.

### Feeding events from C#

Any thread may enqueue. The hub timestamps the event and clamps oversized text for you.

```csharp
using Lilium.LiveStudio.Social;

SocialEventHub.Enqueue(new SocialEvent
{
    source  = SocialEventSources.YouTube,
    type    = SocialEventTypes.SuperChat,
    user    = new SocialUser { name = "viewer", isMember = true },
    message = "congrats!",
    amount  = 500f,
    currency = "JPY",
});
```

### Consuming events

Read `currentEvents` on the main thread. The list is swapped once per frame, so two consumers in the same frame always agree on what arrived:

```csharp
void Update()
{
    var events = SocialEventHub.currentEvents;
    for (int i = 0; i < events.Count; i++)
    {
        if (events[i].type == SocialEventTypes.Chat) { /* ... */ }
    }
}
```

Or subscribe once and be called per event, on the main thread, right after the frame's list is published:

```csharp
SocialEventHub.onEvent += e => Debug.Log($"{e.user.name}: {e.message}");
```

> The hub publishes lazily — a frame only advances when something reads `currentEvents`. In a scene that has a `SocialGateway` or an input source reading events that happens every frame anyway, but a subscriber on its own does not pump the hub. If `onEvent` is your only consumer, read `currentEvents` once per frame as well.

Every string field is non-null by the time you see it, and `user` is always present, so filters need no null checks. `SocialEventHub.totalReceived` / `totalDropped` expose intake counters for diagnostics.

---

## Documentation

| | |
|---|---|
| [SocialEventAPI.md](Documentation~/SocialEventAPI.md) | The wire contract. Schema, endpoints, status codes, limits, and the stability policy feeders are entitled to rely on. |
| [StreamerBot.md](Documentation~/StreamerBot.md) | Setting Streamer.bot up as a feeder, and the composite-logic delegation pattern. |
| [Bridge~/onecomme-plugin/README.md](Bridge~/onecomme-plugin/README.md) | Installing the bundled OneComme plugin and how its comments map to events. |

---

## License

Apache License 2.0 — see the [LICENSE](../../LICENSE) at the repository root.
