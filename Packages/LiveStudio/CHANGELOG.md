# Changelog

## [Unreleased]

### Changed

- **`FrameRecorderController` moved to `jp.lilium.remotecontrol` (`Lilium.RemoteControl`), and this package now only tells it where takes go.** Everything the component did was that package's — the gate, the recorder, the replayer, retaining the state and structure systems for the length of a take, putting the inventory back on a supplied frame — except one thing: that a take belongs to the open project, the same way scenes, decks and avatars do. That answer stays here as `FrameRecorderProject`, which installs itself as the recorder's `recordingFolderProvider` on both a domain reload and a play, and registers `FrameRecorderPage` as a control surface to keep out of the takes. `FrameRecorderPage` is unchanged apart from where the type it drives comes from, the component's exposed name is still `FrameRecorder`, and the script GUID moved with the file so the *Live Studio System* prefab keeps its reference. Breaking only for source naming `Lilium.LiveStudio.FrameRecorderController`; nothing has shipped it. `ResolveRecordingFolder` and its tests came along to `FrameRecorderProject`.

- **A light is an ordinary GameObject that happens to carry a `Light` component — `LiveLight` is gone.** The proxy class was a copy of `LiveGameObjectWithTransform` (the same `transform` property, the same `TransformRef` parent, the same hierarchy re-attach) with four `Light` properties bolted on, so a light no longer needs a class of its own: the scene entry is a plain `LiveGameObjectWithTransform`, and `enabled` / `color` / `intensity` / `shadows` come from a **live class asset shipped with this package** (`Runtime/Resources/LiveStudioLiveClasses.asset`), applied by `LiveStudioLiveClassRegistry` at load. Nothing else had to be built: a `LiveGameObject` already lists every component whose type has a registered live class, and the serializer already writes those as `@source` entries, so the values are saved and restored exactly as before. The declarations register globally rather than per `RemoteControlContainer` because an unregistered type is skipped by the serializer — wiring them per scene would silently drop a light's values in any scene that forgot the reference; assets that travel with a set bundle still go through the container list. Two things look different in the remote app: a light's own properties render one level in, inside the component pane, and `shadows` is the real `LightShadows` dropdown (registered through `[assembly: LiveExternalEnum]`) rather than the old on/off `shadow` toggle. Every scene shipped here was migrated in place and kept its light's live-object id, so `enabled`, `color`, `intensity` and the transform still restore from an existing live scene; `shadow` and the light's `parent` do not carry over, since neither name survives. `LiveLightFactory` goes with it — no UI definition referenced it and the "+" menu never offered a light.

## [0.26.0] - 2026-08-20
<!-- changelog-sha: c639c1ec8e82903d9b2fce176fb5ad194df87332 -->

### Removed

- **The whole `/live/camera` family is gone — `GET /live/camera`, `POST /live/camera`, `POST /live/camera/switch` and `GET /live/camera/image`; a camera is reached through the generic live-object surface like anything else.** A camera has been an ordinary live object (`LiveCamera`) for a long time, so listing one is `GET /live/object` and going live is its `Switch` live function — the remote app already drew its camera page from the object list and wrote lens and controller values as plain properties, and the list route's reply was fetched into state that nothing read. `POST /live/camera` had no caller at all: of its three actions only `switch` did anything, the two streaming ones flipped a flag whose broadcast body had been commented out, and the UE port never implemented the route in the first place. Removing them takes the handler's whole heartbeat with it — the 100 ms poll for a changed live camera, the `camera_update` broadcast it pushed and the `camera_image` event — because the change feed reports the same thing generically: `LiveCamera.priority`'s setter now records real transitions in `LiveChangeLog`, so a switch (which happens through direct C# writes the REST path never sees) marks exactly the two affected cameras for refetch, the same pattern the UE port already used. Without that, a client's LIVE badge froze after a switch. The preview picture, the one thing that genuinely cannot ride JSON, becomes an **image member** instead of a route: `LiveCamera.preview` is a `[LiveProperty, ImagePreview]` getter of RemoteControl's new `LiveImageData`, so `GET /live/object/{cameraId}/preview` answers the PNG while JSON reads carry that address as the value (see the RemoteControl entry). `CameraApiHandler` is deleted outright, leaving the manipulator as this package's only bespoke REST. Two properties also join the generic surface to make it complete: `LiveCamera.isLive` and `aspect` are now exposed (read-only, not persisted, as they are derived from the Cinemachine camera on every read), matching the UE proxy, which had both from the start — a client draws the LIVE badge and sizes the preview from them, and against Unity it previously could not. Breaking, with no compatibility shim.

- **`GET /live/input-actions` and `POST /live/input-actions/bind` are gone; the input map is a live object like anything else.** Neither route had a caller left — the remote app's input page was removed at some point and only its translation strings stayed behind — while the object side was already halfway there: `AvatarInput` has been the live class `InputActions` all along, exposing `deviceName`, `actionNames` and a `settings` snapshot of every binding. What was missing was a per-action handle, so it gains `actions`, a read-only array rebuilt from the live map whose elements carry the action `name` as their `[LiveKey]`, the bound key as a human-readable `binding`, and `enabled`. Rebinding is that element's `Rebind` live function, which runs the same `RuntimeKeyBindingSystem` path as expression and operation rebinding; it returns as soon as listening starts, and the captured key shows up in `binding`. So an action is now addressed by name (`actions[ToggleCamera].binding`) rather than by a list index into a bespoke DTO, and a client renders the list and the rebind button from the generic object surface. `InputActionsApiHandler` and its four DTOs are removed with it. Breaking, with no compatibility shim.

- **`POST /live/vrm/load` and `POST /live/vrm/reset` are gone; loading and clearing an avatar happen through the generic live-object surface.** Both had already lost every caller. Choosing an avatar file is registering it with `ExternalAssetManager` and enabling it (avatars are exclusive, so enabling one clears the rest), and a component that owns a model path — `GltfModel.path` and the like — loads from its own setter when the property is written. The remote app's file selector had moved to that some time ago and only writes the property; the two functions still calling these routes were exported by a hook and invoked by nothing. What the routes did offer over a property write was progress, and that part stays: `VrmLoadApiHandler` becomes `VrmLoadNotifier`, a plain listener on `VRMLoader` that pushes the same `vrm_load_start` / `vrm_load_progress` / `vrm_load_complete` / `vrm_load_error` events into the inbox, with the same payloads, and still tells a client that connects mid-load. Progress now reports **every** load rather than only remote-initiated ones — it used to be gated on a client id that only the REST path ever set, so a load started from the app's own UI silently skipped its progress events. Breaking, with no compatibility shim.

