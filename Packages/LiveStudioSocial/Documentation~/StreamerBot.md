# Connecting Streamer.bot

[Streamer.bot](https://streamer.bot/) is a Windows automation tool for streamers: it connects to Twitch,
YouTube and Kick, and runs actions in response to what happens there. This page sets it up as a feeder for
LiveStudio, so a chat command, a cheer or a raid can drive an operation in your scene.

One Streamer.bot action per event kind you care about. Each action ends by posting one
[social event](./SocialEventAPI.md) to the app.

> Written against the Streamer.bot documentation as of August 2026 (v1.0 era). The UI wording may drift;
> the payload it needs to produce will not. This procedure has not been run on an installed Streamer.bot
> instance — if a step is off, the fix belongs here.

---

## Before you start

- A `SocialGateway` in your LiveStudio scene, and the app running. Note the **Server Port** it reports in
  the remote app — 3003 in a stock Studio build. Everything below assumes `http://127.0.0.1:3003`.
- Streamer.bot connected to your platform, with the triggers you want already firing. Test that first with
  a **Send Message to Channel** sub-action; there is no point debugging two things at once.
- Both programs on the same machine. If they are not, the app has to be set to accept external
  connections, and you should set an [auth token](./SocialEventAPI.md#authentication) — which rules out
  method A below.

---

## There is no built-in POST

Streamer.bot's **Fetch URL** sub-action only does `GET`. The intake only accepts `POST`. So one of:

| | What it is | Custom headers | Effort |
|---|---|---|---|
| **A. curl utility action** | A community action you import. Shells out to Windows' built-in `curl`. | No — so no auth token | Import once, then four **Set Argument** sub-actions per action |
| **B. C# sub-action** | Streamer.bot's built-in C# code block, using `HttpClient`. | Yes | Paste one code block per action |

**A** is the quicker start and is fine on loopback with no token. **B** is what to use if you want the
token, if you want the role flags mapped properly, or if you are setting up more than a couple of actions —
the C# block is the same in all of them and only the arguments above it change.

---

## Method A — the curl utility action

### Import it once

1. Open the Streamer.bot docs page for
   [cURL POST Requests](https://docs.streamer.bot/examples/curl-requests) and copy the import code.
2. In Streamer.bot, click **Import**, paste it into **Import String**, confirm.
3. You should see one action named `wsgs.utils.curl`.

`curl` itself ships with Windows 10 and later; nothing else to install.

### Use it in an action

Add a trigger (say **Twitch → Chat → Message**), then these sub-actions in order:

| # | Sub-action | Setting |
|---|---|---|
| 1 | **Core → Arguments → Set Argument** | `curl.method` = `POST` |
| 2 | **Core → Arguments → Set Argument** | `curl.url` = `http://127.0.0.1:3003/social/event` |
| 3 | **Core → Arguments → Set Argument** | `curl.headers.contentType` = `application/json` |
| 4 | **Core → Arguments → Set Argument** | `curl.data` = the JSON below |
| 5 | **Core → Actions → Run Action** | `wsgs.utils.curl` |

`curl.data` has to be escaped JSON — quotes backslashed, all on one line:

```
{\"source\":\"twitch\",\"type\":\"chat\",\"id\":\"%msgId%\",\"user\":{\"id\":\"%userId%\",\"name\":\"%user%\"},\"message\":\"%rawInputEscaped%\"}
```

Use `%rawInputEscaped%` rather than `%message%`. A viewer typing a `"` into chat would otherwise break the
JSON, and the failure looks like "some comments work and some don't", which is a miserable thing to debug.
Streamer.bot documents that variable only as "the message escaped", so before trusting it, send yourself a
comment containing a quote and a backslash and confirm the app still counts it. If it does not hold up,
that alone is a reason to move to method B, where the JSON is built by a serializer instead of by string
substitution. (Twitch display names cannot contain anything that needs escaping, so `%user%` is safe as-is;
on other platforms, check.)

Check it worked by watching **Events Received** on the gateway in the remote app. `curl.responseCode`
is also available afterwards if you want to log it.

### What method A cannot do

The utility exposes only `accept`, `authorization`, `contentType` and `userAgent` headers — there is no way
to set `X-Social-Token`. So **leave Auth Token empty on the gateway** if you use this, and keep the app on
loopback. If you need the token, use method B.

The role flags are also awkward here: Streamer.bot gives `role` as a number and the escaped-JSON field is
plain text substitution, so mapping it to `isModerator` / `isOwner` means extra logic sub-actions. Method B
does it in two lines.

---

## Method B — the C# sub-action

### Set up the action

1. Add your trigger.
2. Add **Core → Arguments → Set Argument**: `social.type` = the event kind for this action (`chat`,
   `cheer`, `follow`, `raid`, `subscribe`, …). This is the one thing that differs between actions.
3. Add **Core → C# → Execute C# Code** and paste the code below.
4. Click **Compile**. If the compiler complains it cannot find `System.Net.Http`, add a reference to it in
   the C# editor's **References** tab.

### The code

Same in every action. Edit the two constants at the top.

```csharp
using System;
using System.Text;
using System.Net.Http;
using Newtonsoft.Json;

public class CPHInline
{
    // The app's remote-control port, from Server Port on the SocialGateway.
    private const string kUrl = "http://127.0.0.1:3003/social/event";

    // Match Auth Token on the gateway. Leave empty for no authentication.
    private const string kToken = "";

    // One client reused across firings. Building one per request is the classic way to exhaust
    // sockets under load, and chat is exactly the kind of load that finds it. An instance field
    // rather than a static one, so that it shares a lifetime with the Dispose below — Streamer.bot
    // keeps one instance of this class per compiled block, which is the scope we want.
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    public void Init()
    {
        _http.DefaultRequestHeaders.Clear();
        if (kToken.Length > 0) _http.DefaultRequestHeaders.Add("X-Social-Token", kToken);
    }

    public void Dispose() => _http.Dispose();

    public bool Execute()
    {
        // Set by a Set Argument sub-action above this one. Defaults to chat so a forgotten
        // argument produces something usable rather than an event nothing can filter on.
        string type;
        if (!CPH.TryGetArg("social.type", out type) || string.IsNullOrEmpty(type)) type = "chat";

        // role is 1=Viewer 2=VIP 3=Moderator 4=Broadcaster, but only chat triggers carry it. A cheer,
        // a follow or a raid has isModerator instead, so read both — relying on role alone would send
        // isModerator:false for a moderator's raid, and a ModeratorOnly filter would then never match
        // it. Nothing outside chat reports the broadcaster, so isOwner is best-effort.
        int role = ReadInt("role");

        var payload = new
        {
            source = "twitch",
            type = type,
            id = ReadString("msgId"),
            user = new
            {
                id = ReadString("userId"),
                name = ReadString("user"),
                isModerator = role >= 3 || ReadBool("isModerator"),
                isMember = ReadBool("isSubscribed"),
                isOwner = role == 4,
            },
            message = ReadString("rawInput"),
            amount = ReadAmount(),
            currency = ReadCurrency(),
        };

        try
        {
            var body = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = _http.PostAsync(kUrl, body).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                CPH.LogError("[Social] " + (int)response.StatusCode + " " + response.ReasonPhrase);
            }
        }
        catch (Exception e)
        {
            // The app being closed is the normal case, not an emergency.
            CPH.LogWarn("[Social] " + e.Message);
        }

        // Always true. Returning false stops the whole action, so a failed post would silently skip
        // whatever sub-actions the user put after this one — a scene effect that quietly stops firing
        // because the app was closed is worse than the failure it is reporting. The log is the report.
        return true;
    }

    // Bits for a cheer, viewers for a raid — whichever this trigger happens to carry.
    private float ReadAmount()
    {
        int bits = ReadInt("bits");
        if (bits > 0) return bits;
        return ReadInt("viewers");
    }

    private string ReadCurrency()
    {
        return ReadInt("bits") > 0 ? "bits" : "";
    }

    // Arguments are read through object rather than a typed out parameter: which ones exist and what
    // they are boxed as varies by trigger, and a type mismatch would abort the whole action.
    private string ReadString(string name)
    {
        object value;
        return CPH.TryGetArg(name, out value) && value != null ? value.ToString() : "";
    }

    private int ReadInt(string name)
    {
        object value;
        if (!CPH.TryGetArg(name, out value) || value == null) return 0;
        int parsed;
        return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
    }

    private bool ReadBool(string name)
    {
        object value;
        if (!CPH.TryGetArg(name, out value) || value == null) return false;
        bool parsed;
        return bool.TryParse(value.ToString(), out parsed) && parsed;
    }
}
```

`source` is hardcoded to `twitch` above. Change it per platform — the point of the field is the platform
the viewer is actually on, not which tool relayed it, so a filter written for YouTube keeps working when
the user swaps feeders.

---

## Trigger mapping

Which Streamer.bot variables exist depends on the trigger. These are the ones the recipe uses:

| Streamer.bot variable | Available on | Maps to |
|---|---|---|
| `user` | every Twitch trigger | `user.name` (display name) |
| `userName` | every Twitch trigger | login name — use `user` for display |
| `userId` | every Twitch trigger | `user.id` |
| `isSubscribed` | every Twitch trigger | `user.isMember` |
| `isModerator` | every Twitch trigger | `user.isModerator` — the only source of it outside chat |
| `role` | chat triggers only | `1`=Viewer `2`=VIP `3`=Moderator `4`=Broadcaster; the only source of `user.isOwner` |
| `msgId` | chat triggers | `id` |
| `message` / `rawInput` | chat triggers | `message` |
| `bits` | Cheer | `amount`, with `currency` = `bits` |
| `viewers` | Raid | `amount` |
| `tier` | Subscription | not mapped — put it in `message` if you filter on it |

And the `social.type` to set for each:

| Trigger | `social.type` |
|---|---|
| Twitch → Chat → Message | `chat` |
| Twitch → Chat → Cheer | `cheer` |
| Twitch → Channel → Follow | `follow` |
| Twitch → Subscriptions → Subscription | `subscribe` |
| Twitch → Subscriptions → Gift Subscription | `gift` |
| Twitch → Raid → Raid | `raid` |
| Twitch → Channel Reward → Reward Redemption | `custom` |

Triggers that are not chat carry no `message` and no `role`; the helpers above return empty and `0` for
those, and the event is still perfectly usable — a follow needs no message.

Channel point rewards need one more step. The recipe puts the viewer's typed input in `message`, so two
different rewards produce events a keyword cannot tell apart — and the keyword would be matching text the
viewer chose. Put the reward's name there instead, either with a **Set Argument** holding the name and
reading it in place of `rawInput`, or by naming it in the code per action. The same goes for a
subscription's `tier` if you want to filter on it.

---

## Receiving it in LiveStudio

In the remote app, add a `SocialEventInputSource` to an operation set and set the filters. For the chat
action above:

- **Source**: `twitch`
- **Event Type**: `chat`
- **Keyword**: `!confetti`
- **User Filter**: `Any`

The tile the operation set carries decides what happens — a button runs it once, a toggle flips it, a
slider with **Value From Amount** takes the position from `amount`. See the package README.

---

## Composite logic belongs here, not there

The input source's conditions are flat and all have to pass. There is no AND/OR, no counting, no
"only if it is the third time". That is not an omission to work around — it is where the two tools divide.

Streamer.bot is a full logic engine with variables, queues, counters and sub-action branching. Build the
compound condition there, and post the **verdict**:

```csharp
var payload = new { source = "streamerbot", type = "custom", message = "raffle-win" };
```

Then one input source with **Event Type** `custom` and **Keyword** `raffle-win`. Everything Streamer.bot
can express is available, and the app stays a thing that reacts rather than a second place to hunt for the
rule that fired.

This is the intended pattern, not a workaround. Worth doing this way:

- **Bits over 500 from a subscriber** → one Streamer.bot action with the condition, posting `big-cheer`.
- **Every 10th follower** → a Streamer.bot counter, posting `follower-milestone`.
- **A raffle among everyone who typed `!join`** → Streamer.bot collects and draws, posting `raffle-win`
  with the winner's name in `user.name`.

Use distinct, prefixed outcome names. `message` is matched as a case-insensitive substring, so a keyword
of `raffle` would also fire on `raffle-lose`.

---

## Troubleshooting

**Nothing happens, no error.**
Check **Events Received** on the gateway. If it is climbing, Streamer.bot is fine and the filters on the
input source are wrong — clear them all, confirm it fires on anything, then add them back one at a time.
If it is not climbing, the problem is upstream.

**`404`.**
No `SocialGateway` in the running scene, so the routes are not registered. The server itself is up, which
is why the connection succeeds.

**Connection refused.**
The app is not running, or the port is wrong. **Server Port** on the gateway is the authority; do not
assume 3003 if you changed the remote-control port.

**`401`.**
The token does not match. Method A cannot send one at all — clear **Auth Token** on the gateway or move to
method B.

**`400`.**
Escaping in method A: a quote or a newline from chat broke the JSON. Use `%rawInputEscaped%`. Method B
builds its JSON with a serializer and hardcodes both required fields, so it should not be able to produce
one.

**Events arrive, but the wrong filter matches them.**
In method B, the **Set Argument** setting `social.type` is below the C# sub-action instead of above it, so
the code fell back to `chat`. **Events Received** climbing while a `cheer` or `raid` filter never fires is
the signature. Sub-actions run top to bottom; drag it above.

**Fires once, then never again while chat is busy.**
Working as intended. Matches on consecutive frames read as one long press. Set **Cooldown Seconds** if you
want a predictable rate. See "How events reach operations" in [SocialEventAPI.md](./SocialEventAPI.md).

**Comments fire twice.**
Two feeders are running — Streamer.bot and OneComme both connected to the same channel, most likely.
Nothing de-duplicates on the receiving side yet.
