// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

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
    [ExposedClass(Icon = "deployed_code", Category = "Asset")]
    public class ExternalAssetManager : IExposedObject, IExposedDeserializeCallback, IExposedSerializeCallback
    {
        const string kId = "a7d3f1e2-9c4b-4e85-b6a1-2f8c5d3e7b91";

        // Runtime singleton (one manager per scene, fixed id). Lets loaders / inspectors such as
        // ExternalAvatarSource surface the avatar selection without holding a hard reference.
        [NonSerialized]
        private static ExternalAssetManager _current;
        public static ExternalAssetManager current => _current;

        public string name { get; set; } = "Asset Manager";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        // Added assets, exposed as an editable polymorphic array. The remote app toggles each entry's
        // `enabled` flag (load/unload) and removes entries. NOT persisted directly: the live list mixes
        // the project's disabled catalog (re-created by the project crawl) with the assets actually in
        // use; only the used ones are persisted, via the `_persistedAssets` shadow below. Persisting the
        // array directly would let the deferred crawl populate it after the live-scene save baseline is
        // captured, marking the scene unsaved on every launch (and blocking quit) — the shadow exists to
        // keep the crawl-built catalog out of the persisted state.
        [NonSerialized]
        [ExposedField(persistable = false)]
        private AssetBase[] assets = Array.Empty<AssetBase>();

        // Persistence shadow for `assets`: holds only the assets actually in use (enabled / loaded), so
        // the live scene JSON saves the selected avatar / loaded props / active world but NOT the
        // disabled project catalog. Refreshed in OnBeforeExposedSerialize; applied back to `assets` in
        // OnAfterExposedDeserialize (the catalog is then re-added by the project crawl).
        [NonSerialized]
        [ExposedField, Hide]
        private AssetBase[] _persistedAssets = Array.Empty<AssetBase>();

        // The `_persistedAssets` reference this manager last applied to / produced for the live set.
        // Lets OnAfterExposedDeserialize tell a real restore (FromJson replaces the reference) from an
        // unrelated property write (reference unchanged), so a plain enabled-toggle is not clobbered.
        [NonSerialized]
        private AssetBase[] _lastAppliedPersisted;

        // Currently-loaded additive (non-exclusive) assets, tracked so entries removed from the array
        // while still loaded can be detected and unloaded.
        [NonSerialized]
        private readonly List<AssetBase> _loaded = new List<AssetBase>();

        // Id of the exclusive asset (avatar) currently selected, or null. Drives radio reconciliation.
        [NonSerialized]
        private string _selectedExclusiveId;

        [NonSerialized]
        private IAvatarService _avatarService;

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
            ExposedObjectRegistry.Create<ExternalAssetManager>(this, kId);
            ExposedClass.Get<ExternalAssetManager>().onPropertyChanged += _OnPropertyChanged;

            if (Application.isPlaying)
            {
                _avatarService = SingletonService<IAvatarService>.subject;
                if (_avatarService != null) _avatarService.onAvatarChanged += _OnAvatarChanged;
            }

            RemoteControlBehaviour.onBaseSceneReloaded += _OnBaseSceneReloaded;

            // Match the current persisted reference so an unrelated property write before any real
            // restore does not trigger a (clobbering) swap.
            _lastAppliedPersisted = _persistedAssets;
            _initialized = true;
        }

        public void OnDisable()
        {
            _dirty = false;
            _initialized = false;

            RemoteControlBehaviour.onBaseSceneReloaded -= _OnBaseSceneReloaded;

            ExposedClass.Get<ExternalAssetManager>().onPropertyChanged -= _OnPropertyChanged;
            if (_avatarService != null) _avatarService.onAvatarChanged -= _OnAvatarChanged;
            _avatarService = null;

            _UnloadAllAdditive();

            ExposedObjectRegistry.FindByTarget(this)?.Unregister();

            if (_current == this) _current = null;
        }

        public void OnDispose()
        {
            OnDisable();
        }

        // True when _persistedAssets was just replaced by a live-scene restore (FromJson deserializes a
        // brand-new array) rather than left untouched by an unrelated property write. OnAfterExposedDeserialize
        // fires on EVERY exposed-property write on this manager, not only after a full restore, so without
        // this guard a plain enabled-toggle would re-apply the persisted set and wipe the live assets (and
        // the just-toggled selection). Reference identity is the signal: a restore brings in an array this
        // manager did not produce, whereas a property write leaves the reference OnBeforeExposedSerialize
        // last recorded into _lastAppliedPersisted.
        private bool _IsFreshRestore() => !ReferenceEquals(_persistedAssets, _lastAppliedPersisted);

        /// <summary>
        /// Fires after a live scene is restored. The used assets were deserialized into
        /// <see cref="_persistedAssets"/>; make them the live set, schedule a diff to (un)load them, and
        /// re-arm the crawl so the project catalog is re-added on the next <see cref="Update"/>.
        /// </summary>
        public void OnAfterExposedDeserialize()
        {
            if (!Application.isPlaying) return;

            // Apply the persisted set to the live `assets` array ONLY on a genuine restore, not on the
            // every-property-write firings of this callback (see _IsFreshRestore).
            if (!_IsFreshRestore()) return;

            _lastAppliedPersisted = _persistedAssets;
            // A persisted entry deserializes to null when its @type is no longer registered (e.g.
            // saved data referencing a removed/renamed asset kind). Compact those holes out so a null
            // never reaches the live array or a broadcast — consumers that project the list
            // (StageManager, the remote app) would otherwise dereference null and crash.
            assets = _WithoutNulls(_persistedAssets);
            _ResolveAssetPaths(assets);
            _dirty = true;
            _assetManagerReadyNotified = false;
        }

        /// <summary>
        /// Fires before persistence. Refreshes each loaded asset's <see cref="AssetBase.state"/> from its
        /// live values, then captures only the in-use assets (enabled / loaded) into
        /// <see cref="_persistedAssets"/> so the saved live scene excludes the disabled project catalog.
        /// </summary>
        public void OnBeforeExposedSerialize()
        {
            for (int i = 0; i < _loaded.Count; i++) _loaded[i].CaptureState();

            var projectPath = ProjectManager.projectPath;
            var used = new List<AssetBase>();
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null || (!asset.enabled && !asset.isLoaded)) continue;
                // Persist a portable, project-relative path so a saved scene survives moving the project
                // folder / opening it on another machine. The absolute id/filePath are not persisted
                // (persistable=false) and are reconstructed from this on load.
                asset.path = PropPreset.Relativize(asset.filePath, projectPath);
                used.Add(asset);
            }
            _persistedAssets = used.ToArray();
            // We produced this reference (not a restore); record it so OnAfterExposedDeserialize does
            // not re-apply it on a later property write.
            _lastAppliedPersisted = _persistedAssets;
        }

        public void Update()
        {
            // The avatar service may not have existed at OnEnable (load order). Bind late so prop
            // reloads track avatar swaps.
            if (Application.isPlaying && _avatarService == null)
            {
                _avatarService = SingletonService<IAvatarService>.subject;
                if (_avatarService != null)
                {
                    _avatarService.onAvatarChanged += _OnAvatarChanged;
                    _dirty = true; // a new avatar is available; (re)load enabled assets onto it.
                }
            }

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
        [ExposedFunction]
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
            if (_IsInsideProject(sourcePath, projectPath))
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
        /// </summary>
        public static bool IsSupportedAssetFile(string filePath)
        {
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

            if (!changed) return;
            assets = list.ToArray();
            _Broadcast();
        }

        /// <summary>Unloads (if loaded) and removes the entry with the given id.</summary>
        [ExposedFunction]
        public void RemoveAsset(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return;

            var asset = _Find(assetId);
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
        /// remote app is on a different machine. Restricted to preset files (<c>*.preset.json</c>) for
        /// now; other asset kinds are rejected.
        /// </summary>
        [ExposedFunction]
        public void DeleteAssetFile(string assetId)
        {
            var asset = _Find(assetId);
            if (asset == null)
            {
                Debug.LogError($"[LiveStudio] DeleteAssetFile: asset '{assetId}' not found.");
                return;
            }
            if (!PropPreset.IsPresetFile(asset.filePath))
            {
                Debug.LogError($"[LiveStudio] DeleteAssetFile: only preset files can be deleted (got '{asset.filePath}').");
                return;
            }

            try
            {
                if (File.Exists(asset.filePath)) File.Delete(asset.filePath);
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
        [ExposedFunction]
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
        /// Display names of the registered avatar (exclusive) assets, with an empty entry first that
        /// represents "none / default avatar". Used as the option source for an avatar selector UI.
        /// </summary>
        public string[] GetAvatarNames()
        {
            var names = new List<string> { string.Empty };
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset != null && asset.isExclusive) names.Add(asset.name ?? string.Empty);
            }
            return names.ToArray();
        }

        /// <summary>Name of the currently selected (enabled) avatar, or empty when on the default avatar.</summary>
        public string GetSelectedAvatarName()
        {
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset != null && asset.isExclusive && asset.enabled) return asset.name ?? string.Empty;
            }
            return string.Empty;
        }

        /// <summary>
        /// Selects the avatar with the given display name (empty resets to the default avatar), driving the
        /// same exclusive reconcile as toggling the asset's <see cref="AssetBase.enabled"/> flag directly.
        /// </summary>
        public void SelectAvatarByName(string avatarName)
        {
            if (string.IsNullOrEmpty(avatarName))
            {
                // Disable the currently-enabled avatar; the reconcile then resets to the default avatar.
                bool changed = false;
                for (int i = 0; i < assets.Length; i++)
                {
                    var asset = assets[i];
                    if (asset != null && asset.isExclusive && asset.enabled) { asset.enabled = false; changed = true; }
                }
                if (changed) { _dirty = true; _Broadcast(); }
                return;
            }

            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null || !asset.isExclusive || asset.name != avatarName) continue;
                if (!asset.enabled) { asset.enabled = true; _dirty = true; }
                _Broadcast();
                return;
            }
        }

        /// <summary>
        /// Read-only view of the managed assets, so <see cref="StageManager"/> can project the
        /// <see cref="SetBundleAsset"/> entries into its set view without owning the array.
        /// </summary>
        public IReadOnlyList<AssetBase> assetsView => assets;

        /// <summary>Returns the asset with the given id, or null. Used to reach a loaded scene handle.</summary>
        public AssetBase FindAsset(string assetId) => _Find(assetId);

        /// <summary>
        /// Saves a loaded exposed object as a named preset (<c>*.preset.json</c>) in the project folder:
        /// records how to recreate the object (a <c>target</c> descriptor) plus the parameter state to
        /// reapply, then re-crawls so the preset appears as a loadable entry. The user can later load the
        /// preset to recreate the object in the saved state.
        ///
        /// Keyed on the object's exposed <c>@id</c> so the API generalizes to any object; the recreation
        /// strategy is resolved in <see cref="_ResolvePresetTarget"/>. Today only the asset strategy is
        /// implemented: loaded props and the active avatar (both as delta state in the unified envelope).
        /// Objects without a supported strategy are rejected.
        /// </summary>
        [ExposedFunction]
        public void SaveAsPreset(string objectId, string presetName)
        {
            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
            {
                Debug.LogError("[LiveStudio] SaveAsPreset: no project folder is open to save the preset into.");
                return;
            }

            if (!_ResolvePresetTarget(objectId, out var kind, out var source, out var state))
            {
                Debug.LogError($"[LiveStudio] SaveAsPreset: object '{objectId}' is not presetable (loaded prop or active avatar only).");
                return;
            }

            var json = PropPreset.BuildJson(
                kind,
                presetName,
                PropPreset.Relativize(source, projectPath),
                state);

            var fallbackName = Path.GetFileNameWithoutExtension(source);
            var baseName = PropPreset.SanitizeFileName(string.IsNullOrEmpty(presetName) ? fallbackName : presetName);
            var path = _UniquePresetPath(projectPath, baseName);
            try
            {
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] SaveAsPreset: failed to write '{path}': {e.Message}");
                return;
            }

            // Re-crawl so the new preset is registered as a loadable entry right away.
            ProjectManager.RecrawlProject();
            RemoteNotificationSystem.Show(
                LocalizationSystem.Translate("NOTIFY_PRESET_SAVED"), RemoteNotificationSystem.Type.Success, icon: "save");
        }

        // Resolves how to recreate the exposed object identified by objectId into a preset target
        // descriptor (asset kind + source file + state snapshot). Returns false when no supported
        // strategy applies. This is the single extension point for future preset strategies (e.g.
        // factory-created objects, apply-to-existing scene objects).
        private bool _ResolvePresetTarget(string objectId, out PropPreset.AssetKind kind, out string source, out string state)
        {
            kind = default;
            source = null;
            state = null;

            // Prop strategy: a loaded prop whose exposed object id matches. Presets are eligible too —
            // sourceFilePath resolves to the underlying source asset (not the preset file), so re-saving
            // an edited preset writes a new preset against the same source with the current delta.
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is PropAsset prop && prop.isLoaded && prop.objectId == objectId &&
                    !string.IsNullOrEmpty(prop.sourceFilePath))
                {
                    kind = PropPreset.AssetKind.Prop;
                    source = prop.sourceFilePath;
                    state = prop.CaptureDeltaState();
                    return true;
                }
            }

            // Avatar strategy: avatars are exposed through the shared AvatarController (not a per-asset
            // wrapper), so delta-capture against its GameObject and pin the active avatar asset's source.
            // A preset-loaded avatar is eligible too — its sourceFilePath resolves to the underlying file.
            var handle = ExposedObjectRegistry.FindById(objectId);
            if (handle.HasValue && handle.Value.target is AvatarController controller)
            {
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] is AvatarAsset avatar && avatar.enabled &&
                        !string.IsNullOrEmpty(avatar.sourceFilePath))
                    {
                        kind = PropPreset.AssetKind.Avatar;
                        source = avatar.sourceFilePath;
                        // Object-generic delta in the same { wrapper, components } envelope as props.
                        // Requires the baseline captured at avatar load (see AvatarAsset).
                        state = AssetStateSnapshot.CaptureDelta(controller.gameObject);
                        return true;
                    }
                }
            }

            return false;
        }

        // Builds a non-colliding "<name>.preset.json" path in the project folder, appending " (n)"
        // when a file of that name already exists.
        private static string _UniquePresetPath(string folder, string baseName)
            => _UniqueFilePath(folder, baseName + PropPreset.Extension);

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

        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains(nameof(assets))) return;
            _dirty = true;
        }

        // The avatar was swapped: avatar-attached props were destroyed with it. Drop them and re-diff
        // so the enabled ones are reloaded onto the new avatar.
        private void _OnAvatarChanged()
        {
            var context = _MakeContext();
            for (int i = _loaded.Count - 1; i >= 0; i--)
            {
                var asset = _loaded[i];
                if (!asset.reloadsOnAvatarChange) continue;
                // The instance was destroyed with the old avatar; Unload captures state best-effort and
                // clears the load flag. objectId is stable/persisted, so it is left intact and the prop
                // reloads onto the new avatar under the same exposed-object id.
                asset.Unload(context);
                _loaded.RemoveAt(i);
            }
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
            // Additive assets (props).
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null || string.IsNullOrEmpty(asset.id)) continue;
                if (asset.isExclusive) continue;
                if (asset.busy) continue;

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
            try
            {
                await asset.LoadAsync(_MakeContext());
                if (asset.isLoaded && !_loaded.Contains(asset)) _loaded.Add(asset);
            }
            finally
            {
                asset.busy = false;
                _Broadcast();
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
                _ = desired.LoadAsync(_MakeContext());
            }
            else
            {
                _ResetExclusive();
            }

            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null || !asset.isExclusive) continue;
                bool selected = asset.id == desiredId;
                asset.isLoaded = selected;
                if (!selected && asset.enabled) asset.enabled = false; // radio: turn the others off
            }

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
        // when accessed after its GameObject is destroyed). Prefers a container in a build (base) scene;
        // additively-loaded set-bundle scenes have buildIndex -1.
        private RemoteControlContainer _ResolveContainer()
        {
            var all = RemoteControlContainer.all;
            RemoteControlContainer fallback = null;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) continue;
                if (fallback == null) fallback = c;
                if (c.gameObject.scene.buildIndex >= 0) return c;
            }
            return fallback;
        }

        // Derives a stable entry id from the file path. Path separators are normalized so the same
        // file picked with different separators (\\ vs /) maps to one id; the path is otherwise kept
        // verbatim (the id is only used as an equality key / function argument, never as a URL segment).
        private static string _MakeId(string filePath)
        {
            return filePath.Replace('\\', '/');
        }

        // Reconstructs the absolute runtime filePath/id from the persisted (project-relative) path after a
        // live-scene restore. New-format entries persisted only the relative `path` (id/filePath are
        // persistable=false); resolve those to absolutes. Legacy entries that persisted absolute
        // id/filePath instead are kept as-is and back-filled with a relative `path`, so a re-save upgrades
        // them to the portable form. PropPreset.ResolveSource returns a rooted path verbatim, so a `path`
        // that is itself absolute (different drive / out-of-project fallback) still resolves correctly.
        private static void _ResolveAssetPaths(AssetBase[] assets)
        {
            var projectPath = ProjectManager.projectPath;
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset == null) continue;

                if (string.IsNullOrEmpty(asset.filePath) && !string.IsNullOrEmpty(asset.path))
                {
                    // New format: only the relative path was persisted.
                    asset.filePath = PropPreset.ResolveSource(asset.path, projectPath);
                    asset.id = _MakeId(asset.filePath);
                    if (string.IsNullOrEmpty(asset.name)) asset.name = AssetTypeRegistry.DeriveName(asset.filePath);
                }
                else if (!string.IsNullOrEmpty(asset.filePath))
                {
                    // Legacy format: absolute id/filePath were persisted. Normalize id and back-fill path.
                    if (string.IsNullOrEmpty(asset.id)) asset.id = _MakeId(asset.filePath);
                    if (string.IsNullOrEmpty(asset.path)) asset.path = PropPreset.Relativize(asset.filePath, projectPath);
                }
            }
        }

        private AssetBase _Find(string assetId)
        {
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && assets[i].id == assetId) return assets[i];
            }
            return null;
        }

        // True when filePath resolves to a location inside the project folder. Used to register a picked
        // file already in the project in place rather than copying it onto itself.
        private static bool _IsInsideProject(string filePath, string projectPath)
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

        // Returns a copy of `source` with null entries removed (or an empty array). A persisted asset
        // entry deserializes to null when its @type is no longer registered (e.g. saved data referencing
        // a removed/renamed asset kind); such holes must not reach the live array or a broadcast. The
        // input is returned as-is when it has no nulls, so the common case allocates nothing.
        private static AssetBase[] _WithoutNulls(AssetBase[] source)
        {
            if (source == null || source.Length == 0) return Array.Empty<AssetBase>();

            int count = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != null) count++;
            }
            if (count == source.Length) return source;

            var result = new AssetBase[count];
            int w = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != null) result[w++] = source[i];
            }
            return result;
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
            ExposedPropertyBroadcast.BroadcastProperty(this, "assets");
            onAssetsChanged?.Invoke();
        }
    }
}
