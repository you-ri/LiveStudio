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
| Sidebar / menu labels in the WebUI | `label` field on the menu item |
| Help text | The string passed to `[ExposedHelp("...")]` |

---

## REST API

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/language` | Returns the active language and the list of registered languages. |
| `PUT` | `/api/language` | Switches the active language. Body: `{"language":"ja"}`. |