- **`GET` and `POST /live/expressions` are gone; expressions are reached through the generic live-object surface like anything else.** Both halves of the route already had an exact equivalent there, and the remote app had moved to it: the expression list is the live function `POST /live/function/{avatarId}/getavailableexpressions`, and a weight is the ordinary live property `GET/PUT /live/object/{avatarId}/expressions[<name>]/weight` (`AvatarController.ExpressionEntry`, whose `name` is the `[LiveKey]`). The `GET` was in fact the poorer of the two, reporting `weight` as a hard-coded `0` and `displayName` as a copy of `name` — live weights have never flowed from this route. What the route did offer was addressing the active avatar by expression name alone, with no object id to resolve; that is the one thing lost, and no client used it. `ExpressionsApiHandler` and its four DTOs are removed with it. Breaking, with no compatibility shim. `ExpressionService` is untouched — it is what `AvatarController.expressions` reads through.

- **`POST /live/commands` is gone.** It was already `[Obsolete]` with live functions (`POST /live/function/{id}/{path}`) named as its replacement, and it had stopped being reachable in practice: the only command it ever implemented was `input_action`, whose body never simulated a press at all — it just enabled or disabled the action and left a TODO — and nothing called it. On the remote app side the whole chain that fed it was dead too: `sendCommand` → `sendMessage` → a `commonProps.sendMessage` that no page was passed, plus a `useHomePage` hook with a `startCalibration` that no component imported (calibration is Fusion's endpoint, not this one). All of that is removed with the route. `POST /live/commands/quit` and `POST /live/commands/reset` are **unaffected** — they are separate routes served by RemoteControl's `QuitApiHandler` / `ResetApiHandler`, not by this handler, and both are still in use.

### Added

- **One deck is one file (`*.deck.json`), and every deck file in the project is a tab.** The authored operation layer — every `OperationSet` and `Deck` — used to be inlined in the live scene, so a deck could not be reused across scenes, handed to someone else, or seen in the project listing. A deck is now a file in the project's `Decks` folder holding that deck's grid width and the operation sets placed on it, and the operations page's tabs are exactly the deck files the project crawl found. The file name **is** the deck's name (nothing inside the file repeats it), so renaming a tab renames the file; adding a tab creates one; deleting a tab deletes it along with the operations it held (the remote app confirms first). There is no save button and no unsaved state: every edit — from the deck functions and from the generic property REST alike — writes the deck it landed in, and only that one. Dropping a `*.deck.json` into the project folder and re-scanning adds it as a tab. `OperationManager.operationSets` / `decks` are `PersistScope.Custom`, so the live scene carries nothing about decks at all; switching scenes within a project keeps the decks, and switching projects replaces them. One consequence worth knowing: a snapshot (and `POST /live/scene/export`) does not carry decks, since both write Scene scope.

### Changed

- **The Transform manipulator moved into `jp.lilium.remotecontrol`; this package now registers no REST route at all.** The manipulator only ever touched RemoteControl's own concepts — live objects, `TransformValue`, property paths — and its session/frame/pose routes were the last bespoke REST this package carried, so the whole stack (`ManipulatorApiHandler`, `ManipulatorCameraService`, `IManipulatorTarget`) now lives in the base package and every host application gets gizmo editing for free. The wire is unchanged: same four `/live/manipulator/*` routes, same payloads. `PropAttachment` keeps implementing `IManipulatorTarget`, now as a RemoteControl interface — the dependency finally points the right way. With the camera family gone (see below) and this move, LiveStudio's REST reference disappears entirely: everything the package adds is live objects on the generic surface.

- **Load and build notifications are C# events now; the observer interfaces are gone.** `IVRMLoadObserver` and the `VRMLoadObserver` broadcast wrapper are replaced by static events on `VRMLoader` (`onLoadStarted`, `onLoaded`, `onLoadError`, `onLoadProgress`), and `IAvatarBuildObserver` by `AvatarBuildNotifier.onAvatarBuilt`. Both interfaces existed only to receive notifications, yet lived in the `Service<T>` locator registry — so every observer had to implement all four VRM callbacks (usually as empty stubs) and it was never visible from a `Service<T>` use whether the registered object was something to look up or merely a listener. Events subscribe per notification, tolerate unsubscription from inside a callback by language guarantee, and leave `Service<T>` for actual lookups. Breaking for implementors of either interface: subscribe to the events instead (`AvatarBuildNotifier.BuildAndNotify` callers are unaffected).

- **`/live/snapshot/image` is gone too; a snapshot's screenshot comes from the same `/live/asset/{key}/@image`.** A snapshot file is one of the project's asset kinds, so its picture is an asset's picture — there was no reason for a second image route, keyed differently (by snapshot *name* rather than asset reference) and reading its own folder. `AssetBase` gains `thumbnailFilePath` for kinds whose preview is a plain file beside the asset rather than something packed inside it; `SnapshotAsset` returns its `*.snapshot.png` and the provider serves it straight from disk (deliberately not through `ThumbnailCache` — the file already *is* the cache). `SnapshotInfo` gains `reference`, the snapshot's project-relative asset reference, which is what a client now passes as `id`. A side effect worth having: snapshots listed on the **project** page show their screenshot too, where before only the dedicated snapshot page did. Breaking, with no compatibility shim.

- **`/live/avatar/image` is gone; asset thumbnails are served by RemoteControl's `/live/asset/{key}/@image`.** The endpoint accepted any asset kind from the day props and sets grew previews — only the path still said "avatar", and the UE port had to carry a comment explaining that the word meant nothing. Serving pictures for assets is not avatar work, and it is not even LiveStudio work: it belongs beside `/live/asset/{key}` and `/live/assets`, which are RemoteControl's. `AvatarImageHandler` is therefore replaced by `AssetThumbnailProvider`, which registers the same lookup (external asset → `ThumbnailCache`, with first-request VRM/glb extraction) through `AssetRegistry.SetThumbnailProvider`. Behaviour is unchanged apart from the path and a missing key now answering 400 instead of 404. Breaking, with no compatibility shim — the remote app moves with it.

- **Following RemoteControl's namespace move, every route this package serves is under `/live/*` instead of `/api/*`.** `jp.lilium.livestudio` is a general-purpose base meant to be embedded by applications that are not VirgoMotion, so the same reasoning applies to it: `camera`, `assets`, `avatar`, `expressions` and `commands` are names a host application wants for itself, and this package shares the host's route table. `/api/camera` → `/live/camera` (with `/switch` and `/image`), `/api/vrm/load` and `/api/vrm/reset` → `/live/vrm/*`, `/api/avatar/image` → `/live/avatar/image`, `/api/expressions` → `/live/expressions`, `/api/assets` → `/live/assets`, `/api/snapshot/image` → `/live/snapshot/image`, `/api/manipulator/*` → `/live/manipulator/*`, `/api/input-actions` and `/api/input-actions/bind` → `/live/input-actions` and `/live/input-actions/bind`, `/api/commands` → `/live/commands`. Response bodies and query parameters are untouched — only the prefix moves. Breaking, with no compatibility shim.

- `ICameraController` is renamed `CameraControllerBase`: it has always been an `abstract class`, not an interface, and the `I` prefix misled — the new name matches the package's other abstract bases (`AssetBase`, `OperationBase`, `MotionSourceBase`). No serialized data is affected: `[SerializeReference]` and the live `@type` both record concrete controller types only, and the base is never instantiated.

- Snapshots are one of the project's asset kinds, so a `*.snapshot.json` file is listed on the project page beside the live scenes and decks that already were — with a `Restore` button on it and the ordinary delete (which takes the `*.snapshot.png` thumbnail with it, since which files an entry owns is now the kind's own business through `AssetBase.DeleteFiles`). Snapshots were the one file the app itself authors that the project listing could not see: `SnapshotManager` scanned the `Snapshots` folder on its own and never went through `AssetTypeRegistry`. It still owns the dedicated snapshot page — the thumbnails, the capture-time ordering and the take/restore/delete flow are unchanged — and now also asks for a re-crawl when a snapshot appears or goes away, so both views agree.

