# Changelog

## [Unreleased]

### Changed

- **`Service<T>.subjects` is now a read-only view (`IReadOnlyList<T>`), and broadcasting goes through the new `Service<T>.ForEach(action)`.** The raw `List<T>` was public and mutable, so any caller could bypass `Register`/`Unregister`, and `subjects.ForEach(...)` broke with a collection-modified exception the moment a subject unregistered itself from inside the callback. `ForEach` iterates a pooled snapshot, so subjects may safely register and unregister (including themselves) mid-broadcast; read-only LINQ over `subjects` is unchanged. Breaking only for code that mutated the list directly or called `List.ForEach` on it — replace the latter with `Service<T>.ForEach`.
- `SingletonService<T>.subject` is a read-only property now; registration still goes through `Register`/`Unregister`. Its debug assert also actually fires on double-registration — it used to assert `obj != null`, which is never false in practice, while the message talked about an already-registered subject. Overwrite-on-register behaviour in release is unchanged.
- `SelectableService<T>`'s id→subjects dictionary is private; use `Register`/`Unregister`/`Select` (unchanged).

- **Resetting a property is now `POST /live/object/{id}/{path}/@reset`.** Breaking, no compatibility shim. The bare `reset` was ambiguous with the array-append route on a member that happens to be *called* `reset` — `POST /live/object/1/reset` could mean either, and only the order of the route table decided it, which meant such a member could never be appended to. `@` cannot start a real member name, so the pseudo-member spelling removes the ambiguity instead of ranking it, matching `@parent` on objects and `@image` on assets. The batch endpoint takes the same spelling.

- **An asset is now named by a path, not a query parameter: `GET /live/asset/{key}` replaces `GET /live/asset?guid=`.** This is a breaking wire change and ships without a compatibility shim. Objects have always been `GET /live/object/{id}`, and the asset endpoints were the last ones that named their subject a different way. The key takes the whole tail of the URL exactly as a property path does in `/live/object/{id}/{path}` — its inner slashes are kept, so a file reference (`file:C:/props/chair.lsb`) and an engine asset path (`/Game/Props/Chair.Chair`, which puts two slashes in the URL) both survive the round trip. Because the key runs to the end, a sub-resource can only be told apart by a name no key ends with, hence the `@image` pseudo-member, spelled like the `@parent` pseudo-property on objects; `image` on its own would be ambiguous with a key ending in a folder of that name. **Clients must percent-encode `#` and `?` in a key** (a raw `#` is a fragment delimiter and never reaches the server) as well as a literal `%`, and must leave `/` alone; a key containing `..` is still rewritten by URL normalization before it arrives. Two consequences inside the package: the asset routes match and parse the *raw* request path rather than `Url.AbsolutePath`, which has already dropped an escaped `#` and collapsed doubled slashes, and the image handler must be registered before the resolver because the latter claims the whole `/live/asset/` prefix.

### Added

- **`GET /live/type/{name}` and `GET /live/enum/{name}` — one definition, fetched directly.** The singular counterparts of `/live/types` and `/live/enums`, standing to them exactly as `/live/object/{id}` stands to `/live/objects`: the response is the array element itself, not a one-element collection, and a name nothing answers to is a 404 rather than an empty list. A client that has been told a single type went stale (`/live/changes` reporting the pseudo id `@types`) or that meets a type it has never seen can now ask for that one definition instead of refetching the whole table. The plural endpoints and their `?type=` filter are unchanged, so nothing has to migrate. A name goes in the path here because a type name is an identifier; asset keys reach the same shape a different way (see the entry above), since those carry slashes and cannot be a single path segment.

- **`GET /live/asset/{key}/@image` — the picture for an asset, alongside the name and type `/live/asset/{key}` already returns.** A client that can label an asset can now show it, without the host application having to serve an image route of its own. The picture is a pseudo-member of the asset rather than a route of its own, spelled like the `@parent` pseudo-property on objects, because the key runs to the end of the URL (see the entry above). Where the bytes come from stays with whoever owns the assets: `AssetRegistry.SetThumbnailProvider` is the injected hook (the same shape as the existing name-fallback / catalog-prewarm / group-expander hooks), and the key is passed through untouched, so an application-specific reference works as well as a registry key. Without a provider — or for an asset that has no picture — the answer is 404, which clients already treat as "fall back to a placeholder". This replaces LiveStudio's `/live/avatar/image` **and** its `/live/snapshot/image`, both of which were an application serving pictures for things that are assets; moving the one endpoint here is a breaking wire change and ships without a compatibility shim.

