// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting.APIUpdating;

#if UNITY_EDITOR
using UnityEditor;
#endif

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// One entry in <see cref="StageManager.sets"/>: a set bundle (<c>*.set.lsb</c>)
    /// that the user has added. <see cref="enabled"/> is the desired state controlled from the
    /// remote app; <see cref="isLoaded"/> reflects whether the set is actually loaded.
    ///
    /// Surfaced to the remote app's stage detail page through the generic exposed-object UI: the
    /// visible members are <see cref="name"/> and the <see cref="WarpTo"/> / <see cref="WarpToOrigin"/>
    /// buttons. <see cref="WarpTo"/>'s argument is a dropdown sourced from <see cref="marks"/>.
    /// </summary>
    [Serializable]
    [LiveClass]
    public struct SetBundleEntry
    {
        // Identity / state fields are hidden from the generic object UI used by the stage detail page:
        // there they would be noise, and load / activate are driven from the Stage page's cards. They
        // are still serialized (Hide does not skip serialization), so the card list keeps reading them.
        [LiveField, Hide]
        public string id;

        [LiveField]
        public string name;

        [LiveField, Hide]
        public string filePath;

        /// <summary>
        /// Desired state. Toggling this loads (true) or unloads (false) the set. Mirrored to the
        /// backing <see cref="SetBundleAsset"/>, which is where the state is actually persisted.
        /// </summary>
        [LiveField]
        public bool enabled;

        /// <summary>Actual state, projected from the backing <see cref="SetBundleAsset"/>.</summary>
        [LiveField(persistable = false), Hide]
        public bool isLoaded;

        /// <summary>True when this set is the active set.</summary>
        [LiveField, Hide]
        public bool isActive;

        /// <summary>
        /// True for the bootstrap (initial) set the app loads at startup. This entry is always
        /// present and loaded, and cannot be unloaded or removed; only activation is allowed.
        /// Rebuilt at runtime, so it is not persisted.
        /// </summary>
        [LiveField(persistable = false), Hide]
        public bool isPersistent;

        /// <summary>
        /// Labels of the <see cref="StageMark"/>s in this set's loaded scene, in registration order.
        /// The source of <see cref="WarpTo"/>'s dropdown options; not shown as its own control. Empty
        /// when the set is not loaded. Derived view data, so not persisted.
        /// </summary>
        [LiveField(persistable = false), Hide]
        public string[] marks;

        /// <summary>
        /// Warps the current avatar to the named <see cref="StageMark"/> in this set. Surfaced as a
        /// button in the generic object UI; the <paramref name="markLabel"/> argument is a dropdown
        /// sourced from <see cref="marks"/>. Runs on a boxed copy of the entry, so it only reads its own
        /// fields and delegates to the live <see cref="StageManager"/> singleton.
        /// </summary>
        [LiveFunction]
        public void WarpTo([StringSelector(nameof(marks))] string markLabel)
            => StageManager.current?.WarpTo(id, markLabel);

        /// <summary>Warps the current avatar to the origin (doubling as a "reset to zero").</summary>
        [LiveFunction]
        public void WarpToOrigin() => StageManager.current?.WarpTo(id, string.Empty);
    }

    /// <summary>
    /// Surfaces the loaded set bundles to the remote app's Stage page and owns the set-specific
    /// concepts the unified asset pipeline does not model: the active set and the bootstrap
    /// (persistent) set.
    ///
    /// The actual load/unload of sets is delegated to <see cref="ExternalAssetManager"/>, where each set
    /// is an <see cref="ISetAsset"/> alongside props and avatars — either a <see cref="SetBundleAsset"/>
    /// (external <c>*.set.lsb</c>) or a <see cref="BuiltinSetAsset"/> (a scene shipped in the build). This
    /// manager keeps no scenes of its own: <see cref="sets"/> is a runtime-only projection of the ISetAsset
    /// entries (plus the synthetic persistent entry), so the persisted source of truth lives entirely on
    /// the assets side. Remote-app edits to an entry's <see cref="SetBundleEntry.enabled"/> flag are
    /// forwarded to the matching set asset; activation is applied here through the asset's loaded scene
    /// handle. Keying on <see cref="ISetAsset"/> rather than a concrete type is what lets built-in and
    /// bundle sets reconcile through one path.
    /// </summary>
    [Serializable]
    [LiveClass(Icon = "public", Category = "Stage", HideInScene = true)]
    [MovedFrom(false, null, null, "WorldManager")]
    public class StageManager : ILiveObject, ILiveDeserializeCallback
    {
        const string kId = "b2f7c9a1-3d4e-4f8a-9c1b-7e2d5a6f8c30";

        // Stable id for the synthetic entry representing the bootstrap / persistent set. A short
        // literal is safe because real bundle entries use path-based ids and never collide with it.
        const string kPersistentSetId = "persistent";

        // The active manager, so a value-type SetBundleEntry warp function (invoked on a boxed copy) can
        // reach the live instance to warp. Set in OnEnable, cleared in OnDisable. Reset on subsystem
        // registration for safety when Domain Reload is disabled.
        [NonSerialized]
        private static StageManager _current;

        public static StageManager current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeCurrent() => _current = null;

#if UNITY_EDITOR
        private static bool _isExitingPlayMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeEditor()
        {
            _isExitingPlayMode = false;
            EditorApplication.playModeStateChanged -= _OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += _OnPlayModeStateChanged;
        }

        private static void _OnPlayModeStateChanged(PlayModeStateChange state)
        {
            _isExitingPlayMode = state == PlayModeStateChange.ExitingPlayMode;
        }
#endif

        public string name { get; set; } = "Stage Manager";

        public LiveObjectHandle? liveObject => LiveObjectRegistry.FindByTarget(this);

        public string id => kId;

        // Projected view of the set bundles for the remote app's Stage page: the persistent entry
        // followed by each SetBundleAsset in ExternalAssetManager. Not persisted — the SetBundleAsset
        // entries are the persisted source of truth — so it is rebuilt whenever the asset list changes.
        [NonSerialized]
        [LiveField(persistable = false)]
        private SetBundleEntry[] sets = Array.Empty<SetBundleEntry>();

        /// <summary>
        /// Which stage is up, by display name.
        ///
        /// State lane rather than the event lane, and for the same reason
        /// <see cref="ExternalAvatarSource.selectedAvatar"/> is: what belongs in a recording is the
        /// intent ("this stage is up"), not the loading that produces it. Carried as state, any frame
        /// of a take says which stage should be standing, and the setter below produces it -- loading
        /// the set on demand if the machine playing back does not have it up yet. Carried as events,
        /// a take holds the moments someone switched and nothing else, so a replay that starts or
        /// seeks anywhere but the beginning shows whatever stage happened to be loaded.
        ///
        /// ⚠ This is the half that the asset side cannot carry. The sets themselves live in
        /// <see cref="ExternalAssetManager"/>, which is off the frame: which bundles this machine has
        /// on disk and has loaded into memory is a setting of the machine, not something the show
        /// did. Which one is *up* is the show, and it belongs here -- in the scene -- rather than on
        /// the catalog. Props reach a recording the same way without needing a member of their own:
        /// an instance is a registered live object, so the structure lane carries it being brought
        /// out and taken away.
        ///
        /// Carried as the id the frame's symbol table gives the name rather than as fixed-width text,
        /// so there is no length a set name can outgrow (a width here would mean a longer name is not
        /// carried at all, i.e. a take that does not say which stage was up).
        /// </summary>
        [LiveProperty(lane = FrameLane.State)]
        [StringSelector(nameof(setNames))]
        public string activeSet
        {
            // 意図を返す。投影 (sets) を読まないのは、カタログがまだ揃っていない起動直後に
            // 「立っているステージは無い」と答えてしまい、その答えが保存にも記録にも入るため。
            // 実体が別経路で動いたときは _SyncStageFromAssets がこの値を追従させる。
            get => _activeSet ?? string.Empty;
            set
            {
                var name = value ?? string.Empty;
                if (string.Equals(name, _activeSet, StringComparison.Ordinal)) return;

                _activeSet = name;
                _ApplyStageIntent();
            }
        }

        /// <summary>
        /// 読み込まれているステージの集合 (立っているものを含む)、表示名で。
        ///
        /// <see cref="activeSet"/> が「どれが立っているか」しか言わないので、複数セットを同時に
        /// 読み込んでいる状態はこれが言う。⚠ イベントレーン (保存先から導出) — 値の配列は状態
        /// ブロックに載らないため。したがってテイクの途中から再生した場合、ここまでの読み込み履歴を
        /// 辿らないと集合は復元されない。立っているステージだけは <see cref="activeSet"/> が
        /// 状態レーンで運ぶので、どのフレームからでも分かる。
        ///
        /// ⚠ 投影 (<see cref="sets"/>) の enabled は非永続なので、保存も収録もこちらが担う。
        /// </summary>
        [LiveProperty]
        public string[] loadedSets
        {
            get => _loadedSets ?? Array.Empty<string>();
            set
            {
                _loadedSets = value ?? Array.Empty<string>();
                _ApplyStageIntent();
            }
        }

        // 「何が出ているか」の意図。ライブシーンに保存され、同じ宣言から収録レーンも決まる
        // (FrameLaneRules)。カタログ (ExternalAssetManager) はこの機械のディスクにある物なので
        // 保存も収録もしない — どれが立っているかはショーなので、シーン側のここが持つ。
        [SerializeField, LiveField(lane = FrameLane.State), Hide]
        [FormerlyNamedAs("activeSet")]
        // private ではなく internal。ブロックは型の外側に生成されるので (この型は partial ではない)、
        // private だとムーバーから見えず黙って落ちる (LRC009)。AvatarController._avatarLayer と同じ。
        internal string _activeSet = string.Empty;

        [SerializeField, LiveField, Hide]
        [FormerlyNamedAs("loadedSets")]
        internal string[] _loadedSets = Array.Empty<string>();

        // 意図がまだカタログに無くて適用できていない状態。立っている間は実体からの同期を止める
        // — 起動直後のカタログは空で、そこから同期すると復元した意図をその場で消す。
        [NonSerialized]
        private bool _stagePending;

        // 適用中の再入ガード。SetAssetEnabled / _ActivateSet は onAssetsChanged を同期に発火するので、
        // これが無いと適用の途中の状態を「実体」として意図に書き戻してしまう。
        [NonSerialized]
        private bool _applyingStage;

        /// <summary>Dropdown source for <see cref="activeSet"/>.</summary>
        [LiveProperty, Hide]
        public string[] setNames => GetSetNames();

        // The scene that was active when this manager started (the bootstrap / persistent set).
        // When an active set's scene is unloaded, the active scene is restored to this.
        [NonSerialized]
        private Scene _persistentScene;

        /// <summary>
        /// Path of the bootstrap (persistent) scene, or empty before it is captured. Lets
        /// <see cref="BuiltinSetSource"/> keep the base scene out of the loadable set list — it is
        /// already surfaced as the persistent entry here, and is not necessarily build index 0.
        /// </summary>
        internal string persistentScenePath => _persistentScene.IsValid() ? _persistentScene.path : string.Empty;

        [NonSerialized]
        private bool _initialized;

        // The ExternalAssetManager we are subscribed to (null when not subscribed). The manager may not
        // exist yet at OnEnable (load order), so subscription is deferred to Update. Holding the
        // instance — rather than re-reading the singleton — lets us unsubscribe cleanly even if the
        // manager is torn down before us.
        [NonSerialized]
        private ExternalAssetManager _subscribedManager;

        public void OnEnable()
        {
            _current = this;

            LiveObjectRegistry.Create<StageManager>(this, kId);
            LiveClass.Get<StageManager>().onPropertyChanged += _OnPropertyChanged;

            // The active scene at startup is the bootstrap / persistent set. Surface it as the
            // first, non-removable entry so the remote app can see and re-activate it.
            if (Application.isPlaying)
            {
                _persistentScene = SceneManager.GetActiveScene();
            }

            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded += _OnBaseSceneReloaded;

            // Marks self-register globally as set scenes load/unload; rebuild so each set's
            // projected `marks` list (and the remote app's warp UI) tracks them.
            StageMarkRegistry.onChanged += _OnMarksChanged;

            _initialized = true;
            _RebuildSetsView();
        }

        public void OnDisable()
        {
            _initialized = false;

            Lilium.RemoteControl.LiveScene.RemoteControlBehaviour.onBaseSceneReloaded -= _OnBaseSceneReloaded;

            StageMarkRegistry.onChanged -= _OnMarksChanged;

            LiveClass.Get<StageManager>().onPropertyChanged -= _OnPropertyChanged;

            // ExternalAssetManager owns the loaded set bundles and unloads them in its own teardown;
            // here we only drop our subscription.
            if (_subscribedManager != null)
            {
                _subscribedManager.onAssetsChanged -= _OnAssetsChanged;
                _subscribedManager = null;
            }

            LiveObjectRegistry.FindByTarget(this)?.Unregister();

            if (_current == this) _current = null;
        }

        public void OnDispose()
        {
            OnDisable();
        }

        public void Update()
        {
            if (!_initialized) return;
            if (!Application.isPlaying) return;
#if UNITY_EDITOR
            if (_isExitingPlayMode) return;
#endif
            _EnsurePersistentScene();
            _EnsureSubscribed();
        }

        // OnEnable captures the bootstrap scene, but this object also lives in the Editor outside play
        // mode (RemoteControl runs there), where there is no play-mode scene to capture and OnEnable is
        // not replayed when play starts — leaving the persistent entry missing and the base scene
        // unidentifiable (see persistentScenePath). Capture on the first playing frame instead: no set
        // can be loaded yet, so the active scene is still the bootstrap one.
        private void _EnsurePersistentScene()
        {
            if (_persistentScene.IsValid() && _persistentScene.isLoaded) return;
            _persistentScene = SceneManager.GetActiveScene();
            if (!_persistentScene.IsValid()) return;
            _RebuildSetsView();
        }

        public void Reset()
        {
        }

        // After a base-scene switch (persistent host), the previous bootstrap (persistent) scene was
        // unloaded. Adopt the new active scene as the bootstrap set and rebuild the projected view so
        // the Stage page's non-removable entry reflects the new base scene.
        private void _OnBaseSceneReloaded()
        {
            if (!Application.isPlaying) return;
            _persistentScene = SceneManager.GetActiveScene();
            _RebuildSetsView();
        }

        /// <summary>
        /// Adds a set bundle and loads it. Intended to be invoked from the remote app after the
        /// user picks a <c>*.set.lsb</c> file. Delegates to <see cref="ExternalAssetManager"/>, which
        /// creates the backing <see cref="SetBundleAsset"/>; the projected view rebuilds on the resulting
        /// assets-changed notification.
        /// </summary>
        [LiveFunction]
        public void AddSet(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[LiveStudio] Set bundle path cannot be empty.");
                return;
            }

            if (!LiveStudioBundle.IsSetBundle(filePath))
            {
                Debug.LogError($"[LiveStudio] Not a set bundle (*.set.lsb): {filePath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[LiveStudio] Set bundle file not found: {filePath}");
                return;
            }

            var manager = ExternalAssetManager.current;
            if (manager == null)
            {
                Debug.LogError("[LiveStudio] No ExternalAssetManager in the scene; cannot add a set bundle.");
                return;
            }

            manager.AddAsset(filePath);
        }

        /// <summary>Unloads (if loaded) and removes the entry with the given id.</summary>
        [LiveFunction]
        public void RemoveSet(string setId)
        {
            if (string.IsNullOrEmpty(setId)) return;

            // The bootstrap set is always present and cannot be removed.
            if (setId == kPersistentSetId) return;

            ExternalAssetManager.current?.RemoveAsset(setId);
        }

        /// <summary>
        /// Makes the loaded set with the given id the active set (lighting/instantiation target). Only
        /// loaded sets can be activated; this does not load or unload any set. Drives the Stage page's set
        /// selection — loading is controlled independently through each entry's enabled flag, so multiple
        /// sets stay loaded and only the active one changes here. (A stage-switch operation wanting a complete
        /// replace uses <see cref="SwitchToSetByName"/> instead.)
        /// </summary>
        [LiveFunction]
        public void SetActiveSet(string setId)
        {
            if (string.IsNullOrEmpty(setId)) return;

            var manager = ExternalAssetManager.current;

            // Activating the bootstrap set: clear the set assets' active flags so it becomes active.
            if (setId == kPersistentSetId)
            {
                _ClearSetAssetActiveFlags(manager);
            }
            else
            {
                var asset = manager?.FindAsset(setId) as ISetAsset;
                if (asset == null || !asset.hasScene)
                {
                    Debug.LogWarning($"[LiveStudio] Cannot activate a set that is not loaded: {setId}");
                    return;
                }

                // Radio: only the target set asset is flagged active.
                var view = manager.assetsView;
                for (int i = 0; i < view.Count; i++)
                {
                    if (view[i] is ISetAsset s) s.isActive = ReferenceEquals(s, asset);
                }
            }

            _ReconcileActiveScene();
            _RebuildSetsView();
        }

        /// <summary>
        /// Names of the known sets (the bootstrap set plus each added set bundle), in display order.
        /// The option source for a stage selector such as a trigger's switch-stage operation.
        /// </summary>
        public string[] GetSetNames()
        {
            var names = new string[sets.Length];
            for (int i = 0; i < sets.Length; i++) names[i] = sets[i].name ?? string.Empty;
            return names;
        }

        /// <summary>
        /// Switches the active stage to the set with the given display name as a complete switch: every
        /// other loaded set is unloaded and the named set is loaded on demand, leaving only the persistent
        /// bootstrap scene and the named set loaded. Used by <see cref="SwitchStageOperation"/>. A no-op when
        /// no set matches. Distinct from <see cref="SetActiveSet"/>, which only re-flags the active set
        /// among already-loaded sets and never unloads (the Stage page's selection).
        /// </summary>
        [LiveFunction]
        public void SwitchToSetByName(string setName)
        {
            if (string.IsNullOrEmpty(setName)) return;
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i].name == setName) { _SwitchToSet(sets[i].id); return; }
            }
        }

        // Complete-switch core: flag only the target active, unload every other set bundle, and load the
        // target on demand.
        private void _SwitchToSet(string setId) => _ActivateSet(setId, unloadOthers: true);

        /// <summary>
        /// Makes one set the active stage, loading it on demand. <paramref name="unloadOthers"/> is
        /// the difference between the two ways of asking: a complete switch (the deck's switch-stage
        /// operation) takes every other set down, while making a set active
        /// (<see cref="activeSet"/>) leaves whatever else the operator has loaded alone.
        ///
        /// <see cref="_ReconcileActiveScene"/> finishes activation once the target's scene is ready
        /// (or restores the bootstrap scene when switching to the persistent set). SetAssetEnabled is
        /// a no-op when the enabled flag is unchanged, so already-unloaded sets are skipped cheaply.
        ///
        /// One loop for both, rather than two spellings of it: the flags and the unloading are set in
        /// the same pass because a reconcile triggered part-way through must not see a set that is
        /// unloaded and still flagged active.
        /// </summary>
        private void _ActivateSet(string setId, bool unloadOthers)
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return;

            // null target = the bootstrap (persistent) scene: no set asset stays flagged.
            ISetAsset target = null;
            if (setId != kPersistentSetId)
            {
                target = manager.FindAsset(setId) as ISetAsset;
                if (target == null)
                {
                    Debug.LogWarning($"[LiveStudio] Cannot switch to an unknown set: {setId}");
                    return;
                }
            }

            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                var asset = view[i];
                if (asset is not ISetAsset s) continue;
                bool isTarget = ReferenceEquals(s, target);
                s.isActive = isTarget;
                if (unloadOthers && !isTarget) manager.SetAssetEnabled(asset.id, false);
            }

            if (target != null && !target.hasScene)
            {
                manager.SetAssetEnabled(setId, true);
            }

            _ReconcileActiveScene();
            _RebuildSetsView();
        }

        /// <summary>
        /// Warps the current avatar to a <see cref="StageMark"/> within the given set's scene.
        /// Invoked by <see cref="SetBundleEntry.WarpTo"/> / <see cref="SetBundleEntry.WarpToOrigin"/> from
        /// the remote app's generic stage detail UI. An empty or null <paramref name="markLabel"/> warps
        /// to the origin, doubling as a "reset to zero".
        /// </summary>
        /// <param name="setId">Id of the set whose scene the mark lives in (<see cref="SetBundleEntry.id"/>).</param>
        /// <param name="markLabel">Label of the target mark, or empty/null for the origin.</param>
        public void WarpTo(string setId, string markLabel)
        {
            Vector3 targetPosition;
            Quaternion targetRotation;

            if (string.IsNullOrEmpty(markLabel))
            {
                targetPosition = Vector3.zero;
                targetRotation = Quaternion.identity;
            }
            else
            {
                var scene = _ResolveSetScene(setId);
                var mark = _FindMarkInScene(scene, markLabel);
                if (mark == null)
                {
                    Debug.LogWarning($"[LiveStudio] Stage mark not found: '{markLabel}' in set '{setId}'.");
                    return;
                }
                targetPosition = mark.position;
                targetRotation = mark.rotation;
            }

            // The avatar's world placement is driven by its anchor, which AvatarController sets to
            // its own transform. The animator root is rewritten every frame relative to that anchor,
            // so moving the anchor (not the animator GameObject) is what actually warps the avatar.
            var anchor = (SingletonService<IAvatarService>.subject as MonoBehaviour)?.transform;
            if (anchor == null)
            {
                Debug.LogWarning("[LiveStudio] No active avatar to place.");
                return;
            }

            anchor.SetPositionAndRotation(targetPosition, targetRotation);
        }

        // Marks added/removed by set scenes; rebuild so each set's `marks` list reflects them.
        private void _OnMarksChanged()
        {
            if (!_initialized) return;
            _RebuildSetsView();
        }

        /// <summary>
        /// Forwards remote-app edits of <c>sets</c> (an entry's <see cref="SetBundleEntry.enabled"/>
        /// flag) to the backing <see cref="SetBundleAsset"/>, which drives the actual load/unload.
        /// </summary>
        private void _OnPropertyChanged(LiveProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains(nameof(sets))) return;
            _TransferEnabledToAssets();
        }

        // Pushes each non-persistent entry's desired enabled state onto its SetBundleAsset. SetAssetEnabled
        // is a no-op when unchanged, so mirroring the whole list on any edit stays cheap.
        private void _TransferEnabledToAssets()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return;
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i].isPersistent) continue;
                manager.SetAssetEnabled(sets[i].id, sets[i].enabled);
            }
        }

        private void _EnsureSubscribed()
        {
            if (_subscribedManager != null) return;
            var manager = ExternalAssetManager.current;
            if (manager == null) return;
            manager.onAssetsChanged += _OnAssetsChanged;
            _subscribedManager = manager;
            // Catch up on any load/unload that completed before we subscribed.
            _OnAssetsChanged();
        }

        // The asset list changed (load/unload completed, entry added/removed): reconcile the active
        // scene and rebuild the projected view, then bring the intent and the world back together.
        private void _OnAssetsChanged()
        {
            _ReconcileActiveScene();
            _RebuildSetsView();

            // 適用の途中。ここで同期すると、まだ半分しか動いていない世界を意図として採る。
            if (_applyingStage) return;

            // 待っていた意図が通せるようになったかもしれない。通るまで実体からの同期はしない。
            if (_stagePending)
            {
                _ApplyStageIntent();
                return;
            }

            _SyncStageFromAssets();
        }

        /// <summary>
        /// 意図 (<see cref="_activeSet"/> / <see cref="_loadedSets"/>) を実体へ適用する。
        /// まだカタログに無い名前があれば待ち状態にし、次の変化で再試行する。
        /// </summary>
        private void _ApplyStageIntent()
        {
            if (!_initialized || _applyingStage) return;

            _applyingStage = true;
            try
            {
                _stagePending = !_TryApplyStage();
            }
            finally
            {
                _applyingStage = false;
            }

            _RebuildSetsView();
        }

        private bool _TryApplyStage()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return false;

            var wanted = _loadedSets ?? Array.Empty<string>();
            var complete = true;

            // 読み込みの集合をそのまま実体へ。ここに無い名前は「まだ来ていない」で、次の変化を待つ。
            var view = manager.assetsView;
            for (int i = 0; i < wanted.Length; i++)
            {
                if (string.IsNullOrEmpty(wanted[i])) continue;
                if (_FindSetAssetIdByName(manager, wanted[i]) == null) complete = false;
            }

            for (int i = 0; i < view.Count; i++)
            {
                var asset = view[i];
                if (!(asset is ISetAsset) || string.IsNullOrEmpty(asset.id)) continue;

                manager.SetAssetEnabled(asset.id, Array.IndexOf(wanted, asset.name) >= 0);
            }

            // 立っているステージ。空 = 何も言っていないので触らない (起動直後の既定)。
            if (!string.IsNullOrEmpty(_activeSet))
            {
                var activeId = _FindSetIdByName(_activeSet);
                if (activeId == null) complete = false;
                // 完全な切り替え (SwitchToSetByName) ではない。値が言っているのは「どれが立って
                // いるか」だけで、他を降ろすのはそれ以上のことを言う。
                else _ActivateSet(activeId, unloadOthers: false);
            }

            return complete;
        }

        // 実体が別経路で動いたとき (アセットページのトグル、SwitchToSetByName、ロード完了) に
        // 意図を追従させる。これが保存され、記録に載る値になる。
        private void _SyncStageFromAssets()
        {
            var active = string.Empty;
            var loaded = new List<string>();

            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i].isActive) active = sets[i].name ?? string.Empty;
                if (sets[i].isPersistent) continue;
                if (sets[i].enabled) loaded.Add(sets[i].name ?? string.Empty);
            }

            _activeSet = active;
            _loadedSets = loaded.ToArray();
        }

        // 投影 (sets) 上の表示名から id を引く。ブートストラップの合成エントリも含むので、
        // activeSet が持ち回るのはこちら。
        private string _FindSetIdByName(string setName)
        {
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i].name == setName) return sets[i].id;
            }

            return null;
        }

        // カタログ上のセットアセットの id。ブートストラップは含まない (アセットではない)。
        private static string _FindSetAssetIdByName(ExternalAssetManager manager, string setName)
        {
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is ISetAsset && view[i].name == setName) return view[i].id;
            }

            return null;
        }

        /// <summary>
        /// ライブシーンの復元後。意図はシャドウフィールドへ直接書かれるのでセッターを通らず、
        /// 適用されないまま残る。⚠ このコールバックはプロパティ書き込みでも発火するので、
        /// 適用は冪等 (SetAssetEnabled / _ActivateSet はどちらも変化が無ければ何もしない)。
        /// </summary>
        public void OnAfterLiveDeserialize()
        {
            if (!Application.isPlaying) return;

            _ApplyStageIntent();
        }

        /// <summary>
        /// Brings the actual active scene in line with the desired one: the loaded SetBundleAsset flagged
        /// <see cref="SetBundleAsset.isActive"/>, or the bootstrap scene when none is. A set flagged
        /// active but not yet loaded is skipped until it loads (a later assets-changed fires this again).
        /// </summary>
        private void _ReconcileActiveScene()
        {
            if (!Application.isPlaying) return;

            var current = SceneManager.GetActiveScene();
            var desired = _FindActiveSetAsset();

            if (desired != null)
            {
                if (desired.scene != current)
                {
                    SceneManager.SetActiveScene(desired.scene);
                }
            }
            else if (_persistentScene.IsValid() && _persistentScene.isLoaded && current != _persistentScene)
            {
                SceneManager.SetActiveScene(_persistentScene);
            }
        }

        // The loaded set asset that should be the active set, or null when the bootstrap set is.
        private ISetAsset _FindActiveSetAsset()
        {
            var manager = ExternalAssetManager.current;
            if (manager == null) return null;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is ISetAsset s && s.isActive && s.hasScene) return s;
            }
            return null;
        }

        private static bool _AnySetAssetActive(ExternalAssetManager manager)
        {
            if (manager == null) return false;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is ISetAsset s && s.isActive) return true;
            }
            return false;
        }

        private static void _ClearSetAssetActiveFlags(ExternalAssetManager manager)
        {
            if (manager == null) return;
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is ISetAsset s) s.isActive = false;
            }
        }

        /// <summary>
        /// Rebuilds <c>sets</c> from the current state: the persistent entry first, then each
        /// <see cref="SetBundleAsset"/> in <see cref="ExternalAssetManager"/> projected to a
        /// <see cref="SetBundleEntry"/>.
        /// </summary>
        private void _RebuildSetsView()
        {
            var manager = ExternalAssetManager.current;

            var list = new List<SetBundleEntry>();
            if (Application.isPlaying && _persistentScene.IsValid())
            {
                list.Add(_CreatePersistentEntry(manager));
            }

            if (manager != null)
            {
                var view = manager.assetsView;
                for (int i = 0; i < view.Count; i++)
                {
                    var asset = view[i];
                    if (!(asset is ISetAsset s)) continue;
                    list.Add(new SetBundleEntry
                    {
                        id = asset.id,
                        name = asset.name,
                        filePath = asset.filePath,
                        enabled = asset.enabled,
                        isLoaded = asset.isLoaded,
                        isActive = s.isActive,
                        isPersistent = false,
                        marks = _MarkLabelsInScene(s.scene),
                    });
                }
            }

            sets = list.ToArray();
            _Broadcast();
        }

        // The bootstrap set is active whenever no set bundle is flagged active.
        private SetBundleEntry _CreatePersistentEntry(ExternalAssetManager manager)
        {
            var sceneName = _persistentScene.IsValid() ? _persistentScene.name : null;
            return new SetBundleEntry
            {
                id = kPersistentSetId,
                name = string.IsNullOrEmpty(sceneName) ? "Studio" : sceneName,
                filePath = string.Empty,
                enabled = true,
                isLoaded = true,
                isActive = !_AnySetAssetActive(manager),
                isPersistent = true,
                marks = _MarkLabelsInScene(_persistentScene),
            };
        }

        // Resolves the scene a set's marks live in: the bootstrap scene for the persistent entry,
        // otherwise the loaded SetBundleAsset's scene (default/invalid when not loaded).
        private Scene _ResolveSetScene(string setId)
        {
            if (setId == kPersistentSetId) return _persistentScene;
            var asset = ExternalAssetManager.current?.FindAsset(setId) as ISetAsset;
            return asset != null ? asset.scene : default;
        }

        // Labels of the registered marks that belong to the given scene, in registration order.
        // Returns an empty array for an invalid scene (e.g. an unloaded set).
        private static string[] _MarkLabelsInScene(Scene scene)
        {
            if (!scene.IsValid()) return Array.Empty<string>();
            var labels = new List<string>();
            foreach (var mark in StageMarkRegistry.marks)
            {
                if (mark == null || mark.gameObject.scene != scene) continue;
                var label = mark.label;
                if (!string.IsNullOrEmpty(label)) labels.Add(label);
            }
            return labels.ToArray();
        }

        // The registered mark with the given label in the given scene, or null.
        private static StageMark _FindMarkInScene(Scene scene, string label)
        {
            if (!scene.IsValid()) return null;
            foreach (var mark in StageMarkRegistry.marks)
            {
                if (mark != null && mark.gameObject.scene == scene && mark.label == label) return mark;
            }
            return null;
        }

        private void _Broadcast()
        {
            // Pass the target instance (not the nullable handle): the (object) overload resolves the
            // handle via the registry. Passing an LiveObjectHandle? would box to object and fail
            // the registry lookup.
            LivePropertyBroadcast.BroadcastProperty(this, "sets");
        }
    }
}
