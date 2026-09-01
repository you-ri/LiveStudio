# Localization — Lilium Remote Control

`LocalizationSystem` is a static class that resolves translation strings for labels and help text sent to the remote client. Use it when you want a multi-language UI on the remote side without baking strings into your code.

---

## How it works

1. **Translation data** is stored as JSON files, one per language, in the form `{ "key": "translated text" }`.
2. **Resolution**: `LocalizationSystem.Translate(key)` returns the translated string for the active language. If the key is not registered, the key itself is returned (fallback).
3. **Active language**: chosen from `PlayerPrefs` first, then the system language. The remote client can change it at runtime via the REST API.

---

## Registering translations

Translation data is loaded by the application — typically once at startup, from `Resources/`:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Initialize()
{
    LoadLocale("en");
    LoadLocale("ja");
}

private static void LoadLocale(string language)
{
    var textAsset = Resources.Load<TextAsset>($"MyPackageLocales/{language}");
    if (textAsset != null)
    {
        LocalizationSystem.LoadTranslations(language, textAsset.text);
        Resources.UnloadAsset(textAsset);
    }
}
```

Example JSON:

```json
{
  "Light": "ライト",
  "Camera": "カメラ",
  "Background": "背景",
  "Specify a 360-degree image.": "360度画像を指定してください。"
}
```

Recommended folder layout:

```
Runtime/
  Localization/
    Resources/
      MyPackageLocales/
        en.json
        ja.json
```

---

## What gets translated

| Surface | Source |
|---|---|
| Sidebar / menu labels in the Remote Control UI | `label` field on the menu item |
| Help text | The string passed to `[Help("...")]` |
| The package's own editor windows | `RemoteControlEditorLocalization.Tr(key)` |

---

## Text that carries values

Put the placeholders inside the translated text and pass the values in, rather than assembling a
sentence from translated pieces — word order is the first thing a translation changes.

```csharp
// "{0} frames / {1} MB"
label.text = LocalizationSystem.Format("LDS_RECORDED_DETAIL", frames, megabytes.ToString("0.0"));
```

Values that need a number format (`0.0`, `D3`) are formatted before they are passed, so a translator
never has to carry a format specifier through.

---

## Editor windows

The editor windows of this package read their text through `RemoteControlEditorLocalization`, which
is the same `LocalizationSystem` with two differences:

- **The files live outside `Resources`**, under `Editor/Localization/RemoteControlEditorLocales/`,
  and are read through the asset database. They describe windows that exist only in the editor, so a
  player build has no use for the bytes.
- **A missing key falls back to English before it falls back to the key.** A window nobody has
  translated yet is still readable; one full of `LDS_` tokens is not.

The active language is the one the application and the remote app share — an editor window does not
hold a language of its own.

A window that resolves its labels once, when it builds a row, watches
`RemoteControlEditorLocalization.generation` to learn that it has to ask again. It moves whenever the
language or the table behind it changes, including when entering play mode empties the table.

---

## REST API

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/live/language` | Returns the active language and the list of registered languages. |
| `PUT` | `/live/language` | Switches the active language. Body: `{"language":"ja"}`. |
