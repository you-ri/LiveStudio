// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.SceneManagement;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;
using Lilium.RemoteControl.Notification;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Manages a polymorphic list of external file assets (<see cref="AssetBase"/>) at runtime,
    /// unifying prop and avatar loading behind one manager. Each entry is a
    /// concrete asset kind — <see cref="PropAsset"/> (*.prop.lsb / *.glb / *.gltf) or
    /// <see cref="AvatarAsset"/> (*.avatar.lsb / *.vrm) — that knows how to load and unload itself.
    /// The array is editable from the remote app and persisted to the live scene JSON; the per-element
    /// <c>@type</c> discriminator round-trips the concrete kinds.
    ///
    /// Two reconciliation models run side by side:
    /// <list type="bullet">
    ///   <item>Additive assets (props): each entry loads/unloads independently from its
    ///   <see cref="AssetBase.enabled"/> flag.</item>
    ///   <item>Exclusive assets (avatars): a single-selection group — enabling one disables the
    ///   others, since the scene holds exactly one avatar.</item>
    /// </list>
    ///
    /// Avatar-attached props live under the avatar, so swapping the avatar destroys them; this manager
    /// listens for <see cref="IAvatarService.onAvatarChanged"/> and reloads them onto the new avatar.
    ///
    /// This manager is the single source of truth for avatar selection: the active avatar is the enabled
    /// exclusive <see cref="AvatarAsset"/> in <c>assets</c>, persisted here. <see cref="ExternalAvatarSource"/>
    /// holds no persisted file path of its own — it is just the loader the selected avatar asset drives.
    /// </summary>
    [Serializable]
    [LiveClass(Icon = "deployed_code", Category = "Asset", HideInScene = true, lane = FrameLane.None)]
    public class ExternalAssetManager : ILiveObject
    {
        // lane = None on the class: the project's assets are what this machine has on disk, not what
        // the show did. The calls are the operator's housekeeping -- adding, removing, deleting a
        // file, saving a preset, opening a live scene -- and a take that held them would, on replay,
        // reach over and rewrite (DeleteAssetFile: delete) files on the machine playing it back.
        //
        // ⚠ Absorbing, so `assets` goes with it: the writes into `assets[i]/enabled` are no longer
        // recorded either (see LiveProperty.offFrame). Deliberate, and it has a cost worth knowing.
        // Avatar selection is unaffected -- ExternalAvatarSource.selectedAvatar carries it as a view
        // on the state lane whoever writes it -- but **a take no longer says which props or which
        // stage were up**, because those writes were the only thing that said so. The entries are
        // not registered under ids of their own, so this collection was their only path into a frame.
        //
        // ⚠ Related, and older than this decision: the entries were never on the *state* lane
        // either. LiveObjectWalk.HoldsLiveObjectCollection asks LiveClass.Find about the *declared*
        // element type, and AssetBase deliberately carries no [LiveClass] (see its own remarks), so
        // the walk refuses the collection. The generator still emits a block for each concrete asset
        // type and nothing ever feeds it -- which is why AssetBase.OnEnabledApplied, written for
        // exactly that, does not run.
        const string kId = "a7d3f1e2-9c4b-4e85-b6a1-2f8c5d3e7b91";

        // Runtime singleton (one manager per scene, fixed id). Lets loaders / inspectors such as
        // ExternalAvatarSource surface the avatar selection without holding a hard reference.
        [NonSerialized]
        private static ExternalAssetManager _current;
        public static ExternalAssetManager current => _current;

        public string name { get; set; } = "Asset Manager";

        public LiveObjectHandle? liveObject => LiveObjectRegistry.FindByTarget(this);

        public string id => kId;

        // The catalog: every asset file this app knows about, rebuilt by the project crawl each run and
        // exposed as an editable polymorphic array. Nothing here is written to the live scene — the
        // catalog says which files exist, which is a fact about this machine, while what is out is said
        // by the show itself (the avatar by ExternalAvatarSource.selectedAvatar, the stage by
        // StageManager.activeSet / loadedSets, a prop by the scene instance being there). Persisting it
        // also had the deferred crawl populate the array after the save baseline was captured, marking
        // the scene unsaved on every launch and blocking quit.
        [NonSerialized]
        [LiveField(persistable = false)]
        private AssetBase[] assets = Array.Empty<AssetBase>();

        // Currently-loaded additive (non-exclusive) assets, tracked so entries removed from the array
        // while still loaded can be detected and unloaded.
        [NonSerialized]
        private readonly List<AssetBase> _loaded = new List<AssetBase>();

        // Id of the exclusive asset (avatar) currently selected, or null. Drives radio reconciliation.
        [NonSerialized]
        private string _selectedExclusiveId;

        [NonSerialized]
        private bool _dirty;

        [NonSerialized]
        private bool _initialized;

        // One-shot guard so the project manager's pending folder crawl runs once, after the live scene
        // (if any) has been restored in Start. Re-armed (set false) on every live scene restore so the
        // catalog is rebuilt after the used assets are applied.
        [NonSerialized]
        private bool _assetManagerReadyNotified;

        /// <summary>
        /// Raised whenever the <c>assets</c> array or an entry's load state changes (every
        /// <see cref="_Broadcast"/>). <see cref="StageManager"/> subscribes to rebuild its projected
        /// set view from the <see cref="SetBundleAsset"/> entries here.
        /// </summary>
        public event Action onAssetsChanged;

        public void OnEnable()
        {
            _current = this;
            // Resolve deferred external-prop @prefab instances (queued by the live-scene restore before this
            // manager's crawl registered their asset) through this manager.
            PendingPrefabStore.SetProvider(ResolveInstancePrefabForKey);
            LiveObjectRegistry.Create<ExternalAssetManager>(this, kId);
            LiveClass.Get<ExternalAssetManager>().onPropertyChanged += _OnPropertyChanged;

            RemoteControlBehaviour.onBaseSceneReloaded += _OnBaseSceneReloaded;

            _initialized = true;

            // Register app-embedded built-in assets (e.g. Resources animation clips) in the asset registry
            // and inject their catalog entries, so they list on the project asset page and resolve in
            // selectors even before the project crawl runs.
            BuiltinAssetRegistry.EnsureRegistered();
            _EnsureBuiltinAssets();
        }

        public void OnDisable()
        {
            _dirty = false;
            _initialized = false;

            RemoteControlBehaviour.onBaseSceneReloaded -= _OnBaseSceneReloaded;

            LiveClass.Get<ExternalAssetManager>().onPropertyChanged -= _OnPropertyChanged;

            _UnloadAllAdditive();

            LiveObjectRegistry.FindByTarget(this)?.Unregister();

            if (_current == this) _current = null;
        }

        public void OnDispose()
        {
            OnDisable();
        }

        public void Update()
        {
            // Once the live scene (if any) has been restored in Start, let the project manager run its
            // pending folder crawl so discovered assets merge on top of the restored set.
            if (Application.isPlaying && !_assetManagerReadyNotified)
            {
                _assetManagerReadyNotified = true;
                ProjectManager.OnAssetManagerReady();
            }

            if (!_dirty) return;
            if (!_initialized) { _dirty = false; return; }
            _dirty = false;
            _ApplyDiff();
        }

        public void Reset()
        {
        }

        /// <summary>
        /// Imports an external asset into the open project folder and loads it. Invoked from the remote
        /// app after the user picks a file: the picked file is copied into a kind-specific subfolder of
        /// the project (e.g. Avatars / Props / Sets) and the registered entry points at the in-project
        /// copy, so the project folder is the single home for the asset. A file already inside the
        /// project folder is registered in place (no copy). The concrete asset kind is chosen by file
        /// extension.
        ///
        /// Only the single picked file is copied; assets that reference sibling files (e.g. a *.gltf with
        /// an external .bin / textures) are not yet bundled — that is a future per-extension conversion
        /// step. For now the import is a plain copy.
        /// </summary>
        [LiveFunction]
        public void AddAsset(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[LiveStudio] Asset path cannot be empty.");
                return;
            }
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] Asset file not found: {filePath}");
                return;
            }

            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
            {
                Debug.LogError("[LiveStudio] AddAsset: no project folder is open to import the asset into.");
                return;
            }

            var sourcePath = filePath;

            // A file already inside the project folder is part of the project: register it in place (like
            // the crawl) rather than copying it onto itself.
            if (IsInsideProject(sourcePath, projectPath))
            {
                _RegisterImported(sourcePath);
                return;
            }

            // Copy the source into its kind's subfolder under the project, using a non-colliding name.
            var subfolder = AssetTypeRegistry.ResolveImportSubfolder(sourcePath);
            var destFolder = string.IsNullOrEmpty(subfolder) ? projectPath : Path.Combine(projectPath, subfolder);
            string destPath;
            try
            {
                Directory.CreateDirectory(destFolder);
                destPath = _UniqueFilePath(destFolder, Path.GetFileName(sourcePath));
                File.Copy(sourcePath, destPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] AddAsset: failed to import '{sourcePath}': {e.Message}");
                return;
            }

            _RegisterImported(destPath);
        }

        // Registers a project-folder file as an enabled entry and schedules its load. Re-crawls so the
        // catalog stays consistent.
        private void _RegisterImported(string projectFilePath)
        {
            // An already-registered project file (e.g. picked again) is just (re)enabled, not duplicated.
            var existing = _Find(_MakeId(projectFilePath));
            if (existing != null)
            {
                if (!existing.enabled)
                {
                    existing.enabled = true;
                    _dirty = true;
                }
                _Broadcast();
                return;
            }

            AssetBase asset = _CreateEntry(projectFilePath, enabled: true);
            if (asset == null)
            {
                Debug.LogError($"[LiveStudio] Unsupported asset file: {projectFilePath}");
                return;
            }

            var list = new List<AssetBase>(assets) { asset };
            assets = list.ToArray();

            _dirty = true;
            // Keep the project catalog in sync (dedups by id, so the just-added entry is untouched).
            ProjectManager.RecrawlProject();
            _Broadcast();
        }

        /// <summary>
        /// True if the file extension maps to a supported asset kind (bundle / VRM / glTF). Lets a
        /// project crawler classify files by path alone, without reading their contents.
        ///
        /// An avatar's own settings file is the exception: it is a preset by shape, but it belongs to the
        /// avatar beside it rather than being an entry of its own, and listing both would show one avatar
        /// twice (see <see cref="AvatarPresetFile"/>).
        /// </summary>
        public static bool IsSupportedAssetFile(string filePath)
        {
            if (AvatarPresetFile.IsAutoPreset(filePath)) return false;
            return AssetTypeRegistry.IsSupported(filePath);
        }

        /// <summary>
        /// Registers each supported file as a path-only, disabled (unloaded) entry — the content is not
        /// read until the entry is enabled. Re-adding a known file keeps its existing entry/state (dedup
        /// by path). Catalog-only entries (disabled and unloaded) whose file is absent from the supplied
        /// set are pruned, so the list stays in sync with the project folder. Used by the project crawler.
        /// </summary>
        public void RegisterDiscoveredAssets(IReadOnlyList<string> filePaths)
        {
            var discovered = new HashSet<string>();
            if (filePaths != null)
            {
                for (int i = 0; i < filePaths.Count; i++)
                {
                    if (string.IsNullOrEmpty(filePaths[i])) continue;
                    discovered.Add(_MakeId(filePaths[i]));
                }
            }

            var list = new List<AssetBase>(assets);
            bool changed = false;

            // Prune stale catalog-only entries (disabled + unloaded) no longer present in the folder.
            // Enabled or loaded entries are user-curated and left untouched.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var entry = list[i];
                if (entry == null) continue;
                if (entry.isBuiltin) continue; // app-embedded, not a project file — never pruned by the crawl.
                if (entry.enabled || entry.isLoaded) continue;
                if (discovered.Contains(entry.id)) continue;
                list.RemoveAt(i);
                changed = true;
            }

            // Track ids already present so each discovered file is added at most once.
            var existing = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null) existing.Add(list[i].id);
            }

            if (filePaths != null)
            {
                for (int i = 0; i < filePaths.Count; i++)
                {
                    var path = filePaths[i];
                    if (string.IsNullOrEmpty(path)) continue;
                    var id = _MakeId(path);
                    if (!existing.Add(id)) continue; // already registered or duplicate within this batch

                    var entry = _CreateEntry(path, enabled: false);
                    if (entry == null) { existing.Remove(id); continue; }
                    list.Add(entry);
                    changed = true;
                }
            }

            if (changed)
            {
                assets = list.ToArray();
                _Broadcast();
            }

            // Keep the app-embedded built-in entries present alongside the crawled catalog (idempotent;
            // broadcasts only if it actually adds any).
            _EnsureBuiltinAssets();

            // Announce the catalog even when nothing moved: a listener that derives its own state from the
            // project's files (the operations page's decks) has to be able to converge after any crawl,
            // including the first one, where its own view is empty and this list is already complete.
            onCatalogChanged?.Invoke();
        }

        /// <summary>
        /// Raised after a project crawl has reconciled the catalog. For features whose own state is derived
        /// from the set of files in the project rather than from the live scene — the deck files behind the
        /// operations page's tabs — this is when to re-derive it. The catalog itself is the argument:
        /// listeners read <see cref="assetsView"/>.
        ///
        /// Static because listeners (plain <c>ILiveObject</c>s) come and go independently of the manager
        /// instance; subscribe on enable and unsubscribe on disable.
        /// </summary>
        public static event Action onCatalogChanged;

        // Cleared at runtime startup so a subscriber from a previous play session never stays attached
        // when Domain Reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _ResetCatalogEvent() => onCatalogChanged = null;

        /// <summary>
        /// Injects the app-embedded (built-in) catalog assets — reference resources (e.g. Resources
        /// animation clips) and loadable ones (e.g. Resources prop prefabs) — into the live list when not
        /// already present. Built-in entries are not project-folder files, so the crawl never discovers
        /// them; they are added here and protected from the crawl's prune (<see cref="AssetBase.isBuiltin"/>).
        /// Cheap and idempotent — dedups by id (a loadable built-in restored from the live scene therefore
        /// wins over the fresh catalog entry, keeping its enabled state / objectId) and only broadcasts when
        /// it adds something. Called at init and after every rebuild of <c>assets</c> (crawl / restore).
        /// </summary>
        private void _EnsureBuiltinAssets()
        {
            // Retry any deferred external-prop @prefab instances now that the asset set may have changed
            // (crawl / restore); fire-and-forget, and a no-op until the owning asset is registered.
            _ = PendingPrefabStore.DrainAsync();

            // Two built-in sources: catalog-baked Resources assets (props / clips) and the scenes the
            // project declares as sets. Both are app-embedded, so both are injected here and protected from
            // the crawl's prune; a set is declared by hand rather than baked because a scene cannot live
            // under Resources for the baker to find.
            var catalogAssets = BuiltinAssetRegistry.GetAssets();
            var declaredSets = BuiltinSetSource.GetSets();

            // A built-in set restored from a saved scene carries only its GUID, plus whatever name it went by
            // when it was saved. Re-read the rest from the declaration first: the de-dup below keeps the
            // restored entry (for its enabled state), so without this a set renamed in the editor would keep
            // the old name, and one whose scene moved would keep a path that no longer loads.
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is BuiltinSetAsset set) set.RefreshFromDeclaration();
            }

            if (catalogAssets.Count == 0 && declaredSets.Count == 0) return;

            var existing = new HashSet<string>();
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && !string.IsNullOrEmpty(assets[i].id)) existing.Add(assets[i].id);
            }

            List<AssetBase> list = null;
            _AddBuiltins(catalogAssets, existing, ref list);
            _AddBuiltins(declaredSets, existing, ref list);
            if (list == null) return; // all built-ins already present.

            assets = list.ToArray();
            _Broadcast();
        }

        // Appends each not-yet-present built-in (deduped by id) from `builtins`, lazily copying the live
        // `assets` array into `list` on the first addition. A restored loadable built-in (already in
        // `assets` under the same id) is therefore kept, not duplicated.
        private void _AddBuiltins(IReadOnlyList<AssetBase> builtins, HashSet<string> existing, ref List<AssetBase> list)
        {
            for (int i = 0; i < builtins.Count; i++)
            {
                var builtin = builtins[i];
                if (builtin == null || string.IsNullOrEmpty(builtin.id) || existing.Contains(builtin.id)) continue;
                (list ??= new List<AssetBase>(assets)).Add(builtin);
                existing.Add(builtin.id);
            }
        }

        /// <summary>
        /// Raises the avatar with the given display name, dropping to the default one when empty.
        ///
        /// Exposed so the operation that does this from a deck tile has a remote address to be
        /// recorded under: an operation with no address can be applied but not replayed. Selection
        /// by name rather than by id because that is what the operator bound the key to, and ids
        /// change when a project is rebuilt.
        /// </summary>
        [LiveFunction]
        public void SelectAvatarByName(string avatarName)
            => AvatarSelection.SelectByName(this, avatarName);

        /// <summary>Unloads (if loaded) and removes the entry with the given id.</summary>
        [LiveFunction]
        public void RemoveAsset(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return;

            var asset = _Find(assetId);
            // Built-in assets are app-embedded and re-injected each run; removing one would only make it
            // reappear on the next crawl. Reject the request so the list stays consistent.
            if (asset != null && asset.isBuiltin) return;
            if (asset != null)
            {
                if (asset.isExclusive)
                {
                    // Removing the selected avatar drops the selection back to the default avatar.
                    if (_selectedExclusiveId == asset.id)
                    {
                        asset.Unload(_MakeContext());
                        _selectedExclusiveId = null;
                    }
                }
                else if (asset.isLoaded)
                {
                    asset.Unload(_MakeContext());
                    _loaded.Remove(asset);
                }
            }

            var list = new List<AssetBase>(assets);
            list.RemoveAll(e => e != null && e.id == assetId);
            assets = list.ToArray();

            _Broadcast();
        }

        /// <summary>
        /// Permanently deletes an asset's backing file from disk, then unloads and removes the entry.
        /// The remote app drives this, but the file IO runs here (Studio) so it works even when the
        /// remote app is on a different machine. Restricted to the kinds the app itself authors — presets
        /// (<c>*.preset.json</c>), decks (<c>*.deck.json</c>) and snapshots (<c>*.snapshot.json</c>) —
        /// since those are the ones a user creates from the remote app and therefore expects to remove from
        /// there too. Imported source files (avatars, props, bundles) are rejected: deleting what the user
        /// dragged in is the file manager's job. Which files go with the entry is the kind's own business
        /// (see <see cref="AssetBase.DeleteFiles"/>) — a snapshot takes its thumbnail with it.
        /// </summary>
        [LiveFunction]
        public void DeleteAssetFile(string assetId)
        {
            var asset = _Find(assetId);
            if (asset == null)
            {
                Debug.LogError($"[LiveStudio] DeleteAssetFile: asset '{assetId}' not found.");
                return;
            }
            // Which side a kind falls on is the kind's own answer; the manager does not list them.
            if (!asset.isAppOwnedFile)
            {
                Debug.LogError($"[LiveStudio] DeleteAssetFile: only app-created files can be deleted (got '{asset.filePath}').");
                return;
            }

            try
            {
                asset.DeleteFiles();
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] DeleteAssetFile: failed to delete '{asset.filePath}': {e.Message}");
                return;
            }

            // Unload (if loaded) and drop the entry from the list.
            RemoveAsset(assetId);
        }

        /// <summary>
        /// Opens the live scene with the given id. Unlike load/unload, this replaces the whole app state
        /// (the scene JSON deserializes over every exposed object, including this `assets` array), so the
        /// project folder is re-crawled afterwards to restore the available-file listing — done by
        /// re-arming the one-shot ready hook so the next <see cref="Update"/> re-runs the crawl once the
        /// restore has settled (same deferred path as startup).
        /// </summary>
        [LiveFunction]
        public void OpenLiveScene(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return;

            var asset = _Find(assetId);
            if (asset is LiveSceneAsset scene)
            {
                scene.Open();
                _assetManagerReadyNotified = false;
            }
            else if (asset != null)
            {
                Debug.LogError($"[LiveStudio] Asset is not a live scene: {assetId}");
            }
        }

        /// <summary>
        /// Read-only view of the managed assets, so <see cref="StageManager"/> can project the
        /// <see cref="SetBundleAsset"/> entries into its set view without owning the array.
        /// </summary>
        public IReadOnlyList<AssetBase> assetsView => assets;

        /// <summary>
        /// The single-selection asset that is out, or is about to be: what the next reconcile will
        /// settle on, which for everything except the moment between a request and its diff is what
        /// is already out.
        ///
        /// This is the answer to "which one is selected", and reading the enabled flags instead is
        /// not. A request raises the chosen asset and leaves lowering the others to the reconcile,
        /// so between the two there are two raised flags and whichever comes first in the list wins
        /// -- an answer that depends on the order assets happen to be registered in, and that says
        /// the old one right after someone picked the new one. A write recorded through such a
        /// getter goes into the file naming an asset nobody chose.
        /// </summary>
        public AssetBase selectedExclusive => _PickDesiredExclusive();

        /// <summary>Returns the asset with the given id, or null. Used to reach a loaded scene handle.</summary>
        public AssetBase FindAsset(string assetId) => _Find(assetId);

        /// <summary>
        /// The portable, project-relative reference (forward slashes) for the asset with the given runtime
        /// <see cref="AssetBase.id"/> (an absolute path), so callers can persist a stable cross-machine handle
        /// instead of the machine-specific id — e.g. a deck tile's background. Mirrors the persisted
        /// <see cref="AssetBase.path"/>. Returns the input unchanged when the asset is unknown or no relative
        /// path can be formed (file outside the project / different drive). Resolve it back with
        /// <see cref="FindAssetByReference"/>.
        /// </summary>
        public string GetPortableAssetReference(string assetId)
        {
            var asset = _Find(assetId);
            if (asset == null) return assetId;
            var relative = PropPreset.Relativize(asset.filePath, ProjectManager.projectPath);
            return string.IsNullOrEmpty(relative) ? assetId : relative;
        }

        /// <summary>
        /// Finds an asset by either its runtime absolute <see cref="AssetBase.id"/> (the remote app's
        /// within-session handle) or a persisted project-relative reference (see
        /// <see cref="GetPortableAssetReference"/>). An absolute reference matches directly; a relative one is
        /// resolved against the project folder before matching. Legacy absolute references stored before the
        /// portable form still resolve. Returns null when nothing matches.
        /// </summary>
        public AssetBase FindAssetByReference(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            var direct = _Find(reference);
            if (direct != null) return direct;
            // A project-relative reference: resolve to an absolute path, then to the normalized id.
            // PropPreset.ResolveSource returns a rooted path verbatim, so an absolute reference that failed
            // the direct match above is simply re-normalized here (handles slash differences).
            var abs = PropPreset.ResolveSource(reference, ProjectManager.projectPath);
            return _Find(_MakeId(abs));
        }

        /// <summary>
        /// Resolves a live-scene <c>@prefab</c> key (an <see cref="IInstantiableProp.instanceKey"/>) to its
        /// root prefab, loading the owning bundle if needed, so a deferred instance can be created once its
        /// asset has registered. Static so a single registration routes to whichever manager is current.
        ///
        /// The one answer to "what prefab is this key", shared by the two things that ask: the live scene's
        /// deferred restore (<see cref="PendingPrefabStore"/>) and a replay standing an instance back up
        /// (<see cref="PropInstanceRecipeResolver"/>). Two resolutions would be two ways for a saved scene
        /// and a recording to disagree about what an instance is.
        /// </summary>
        internal static Task<GameObject> ResolveInstancePrefabForKey(string key)
        {
            var prop = FindInstantiableProp(key);
            return prop != null ? prop.LoadInstancePrefabAsync() : Task.FromResult<GameObject>(null);
        }

        /// <summary>
        /// The prop a live-scene <c>@prefab</c> key names, or null when no registered asset claims it.
        ///
        /// Separate from the load above because "no asset owns this key" and "the owner could not produce a
        /// prefab" are different answers, and a caller that has to tell them apart cannot: both come back as
        /// a task holding null. A replay uses the difference — an unclaimed key belongs to somebody else,
        /// while a failed load is this manager's and must not be attempted again every frame.
        /// </summary>
        internal static IInstantiableProp FindInstantiableProp(string key)
        {
            var manager = _current;
            if (manager == null || string.IsNullOrEmpty(key)) return null;

            // Match by instanceKey (a built-in prop's catalog GUID or an external prop's portable reference).
            var assets = manager.assets;
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is IInstantiableProp p && p.supportsInstancing && p.instanceKey == key)
                    return p;
            }
            // Fall back to reference resolution so path-variant keys (absolute vs project-relative) still match.
            if (manager.FindAssetByReference(key) is IInstantiableProp byRef && byRef.supportsInstancing)
                return byRef;
            return null;
        }

        // Builds a non-colliding path for fileName inside folder, inserting " (n)" before the extension
        // when a file of that name already exists. The "extension" is everything after the asset kind's
        // base name, so compound suffixes (e.g. ".avatar.lsb") are preserved; unknown kinds fall back to
        // the last extension.
        private static string _UniqueFilePath(string folder, string fileName)
        {
            var path = Path.Combine(folder, fileName);
            if (!File.Exists(path)) return path;

            var baseName = AssetTypeRegistry.DeriveName(fileName);
            var suffix = fileName.Substring(baseName.Length); // leading '.' / compound suffix, or empty
            int counter = 1;
            do
            {
                path = Path.Combine(folder, $"{baseName} ({counter}){suffix}");
                counter++;
            } while (File.Exists(path));
            return path;
        }

        /// <summary>
        /// Sets an asset's desired <see cref="AssetBase.enabled"/> state and schedules a diff, so a
        /// facade such as <see cref="StageManager"/> can drive load/unload through the same pipeline as
        /// a direct remote-app edit. No-op when the id is unknown or the value is unchanged.
        /// </summary>
        public void SetAssetEnabled(string assetId, bool value)
        {
            var asset = _Find(assetId);
            if (asset == null || asset.enabled == value) return;
            asset.enabled = value;
            _dirty = true;
            _Broadcast();
        }

        /// <summary>
        /// Says the desired state of some asset has moved and the world has not caught up yet.
        ///
        /// Every path that changes <see cref="AssetBase.enabled"/> has to say so, because what the
        /// flag means is "load this" and nothing loads until the diff runs. A remote write goes
        /// through <see cref="SetAssetEnabled"/> above; a replay writes the flag straight into the
        /// object off the state lane, which is a store and nothing else -- so it says so here
        /// instead (<c>AssetBase.OnEnabledApplied</c>).
        ///
        /// ⚠ Without this a replay restores the flag, the getter that reports which avatar is out
        /// starts answering with the recorded one, and the write that would have loaded it is
        /// skipped as "no change" -- the value comes back and the avatar does not.
        /// </summary>
        public void MarkAssetsDirty() => _dirty = true;

        private void _OnPropertyChanged(LiveProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains(nameof(assets))) return;
            _dirty = true;
        }

        // A persistent host reloaded the base scene: every loaded asset's GameObject was destroyed with
        // it, but this manager (and its container entries) survived. Drop the now-dangling additive
        // wrappers — Unload removes each from the container, so a stale reference is never serialized by
        // the re-deserialize that follows — and forget the exclusive (avatar) selection so the reconcile
        // reloads it. The pending diff then reloads every enabled asset onto the freshly loaded scene.
        private void _OnBaseSceneReloaded()
        {
            if (!Application.isPlaying) return;
            _UnloadAllAdditive();
            _selectedExclusiveId = null;
            _dirty = true;
        }

        /// <summary>
        /// Brings the actual loaded assets in line with the desired <see cref="AssetBase.enabled"/>
        /// flags: additive assets load/unload independently, exclusive assets reconcile as a group.
        /// </summary>
        private void _ApplyDiff()
        {
            // Additive assets (sets).
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null || string.IsNullOrEmpty(asset.id)) continue;
                if (asset.isExclusive) continue;
                if (asset.busy) continue;
                // A catalog of files that are put out as scene objects of their own (props) has nothing
                // to load here; the scene holds the instances.
                if (!asset.isLoadable) continue;

                if (asset.enabled && !asset.isLoaded)
                {
                    _ = _LoadAdditiveAsync(asset);
                }
                else if (!asset.enabled && asset.isLoaded)
                {
                    asset.Unload(_MakeContext());
                    _loaded.Remove(asset);
                    _Broadcast();
                }
            }

            // Additive assets removed from the array while still loaded: unload them.
            for (int i = _loaded.Count - 1; i >= 0; i--)
            {
                var asset = _loaded[i];
                if (asset.busy) continue;
                if (Array.IndexOf(assets, asset) >= 0) continue;
                asset.Unload(_MakeContext());
                _loaded.RemoveAt(i);
                _Broadcast();
            }

            // Exclusive assets (avatars): single-selection reconcile.
            _ReconcileExclusive();
        }

        private async Task _LoadAdditiveAsync(AssetBase asset)
        {
            asset.busy = true;

            // A replay waits here. The recording carries the write that asked for this asset, not
            // the asset, so the frames behind it address an object that only exists once the load
            // is done -- and how long that takes is a property of this machine, not of the take.
            Lilium.RemoteControl.Frames.FrameGate.HoldSupply(kLoadHold);
            try
            {
                await asset.LoadAsync(_MakeContext());
                if (asset.isLoaded && !_loaded.Contains(asset)) _loaded.Add(asset);
            }
            finally
            {
                Lilium.RemoteControl.Frames.FrameGate.ReleaseSupply(kLoadHold);
                asset.busy = false;
                _Broadcast();
            }
        }

        /// <summary>What a stalled replay is waiting on, for a viewer to show.</summary>
        private const string kLoadHold = "asset load";

        /// <summary>
        /// Runs an exclusive (avatar) load, holding a replay until it finishes.
        ///
        /// The task is not awaited by the caller -- the swap is requested and the reconcile carries
        /// on -- but a replay still must not run past it, so the hold is taken here and given back
        /// when the load settles either way.
        /// </summary>
        private async void _LoadExclusiveHeld(Task load)
        {
            Lilium.RemoteControl.Frames.FrameGate.HoldSupply(kLoadHold);
            try
            {
                await load;
            }
            catch (Exception e)
            {
                // Reported rather than swallowed: nothing else is awaiting this task, so a failure
                // here would otherwise be invisible -- and the asset itself has already marked
                // whatever it needed to.
                Debug.LogError($"[LiveStudio] Avatar load failed: {e}");
            }
            finally
            {
                Lilium.RemoteControl.Frames.FrameGate.ReleaseSupply(kLoadHold);
            }
        }

        // Reconciles the exclusive (avatar) group to a single selection: load the desired avatar (or
        // reset to default when none is desired) and turn the others off.
        private void _ReconcileExclusive()
        {
            var desired = _PickDesiredExclusive();
            var desiredId = desired?.id;
            if (desiredId == _selectedExclusiveId) return;

            if (desired != null)
            {
                // LoadAsync is synchronous for avatars (delegates to AvatarService), so the swap is
                // requested immediately; the previous avatar is replaced in place — no reset needed.
                _LoadExclusiveHeld(desired.LoadAsync(_MakeContext()));
            }
            else
            {
                _ResetExclusive();
            }

            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null || !asset.isExclusive) continue;
                if (asset.id == desiredId)
                {
                    // Keep whatever LoadAsync set for the selected avatar. A synchronous failure (e.g. a
                    // built-in avatar whose Resources prefab is missing) calls MarkLoadFailed, clearing
                    // isLoaded/enabled; forcing isLoaded back to true here would persist a broken entry
                    // (enabled=false but isLoaded=true bypasses the not-in-use persist skip) and never fall
                    // back to the default avatar. Success paths already set isLoaded=true themselves.
                    continue;
                }
                asset.isLoaded = false;
                if (asset.enabled) asset.enabled = false; // radio: turn the others off
            }

            // The desired avatar failed to load synchronously (LoadAsync cleared its enabled flag): re-arm
            // a diff so the exclusive group reconciles back to the default avatar on the next pass.
            if (desired != null && !desired.enabled) _dirty = true;

            _selectedExclusiveId = desiredId;
            _Broadcast();
        }

        // The exclusive asset that should be selected: a newly-enabled one wins over the current
        // selection; otherwise the still-enabled current selection is kept; otherwise none.
        private AssetBase _PickDesiredExclusive()
        {
            AssetBase keep = null;
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null || !asset.isExclusive || !asset.enabled) continue;
                if (asset.id == _selectedExclusiveId) { keep = asset; continue; }
                return asset; // newly enabled
            }
            return keep;
        }

        private void _ResetExclusive()
        {
            // Reset through any one exclusive asset (the call targets the shared avatar slot).
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && assets[i].isExclusive)
                {
                    assets[i].Unload(_MakeContext());
                    return;
                }
            }
        }

        private void _UnloadAllAdditive()
        {
            var context = _MakeContext();
            for (int i = 0; i < _loaded.Count; i++) _loaded[i].Unload(context);
            _loaded.Clear();
        }

        private AssetLoadContext _MakeContext()
        {
            return new AssetLoadContext
            {
                avatarRoot = _AvatarRoot(),
                container = _ResolveContainer(),
            };
        }

        // Current avatar root transform, or null if no avatar is loaded.
        private Transform _AvatarRoot()
        {
            var service = SingletonService<IAvatarService>.subject;
            var target = service?.target;
            return target != null ? target.transform : null;
        }

        // The base-scene RemoteControlContainer that loaded props register into. A loaded prop's
        // GameObject lives in the (reloadable) base scene, so its wrapper belongs in that scene's
        // container: both are then torn down and rebuilt together on a base-scene reload, leaving no
        // dangling wrapper in the persistent host container (which would crash live-scene serialization
        // when accessed after its GameObject is destroyed). Prefers a container that is NOT in a loaded
        // set scene — i.e. the persistent (bootstrap) scene, the one not backed by any set asset.
        private RemoteControlContainer _ResolveContainer()
        {
            var all = RemoteControlContainer.all;
            RemoteControlContainer fallback = null;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) continue;
                if (fallback == null) fallback = c;
                if (!_IsSetScene(c.gameObject.scene)) return c;
            }
            return fallback;
        }

        // True when the scene is the loaded scene of a set asset (bundle or built-in). The set's own scene
        // handle is the source of truth, so this holds regardless of build index — a built-in set scene
        // (real build index, unlike a bundle set scene at -1) is recognized too. Lets _ResolveContainer
        // keep props out of a set scene without the old build-index heuristic.
        private bool _IsSetScene(Scene scene)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is ISetAsset s && s.hasScene && s.scene == scene) return true;
            }
            return false;
        }

        // Derives a stable entry id from the file path. Path separators are normalized so the same
        // file picked with different separators (\\ vs /) maps to one id; the path is otherwise kept
        // verbatim (the id is only used as an equality key / function argument, never as a URL segment).
        private static string _MakeId(string filePath)
        {
            return filePath.Replace('\\', '/');
        }


        private AssetBase _Find(string assetId)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && assets[i].id == assetId) return assets[i];
            }
            return null;
        }

        /// <summary>
        /// True when <paramref name="filePath"/> resolves to a location inside the project folder. Used to
        /// register a picked file already in the project in place rather than copying it onto itself, and
        /// to decide whether a file is ours to write beside (<see cref="AvatarPresetFile"/>).
        /// </summary>
        internal static bool IsInsideProject(string filePath, string projectPath)
        {
            string full, root;
            try
            {
                full = Path.GetFullPath(filePath);
                root = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return false;
            }
            return string.Equals(full, root, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Builds a registered (but not yet loaded) entry for the file, or null if the extension is
        // unsupported. The caller appends it to `assets` and broadcasts. enabled=true means the diff
        // pass will load it; enabled=false registers it path-only (content read lazily on enable).
        private static AssetBase _CreateEntry(string filePath, bool enabled)
        {
            // The concrete asset kind is resolved by the registry (extension / content), so adding a new
            // kind needs no change here.
            var asset = AssetTypeRegistry.Create(filePath);
            if (asset == null) return null;

            asset.id = _MakeId(filePath);
            asset.name = AssetTypeRegistry.DeriveName(filePath);
            asset.filePath = filePath;
            asset.enabled = enabled;
            asset.isLoaded = false;
            // Assign the stable exposed-object id up front so it persists and is reused on every load.
            asset.objectId = Guid.NewGuid().ToString();
            return asset;
        }

        private void _Broadcast()
        {
            LivePropertyBroadcast.BroadcastProperty(this, "assets");
            onAssetsChanged?.Invoke();
        }
    }
}