- `PersistScope.Custom`: a member declared with it is written by its owner into its own file, so neither the live scene nor the project settings carry it. The scope filter already routed members to one file or the other; a feature whose state is an asset in its own right (LiveStudio's deck files) needed a third answer — "not here" — rather than a bespoke exclusion at each writer. Such an owner also keeps its own baseline and writes when the user edits, so nothing else has to know when it is out of date. Deserialization ignores the scope, so a file written before a member moved to `Custom` still restores it. `LiveObjectSnapshot.Capture(handle, scope)` is the public way to serialize one scope from another assembly.

- **A server toggle on the editor toolbar**, left of Live Studio's Remote app button (Material Symbols "devices" icon, grey when stopped and green while listening, port in the tooltip). The server is thread-based, so it serves the remote app with the editor merely open just as well as it does in play mode — this button is what turns that on without hunting for the Remote Control Server window. Starting binds it to the `RemoteControlBehaviour` hosts in the loaded scenes, so the live objects the scene registers in edit mode are immediately browsable; projects that configure their servers in the window instead are started from there (the settings asset is never created as a side effect of a click). Stopping tears the server down rather than parking it, keeping the toolbar's stopped state identical to "never started". While play mode is running the application owns the server — the app's own routes go with the instance and cannot be re-registered from the toolbar — so the button reports state there instead of toggling, and says so in its tooltip.

- **Attribute-less exposure of arbitrary objects via `LiveClassAsset`.** The asset-based counterpart of `[LiveClass]`: any scene component (built-in or third-party; assets such as ScriptableObjects are the intended future scope) can now be exposed to the remote app without code changes. Exposure settings live in a `LiveClassAsset` shared across scenes: per-type member definitions (with label, help text, persistence metadata and a polymorphic `[SerializeReference]` control choice — `SliderControl`, `HideControl`, extensible by deriving `LiveBindingControl` — the single source of the type-level `LiveClass` registration, so there is no per-instance member-set ambiguity) plus instance-binding entries that carry only a stable GUID key. The scene side is `RemoteControlContainer` itself, which implements the standard `IExposedPropertyTable` (the same mechanism PlayableDirector uses): it maps keys to direct scene-object references — robust against renames and hierarchy moves — and turns each resolved entry into a runtime `LiveBinding` ILiveObject, so listing, editing and live-scene save/restore all work unchanged. Attribute-less exposure is a base capability of a container rather than a companion component: the object list the bindings join and the enable/disable that applies and drops them are the container's own. The binding key doubles as the LiveObject id, so persisted values stay stable across scenes sharing the asset. Because a set bundle carries its own container, the assets and their scene references travel with it and apply on load / drop on unload — `LiveClassAssetSystem.UnregisterTypes` undoes a registration when the last container defining a type goes away, leaving nothing behind for the next bundle. `RemoteControlBehaviour` is now a `RemoteControlContainer` (its own object list and assets are the base class's) and merges the other containers in the loaded scenes on top of its own. The runtime binding wrappers are deliberately kept out of the serialized `_objects` list and merged as a source of their own: `_objects` is `[SerializeReference]`, so wrappers placed there were written into the scene file on every save — under `[ExecuteAlways]` they exist while merely editing — and came back as empty `LiveBinding` entries that accumulated one set per save. Classes are picked in the new **Window ▸ Lilium Remote Control ▸ Live Class Asset** panel: "Add Class" picks a Component/ScriptableObject class through a searchable dropdown (grouped by namespace), or "From Selected" picks straight from the currently selected GameObject's own components — either way it adds an empty type definition to the class list. "Add Member" then opens a checkbox-list companion window over that class's exposable properties/fields/methods, left open so several can be toggled in one sitting instead of one dropdown pick at a time. The header holds the asset and the container; the footer's instance-bindings list is hidden entirely until a container is assigned, since there is nothing to bind into without one. Newly exposed members default their label to the nicified member name (`useColorTemperature` → "Use Color Temperature").
- `LiveClass.Register(Type, ...)` — non-generic attribute-less registration that also accepts `LiveFunctionDefine[]` for exposing methods. `LivePropertyDefine` gains define-side metadata overrides (`control`, `label`, `help`, `section`) that take precedence over member attributes, and `LivePropertyType` / `LiveFunctionType` constructors accept the corresponding overrides — the registration path attribute-less callers (bindings) use to supply UI metadata.

