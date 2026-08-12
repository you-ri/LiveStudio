# Social Event API

The contract between LiveStudio and any external feeder that wants to drive it from streaming chat.

`jp.lilium.livestudio.social` never connects to YouTube, Twitch or anything else. It defines the shape of
an event and a place to post it; a feeder — [OneComme](../Bridge~/onecomme-plugin/README.md),
[Streamer.bot](./StreamerBot.md), or a tool you write — does the platform-specific half and posts the
result here. That split is the whole
design: when a platform breaks its API, the feeder is what needs fixing, and it is not shipped inside
anybody's Unity project.

This document is normative. If the implementation and this page disagree, that is a bug in one of them.

---

## Stability

The schema and the endpoints only ever grow.

- **Fields are added, never removed and never repurposed.** A feeder written against version 1 keeps
  working against every later version without being touched.
- **Unknown fields are ignored,** in both directions. A feeder may send extras it finds useful; they cost
  nothing and are dropped on arrival. A feeder must not fail on fields it does not recognize in a response.
- **Vocabularies are open.** `source` and `type` are strings, not enumerations. The values listed below are
  the well-known ones, not the allowed ones — an unheard-of platform round-trips untouched and can be
  matched by name in the remote app.
- **Status codes and paths are fixed.** New paths may appear; existing ones do not change meaning.

`GET /social/status` reports `formatVersion`, currently **1**. It is bumped only if some change cannot be
expressed as an added field — which the rules above are designed to make very unlikely. A feeder that wants
to be defensive can read it once at startup and warn; it should not refuse to run on a number it does not
know, since a higher version still accepts version 1 requests.

---

## Transport

Plain HTTP/1.1 on the app's remote-control server. There is no separate port and no separate process: the
intake is registered onto the server the scene is already running, and the port it landed on is reported
by the `SocialGateway` object in the remote app as **Server Port** (3003 in a stock Studio build).

- Bodies are read as **UTF-8**, whatever `Content-Type` claims. Sending `application/json` is good manners
  and is ignored.