- The Remote app toolbar button is hosted by Remote Control's shared toolbar host and takes its tints from there, so it and the new Remote Control server toggle read as one pair: the Remote app button sits immediately right of the server toggle whatever order the two assemblies happen to initialize in. The button itself is unchanged.

- The package's asset-creation entries share a single **Live Studio** submenu. `Assets ▸ Create` listed three roots for one package — `Live Studio` (VRM Avatar Setup Settings), `LiveStudio` (AvatarExpressionConfig) and `Lilium Live Studio` (Bundle Thumbnail) — which read as three unrelated packages. The two odd ones out now sit under `Live Studio`, and `AvatarExpressionConfig` is spelled `Avatar Expression Config` to match its siblings. Menu paths only affect where the command appears; existing assets and their serialized types are untouched.

- Following RemoteControl's Exposed → Live rename: `ExposedCamera` / `ExposedLight` are now `LiveCamera` / `LiveLight`. Their serialized `@type` names change with the class names, so both carry `[FormerlyNamedAs]` and scenes saved under the old names still restore (and re-save under the new names). The factories are `LiveCameraFactory` / `LiveLightFactory` with `[MovedFrom]` covering Unity-serialized references, and the runtime folder `ExposedType/` is now `LiveType/`.

- Expression weights no longer depend on face tracking. `VRM1Avatar.Update` used to return early whenever the body driver reported no tracking, which skipped `IExpressionResolver.Resolve` entirely — so `smoothedOutputs` never updated and every expression read 0. An operator driving expressions from a gamepad binding or the remote app therefore got nothing at all while the avatar was still on screen in its base pose. Resolve and the expression apply now run every frame (with neutral ARKit weights when untracked) in all three avatar backends — `VRM1Avatar`, `VRCAvatar` and `VRCFTAvatar`. Everything that IS the tracking input stays gated on tracking: blink, look-at, visemes, the ARKit-derived expressions and the face-tracking parameter writes. Applying neutral values for those would fight the base pose animation and the FX clips the avatar hands them to when untracked.

- The animation bundle is now a general-purpose **asset pack**: `*.anim.lsb` becomes `*.pack.lsb`, and a pack may hold assets of any type rather than `AnimationClip` only. The kind-bearing extensions are unchanged — `*.set.lsb`, `*.avatar.lsb` and `*.prop.lsb` each name what they contain, and renaming them would delete the only content-free way to classify them — so the pack takes the catch-all slot instead: its name deliberately says nothing about its payload. `PackBundleLoader` (was `AnimationBundleLoader`) therefore loads members untyped and registers each under its `file:<path>#<assetName>` key, letting the runtime type of each member decide which selectors offer it; because `GET /live/assets?type=` already filters the registry by type name, a new selectable asset type needs no change here. Since a pack's name no longer hints at its contents, that endpoint can no longer skip the pre-warm for non-clip types and now opens every pack once per session (the existing per-file cache keeps that a one-time cost). Existing `*.anim.lsb` files keep working as legacy input — the extension stays registered, so neither the files nor the `file:` references saved against them need touching — and the export menu is now `Assets/Lilium Live Studio/Export Asset Pack (.pack.lsb)`, accepting any selected main asset (folders and scenes excluded) with the pack-wide unique-name requirement now applied across types. Imported packs land in `Packs/` rather than `Animations/`; the project crawl is recursive, so files already under `Animations/` are still found. `AnimationBundleAsset` is renamed `PackBundleAsset` and carries `[FormerlyNamedAs("AnimationBundleAsset")]` so scenes saved under the old `@type` still restore, and `ExternalAssetKey.BuildClipKey` / `TryParseClipKey` are renamed `BuildMemberKey` / `TryParseMemberKey` (the key format itself is unchanged). Listing the members of a pack is now a second filter axis on `GET /live/assets` (see below) and returns every member whatever its type.