### Fixed

- Stop → Start now actually restarts a server. `HttpServerCore.StopServer` used to tear down every registered route, so an instance restarted from the Remote Control Server window came back up answering nothing but 404. Handlers now survive a stop and are torn down only when the server instance itself is disposed, making stop and start symmetric.
- `HttpServerCore.Dispose` flagged the instance disposed before calling `StopServer` — which refuses to run on a disposed instance — so disposing a running server without an explicit stop beforehand leaked the listener. It now stops (and releases the listener) first.
- An unexpected exception in the listener loop was swallowed by `Task.Run`, leaving the server silently dead while `IsRunning` still reported true. The loop now logs the exception and exits, so the running state and `OnServerStopped` tell the truth.
- In a player build nothing ever tore the servers down: every teardown hook (`beforeAssemblyReload`, play-mode exit, editor quit) was an editor event, so the background cleanup tasks each server spawns only died with the process. `Application.quitting` now removes all servers in players too.
- The Remote Control Server window and its editor auto-start no longer throw when the settings asset lists a config whose asset does not exist in the current project (a shared settings asset can reference configs from packages the project does not include).

### Changed

- **Every route this package serves moved out of `/api/*` and `/ui/*` into `/live/*`.** RemoteControl is a library, and its handlers share one server and one route table with the host application's own — so each generic name it claimed under `/api/` was a name the application could never use again, and the ones it had claimed (`status`, `events`, `language`, `commands`, `asset`) are exactly the names an application reaches for first. The collision was already demonstrable in-tree: two servers in one Unity process had independently picked `/api/commands`. `/api/` is now left entirely to the host. `/api/status` → `/live/status`, `/api/events` → `/live/events`, `/api/confirm` → `/live/confirm`, `/api/performance` → `/live/performance`, `/api/language` → `/live/language`, `/api/commands/quit` → `/live/commands/quit`, `/api/commands/reset` → `/live/commands/reset`, `/api/asset` → `/live/asset`, and the Web UI's `/ui/sidemenu` / `/ui/info` → `/live/ui/sidemenu` / `/live/ui/info`. The live-object surface (`/live/objects`, `/live/object/…`, `/live/types`, `/live/batch`, …) is unchanged. `/live/` directly is reserved for the package; an application that wants routes there should nest them one level (`/live/app/*`) so a future package route cannot collide. This is a breaking wire change and ships without a compatibility shim — clients move with it.

- **The live scene's routes are grouped under `/live/scene/*`.** `export`, `import` and `orphans` sat directly on `/live/`, where each read as a verb about nothing in particular — "export" says neither what is exported nor that the two file endpoints and the orphan list are one feature served by one handler (`LiveSceneIoHandler`). The live scene is one subject among several the package serves, so it gets a segment of its own like every other: `/live/export` → `/live/scene/export`, `/live/import` → `/live/scene/import`, `/live/orphans` → `/live/scene/orphans`, `/live/orphans/remove` → `/live/scene/orphans/remove`. Request and response bodies are untouched, and the scene file format is unaffected — only the paths move. Breaking, with no compatibility shim.

