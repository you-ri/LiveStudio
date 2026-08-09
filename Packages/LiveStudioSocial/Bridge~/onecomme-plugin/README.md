# LiveStudio Social — OneComme plugin

Forwards comments and gifts from [OneComme (わんコメ)](https://onecomme.com/) to a LiveStudio app running the `jp.lilium.livestudio.social` intake, so chat can fire deck operations.

One plugin covers every platform OneComme supports — YouTube, Twitch, niconico, TwitCasting, Kick and the rest — because it reads OneComme's already normalized comment stream instead of talking to any platform directly. Nothing in LiveStudio has to know a platform API exists.

日本語版は [README.ja.md](README.ja.md) にあります。

---

## Requirements

- OneComme **5.2** or newer (this is the version that introduced the plugin mechanism)
- A LiveStudio app with a **Social** gateway in its scene, running on the same machine

## Install

1. In OneComme, open the menu at the top right → **プラグイン** (Plugins), then click **プラグインフォルダ** (plugin folder). On Windows this opens `C:\Users\<you>\AppData\Roaming\onecomme\plugins\`.
2. Copy the whole `onecomme-plugin` folder in there. `plugin.js` has to sit one folder down, i.e. `…\plugins\onecomme-plugin\plugin.js`.
3. Back in the plugin window, press **再読み込み** (reload). *LiveStudio Social* appears in the list.
4. Turn it on with the toggle.

## Configure

Open the plugin's settings page from its entry in the plugin list.

| Setting | What to put there |
|---|---|
| **Port** | The number the LiveStudio app shows on its **Social** page (3003 for the standard Studio app). |
| **Token** | Only if the app has one set. Leave it empty otherwise. |

If nothing arrives, check the app's **Social** page: the *Events Received* counter goes up the moment a comment lands. When it does not, the port is usually wrong, or the app has a token set and the plugin does not.

## What gets sent

Every comment becomes one event on the LiveStudio contract. OneComme's platform ids are passed through, except two that LiveStudio spells differently: `niconama` → `niconico`, `twicas` → `twitcasting`.

| OneComme | LiveStudio `type` |
|---|---|
| Ordinary comment (`hasGift` false) | `chat` |
| YouTube superchat | `superchat` |
| YouTube super sticker | `sticker` |
| YouTube new member / milestone chat | `membership` |
| YouTube membership gift (given or received) | `gift` |
| Twitch cheer (bits) | `cheer` |
| Any other gift | `gift` |

`amount` carries the tip (`price`, or the bit count for a cheer) and `currency` its unit as the platform reported it — no conversion. Moderator, member and owner flags come across, so an operation can be limited to members or moderators. `isMember` is taken from YouTube's membership flag and from Twitch's subscriber flag.

Comments arrive in batches and are posted as one request. Recently forwarded ids are remembered, so a redelivery does not fire an operation twice.

## Credit

OneComme's terms ask you to credit the app in your stream description when you use the **free** version — it is optional on PRO. Something like this is enough:

```
コメント表示: わんコメ https://onecomme.com/
```

This applies to your stream, not to this plugin: the requirement comes from using OneComme itself.

## License

Apache License 2.0, same as the rest of LiveStudio. See the [LICENSE](../../LICENSE.md) in the package root.