- The server binds to loopback unless the app is configured to accept external connections. See
  [Authentication](#authentication) before opening it up.
- Requests are answered on a worker thread without waiting for a frame, so a feeder is never blocked by the
  app's frame rate. It is still fire-and-forget by nature — a feeder should not make delivery of a comment
  to its own UI depend on this call succeeding.

If nothing is listening, the connection is refused. A `SocialGateway` in the scene is what puts the routes
there; without one the same server answers `404`.

---

## Authentication

Optional, off by default, and a single shared secret.

Set **Auth Token** on the `SocialGateway` and every route then requires that value in an
`X-Social-Token` header. Empty (the default) leaves the intake open, which is what you want while the
server is on loopback and is not what you want the moment it is not.

```
X-Social-Token: your-secret-here
```

Notes a feeder author should know:

- **All three routes are guarded, `/social/status` included.** The counters describe traffic on a protected
  endpoint, so they are not a way around the token.
- **The comparison runs in fixed time.** Length is allowed to leak; how far a wrong token matched is not.
- **A wrong or missing token is `401`,** not `403`. Strictly, a `401` should carry a `WWW-Authenticate`
  header and this one does not — but every API-key scheme in practice answers `401`, and matching that
  expectation is worth more than the letter of RFC 9110 here.
- **Browser-based feeders cannot send it.** The server answers CORS preflight itself, from its own
  allow-list, before any of this code runs — and that list does not include `X-Social-Token`. Feeders that
  are not browsers (a Node.js plugin, Streamer.bot, curl) are not subject to CORS and are unaffected. If
  you are writing a feeder that runs in a page, leave the token empty and keep the server on loopback.

---

## Endpoints

| Method | Path | Body | Success |
|---|---|---|---|
| `POST` | `/social/event` | one event object | `200` `{"accepted":true}` |
| `POST` | `/social/events` | array of event objects | `200` `{"accepted":N,"rejected":M}` |
| `GET` | `/social/status` | – | `200` `{"formatVersion":1,"totalReceived":n,"totalDropped":n,"queueCapacity":256}` |

### POST /social/event

One event. Rejected as a whole if the JSON is malformed or `source` or `type` is missing.

```sh
curl -X POST http://127.0.0.1:3003/social/event \
  -H "Content-Type: application/json" \
  -d '{"source":"test","type":"chat","user":{"name":"tester"},"message":"!confetti"}'
```

```json
{"accepted":true}
```

### POST /social/events

An array. **Prefer this whenever you have more than one event to send** — feeders that receive comments in
batches (OneComme does) should forward the batch as one request rather than looping.

A malformed entry is counted and skipped; it does not fail the request. A feeder batching 50 comments
should not lose 49 good ones to one bad one.

```sh
curl -X POST http://127.0.0.1:3003/social/events \
  -H "Content-Type: application/json" \
  -d '[{"source":"youtube","type":"chat","message":"hi"},{"source":"youtube","type":"superchat","amount":500,"currency":"JPY"}]'
```

```json
{"accepted":2,"rejected":0}
```

`rejected` counts entries that **never reached the app**: an entry that was missing `source` or `type`, was
not an object at all, or carried a field of the wrong JSON type (`"amount":"lots"`). It is a different
thing from `totalDropped` below, which counts events that did arrive and were later displaced by newer
traffic. If `rejected` is non-zero, the feeder has a bug — the response says how many, not which, so find
it against a single-event POST where the `400` names the field.

The body as a whole must be a JSON array. A single object posted here is `400`; use `/social/event`.

### GET /social/status

Liveness and intake counters. Useful as a "is the app there and listening" probe.

```sh
curl http://127.0.0.1:3003/social/status
```

```json
{"formatVersion":1,"totalReceived":42,"totalDropped":0,"queueCapacity":256}
```

| Field | Meaning |
|---|---|
| `formatVersion` | Version of this contract. See [Stability](#stability). |
| `totalReceived` | Events accepted since the app started, including any later dropped. |
| `totalDropped` | Events discarded because they arrived faster than the app consumed them. |
| `queueCapacity` | How many events can be buffered between two frames. |

`totalDropped` climbing means a feeder is flooding — see [Limits](#limits). The counters reset when the app
restarts and are not persisted.

---

## Status codes

| Code | When |
|---|---|
| `200` | Accepted. For a batch, check `rejected`. |
| `400` | Body is empty, is not valid JSON, is not the shape the route expects, or (single events) is missing `source` or `type`. |
| `401` | `X-Social-Token` missing or wrong, and a token is configured. |
| `404` | No `SocialGateway` in the scene, so the routes were never registered. Body is empty — this one comes from the server, not from here. |
| `405` | Right path, wrong method — e.g. `POST /social/status`, or any method other than GET, POST and OPTIONS. |
| `413` | Body over 64 KB. |
| `500` | An unhandled fault in the server. Body is empty. Not something a well-formed request should be able to cause; if you can reproduce one, it is a bug worth reporting. |

`OPTIONS` on any path is answered `200` by the server's preflight handling before a handler is reached, so
it is never `405` and never carries a token check.

Errors this endpoint raises itself carry an English message prefixed with `[Social]`:

```json
{"error":"[Social] Event is missing the required field 'source'."}
```

`405` is answered by the shared handler base and reads `{"error":"Method not allowed"}` without the prefix.
Treat any message as diagnostic text for a human, not something to parse. The status code is the contract.

---

## The event

```json
{
  "source": "youtube",
  "type": "superchat",
  "id": "evt-abc123",
  "user": {
    "id": "UCxxxx",
    "name": "Viewer A",
    "isModerator": false,
    "isMember": true,
    "isOwner": false
  },
  "message": "congrats!",
  "amount": 500,
  "currency": "JPY"
}
```

| Field | Type | Required | Meaning |
|---|---|---|---|
| `source` | string | ✔ | Platform identifier. Open vocabulary. |
| `type` | string | ✔ | Event kind. Open vocabulary. |
| `id` | string | – | Feeder-assigned unique id. **Reserved**: nothing reads it yet. Send it anyway — receiver-side de-duplication is a planned use, and an event stream that already carries stable ids can adopt it for free. |
| `user.id` | string | – | Platform-scoped user id. Not unique across platforms. |
| `user.name` | string | – | Display name, as the platform renders it. Untrusted plain text. |
| `user.isModerator` | bool | – | Default `false`. |
| `user.isMember` | bool | – | Channel member / subscriber. Default `false`. |
| `user.isOwner` | bool | – | The broadcaster. Default `false`. |
| `message` | string | – | Chat body, or the outcome name for a `custom` event. Truncated at 1000 characters. |
| `amount` | number | – | Money, bits or gift count. Default `0`. |
| `currency` | string | – | Unit of `amount`, e.g. `JPY`, `USD`, `bits`. Never converted. |

Every optional field may be **omitted or sent as JSON `null`** — the two mean the same thing, including
for `amount` and the role flags, where a null would otherwise have no value to fall back on. Serializing
your unset members as `null` is the default in plenty of libraries and it is not worth making anyone work
around.

`timestamp` is **accepted and discarded**. The app stamps its own arrival time, because that is the clock
every consumer inside it shares; a sender's wall clock would be one more thing that can be wrong. Send it
if your JSON already has it — it costs nothing, and if a reason to keep it ever appears it can be added
without breaking anything.

### Well-known `source` values

`youtube` · `twitch` · `niconico` · `twitcasting` · `kick` · `bilibili` · `showroom` · `streamerbot` · `test`

Lower case by convention; matching in the remote app is case-insensitive. Use `test` for smoke traffic so
it can be told apart from a real audience. A feeder that aggregates several platforms should report the
**originating platform**, not itself — `youtube`, not `onecomme` — so a filter written for YouTube keeps
working when the user changes which tool feeds it. `streamerbot` is the exception and exists for events
that genuinely originate in the feeder's own logic (see [Composite logic](#composite-logic)).

### Well-known `type` values

| Value | Meaning |
|---|---|
| `chat` | An ordinary comment. |
| `superchat` | A paid, highlighted comment. `amount` + `currency`. |
| `sticker` | A paid sticker. YouTube's Super Sticker. |
| `membership` | Someone joined or renewed a channel membership. |
| `gift` | A gifted membership/sub, or a platform gift item. `amount` is the count where the platform gives one. |
| `follow` | A new follower. |
| `subscribe` | A Twitch-style subscription. |
| `cheer` | Twitch bits. `amount` is the bit count, `currency` is `bits`. |
| `raid` | An incoming raid. `amount` is the viewer count where known. |
| `custom` | A verdict decided by the feeder. See below. |

The line between `membership` and `subscribe` is a judgement call every feeder has to make and then
document. YouTube's "new member" is `membership`; Twitch's subscription is `subscribe`. What matters is
that a given feeder is consistent, so a filter set up once keeps meaning the same thing.

### Composite logic

There is deliberately no way to express "if a moderator says X **and** the total this stream exceeds Y" in
this package. Conditions on a `SocialEventInputSource` are flat and all have to pass; there is no second
rules engine here, and there will not be one.

Feeders are already logic engines. Do the compound part there and post the answer:

```json
{"source":"streamerbot","type":"custom","message":"confetti-big"}
```

Then in the remote app: one input source, `type` = `custom`, `keyword` = `confetti-big`. The app stays a
thing that reacts, the feeder stays the thing that decides, and every bit of expressiveness the feeder has
is available without this package growing a node graph.

Use a stable, unambiguous name in `message` — it is matched as a case-insensitive substring, so
`confetti` would also fire on `confetti-big`. Prefixing your outcomes (`raffle-win`, `raffle-lose`) keeps
them from colliding with ordinary chat.

---

## What the app guarantees

Once an event is accepted, before any consumer sees it:

- **No nulls.** Every string field is at worst empty, and `user` always exists. Filters do not need null
  checks, which is why this is done at the boundary and not in every consumer.
- **`message` is at most 1000 UTF-16 code units,** cut without splitting a surrogate pair. Emoji outside
  the BMP count as two, so an emoji-heavy line reaches the limit sooner than its character count suggests
  — pre-truncate on the same unit if you truncate at all. Half a surrogate pair is an ill-formed string
  that breaks whatever serializes it next, which is why the cut backs off rather than landing inside one.
- **`amount` is never NaN.** JSON can carry `NaN` and Newtonsoft accepts it. Left alone it defeats every
  numeric filter — comparisons against NaN are all false — and a consumer writing it to a property would
  rewrite it forever, since NaN never equals what is already there. Infinities pass through: they compare
  and clamp the way an absurdly large tip should.
- **Order is preserved within a request.** A batch is enqueued in the order it was written. Across
  separate requests, order follows the order they were accepted, which for concurrent requests is whatever
  the server got to first — so a feeder that needs two events to land in a definite order should put them
  in one batch rather than in two calls.
- **Nothing is deduplicated.** Post the same event twice and it fires twice. If your feeder can replay —
  and most can, on reconnect — remember what you have already sent. The OneComme plugin keeps a 512-entry
  ring of ids for exactly this.

---

## How events reach operations

A feeder does not need this to send correctly, but it explains behaviour that otherwise looks like a bug.

Events are published to consumers **once per frame**, as a batch. Every consumer in a frame sees the same
list in the same order, so an input source that fires and a logger that records can never disagree.

An input source outputs a pulse on the frame a match lands on, and the operation fires on the **edges** of
that pulse. So matches on consecutive frames read as one long press and fire **once** — the same way
holding a key down does.

The consequence: in quiet chat, one comment is one firing. In a flood, firings collapse. That is
intentional — a confetti cannon should not run at 60 Hz — but it means a feeder cannot assume one POST
produces one visible action. When an exact rate matters, the user sets a cooldown on the input source. When
an exact count matters, the feeder should be counting, not the app.

Two events matching the same source in one frame: **the first wins**. Chat order is the order viewers
experienced, and picking the larger tip would make the person who tipped first look ignored.

---

## Limits

| Limit | Value | What happens past it |
|---|---|---|
| Request body | 64 KB | `413`. Enforced both from `Content-Length` and while reading, so chunked encoding cannot get around it. |
| `message` | 1000 UTF-16 code units | Truncated on arrival. Not an error. |
| Queue between frames | 256 events | Oldest are dropped and counted in `totalDropped`. |
| `amount` precision | float32 | Integers are exact to 2^24 (~16.7 million). Scale before sending if your raw values are larger. |

The queue bound is per frame, not per second: at 60 fps it takes more than 15,000 events per second to
overflow. If `totalDropped` is climbing, something is wrong upstream rather than merely busy.

There is no rate limit and no request-count cap. The intake trusts the network it is on, which is why the
default is loopback.

---

## Writing a feeder

The short version:

1. Read the port from **Server Port** on the `SocialGateway` in the remote app.
2. `POST /social/events` with a batch whenever your source gives you one; `/social/event` when it gives you
   one at a time.
3. Set `source` to the originating platform and `type` to the closest well-known value. Invent one only
   when nothing fits.
4. Fill `user` role flags if you have them. Without them, `MemberOnly` and `ModeratorOnly` filters can
   never match your events — which is a silent failure from the user's side, so document what you send.
5. Send a stable `id` if your source has one, and de-duplicate on your side.
6. Do not block your own pipeline on the response. The app may be closed; that is normal, not an error
   worth showing the user every comment. Throttle failure logging.
7. Ship a way to set the port and the token.

Two working implementations to read: the OneComme plugin in `Bridge~/onecomme-plugin/` (Node.js, batches,
de-duplicates, throttles its own error log) and the Streamer.bot C# recipe in [StreamerBot.md](./StreamerBot.md).

---

## Version history

| `formatVersion` | Change |
|---|---|
| 1 | Initial contract: the event schema above, `/social/event`, `/social/events`, `/social/status`, optional `X-Social-Token`. |