- Server creation moved out of the config asset: `RemoteControlServerConfig.CreateServer` is gone and `RemoteControlServerManager.GetOrCreateServer` builds, wires and registers the instance itself. The config is pure data now, which also removes the duplicated "already exists on this port" check the two paths each carried.
- Starting a server no longer clears the presence list and broadcasts "Remote Control Server started". The broadcast could not reach anyone (a fresh server has no inboxes yet), and the clear touched only one of the two places that track clients; presence self-heals through the status-poll timeout instead.
- Removed unused connection plumbing: `HttpServerCore.IsPortAvailable`, `RemoteControlServerManager.GetServer`/`GetAllPorts`, `RemoteControlServerCore.SendToClient`, `EventQueue.SendToClientsAsync`/`ClearEvents`/`GetStats`/`GetActiveClientIds` (and the `TargetedMessage`/`DirectMessage`/`EventQueueStats` DTOs), and the `RestApiConnectionManager` statistics surface (`ProcessClientMessage`, `GetStats`, per-client message/response-time tracking, `GetClient`/`GetActiveClients`/`GetAllClients`/`GetActiveClientIds`). None of it had a caller; the client record is now just identity plus last-activity.

### Fixed

- `GET /live/types` could 500 with "Collection was modified" when serializing a type lazily registered another [LiveClass] type (derived-type resolution inside ToJObject mutates the registry). The handler now snapshots the type list before serializing.

### Changed

- **Renamed the whole "Exposed" vocabulary to "Live".** The system stopped being merely "exposed to the remote" long ago — the same property set is what LiveScene persists, operations bind to and the remote app edits — so the name now matches LiveStudio / LiveScene / `live.json`. Attributes: `[ExposedClass]` → `[LiveClass]`, `[ExposedProperty]` → `[LiveProperty]`, `[ExposedField]` → `[LiveField]`, `[ExposedFunction]` → `[LiveFunction]`, `[ExposedEnum]` → `[LiveEnum]`, `[ExposedExternalEnum]` → `[LiveExternalEnum]`, `[ExposedKey]` → `[LiveKey]`, `[ExposedDefault]` → `[LiveDefault]`. `[ExposedHelp]` is simplified to `[Help]` (decoration attributes carry no prefix), and `[FormerlyExposedAs]` becomes `[FormerlyNamedAs]`. Every `Exposed*` type and file is `Live*` accordingly (`ExposedObjectHandle` → `LiveObjectHandle`, `ExposedObjectRegistry` → `LiveObjectRegistry`, `ExposedPropertySerializer` → `LivePropertySerializer`, the "ExposedObjects Viewer" window → "LiveObjects Viewer", …), the `SendMessage` hooks `OnExposedChanged` / `OnExposedReset` are now `OnLiveChanged` / `OnLiveReset`, and the source generator matches the Live attribute names. REST routes move from `/exposed/*` to `/live/*` (objects, object, types, enums, changes, batch, function, export, import, orphans) — a deliberate wire break; remote clients update in lockstep. Saved data keeps loading: the built-in proxy typeNames (`GameObject`, `Asset`, `Component`, …) never contained "Exposed", and Unity-serialized renames carry `[MovedFrom]`.
- Removed `[LiveValue]`. The displayed-property sync already polls every visible property, so a dedicated per-property polling control had nothing left to do; read-only members now render as plain read-only values and update through the ordinary sync.
- The package's asset-creation entries share a single **Live Studio ▸ Remote Control** submenu. `Assets ▸ Create` listed them under two roots — `Remote Control` (UI Definition, Server Config) and `Lilium ▸ Remote Control` (Live Binding Preset) — and neither sat with the Live Studio packages they ship alongside. Menu paths only affect where the command appears; existing assets and their serialized types are untouched.

### Added

- `GET /live/changes` reports which live objects changed since a revision, as ids only. `LiveChangeLog` keeps the latest revision per object id rather than a queue of events, so memory is bounded by the number of distinct objects and a client that has been away for any length of time still gets exactly the set it missed — there is no "cursor fell off the end" case to recover from.
- `GET /api/events?since={lastEventId}` hands a client the one-shot notices queued for it — toast notifications and confirmation prompts, the only things left that cannot be recovered by re-reading state. The queue is per client with its own read position, so missing a poll delays a notice instead of dropping it. It is also resolvable as a sub-request of `POST /live/batch`, which is how the remote app collects it: riding the poll it already sends for displayed values means the inbox costs no extra round trip.
- `/api/status` reports an `instanceId` that changes whenever the server is rebuilt, and polling it marks the calling client present. Without a held-open connection there is nothing to notice a restart that polling happened to poll straight through, and a client would sit on cursors and a type table describing a state that no longer exists.

