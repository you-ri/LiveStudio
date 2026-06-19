// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

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
        // use; only the used ones are persisted, via the `_persistedAssets` shadow below.
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
        /// <see cref="_Broadcast"/>). <see cref="WorldManager"/> subscribes to rebuild its projected
        /// scene view from the <see cref="SceneBundleAsset"/> entries here.
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

            // Match the current persisted reference so an unrelated property write before any real
            // restore does not trigger a (clobbering) swap.
            _lastAppliedPersisted = _persistedAssets;
            _initialized = true;
        }

        public void OnDisable()
        {
            _dirty = false;
            _initialized = false;

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

        /// <summary>
        /// Fires after a live scene is restored. The used assets were deserialized into
        /// <see cref="_persistedAssets"/>; make them the live set, schedule a diff to (un)load them, and
        /// re-arm the crawl so the project catalog is re-added on the next <see cref="Update"/>.
        /// </summary>
        public void OnAfterExposedDeserialize()
        {
            if (!Application.isPlaying) return;

            // This callback also fires after every individual exposed-property write (the owner's
            // IExposedDeserializeCallback), not only after a full live-scene restore. Apply the
            // persisted set to the live `assets` array ONLY when `_persistedAssets` was actually
            // re-deserialized by a restore — detected by its reference changing to one this manager did
            // not produce in OnBeforeExposedSerialize. Otherwise a plain enabled-toggle write would wipe
            // the live assets (and the just-toggled selection).
            if (ReferenceEquals(_persistedAssets, _lastAppliedPersisted)) return;

            _lastAppliedPersisted = _persistedAssets;
            assets = _persistedAssets ?? Array.Empty<AssetBase>();
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

            var used = new List<AssetBase>();
            for (int i = 0; i < assets.Length; i++)
            {
                var asset = assets[i];
                if (asset != null && (asset.enabled || asset.isLoaded)) used.Add(asset);
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
        /// Adds an external asset and loads it. Invoked from the remote app after the user picks a file.
        /// The concrete asset kind is chosen by file extension.
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

            // The entry id is derived from the file path (not a random GUID) so re-adding the same
            // file — or, in the future, re-crawling a project folder — resolves to the same entry and
            // keeps its enabled / state instead of spawning a duplicate.
            var assetId = _MakeId(filePath);
            var existing = _Find(assetId);
            if (existing != null)
            {
                // Same file already registered: (re)enable it instead of duplicating.
                if (!existing.enabled)
                {
                    existing.enabled = true;
                    _dirty = true;
                }
                _Broadcast();
                return;
            }

            AssetBase asset = _CreateEntry(filePath, enabled: true);
            if (asset == null)
            {
                Debug.LogError($"[LiveStudio] Unsupported asset file: {filePath}");
                return;
            }

            var list = new List<AssetBase>(assets) { asset };
            assets = list.ToArray();

            _dirty = true;
            _Broadcast();
        }

        /// <summary>
        /// True if the file extension maps to a supported asset kind (bundle / VRM / glTF). Lets a
        /// project crawler classify files by path alone, without reading their contents.
        /// </summary>
        public static bool IsSupportedAssetFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            foreach (var suffix in kKnownSuffixes)
            {
                if (filePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
        /// Read-only view of the managed assets, so <see cref="WorldManager"/> can project the
        /// <see cref="SceneBundleAsset"/> entries into its scene view without owning the array.
        /// </summary>
        public IReadOnlyList<AssetBase> assetsView => assets;

        /// <summary>Returns the asset with the given id, or null. Used to reach a loaded scene handle.</summary>
        public AssetBase FindAsset(string assetId) => _Find(assetId);

        /// <summary>
        /// Sets an asset's desired <see cref="AssetBase.enabled"/> state and schedules a diff, so a
        /// facade such as <see cref="WorldManager"/> can drive load/unload through the same pipeline as
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

        // The RemoteControlContainer this manager lives in (so loaded assets join the same container);
        // falls back to the first registered container.
        private RemoteControlContainer _ResolveContainer()
        {
            var all = RemoteControlContainer.all;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i]._objects.Contains(this)) return all[i];
            }
            return all.Count > 0 ? all[0] : null;
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

        // Builds a registered (but not yet loaded) entry for the file, or null if the extension is
        // unsupported. The caller appends it to `assets` and broadcasts. enabled=true means the diff
        // pass will load it; enabled=false registers it path-only (content read lazily on enable).
        private static AssetBase _CreateEntry(string filePath, bool enabled)
        {
            var asset = _CreateAsset(filePath);
            if (asset == null) return null;

            asset.id = _MakeId(filePath);
            asset.name = _DeriveName(filePath);
            asset.filePath = filePath;
            asset.enabled = enabled;
            asset.isLoaded = false;
            // Assign the stable exposed-object id up front so it persists and is reused on every load.
            asset.objectId = Guid.NewGuid().ToString();
            return asset;
        }

        private static AssetBase _CreateAsset(string filePath)
        {
            // Live scenes (*.live.json / *.scene.json) are launcher entries, not loadable resources.
            if (LiveSceneSaveSystem.IsLiveSceneFile(filePath))
            {
                return new LiveSceneAsset();
            }
            // Evaluated first: IsSceneBundle matches the *.scene.lsb compound suffix only, so it never
            // collides with the *.avatar.lsb / *.prop.lsb suffixes checked below.
            if (LiveStudioBundle.IsSceneBundle(filePath))
            {
                return new SceneBundleAsset();
            }
            if (LiveStudioBundle.IsAvatarBundle(filePath) ||
                filePath.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
            {
                return new AvatarAsset();
            }
            if (LiveStudioBundle.IsPropBundle(filePath) ||
                filePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                filePath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
            {
                return new PropAsset();
            }
            return null;
        }

        private void _Broadcast()
        {
            ExposedPropertyBroadcast.BroadcastProperty(this, "assets");
            onAssetsChanged?.Invoke();
        }

        // Derives a display name by stripping a known asset suffix, else the plain file name.
        private static readonly string[] kKnownSuffixes =
        {
            LiveStudioBundle.SceneExtension,
            LiveStudioBundle.PropExtension,
            LiveStudioBundle.AvatarExtension,
            LiveStudioBundle.LegacyAvatarExtension,
            ".vrm",
            ".glb",
            ".gltf",
            ".live.json",
            ".scene.json",
        };

        private static string _DeriveName(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            foreach (var suffix in kKnownSuffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return fileName.Substring(0, fileName.Length - suffix.Length);
                }
            }
            return Path.GetFileNameWithoutExtension(fileName);
        }
    }
}