- **`GET /live/animation/clips` is gone; listing one pack's members is now `GET /live/assets?pack=<assetId>`.** The two endpoints had drifted into the same job under different names: both answer "what can I write into this asset reference slot", both hand back `file:<path>#<name>` keys built the same way, and both open a pack through `PackBundleLoader` as a side effect of listing it. All that actually differed was the filter axis — by type, or by containing pack — and the response shape wrapped around it. `/live/assets` now takes both, and they combine: `?pack=X&type=AnimationClip` lists just the clips inside X. The reply is the ordinary `{ "type", "assets": [{ "key", "name", "type" }, ...] }` either way, so a member now arrives with its runtime type where the old `clips` array gave only a name and a key — which is what a general-purpose pack needs, since its contents are no longer clips by construction. Drilling into a pack stays as cheap as it was: `?pack=` opens that one pack and skips the catalog-wide pre-warm entirely, and its keys are read back from `AssetRegistry` rather than rebuilt, so the same member lists byte-identically on both axes. An unknown `pack` id, or an id that is not a pack, is a 404, while an unmatched `type` is still just an empty catalog. Breaking, with no compatibility shim: a remote app build that still calls `/live/animation/clips` gets a 404 for the pack expansion. `PackBundleAsset.GetMemberNamesAsync()` is replaced by `GetMembersAsync()`, which returns the members themselves.

## [0.25.3] - 2026-07-22
<!-- changelog-sha: ead5a500c2674f81ae92f66e88e1b3eacac8bd4f -->

### Added