### Changed

- Property changes are no longer pushed over SSE. `LivePropertyBroadcast` records the changed object's id in `LiveChangeLog` instead of serializing the value and fanning it out to every connected client. A broadcast went to all clients regardless of what each was looking at, and the value was serialized once per change even when nobody displayed it; now a client polls the change feed and refetches only the objects it holds. The entry points and their call sites are unchanged.
- `types_update` and `ui_update` events are replaced by the pseudo ids `@types` and `@ui` in the same change feed, so cache invalidation travels the same path as everything else.
- Nothing is pushed to a remote app any more. The SSE stream (`/api/stream`) and its long-poll machinery are gone; notifications and confirmation prompts travel through the per-client inbox above. `EventQueue` keeps its queues and read positions — only the held-open connection that drained them was replaced.
- "Is a remote app connected" (which decides whether a confirmation prompt has anywhere to show) is now "did one poll us in the last 5 seconds" — five times the connection-check interval. A shorter window would drop a prompt on the floor whenever a phone answered slowly; the event inbox keeps its own, much longer retention, since that answers a different question (is there anything left to hand over when a client comes back).
- `GetClientId` prefers the client-declared `X-Client-ID` header over the TCP endpoint. Inbox delivery has to survive across separate requests, and the source port does not.
- Removed `/api/heartbeat`. It was never registered on any route, and the client-liveness it was meant to carry now comes from the status poll.
- Nothing is broadcast on a timer any longer. The last such producer, the expression-weight update, sent every active weight to every connected client ten times a second no matter what page each had open; the remote app now polls `AvatarController.expressions` through the ordinary displayed-property sync, so the weights cost nothing while the expression cards are closed and reach only the client looking at them.
<!-- changelog-sha: ead5a500c2674f81ae92f66e88e1b3eacac8bd4f -->

### Added

- `RemoteConfirmSystem` raises a confirmation on every surface at once — the OS dialog on the machine running the app and a modal in each connected remote app — and resolves it from whichever answers first, dismissing the rest. An operator working from a phone could not answer a prompt that only existed on the PC, and someone at the PC could not answer one that only existed in the remote app. Prompts travel as localization keys with the app's translation attached as a fallback, so each remote app renders them in ITS language. Answers come back over `POST /api/confirm` (a default route, since the framework itself raises prompts).
- `NativeConfirmDialog` shows the OS dialog on its own thread and can be closed from another one. The blocking `ConfirmDialog` cannot back a mirrored prompt: while it is up the app cannot serve the REST call carrying the remote answer, let alone act on it. `ConfirmDialog` stays for terminal, single-surface paths.
- `RemoteControlBehaviour.TrySaveOrPrompt` saves to the current path or opens the platform's "Save As" picker, picking the Editor or player dialog itself.
- `QuitApiHandler.onQuitRequesting` vetoes a quit asked for over REST. A player already has `Application.wantsToQuit` for this, but in the Editor a remote quit forces play mode off and the resulting `ExitingPlayMode` cannot be aborted — so without a veto here the Editor tore down before an asynchronous unsaved-changes prompt could be answered, and the prompt never reached the remote apps at all. Pressing Stop in the Editor still uses the local, un-mirrored dialog: nothing can hold that path open.
- `LiveSceneSaveSystem.ResetAllToDefault` reverts every contained object to its captured defaults regardless of dirty state. `RevertAllToDefault` only touches objects reporting dirty properties, and a load re-baselines everything as clean, so right after one it reverts nothing — a caller that has to discard the current state (switching to another project) needs the unconditional form.
- `LoadCurrentData` / `LoadCurrentDataFrom` take `forceBaseSceneReload`, which reloads the base scene even when the file already targets the active one. Without it a load into the same base scene deserializes on top of the live state, so every value the (delta) file omits keeps whatever was there before.

### Changed

- The unsaved-changes prompt shown when quitting now goes through `RemoteConfirmSystem`, so it appears in the remote apps as well as on the machine. It no longer needs the deferring coroutine either: the prompt never blocks `wantsToQuit`, which simply declines the quit and lets the answer start a new one.

