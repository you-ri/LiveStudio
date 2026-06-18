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
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "deployed_code", Category = "Asset")]
    public class ExternalAssetManager : IExposedObject, IExposedDeserializeCallback, IExposedSerializeCallback
    {
        const string kId = "a7d3f1e2-9c4b-4e85-b6a1-2f8c5d3e7b91";

        public string name { get; set; } = "Asset Manager";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        // Added assets, exposed as an editable polymorphic array. The remote app toggles each entry's
        // `enabled` flag (load/unload) and removes entries; persisted to the live scene JSON.
        [NonSerialized]
        [ExposedField]
        private AssetBase[] assets = Array.Empty<AssetBase>();

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

        [NonSerialized]
        private bool _restorePending;

        public void OnEnable()
        {
            ExposedObjectRegistry.Create<ExternalAssetManager>(this, kId);
            ExposedClass.Get<ExternalAssetManager>().onPropertyChanged += _OnPropertyChanged;

            if (Application.isPlaying)
            {
                _restorePending = true;
                _avatarService = SingletonService<IAvatarService>.subject;
                if (_avatarService != null) _avatarService.onAvatarChanged += _OnAvatarChanged;
            }

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
        }

        public void OnDispose()
        {
            OnDisable();
        }

        /// <summary>Fires after the <c>assets</c> array is restored from a saved live scene; schedules a diff.</summary>
        public void OnAfterExposedDeserialize()
        {
            if (!Application.isPlaying) return;
            if (!_restorePending) return;
            _dirty = true;
        }

        /// <summary>
        /// Fires before <c>assets</c> is serialized (e.g. on save). Refreshes each loaded asset's
        /// <see cref="AssetBase.state"/> from its live values so the save captures the latest edits.
        /// </summary>
        public void OnBeforeExposedSerialize()
        {
            for (int i = 0; i < _loaded.Count; i++) _loaded[i].CaptureState();
        }

        public void Update()
        {
            _restorePending = false;

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

            AssetBase asset = _CreateAsset(filePath);
            if (asset == null)
            {
                Debug.LogError($"[LiveStudio] Unsupported asset file: {filePath}");
                return;
            }

            asset.id = assetId;
            asset.name = _DeriveName(filePath);
            asset.filePath = filePath;
            asset.enabled = true;
            asset.isLoaded = false;
            // Assign the stable exposed-object id up front so it persists and is reused on every load.
            asset.objectId = Guid.NewGuid().ToString();

            var list = new List<AssetBase>(assets) { asset };
            assets = list.ToArray();

            _dirty = true;
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

        private static AssetBase _CreateAsset(string filePath)
        {
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
        }

        // Derives a display name by stripping a known asset suffix, else the plain file name.
        private static readonly string[] kKnownSuffixes =
        {
            LiveStudioBundle.PropExtension,
            LiveStudioBundle.AvatarExtension,
            LiveStudioBundle.LegacyAvatarExtension,
            ".vrm",
            ".glb",
            ".gltf",
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