- Opening a project asks before discarding unsaved live-scene edits (Save / Don't Save / Cancel), instead of dropping them silently. The prompt is raised through `RemoteConfirmSystem`, so it shows both on the machine and in the remote app that asked for the project — a cancelled "Save As" abandons the open rather than discarding the very edits the user just chose to keep.

### Fixed

- Opening a project now leaves the app in the state a launch into that project would produce, instead of carrying the previously open project over. `OpenProject` reused the ordinary live-scene load, which only reloads the base Unity scene when the incoming scene targets a different one — so two projects sharing a base scene skipped the reload entirely, leaving the old project's loaded props/avatars in place and keeping every value the incoming (delta) scene file omits. Project-scoped settings were worse off: they are not in the scene file at all and are applied additively, so any setting the new project does not record kept the old project's value. `LiveSceneManager.OpenProjectScene` resets every exposed object to its captured defaults and forces the base-scene reload before restoring. The project folder crawl also moved after the reset, so the previous project's enabled/loaded catalog entries — which the crawl deliberately never prunes — no longer survive the switch.

### Changed

- The built-in asset catalog is no longer `AnimationClip`-specific. Which asset types are baked out of the `Resources` folders now comes from `BuiltinAssetTypeRegistry`, the built-in counterpart of `AssetTypeRegistry`: a kind declares the Unity asset type it owns and the `AssetBase` entry it lists as, so a package can add one without touching the baker or the runtime registry. `BuiltinAssetCatalog` stores a single entry list whose entries name their owning kind instead of one array per type; `BuiltinAssetCatalogBuilder` scans once per registered kind, classifies imports by asset type rather than by file extension (so a kind never enumerates the extensions its type can come from), and rebuilds once per editor load, which is what picks up a newly registered kind — no asset import announces one. The reference-resource behavior of `BuiltinAnimationAsset` (no load/unload toggle, never pruned, never persisted) moved to a shared `BuiltinAssetBase`, leaving each kind as its own exposed `@type`. Existing baked catalogs are rewritten in the new shape on the next editor load.

## [0.25.2] - 2026-07-21
<!-- changelog-sha: 9a672726afe9b37983b2e8e941a5380792866d98 -->

### Fixed

- A child process started through `ChildProcessHost` is now tied to the host's lifetime by a kill-on-close job object. Every graceful stop path ran from managed cleanup, so a host that died without unwinding (a crash, End task, the quit watchdog) orphaned Fusion and the Remote app, which went on holding their ports and locking build output. `StartChildApplication` also returns early when the process fails to start instead of falling through to use it.
- `ExpressionEntry.weight` no longer throws on the empty-named template instance that the array diff builds: `FacialKey` rejects an empty custom name, and there is no expression slot to drive, so the property now reads `0` and ignores writes. The exception escaped `wantsToQuit`, and Unity then let the quit through without the unsaved-changes check ever running — which looks like a successful quit and is not.

### Changed

- The render quality setting moved off the static `LiveSceneManager` class into `RenderQuality`, a serializable `IExposedObject` (like `StageManager`). Whether the remote app's settings page shows the quality section is now decided by registration: the settings `NavigatePage` lists the `RenderQuality` id, and `NavigateObjectSelector` skips ids that resolve to nothing, so an app exposes quality control by putting a `RenderQuality` in a `RemoteControlContainer` (or the host `RemoteControlBehaviour`) object list and hides it by leaving the instance out. The `Live Studio System` prefab registers one by default. `LiveSceneManager.quality` / `.qualityNames` / `.currentQualityIndex` / `.SetQuality` are removed, and the persisted level moves from `LiveSceneManager.settings.json` to `RenderQuality.settings.json` (the level resets to the project default once).

## [0.25.0] - 2026-07-17
<!-- changelog-sha: 84adc94402cc38a0d4f33c34ea8e7c9364bbd108 -->

### Added

- `AddSwitchAvatarOperation` creates an operation set that switches the active avatar, with the deck tile's full-frame background set to that avatar's thumbnail. `DeckControl` gains a `backgroundAssetId` stored as a portable project-relative reference (resolved back by `AvatarImageHandler`), so a saved deck survives moving the project or opening it on another machine.
- `InvokeFunctionOperation` can invoke exposed functions that take arguments and reach nested functions: it stores the positional call arguments as a JSON array (`argsJson`) plus an optional `propertyPath` (e.g. a `StageManager` set element's `WarpTo`), not just a no-argument function on the target. `AddFunctionOperation` gains trailing optional `argsJson` / `propertyPath` parameters, keeping the existing no-argument overload source-compatible.
- `VRCAvatar` now drives body animation, tracking gating, mesh visibility and the lower-body lock through `AvatarBodyDriver` (PlayableGraph) like `VRCFTAvatar`, so VRChat avatars gain untracked-part body-override clips and the lower-body pose lock. Viseme / voice and expression parameters are read and written through the wrapped controller playable.
- Built-in animation asset catalog: `AnimationClip`s shipped in `Resources` are baked into a `BuiltinAssetCatalog` (Editor `BuiltinAssetCatalogBuilder`) and surfaced as `BuiltinAnimationAsset` resources. `BuiltinAssetRegistry` registers each clip in `AssetRegistry` by GUID eagerly and idempotently (play start, editor load, on demand), so persisted body-override references resolve synchronously on scene restore. Catalog entries are injected into the project asset list and marked `isBuiltin`, so `ExternalAssetManager` never prunes or persists them and removal is rejected.
- `AvatarController`'s body-override slot now sources its candidates from `GET /api/assets?type=AnimationClip` (built-in `Resources` clips plus external `*.anim.lsb` bundles) instead of the inspector `_bodyOverrideClipPresets` array, which is removed.
- `VRCFTAvatar` exposes `_bodyOverrideClip` as a serialized field and forwards it on `Initialize` / `OnValidate`.
- `AvatarExpressionConfig.syncBlink` levels both eye-blink weights to the smaller of the two. Some avatars have asymmetric blink-weight accuracy between the left and right eyes and blink lopsidedly; enabling this makes the eyes blink symmetrically.
- `LiveStudioOrbitalFollow`: a Body-stage Cinemachine component that orbits a single target on a sphere (`yaw` / `pitch` / `distance`, with position damping and a local-frame target offset), resolved in the camera's own parent frame so rotating the camera's parent rotates the whole orbit rig. `OrbitalFollowCameraController` now positions through it and drives a single tracking target for both position and aim.
- The Studio Template scene ships with an `OrbitalFollow` camera out of the box.

### Changed

- Cut per-frame GC and hitches along the Operation-driven property path: `SetPropertyOperation` caches its resolved `ExposedProperty` (self-healing on a failed typed read) and uses box-free typed accessors for its per-frame `bool` / `float` writes instead of re-walking `FindProperty` and allocating element paths and component arrays every frame. `AvatarController` caches its expressions array (invalidated on avatar / `AvatarItem` change) to avoid O(N^2) GC when resolving keyed-array paths, bumps the registry's keyed-collection generation from `InvalidateExpressions` so cached resolutions re-resolve after expression elements are rebuilt (preventing writes to stale keys), and narrows `OnPropertyChanged` to reapply only the changed property instead of the full `_PostSetupAvatar` — which re-ran the T-pose / socket rebuild every frame and caused multi-millisecond hitches. Custom `FacialKey` hashing is now allocation-free.
- The Fusion UI Definition orders the capture page ahead of the license settings.
- Avatar attachment sockets created by `AvatarController` are now prefixed with `S_` (`S_Hips` / `S_Spine` / `S_Chest` / `S_Head` / `S_Neck` / `S_WristLeft` / `S_WristRight`) so a bone of the same name can no longer shadow them; the name-based references were updated (`PropAttachment` default socket, the bone-follower / look-at / orbital-camera `S_Head` target). Props and cameras that referenced the old socket names need re-selecting.

### Fixed

- The lower-body lock now honors a body-override clip's "Root Transform Position (Y)" offset (the humanoid muscle-clip `level` setting). `AvatarBodyDriver` baked the locked hip position and foot-IK goals with `AnimationClip.SampleAnimation` while `applyRootMotion` was disabled, which silently drops that offset, so a sitting clip authored with a Y offset (to sink the hips) locked at the un-offset height and floated — while a clip with a zero offset looked fine. The lock offset and foot goals are now re-sampled with root motion enabled so the clip's Y offset is applied; the base pose used off the lock path is unchanged.
- A momentary deck button no longer intermittently misses its action on fast taps: press and release arrive as two REST calls, and when both collapsed within one `Update` frame the manager never observed `held=true`, silently losing the release trigger. `SetHeld` now latches a release pulse on the rising edge so it fires exactly once.
- The lower-body lock no longer shakes the head from side to side on rigs whose hips parent carries a rotation (e.g. Blender Z-up armatures imported with X=-90). The root-yaw compensation was prepended to the hips local rotation, which turned the world yaw delta into roll; it is now applied as a world-space delta on the hips world rotation, independent of the rig's parent chain.
- The lower-body lock no longer clamps the legs fully straight with a degenerate knee axis on avatars whose `humanScale` is not 1. Foot IK goals were captured as raw root-local positions while the locked hip is humanScale-normalized, so the hip-to-foot distance changed and the clip's leg pose became unreachable. Goals are now captured relative to the sampled hips and rebased onto the normalized lock hip offset.
- The lower-body lock height no longer shifts with avatar height: `AvatarBodyDriver` locks the hips to the override clip's hip position through a humanScale-normalized, root-relative offset applied in world space.
- `QuitTerminationGuard` now arms a detached watchdog process on quitting instead of a managed watchdog thread, which dies with the Mono runtime before a late native teardown (WGI) wedge — this left the Player-built Fusion lingering after quit. The fire-time log, which can itself deadlock during teardown, is dropped.
- `LookAtCameraController` again drives the LookAt target on its own under Cinemachine 3: it sets `CustomLookAtTarget` so the aim uses the assigned look-at target instead of silently falling back to the tracking target (Cinemachine 2 parity), and clears the flag on teardown.

## [0.24.3] - 2026-07-09
<!-- changelog-sha: 66ecbc810ebc2757306d12a4014067d834b7944b -->

### Added

- `AvatarController` can drive body parts the capture does not track with an override `AnimationClip`. The clip is chosen from Inspector presets or from an external animation bundle (`*.anim.lsb`, which packs several clips) via a unified asset key — a bare GUID for baked assets, `file:<path>#<clip>` for external ones — resolved lazily and re-applied after scene restore and REST writes so the selection survives the bundle's asynchronous load. VRM1 / VRCFT playback is supported.
- A "lock lower body pose" toggle pins the hips and root to the capture anchor with foot IK, holding the lower body in place while the upper body keeps following the capture.
- Per-project session log: the Unity log is mirrored into `{project}/.livestudio/log` as one file per session, alongside the native Player.log. `ProjectPaths` gains `GetLogDir` / `EnsureLogDir` helpers.

### Changed

- Operation set manual hold and manual value now persist across scene reloads (previously both reset). On restore the edge baseline is seeded to the restored hold, so reloading a scene with a set left on no longer manufactures a rising edge that would re-fire the operation.

### Fixed

- Avatar meshes no longer blink while the subject leaves the capture frustum. The avatar root's valid byte is now a body/face tracking bitmask (body = MediaPipe, face = ARKit) and visibility is asymmetric — hidden only when both signals drop, shown on the face rising edge — so the frame-to-frame body-validity beat can no longer flicker the meshes.

## [0.24.2] - 2026-07-05
<!-- changelog-sha: 7a8804f7770e1b90b74b7719ca9885f2926f8d1f -->

### Added

- The editor gains a toolbar button to launch and close the Remote app, matching the runtime RemoteAppHost (same configured path / args and child-process handling).
- `LookAtCameraController` (`ICameraController`) assigns a bone — resolved from a `TransformRef` (target owner name + bone name / path) — to a camera's `LookAt` target, re-resolving only when the reference changes or the avatar is swapped, so a look-at bone survives avatar reloads. Only `LookAt` is touched, leaving the `Tracking` target and any Aim component intact.
- `BoneFollower` component drives its own transform to follow a bone resolved from a `TransformRef`, re-resolving on avatar swap. It bridges a swappable avatar bone to a Cinemachine camera authored in a separate stage / set bundle (outside the LiveStudio camera pipeline), where a direct cross-scene reference to the bone cannot survive the swap.
- `AvatarController` gains an avatar animation layer override, letting a loaded avatar's layer be overridden per scene.
- `ProjectManager` exposes selected / unselected camera-preview polling interval settings (per-project persist scope), controlling how often camera previews are refreshed.

### Changed

- Value-mode Deck sliders now map their drag onto the source property's min/max range instead of always writing the raw normalized 0..1 value, so a slider bound to a property spanning e.g. 0..100 can drive the full range. The normalized value is retained internally so gauges and edge detection are unaffected.
- Loaded prop / avatar objects are now persisted through the live scene's top-level diff as the single source of truth. State whose owning asset has not finished loading is queued and bound once the asset arrives, and any entry that never binds during a session is preserved verbatim across a load→save cycle instead of being warned about and dropped.

### Fixed

- Operation sets authored directly in a scene or prop bundle now have a stable id backfilled on init and restore. Previously they kept the default empty id, so every id-addressed function was a silent no-op and the set was skipped when rebuilding the input map.
- Multiple single-prefab props that shared the same internal AssetBundle CAB could collide when loaded concurrently ("same files already loaded"). Bundle opens are now serialized, and each built bundle is given a unique internal CAB seeded from its source asset GUID so props no longer share one.
- Loaded prop / avatar overrides (e.g. a prop moved from its authored position) are no longer lost when the live scene reloads. The load-complete re-baseline now preserves restore-applied overrides instead of folding the applied value back into the dirty-detection baseline.

## [0.24.1] - 2026-06-30
<!-- changelog-sha: 7c999a055f01c038e5793031dd7b3efcabf17d16 -->

### Added

- Operation sets can now be arranged on a **Deck**: a grid of push-button / toggle / slider controls, freely placed and editable from the remote app. The control type is chosen when a control is placed and is independent of the operation set's input mode.
- Value-mode operation sets gain a manual value override, so a slider control can drive a 0..1 value directly (throttled to the studio).

### Changed

- The action system was renamed to **Operation** throughout (`ActionManager` → `OperationManager`, `ActionSet` → `OperationSet`, `ActionBase` → `OperationBase`, and the concrete `*Action` types to `*Operation`); the remote app's "Actions" page is now "Operation". Existing live scenes and key bindings keep loading via compatibility aliases.
- The operation "panel" concept was renamed to **Deck** throughout, and an operation set's behaviour is now driven by a single control axis. Deck tile width is declared per control kind, missing deck panels are recreated automatically, and tile-layout fields are hidden from the generic remote editor.

### Fixed

- Expression weights can now be read while the avatar is inactive.
- `KeyInputSource`'s control path is stored as a shadow field of its binding, so key bindings persist and resolve correctly.
- The active live-scene path is re-resolved from startup state when entering play mode, so the last-opened scene restores reliably.

## [0.24.0] - 2026-06-26

### Added

- Action set / input binding system (`ActionManager` / `ActionSet` / `InputSource` / `ActionBase`), editable from the remote app: bind a keyboard or gamepad input to an ordered list of actions (set an expression weight, switch avatar / stage, toggle a GameObject). Polymorphic input/actions use `[SerializeReference]` + `[TypeSelector]`; input modes are Button / Toggle / Value. Wired into the `Live Studio System` prefab.
- `InvokeFunctionAction` and `SetPropertyAction` back a generic "bind to key" affordance next to any `[ExposedFunction]` or bool/float control (addressed by stable id), replacing feature-specific bindings.
- Mutually exclusive toggle groups for action sets: sets sharing a non-empty `group` whose input is in Toggle mode behave as a radio group (turning one on clears the others; all-off is still allowed).
- `SwitchStageAction` performs a complete stage switch (`SwitchToSetByName` unloads the other loaded sets and loads the target on demand); the Stage page's `SetActiveSet` keeps its original non-exclusive selection behavior.
- `AvatarChair` component for a chair prop the avatar sits on. It re-parents under the `AvatarController` so avatar root motion does not drag it, then makes its parts track the avatar's pelvis (Hips socket) relative to a recorded rest pose: `swivel` / `recline` rotate a target transform about an authored local axis (hips yaw / pitch), and `lateral` / `height` / `depth` translate one along an axis (hips X / Y / Z). The rest pose is recorded via an `Activate` action. Each axis (a `ChairAxis`) has an operating range (`min`/`max`, `0/0` locks it), a deadzone that is the play/backlash between the source and the follow target (absorbs hips tremor), and damping (`SmoothDampAngle` for rotation).
- Per-project state and project-scoped settings: exposed members gain a persist scope (Scene / Project); project-scoped values are stored in per-class `{Class}.settings.json` (screen output size/fullscreen/Spout and live-scene quality are now Project scope). `StartupStateStore` remembers the last opened live scene per project (`Settings/startup.json`), and `StartupSceneSwitcher` redirects to it before the first scene load.
- Two-tier (memory + file) thumbnail cache stored under a hidden `.livestudio` project folder (`ProjectPaths`).

### Changed

- **Breaking:** the avatar-prop `AvatarProp` component was split into composable siblings: a new shared `Prop` (keeps the exposed name `"Prop"`; owns the socket follow + position/rotation/scale offsets) plus a behavior component. `AvatarProp` was renamed to `AvatarItem` (the avatar→prop parameter bridge + expression driving); a `[MovedFrom]` keeps old type references resolving. Existing `*.prop.lsb` bundles carry only the old single component and therefore **must be re-exported** (a re-exported bundle root carries both `Prop` and `AvatarItem`); the live-scene `"Prop"` state key is unchanged.
- Expression key bindings are now driven generically through the action system: a `SetPropertyAction` writes `expressions[name].weight` (the data-driven expression slot), so the bespoke `SetExpressionAction` / `ExpressionBindingSystem` and the old `/api/expressions` bind routes were removed. Existing expression key bindings must be re-created.
- Shared `FormatHeader` unifies versioning across live-scene / project-settings / preset files (raw-JSON serialization, orphan prune).
- Object presets (`.preset.json`) now capture prop and avatar state as a delta with a lenient reader.
- Button-mode one-shot actions (switch avatar / stage) now commit on key release instead of press (`ActionContext.triggered`).
- The `ActionManager`, `ExternalAssetManager`, and `StageManager` singletons are now hidden from the remote app's generic scene object list (`HideInScene`); they remain reachable through their dedicated pages.

## [0.23.6] - 2026-06-23

### Added

- `AvatarProp` now exposes a `scaleOffset` (Vector3, default `1,1,1`) that multiplies the prop's authored local scale, so an avatar-attached prop's size can be tuned from the remote app / inspector. It is applied every frame and persists across an unload/reload like the position/rotation offsets.

### Changed

- Stage marks now warp the avatar through `StageManager` instead of the standalone `AvatarStageController`. Each `SetBundleEntry` exposes the `StageMark` labels in its own set scene (the new `marks` field, scoped per set), and `StageManager.WarpTo(setId, markLabel)` moves the avatar's anchor to the named mark within that set's scene (an empty label warps to the origin).

### Removed

- **Breaking:** `AvatarStageController` (the "Avatar Placement" exposed object with the `place` / `availablePlaces` dropdown of all loaded marks) was removed; warping is now per-set on `StageManager.WarpTo` (see above). It is also dropped from the `Live Studio System` prefab.

## [0.23.5] - 2026-06-22

### Changed

- Object preset files (`*.preset.json`) now use the `jp.lilium.remotecontrol.preset` format identifier with a `formatVersion` field, aligned with the live-scene file format. The redundant `sourceKind` hint was dropped (it is derived from the source path).

### Removed

- Backward-compatible reading of the legacy `prop.preset` preset format; presets saved before this change must be re-saved.

## [0.23.1] - 2026-06-21

### Fixed

- The Studio no longer reports unsaved changes on every launch (which blocked quit with an unsaved-changes dialog when a project folder contained files). Persisting the `ExternalAssetManager` asset array directly let the deferred project-folder crawl populate it after the live-scene save baseline was captured; the in-use-only persistence shadow is restored so the crawl-built catalog stays out of the saved/dirty state.

## [0.23.0] - 2026-06-21

### Added

- `ExternalAssetManager`: a single runtime catalog that loads and unloads props, avatars and set bundles uniformly (replacing the old `PropManager`). Avatar selection, prop loading and set-bundle management all flow through it.
- Runtime avatar props: a `.prop.lsb` bundle can be attached to an avatar at runtime, following a bone/socket through `AvatarProp`. Socket attachment is now robust to the avatar's rig and scale, and a prop's exposed-object parameters and id persist across unload/reload.
- Prop presets: a loaded prop's tweaked parameters can be saved as a `.preset.json` (`SaveAsPreset`) and re-loaded as a new prop, storing only the delta from the prop's defaults.
- Project asset catalog with live-scene assets and bundle thumbnails; exported `.prop.lsb` / `.avatar.lsb` bundles embed a thumbnail used by the remote app's asset cards.
- Avatar VRM thumbnail served over `/api/avatar/image?id=`, extracted from the VRM's embedded thumbnail without a UniVRM dependency.
- Cross-scene remote control: additively loaded set bundles expose their objects to the single persistent host server. New stage marks (`StageMark` / `StageMarkRegistry` / `AvatarStageController`) warp the avatar to named marks (plus a built-in Zero origin) from the remote app, and the saved active scene is restored on load.
- Adding an asset now imports it into the project: `AddAsset` copies the picked file into a kind-specific subfolder of the open project folder (`Avatars` / `Props` / `Sets`, resolved per asset kind, colliding names get a ` (n)` suffix) and registers the in-project copy. A picked file already inside the project folder is registered in place without copying. Only the single picked file is copied for now.
- A project folder is the home for all saved data: on first launch `Documents/<brand>/<project>` is created and opened, it is used as the default Save As location for live scenes, and "Open Save Folder" opens it.

### Changed

- The remote-control host and its managers now stay alive across base-scene reloads instead of being torn down and recreated, so remote connections and manager state survive scene switches; the Studio prefabs and template were reworked around this persistent host.
- `VRCFTAvatar` expression now flows through `ExpressionResolver`, and converted avatars auto-gain hand-gesture expressions.
- Received poses are now time-interpolated between the two nearest received 60fps frames during the variable-fps render update (`AvatarAnimationSystem.Lerp`), so the avatar advances smoothly and SpringBones no longer shiver on held frames.
- **Breaking:** Renamed the scene-bundle concept to "set" and the world concept to "stage", aligning the vocabulary with virtual-production terms (a stage holds sets, props and marks). Set bundles are now exported as `.set.lsb`; `SceneBundleAsset` → `SetBundleAsset`, `SceneBundleLoader`/`LoadedSceneBundle` → `SetBundleLoader`/`LoadedSetBundle`, `SceneBundleExporter` → `SetBundleExporter` (menu "Export Set Bundle (.set.lsb)"), `LiveStudioBundle.SceneExtension`/`IsSceneBundle` → `SetExtension`/`IsSetBundle`, and `WorldManager` → `StageManager` (exposed functions `AddSet`/`RemoveSet`/`SetActiveSet`, exposed property `sets`). The legacy `.scene.lsb` extension is still accepted on input (loaded as a `SetBundleAsset`), no longer produced on export. Saved live scenes referencing the old `@type` names (e.g. `SceneBundleAsset`/`WorldManager`) are not migrated and must be re-saved; such unresolved entries now deserialize away instead of leaving a null hole. `StageManager` carries `[MovedFrom("WorldManager")]` so in-scene `[SerializeReference]` wiring migrates automatically.
- `ExternalAssetManager` now persists its asset array directly to the live scene; the project-folder crawl rebuilds the disabled catalog on load, so the separate persistence shadow was removed.
- `SavedPaths` derives its base directory from the configured brand name and organizes saved data into per-project folders, replacing the single `Scene` subfolder and the standalone `LiveStudioPathsInitializer` (folder setup now lives in `ProjectManager`).

### Fixed

- `ResetCamera` now derives its yaw/position offset from the capture-camera channel instead of the pose-driven avatar root, so repeated resets are stable; a configurable camera height (default 1.5 m) keeps the virtual camera at eye level rather than dropping to the floor.
- Arms Post Rig is now added only for `VRM1Avatar`.
- Stage/scene visibility and active state are restored correctly on startup, and the persistent active entry settles at restore so the save baseline matches the post-restore state (no false unsaved-changes prompt at quit).
- A runtime-loaded avatar re-applies its animation parameter overrides one frame after its `PlayableGraph` builds, instead of reverting to the controller defaults; `VRMAvatarSetupSystem` skips adding a duplicate facial controller when an `IAvatar` already exists.

## [0.22.0] - 2026-06-14

### Added

- Scene bundle import: a `.scene.lsb` AssetBundle can now be loaded additively at runtime. New `RuntimeSceneManager` and `SceneBundleLoader` components plus a shared `BundleBuildUtility`, with `LiveStudioBundle` detecting the compound extension so `.scene.lsb` (scene) and `.avatar.lsb` (avatar) bundles share one pipeline.
- `CaptureCameraController` exposes a `channelIndex` property (with `CAMERA_CHANNELINDEX` localization), so a `CaptureCameraTracker` can choose which capture-camera channel (0 or 1) it follows.
- `HumanoidPoseData` now carries a per-bone tracking presence (`bonePresences`, 0..1). `AvatarBodyDriver`'s pose job blends the mocap pose over the avatar's animation per bone by this weight: a bone with presence 1 takes the mocap rotation fully, presence 0 leaves the animation flowing through, and values in between slerp between the two. This lets untracked body parts keep playing their `AnimatorController` animation while tracked parts follow the mocap.

### Changed

- `ExternalAvatarSource`'s file selector now filters the compound `.avatar.lsb` extension (was `.lsb`) to distinguish avatar bundles from `.scene.lsb`; the legacy `.lsavatar` extension is still accepted.
- Avatar frames now carry multiple capture-camera channels per frame (one `CameraData` per channel), so the worldA / worldB cameras are conveyed independently.
- `VRM1Avatar` and `VRCFTAvatar` now drive body animation through a `PlayableGraph` (new shared `AvatarBodyDriver`) instead of writing the humanoid bone transforms directly every frame. An optional `AnimatorController` on the avatar is wrapped into the graph: its animation (e.g. an idle or range-of-motion clip) plays through while tracking is lost and is overwritten by the mocap pose while tracking. Shared tracking-state handling, mesh visibility, and the avatar-build boilerplate were extracted into `AvatarBodyDriver` and `AvatarBuildNotifier.BuildAndNotify`.

### Fixed

- `VRM1Avatar` / `VRCFTAvatar` now resolve their `Animator` lazily in `BuildAvatar`, which can run before `Start` during a synchronous `.lsavatar` load and previously left the animator null and logged an error.
- `AvatarBodyDriver` now disables `applyRootMotion` on the wrapped `Animator`, so a controller's root motion no longer sinks the whole avatar to the floor (VRCFT avatars).

## [0.21.3] - 2026-06-12

### Changed

- The exposed `Light`'s shadow toggle now enables soft shadows (`LightShadows.Soft`) instead of hard shadows, both in the property setter and on deserialize, for smoother shadow edges.

## [0.21.2] - 2026-06-11

### Added

- `ChildProcessHost.RequestCloseAndRelease` posts WM_CLOSE to a windowed child without a quit signal and releases it immediately, without waiting for exit.

### Changed

- Quitting Studio no longer blocks waiting for child processes to exit. The Fusion process is signaled to quit and released immediately (`RequestStopAndRelease`), and the Remote app is asked to close (WM_CLOSE) and released immediately (`RequestCloseAndRelease`), instead of each blocking up to 5 seconds on `WaitForExit`.
- The Studio build output folder and exe name are now derived from the project folder name, dropping the dedicated VRC build entry point and the App VRC build profile path.

### Fixed

- `SkyboxBackground` now logs an error instead of propagating a null shader when the Skybox/Cubemap shader is stripped from a player build (it runs during `ExposedObjectContainer.Initialize` and must not throw).

## [0.21.1] - 2026-06-09

### Added

- `ExternalAvatarSource` can now import avatars from AssetBundle files (`.lsavatar`) in addition to VRM, chosen automatically by file extension.

### Changed

- Renamed `VRMAvatarSource` to `ExternalAvatarSource` (the former exposed name is preserved for existing scenes). The avatar load API `RequestLoadVRM` was renamed to `RequestLoad` across `IAvatarService`, `AvatarController`, and `AvatarService`.
- Generalized the model file field label and scoped its help text to VRM-only guidance.

## [0.21.0] - 2026-06-08

### Fixed

- Windows builds could leave a lingering background process after quitting, caused by a native shutdown hang related to Windows.Gaming.Input. A watchdog (`QuitTerminationGuard`) now force-terminates the process if graceful shutdown stalls.

## [0.20.12] - 2026-06-04

### Added

- Eye gaze is now delegated to the VRM10 `LookAt` component, so the avatar's eyes follow the configured look-at target instead of being driven directly.

## [0.20.11] - 2026-06-03

### Added

- `ChildProcessQuitSignal`: signals a specific child process (keyed by its PID) to quit gracefully via a Windows named event, so a windowless child can run its own save-on-quit instead of being hard-killed.
- "Assets/Lilium Live Studio/Remove Missing Scripts" context-menu command that removes missing scripts from the selected prefab assets.

### Changed

- Stopping a windowless child process now signals it to quit and returns immediately without waiting for it to exit (`ChildProcessHost.RequestStopAndRelease`).

### Fixed

- The package Readme inspector buttons (language toggle, section actions, Install) are now clickable even when the package is consumed as an immutable git/registry dependency.

## [0.20.10] - 2026-06-02

### Added

- `WindowsFirewall` helper that registers inbound UDP allow rules via `netsh`. Rules are keyed by the listening port (not the program path), so a single rule survives the tool being relocated, reinstalled, or shipped from a different package-cache path. Elevation is requested only when the rule is missing.

## [0.20.9] - 2026-06-02

### Changed

- RemoteControl handlers are now registered per-instance with their routes declared in the constructor.

## [0.19.1] - 2026-04-29

### Added

- Initial release. Split out from `jp.lilium.virgo.studio` to separate VTuber-app generic infrastructure (Camera / Lighting / Scene / Build / RemoteControl base / shared Localization) from VirgoMotion-specific motion reception and avatar control.