### Fixed

- The package compiles on Unity 6.5 (6000.5), where `Object.GetInstanceID` / `Resources.InstanceIDToObject` — and the `EntityId`⇔`int` implicit conversions that had kept them working since Unity 6.3 — were promoted to Obsolete errors. Every call site goes through `LiveObjectUtility`, which is now the single place holding the version split, and the ID is carried as `long` so it still fits once `EntityId` outgrows 32 bits. The `@instanceID` field and the numeric-id fallback in `LiveObjectHandler` are unchanged for callers; the values remain session-scoped and must not be persisted.

## [0.25.2] - 2026-07-21
<!-- changelog-sha: 9a672726afe9b37983b2e8e941a5380792866d98 -->

### Added

- `ExposedObjectHandle.MarkClean` / `ExposedObjectDefaultRegistry.MarkClean` adopt an object's current state as the user-change baseline, so nothing on it counts as an unsaved edit until the next write. Only the user-change baseline moves; the serialization baseline keeps the captured defaults, so a delta save that follows still writes the full diff. Call it wherever state arrives from disk.

### Fixed

- Unsaved-change detection no longer reports a scene as dirty on every launch, which blocked quitting a Studio that had opened an existing project. The check serialized the whole scene and string-compared it against the file, so it could not tell "the user edited something" apart from "this build serializes the scene differently than the build that wrote the file" — any project whose `live.json` came from an older version failed it forever. It now asks the per-object dirty tracking, re-baselined wherever state arrives from disk (scene load, save, deferred pending entries, asset load tails).
- `IsDirty` and `GetDirtyProperties` compare in persistence shape (`forPersistence: true`), matching `CaptureDefaults` and the per-property baseline. Dirty must mean "a save would write something different", so read-only and non-persistable members — exactly what async loading churns after a restore: the crawl-built `ExternalAssetManager.assets` catalog, rebuilt `StageManager.sets`, `TransformRef.availableOwnerNames` — no longer count.

## [0.25.0] - 2026-07-17
<!-- changelog-sha: e35e6b466c57bae26ebaa8ecb4e4c73921db66bb -->

### Added

- Invoking exposed functions with arguments and through property paths: `ExposedPropertySerializer.BuildInvokeArguments` builds a typed positional argument array from a JSON array (shared by the REST invoke path and callers that replay a stored call), and `ExposedObjectHandle.ResolveFunction` resolves a function reached through a property path (a nested function on a member), mirroring how the REST layer resolves nested members.
- `ExposedProperty.TryGetValue<T>` / `TrySetValue<T>`: read and write value-type members without boxing, through typed delegates (`Func<object,T>` / `Action<object,T>`) emitted by the Source Generator.
- `ShowIf` / `HideIf` accept multiple conditions and can be placed on methods. Conditions serialize as a `visibilityConditions` array and are AND-evaluated on the client, so a member — or a function button — can be gated by several conditions at once. A single condition still emits the legacy `visibility` field, keeping the type output byte-compatible.
- `ImagePreviewAttribute`: a read-only control that polls a server-relative image URL and renders it (backs the Capture camera preview).
- `ExposedObjectRegistry.keyedCollectionVersion` and `NotifyKeyedCollectionChanged()`, so consumers can invalidate cached property resolutions when a keyed collection's elements are rebuilt and stop writes landing on stale keys.

### Changed

- Cut GC from the exposed property pipeline. `bool` / `enum` getters return canonical boxed instances via `BoxedValues`; `oldValue` reads and property-change events are skipped when nothing is subscribed; `FindProperty` / `GetProperty` are span-based (no `name.ToString()`); array-element descriptors are cached (`ExposedPropertyType.GetArrayElement`) and element lookup reads the collection once, building only the matched element; `PropertyPath` slash conversion and `AppendIndex` are allocation-free; `ExposedUnityObject.components` replaces its LINQ filter with a two-pass hand roll. REST responses and `scene.json` are unchanged.

## [0.24.3] - 2026-07-09
<!-- changelog-sha: 66ecbc810ebc2757306d12a4014067d834b7944b -->

### Added

- `AssetSelector` control: asset-reference fields serialize as asset GUIDs (`AssetRegistry`, baked in `OnValidate` and registered at runtime). `GET /api/asset?guid=` resolves a GUID to the asset's name and type so clients can label the choices. Sub-assets use a `guid:localId` composite key, and an optional `refProperty` carries scheme-prefixed external keys (e.g. `file:<path>#<sub>`) whose target may not be loaded yet; `AssetRegistry` gains name-fallback and display-name resolution so those unloaded keys can still be labelled.
- `GET /exposed/orphans` and `POST /exposed/orphans/remove`: root references a saved scene carries but whose object is absent this session are surfaced as removable "missing" items (path-carrying child overrides are excluded — they still bind once their asset loads asynchronously). Removing one drops every queued entry rooted at that id, so the next scene save stops re-emitting it.
- `RemoteErrorNotifier` forwards Unity errors (Error / Exception / Assert) to every connected remote app as an error notification. The broadcast is marshalled to the main thread (the server registry is mutated only there), identical messages are de-duplicated and bursts are capped so a per-frame exception cannot flood the app. Enabled by default (`RemoteErrorNotifier.enabled`).

### Changed

- The live-scene serializer no longer logs an ambiguous-restore warning for persisted-file entries. They carry no `@name` and cannot be disambiguated by type, so they are left unresolved for the remote app to present and delete instead of emitting a warning the user cannot act on.

## [0.24.2] - 2026-07-05
<!-- changelog-sha: 7a8804f7770e1b90b74b7719ca9885f2926f8d1f -->

### Changed

- **Breaking:** `GET /exposed/objects` and `GET /exposed/object/{id}` now return objects at **depth 1** by default. A nested inline (unregistered) composite child is emitted as a truncation stub `{ "@type": ..., "@truncated": true }` instead of being fully expanded, keeping the payload small and scalable as the object graph grows. Arrays do not consume depth, so element count and per-element type stay visible, and registered children keep their `@ref` form. Pass `?nested` (or `?nested=true`) to restore the previous unbounded expansion. Property reads (`GET /exposed/object/{id}/{path}`), SSE broadcasts, PUT responses and persistence (scene / project / preset) are unchanged — always fully expanded, so saved files stay byte-identical. Clients that walked nested values from the object list must either pass `?nested` or lazily fetch each truncated child via the property GET.

### Added

- `POST /exposed/batch` endpoint applies multiple object / property / function operations in a single request with per-item continue-on-error (each item's status and body are echoed back in order). The exposed REST API is now documented in `Documentation~/openapi.yml`.
- Per-member persist scope: `[ExposedField]` / `[ExposedProperty]` gain a `persistScope` (Scene default / Project), so the serializer can split live-scene state from per-class project settings.
- Exposed key-path addressing: a property path can target an array element by a stable `[ExposedKey]` value (e.g. `expressions[Joy].weight`), which backs the generic `SetPropertyAction` "bind to key" flow.
- `[Collapsed]` attribute: a hint that makes the remote app render an array or nested struct collapsed by default (the expand toggle remains). Emitted as a standalone `collapsed` flag, independent of the property controller.
- `elementTypeOptions` is emitted for polymorphic array properties so the remote app can offer "add an element of type …".
- A dedicated `GET /api/performance` endpoint reports the app's current FPS and process memory (sampled from the player loop via `PerformanceMonitor`; memory is read through the native `ProcessMemoryReader` on Windows), so the remote app can poll it only while an overlay is showing instead of piggybacking on other requests.

### Fixed

- `ExposedPropertyUtility.CreateDefaultElement` falls back to the first concrete `[ExposedClass]` subtype for abstract / interface element types, so adding an element to a polymorphic `[SerializeReference]` array no longer throws `MissingMethodException`.
- Constructing an `ExposedObjectHandle` no longer bakes an already-applied override value into its dirty-detection baseline. The constructor now uses `EnsureDefaultsCaptured` (capture only when unset, preserve an existing baseline) instead of an unconditional `SetDefault`, so an override captured by live-scene restore before the handle was registered is no longer overwritten with the current (applied) value and then lost on save.

## [0.23.6] - 2026-06-23

### Changed

- `[StringSelector]` can now annotate method parameters in addition to properties and fields. An `ExposedFunction` argument so marked is rendered as a dropdown whose choices come from the owning object's `sourcePropertyName` property, letting a function take a constrained string argument (e.g. selecting a named stage mark).

## [0.23.4] - 2026-06-22

### Fixed

- A `?type=X` query no longer drops a first-class `[ExposedClass]` component (e.g. `AvatarController` exposed as "Avatar") when its GameObject is also surfaced through a generic wrapper handle. The component and the wrapper are distinct exposed identities; the previous GameObject-identity de-duplication made RemoteApp pages report "No avatars available" once the avatar was exposed as an `ExposedGameObjectWithTransform`. Genuine same-target duplicates are still collapsed via `FindByTarget`.
- Live scene serialization now skips only a real-but-destroyed (fake-null) Unity reference during a base-scene reload, while still serializing a pure proxy that legitimately never had a backing reference.

## [0.23.2] - 2026-06-21

### Fixed

- `FrameRate.AsFrameNumber` now computes in `double` instead of casting through `float`. Past 2^24 (~3.2 days at 60fps) `float` lost integer precision, collapsing consecutive frame numbers onto the same value and stuttering the Studio playback buffer routing; `double` is exact for frame integers up to 2^53.

## [0.23.0] - 2026-06-21

### Added

- `[ExposedExternalEnum(typeof(T))]` assembly attribute registers external or built-in enums (e.g. `HumanBodyBones`) as remote-app dropdowns without modifying the type; supports `excludeNames` and the registration survives `Reset()`.
- `ExposedObjectSnapshot` can now capture an exposed object's default state and compute the delta against it (`CaptureDefaults` / `CaptureDelta`), the basis for prop presets.
- The remote app is now notified when a live scene is saved.

### Fixed

- Saved live-scene entries that reference an unresolved `@type` now deserialize away cleanly instead of leaving a null hole.
- Live-scene restore now reapplies scene visibility and active state correctly on startup.

## [0.22.1] - 2026-06-14

### Fixed

- Live scene saves now record only a build-settings scene as `baseSceneName`. Saving while an additive `.scene.lsb` world (buildIndex -1) was the active scene previously wrote the world's name, so loading that file in a build without the world dirtied the active scene and blocked graceful quit with an unsaved-changes dialog. The name is now resolved to the active scene only when it is a build scene, otherwise the first loaded build scene (worlds are calibrated on top of it), applied to both the save and the `HasUnsavedChanges` baseline.

## [0.21.2] - 2026-06-11

### Fixed

- `ExposedObjectUtility.InstanceIDToObject` now calls the public `Resources.InstanceIDToObject` instead of reflecting the internal `UnityEngine.Object.FindObjectFromInstanceID`, which broke on Unity 6.1 (6000.3) where its argument changed from `int` to an `EntityId` struct. The public API keeps an `int` argument across Unity 2021.3–6000.3.

## [0.21.0] - 2026-06-08

### Changed

- Member access is now backed by source-generated accessors, removing the runtime dependency on `Unity.Properties` and reducing reflection overhead.
- `ExposedObject` is now a readonly struct and was renamed to `ExposedObjectHandle`, eliminating transient view allocations during property access.

### Fixed

- Static `[ExposedClass]` types no longer disappear from the exposed set when their static constructor runs before all game assemblies are loaded; attributes are re-scanned after assembly load.
- Inherited `[ExposedClass]` members are now registered in declaration order.

## [0.20.12] - 2026-06-04

### Added

- `switchSceneOnLoad` option for LiveScene loading, controlling whether loading a scene also switches to it.

## [0.20.11] - 2026-06-03

### Added

- Log the live scene file path when a scene is saved.

## [0.20.10] - 2026-06-02

- No functional changes (version synchronized with the monorepo release).

## [0.20.9] - 2026-06-02

### Added

- `allowExternalConnections` option to bind the server to all network interfaces.

### Changed

- Handlers are now registered per-instance with their routes declared in the constructor.
