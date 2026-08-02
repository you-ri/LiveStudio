// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The general "fire and act" base feature of LiveStudio: a list of <see cref="OperationSet"/>s the user
    /// authors from the remote app, each binding one input <see cref="InputSource"/> to an ordered set of
    /// <see cref="OperationBase"/>s. Each frame every enabled set evaluates its input and runs its operations in
    /// order.
    ///
    /// A plain serializable <see cref="ILiveObject"/> (like <see cref="StageManager"/> /
    /// <see cref="ExternalAssetManager"/>), stored in the scene's <c>RemoteControlBehaviour._objects</c>
    /// through its <c>[SerializeReference]</c> list, so the authored sets persist in the scene.
    /// </summary>
    [Serializable]
    [LiveClass(Icon = "bolt", Category = "Operation", HideInScene = true)]
    // Renamed from ActionManager (and earlier TriggerManager). MovedFrom restores the
    // [SerializeReference] managed type in old scene/prefab YAML; FormerlyNamedAs restores the
    // RemoteControl @type discriminator from old *.live.json. MovedFrom allows only one source, so it
    // names the most recent prior type (ActionManager); the @type history keeps both old names.
    [MovedFrom(false, null, null, "ActionManager")]
    [FormerlyNamedAs("ActionManager")]
    [FormerlyNamedAs("TriggerManager")]
    public class OperationManager : ILiveObject, ILiveDeserializeCallback
    {
        const string kId = "c4e8b2d6-7a91-4f53-8e0c-1d9a6b3f2e74";

        // The active manager, so operations / sources can reach it. Set in OnEnable, cleared in OnDisable.
        // Reset on subsystem registration for safety when Domain Reload is disabled.
        [NonSerialized]
        private static OperationManager _current;

        public static OperationManager current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeCurrent() => _current = null;

        public string name { get; set; } = "Operation Manager";

        public LiveObjectHandle? liveObject => LiveObjectRegistry.FindByTarget(this);

        public string id => kId;

        /// <summary>The authored operation sets. Polymorphic input/operations serialize via SerializeReference.
        /// Persisted to the deck file (<see cref="PersistScope.Custom"/>), not to the live scene — see
        /// <see cref="DeckFile"/>.</summary>
        [SerializeReference, Select]
        [LiveField(persistScope = PersistScope.Custom)]
        [FormerlyNamedAs("actionSets")]
        [FormerlySerializedAs("actionSets")]
        public List<OperationSet> operationSets = new List<OperationSet>();

        /// <summary>The control decks: named grids of <see cref="DeckControl"/> tiles the remote app lays
        /// out, each operating an <see cref="OperationSet"/> by id. Persisted to the deck file with the
        /// operation sets (<see cref="PersistScope.Custom"/>).
        /// Tiles are added/removed/moved through the generic array REST; only whole decks need add/remove
        /// functions here.</summary>
        [LiveField(persistScope = PersistScope.Custom)]
        public List<Deck> decks = new List<Deck>();

        // Deck name -> the file that deck is (absolute path). One tab is one file, so this is the whole
        // deck/file relationship; nothing about it is persisted, since the files themselves are the state.
        [NonSerialized]
        private readonly Dictionary<string, string> _deckFilePaths = new Dictionary<string, string>(StringComparer.Ordinal);

        // Deck name -> the file text last written (or read) for it. The comparison baseline that keeps
        // autosave from rewriting untouched decks.
        [NonSerialized]
        private readonly Dictionary<string, string> _writtenDeckFiles = new Dictionary<string, string>(StringComparer.Ordinal);

        // The project the current deck model was built from. A different one means every deck belongs to
        // the previous project and has to go.
        [NonSerialized]
        private string _syncedProjectPath;

        // True while the deck files are being applied to the model. The apply restores the manager, which
        // fires the deserialize callback and property-changed events that would otherwise re-enter here.
        [NonSerialized]
        private bool _applyingDeckFiles;

        // Whether this manager may touch the project's deck files at all: only an enabled one that is not
        // in the middle of applying them. Keeps a bare instance (a unit test, a template being restored)
        // from writing into the user's project.
        private bool _deckFilesActive => _initialized && !_applyingDeckFiles;

        // The shared input map all KeyInputSources create their input actions in. Rebuilt when the set of
        // inputs changes or an input's binding/type changes. Runtime-only.
        [NonSerialized]
        private InputActionMap _map;

        [NonSerialized]
        private bool _initialized;

        // The control path captured by the most recent StartKeyCapture (empty while waiting / on cancel /
        // timeout). The add-operation dialog polls `capturedBinding` to show the key before committing.
        [NonSerialized]
        private string _capturedBinding = string.Empty;

        public void OnEnable()
        {
            _current = this;

            LiveObjectRegistry.Create<OperationManager>(this, kId);
            LiveClass.Get<OperationManager>().onPropertyChanged += _OnPropertyChanged;

            _EnsureOperationSetIds();
            _RebuildInputMap();

            // Marked live before the first sync: the deck-file work checks this to tell a running app
            // apart from a bare instance (a unit test, a template being deserialized).
            _initialized = true;

            // The decks are the project's deck files, so they follow the project crawl rather than the
            // live scene. Sync once now for whatever the catalog already holds (the crawl may have run
            // before this object was enabled) and follow it from here on.
            ExternalAssetManager.onCatalogChanged += _OnCatalogChanged;
            SyncDeckFiles();
        }

        public void OnDisable()
        {
            _initialized = false;

            LiveClass.Get<OperationManager>().onPropertyChanged -= _OnPropertyChanged;

            ExternalAssetManager.onCatalogChanged -= _OnCatalogChanged;

            _TeardownInputMap();

            LiveObjectRegistry.FindByTarget(this)?.Unregister();

            if (_current == this) _current = null;
        }

        public void OnDispose() => OnDisable();

        public void Update()
        {
            if (!_initialized) return;
            if (!Application.isPlaying) return;

            for (int i = 0; i < operationSets.Count; i++)
            {
                var set = operationSets[i];
                if (set == null) continue;

                // Snapshot the previous frame's hold before overwriting it, so the helper can detect the
                // rising/falling edge of the manual hold.
                bool wasHeld = set.heldPrev;
                set.heldPrev = set.held;

                bool fired = TryGetFiringContext(set, wasHeld, out var context);
                // Consume the sub-frame press pulse: it was read during evaluation above (release edge), so
                // clear it now that this frame has accounted for it. A collapsed press-release fires once.
                set.heldPulse = false;
                if (!fired)
                {
                    set.lastValue = 0f;
                    continue;
                }

                // Record this frame's firing output so the remote app can poll it (operationSetValues).
                set.lastValue = context.value;

                // A set that just latched on (rising active edge) clears its groupmates so only one set in a
                // named group stays on. No-op for ungrouped or non-toggle winners (see ApplyExclusiveGroup),
                // so this is cheap for the common case.
                if (context.pressed && context.active)
                {
                    ApplyExclusiveGroup(operationSets, i);
                }

                var operations = set.operations;
                if (operations == null) continue;
                for (int j = 0; j < operations.Count; j++)
                {
                    operations[j]?.Apply(in context);
                }
            }
        }

        /// <summary>
        /// Computes a set's firing context for this frame given the previous frame's hold state. Returns
        /// false (skip, value 0) when the set is disabled or has no input and is not held. Pure aside from
        /// advancing the input source's edge state, so it is unit-testable without play mode.
        /// </summary>
        /// <param name="set">The operation set to evaluate. Must not be null.</param>
        /// <param name="wasHeld">The set's <see cref="OperationSet.held"/> on the previous frame.</param>
        internal static bool TryGetFiringContext(OperationSet set, bool wasHeld, out OperationContext context)
        {
            if (!TryEvaluateContext(set, wasHeld, out context)) return false;
            // A DeckSlider maps its normalized 0..1 output onto [min, max] so value operations write real-range
            // values; every other control keeps the identity 0..1 range. Applied once here (regardless of manual
            // vs bound input) so the range is honored uniformly. value itself stays normalized (see MappedValue).
            if (set.control is DeckSlider slider) context = context.WithRange(slider.min, slider.max);
            return true;
        }

        // The raw evaluation (normalized 0..1, no value-range awareness). Split out so TryGetFiringContext can
        // apply the control's value range in one place instead of at every OperationContext construction site.
        private static bool TryEvaluateContext(OperationSet set, bool wasHeld, out OperationContext context)
        {
            // The behaviour axis is the set's control kind (the single source of truth); the bound input
            // interprets its raw value through it. A button-mode set commits its one-shot trigger on release,
            // so a held press must not trigger yet; other modes commit on the press edge. Mirrors
            // InputSource.Evaluate for the manual hold.
            InputMode mode = set.control?.mode ?? InputMode.Button;
            bool isButton = mode == InputMode.Button;

            if (set.hasManualValue)
            {
                // Manual value from the remote app's Value-mode slider. Takes precedence over the bound
                // input and works even when disabled (the user dragged it explicitly), like the manual hold.
                // Edges stay false: SetPropertyOperation's float path reads only the value (mapped through the
                // control's range), and exclusivity needs context.pressed — so a Value set never disturbs group
                // radios. 0.5 mirrors
                // InputSource.kThreshold (protected there, so inlined) for bool-property targets.
                float manual = Mathf.Clamp01(set.manualValue);
                context = new OperationContext(manual, pressed: false, released: false,
                    active: manual >= 0.5f, triggered: false);
                return true;
            }

            if (set.held)
            {
                // Manual hold: fire as if the input were held active. Overrides the bound input and works
                // even when the set is disabled, since the user triggered it explicitly. Rising edge on the
                // first held frame; button defers its trigger to the release frame.
                bool rising = !wasHeld;
                context = new OperationContext(1f, pressed: rising, released: false, active: true,
                    triggered: isButton ? false : rising);
                return true;
            }

            if (wasHeld || set.heldPulse)
            {
                // Hold released this frame: a single falling edge, then back to normal next frame. Button
                // commits its one-shot trigger here. heldPulse covers the sub-frame case where a momentary
                // press (held true then false) collapsed within one Update frame so wasHeld never became true —
                // without it the button's one-shot would be dropped on fast taps (intermittent "no reaction").
                context = new OperationContext(0f, pressed: false, released: true, active: false,
                    triggered: isButton);
                return true;
            }

            if (!set.enabled || set.input == null)
            {
                context = default;
                return false;
            }

            context = set.input.Evaluate(mode);
            return true;
        }

        /// <summary>Per-set firing output (0..1) in <see cref="operationSets"/> order, for the remote app to
        /// poll and light its cards while an input (or the manual hold) is firing. Read-only and hidden;
        /// reflects the value recorded by the most recent <see cref="Update"/>. Polled rather than pushed
        /// to every client, so it costs nothing while the Operations page is not open.</summary>
        [LiveProperty, Hide]
        public float[] operationSetValues
        {
            get
            {
                var values = new float[operationSets.Count];
                for (int i = 0; i < operationSets.Count; i++)
                {
                    values[i] = operationSets[i]?.lastValue ?? 0f;
                }
                return values;
            }
        }

        public void Reset() { }

        public void OnAfterLiveDeserialize()
        {
            // A live-scene restore replaces the operation sets list; make sure every set is addressable
            // before anything reads it by id (see _EnsureOperationSetIds).
            _EnsureOperationSetIds();
            // Rebuild the input map so the restored inputs are bound. Idempotent, so harmless if it also
            // fires on an unrelated property write.
            _RebuildInputMap();
            // No unplaced state: a restored / pasted scene may carry controls with no deck (→ default page) or
            // a name no deck has yet (→ recreate that deck by name, so pasted operation data brings its deck
            // along). Idempotent once every control resolves to an existing deck.
            _NormalizeControlPlacement();
            // Enforce each tile's fixed per-kind width (DeckControl.fixedWidth) on restored/older scenes. Idempotent.
            _EnforceControlWidths();
            // A persisted manual hold is a steady state, not a fresh press: seed each set's edge baseline to
            // its restored hold so the first Update after a reload resumes the hold without manufacturing a
            // rising edge (which would re-fire the operation / re-run group exclusivity for a set left on).
            for (int i = 0; i < operationSets.Count; i++)
            {
                var set = operationSets[i];
                if (set != null) set.heldPrev = set.held;
            }
        }

        /// <summary>Adds a new operation set (default key input, no operations) and rebuilds the input map.</summary>
        [LiveFunction]
        public void AddOperationSet() => AddOperationSet(new KeyInputSource());

        /// <summary>Adds a pre-built operation set from the given input and operations, rebuilds the input map and
        /// returns the created set. Generic and feature-agnostic: callers (e.g. the expression binding bridge)
        /// supply whatever concrete <see cref="InputSource"/> / <see cref="OperationBase"/>s they need without
        /// this manager knowing about them.</summary>
        public OperationSet AddOperationSet(InputSource input, params OperationBase[] operations)
            => _AddOperationSet(input, null, operations);

        // Shared creation. A null/empty name keeps the generic default; the bind-a-control flows pass the
        // target's function/property name so the new set reads meaningfully in the remote app's list.
        private OperationSet _AddOperationSet(InputSource input, string name, OperationBase[] operations, string group = null, DeckControl control = null)
        {
            var set = new OperationSet
            {
                id = Guid.NewGuid().ToString(),
                name = string.IsNullOrEmpty(name) ? "Operation Set" : name,
                enabled = true,
                group = string.IsNullOrEmpty(group) ? string.Empty : group,
                input = input ?? new KeyInputSource(),
                operations = operations != null ? new List<OperationBase>(operations) : new List<OperationBase>(),
                control = control ?? new DeckButton(),
            };
            // Apply the tile's fixed per-kind width before placing so the free-cell scan accounts for the span.
            _ApplyControlWidth(set.control);
            // No unplaced state: every new control is placed on the default page at a free cell.
            _PlaceOnDefaultDeck(set.control);
            operationSets.Add(set);
            _RebuildInputMap();
            _Broadcast();
            _FlushDirtyDecks();
            return set;
        }

        // The deck control kind for a requested behaviour mode (changeable later from the remote app via
        // SetControlType): Toggle→checkbox, Value→slider, otherwise momentary push. The control is the set's
        // single behaviour axis, so this maps the add-operation dialog's mode choice onto the control kind.
        private static DeckControl _DefaultControlForMode(InputMode mode)
        {
            if (mode == InputMode.Toggle) return new DeckToggle();
            if (mode == InputMode.Value) return new DeckSlider();
            return new DeckButton();
        }

        // Creates a fresh concrete control of the named kind, or null for an unknown name.
        private static DeckControl _CreateControl(string typeName)
        {
            if (typeName == "DeckToggle") return new DeckToggle();
            if (typeName == "DeckSlider") return new DeckSlider();
            if (typeName == "DeckButton") return new DeckButton();
            return null;
        }

        // Builds a key input already bound to the given control path (empty = unbound). The add-operation dialog
        // captures the key first (see StartKeyCapture) and commits it here, so the set is born bound. The
        // behaviour mode is no longer carried by the input; it comes from the set's control kind.
        private static KeyInputSource _KeyInput(string binding)
        {
            var input = new KeyInputSource();
            if (!string.IsNullOrEmpty(binding)) input.SetInitialBinding(binding);
            return input;
        }

        /// <summary>Creates an operation set that invokes the <c>[LiveFunction]</c> (named
        /// <paramref name="functionName"/>) on the object with id <paramref name="targetId"/>, bound to the
        /// momentary key <paramref name="binding"/> (a control path; empty = unbound). The set is named
        /// <paramref name="name"/> (the function's display name; falls back to the generic default when empty).
        /// <paramref name="argsJson"/> carries the positional call arguments as a JSON array string (the same
        /// shape the REST invoke body uses); empty = no arguments. <paramref name="propertyPath"/> (slash
        /// transport form; e.g. "sets/0") addresses the property value that owns the function when it is not on
        /// the target object directly (a nested exposed function); empty = on the target itself. Both are
        /// trailing optionals, so callers of the no-argument / non-nested overload are unaffected. Returns the
        /// new set's id. Drives the "bind this function button to a key" flow.</summary>
        [LiveFunction]
        public string AddFunctionOperation(string targetId, string functionName, string name, string binding, string argsJson = "", string propertyPath = "")
        {
            var set = _AddOperationSet(
                _KeyInput(binding),
                name,
                new OperationBase[] { new InvokeFunctionOperation { targetId = targetId, functionName = functionName, argsJson = argsJson, propertyPath = propertyPath } });
            return set.id;
        }

        /// <summary>Creates an operation set that drives the property <paramref name="propertyPath"/> on the object
        /// with id <paramref name="targetId"/>, bound to the key <paramref name="binding"/> (a control path;
        /// empty = unbound) in the given <paramref name="mode"/> (<see cref="InputMode"/> name, case-insensitive;
        /// unrecognized/empty falls back to <see cref="InputMode.Toggle"/>). Toggle latches on/off per press;
        /// Button is momentary (on while held). The set is named <paramref name="name"/> (the property's display
        /// name; falls back to the generic default when empty). The optional <paramref name="group"/> assigns an
        /// exclusivity group (empty = ungrouped); Toggle-mode sets sharing a non-empty group act as a radio.
        /// For a Value-mode (slider) control, <paramref name="min"/> and <paramref name="max"/> are inherited
        /// from the source slider so the drag maps its 0..1 onto that range; they are ignored for non-slider
        /// kinds. Returns the new set's id. Drives the "bind this control to a key" flow.</summary>
        [LiveFunction]
        public string AddPropertyOperation(string targetId, string propertyPath, string mode, string name, string binding, string group, float min = 0f, float max = 1f)
        {
            var inputMode = System.Enum.TryParse<InputMode>(mode, ignoreCase: true, out var m)
                ? m
                : InputMode.Toggle;
            var control = _DefaultControlForMode(inputMode);
            // Only a slider carries a value range; the mode maps to DeckSlider for Value inputs.
            if (control is DeckSlider slider)
            {
                slider.min = min;
                slider.max = max;
            }
            var set = _AddOperationSet(
                _KeyInput(binding),
                name,
                new OperationBase[] { new SetPropertyOperation { targetId = targetId, propertyPath = propertyPath } },
                group,
                control);
            return set.id;
        }

        /// <summary>Creates an operation set that switches the active avatar to the one with asset id
        /// <paramref name="avatarAssetId"/> (a <see cref="SwitchAvatarOperation"/> matching that asset's display
        /// name) when its tile fires, with the deck tile's full-frame background set to that avatar's preview
        /// (<see cref="DeckControl.backgroundAssetId"/> = the same id). The set is named after the avatar (falls
        /// back to the generic default when the name is empty). A momentary <see cref="DeckButton"/> control — a
        /// tap switches the avatar. The avatar name and label are resolved from
        /// <see cref="ExternalAssetManager"/> so the remote app only supplies the id. Returns the new set's id.
        /// Drives the remote app's "add this avatar as a switch tile" affordance on the Avatar page.</summary>
        [LiveFunction]
        public string AddSwitchAvatarOperation(string avatarAssetId)
        {
            // Resolve the avatar's display name from the asset (SwitchAvatarOperation matches avatars by name,
            // like its dropdown). Empty id / unknown asset yields an empty name, which selects the default avatar.
            var manager = ExternalAssetManager.current;
            var asset = manager?.FindAsset(avatarAssetId);
            string avatarName = asset?.name ?? string.Empty;
            // Persist a portable, project-relative reference for the tile background (the incoming id is an
            // absolute, machine-specific path), so a saved deck survives moving the project / another machine.
            // AvatarImageHandler resolves it back through FindAssetByReference.
            string background = manager?.GetPortableAssetReference(avatarAssetId) ?? avatarAssetId ?? string.Empty;
            var control = new DeckButton { backgroundAssetId = background };
            var set = _AddOperationSet(
                new KeyInputSource(),
                avatarName,
                new OperationBase[] { new SwitchAvatarOperation { avatar = avatarName } },
                null,
                control);
            return set.id;
        }

        /// <summary>The control path captured by the most recent <see cref="StartKeyCapture"/> (empty while
        /// waiting, or if it was cancelled / timed out). The remote app's add-operation dialog polls this to show
        /// the captured key before committing the new set. Runtime-only.</summary>
        [LiveProperty, Hide]
        public string capturedBinding => _capturedBinding;

        /// <summary>Listens for the next key/button on the Studio machine and stores it in
        /// <see cref="capturedBinding"/>, creating no operation set. Lets the add-operation dialog capture an input
        /// before the user commits (Add), so the set can be born already bound. Hidden so the generic UI does
        /// not surface it as a bare button (it needs the dialog's key-capture feedback).</summary>
        [LiveFunction, Hide]
        public void StartKeyCapture() => _StartKeyCaptureAsync();

        // Captures into a throwaway map/action so no operation set is created and the shared input map is left
        // untouched. RuntimeKeyBindingSystem detects the key via the global InputSystem.onEvent, so the map
        // does not need to be enabled.
        private async void _StartKeyCaptureAsync()
        {
            _capturedBinding = string.Empty;

            var map = new InputActionMap("KeyCapture");
            const string probe = "Probe";
            var action = map.AddAction(probe, InputActionType.Value, expectedControlLayout: "<Value>");
            try
            {
                var (success, _) = await RuntimeKeyBindingSystem.StartBindingAsync(
                    new RuntimeKeyBindingData(), map, probe, 0);
                if (success && action.bindings.Count > 0)
                {
                    _capturedBinding = action.bindings[0].effectivePath;
                    _Broadcast();
                }
            }
            finally
            {
                map.Dispose();
            }
        }

        /// <summary>Removes the operation set with the given id and rebuilds the input map.</summary>
        [LiveFunction]
        public void RemoveOperationSet(string operationSetId)
        {
            int index = _IndexOf(operationSetId);
            if (index < 0) return;
            operationSets.RemoveAt(index);
            _RebuildInputMap();
            _Broadcast();
            _FlushDirtyDecks();
        }

        /// <summary>Adds a new control deck with a unique auto-generated name and returns that name. The deck
        /// is a file (<c>Decks/&lt;name&gt;.deck.json</c>), written immediately so the tab always has one behind
        /// it. The remote app then adds tiles by placing controls' <see cref="DeckControl.deckName"/> onto it.</summary>
        [LiveFunction]
        public string AddDeck()
        {
            // The name becomes the file name, so reserve the sanitized form: otherwise the file would be
            // named one thing, the tab another, and the next crawl would rename the tab under the user.
            var deck = new Deck { name = _UniqueDeckName(DeckFile.SanitizeFileName("Deck"), null) };
            decks.Add(deck);
            // ⚠ 名前を写しておく。下の書き出しはファイル集合との同期を伴い、その復元が decks の
            // 要素を作り直す (この deck が別の名前で上書きされる)。返し先は「今作ったタブを選ぶ」
            // ために使われるので、写さないと別のタブが選ばれる。
            var name = deck.name;
            _BroadcastDecks();
            _FlushDirtyDecks();
            return name;
        }

        /// <summary>Removes the deck with the given name, <b>deleting its file and the operation sets placed on
        /// it</b> — a deck is a file, so removing the tab removes what it held. The remote app confirms with the
        /// user first. No-op if the name is unknown.</summary>
        [LiveFunction]
        public void RemoveDeck(string deckName)
        {
            if (string.IsNullOrEmpty(deckName)) return;
            int index = decks.FindIndex(p => p != null && p.name == deckName);
            if (index < 0) return;
            decks.RemoveAt(index);

            bool anyRemoved = false;
            for (int i = operationSets.Count - 1; i >= 0; i--)
            {
                var control = operationSets[i]?.control;
                if (control != null && control.deckName == deckName)
                {
                    operationSets.RemoveAt(i);
                    anyRemoved = true;
                }
            }

            _DeleteDeckFile(deckName);
            // The catalog still lists the file that was just deleted; re-crawl so the entry goes with it
            // (and so this manager's own view of the file set stays the one the crawl reports).
            if (_deckFilesActive) ProjectManager.RecrawlProject();

            if (anyRemoved) _RebuildInputMap();
            _BroadcastDecks();
            if (anyRemoved) _Broadcast();
        }

        /// <summary>Renames the deck currently named <paramref name="deckName"/> to <paramref name="newName"/>,
        /// auto-suffixing on collision so deck names stay unique, and updates every control placed on it so the
        /// reference follows the rename. The deck's file is renamed with it (the file name is the deck's name).
        /// Returns the resulting (possibly suffixed) name; returns the original name unchanged for an unknown
        /// current name, an empty new name, or a no-op rename.</summary>
        [LiveFunction]
        public string RenameDeck(string deckName, string newName)
        {
            if (string.IsNullOrEmpty(deckName) || string.IsNullOrEmpty(newName)) return deckName;
            int index = decks.FindIndex(p => p != null && p.name == deckName);
            if (index < 0) return deckName;

            // The new name has to survive as a file name, since that is where it is kept.
            string unique = _UniqueDeckName(DeckFile.SanitizeFileName(newName), decks[index]);
            if (unique == deckName) return deckName; // no effective change
            decks[index].name = unique;

            // Controls reference decks by name; follow the rename so their tiles stay on this page.
            bool anyMoved = false;
            for (int i = 0; i < operationSets.Count; i++)
            {
                var control = operationSets[i]?.control;
                if (control != null && control.deckName == deckName)
                {
                    control.deckName = unique;
                    anyMoved = true;
                }
            }

            _MoveDeckFile(deckName, unique);
            // The catalog holds the old file name; re-crawl so the entry follows the rename.
            if (_deckFilesActive) ProjectManager.RecrawlProject();

            _BroadcastDecks();
            if (anyMoved) _Broadcast();
            _FlushDirtyDecks();
            return unique;
        }

        /// <summary>Places (or moves) the control of the operation set with the given id onto the deck named
        /// <paramref name="deckName"/> at grid cell (<paramref name="x"/>, <paramref name="y"/>). An empty
        /// <paramref name="deckName"/> falls back to the default page at a free cell (no unplaced state).
        /// No-op for an unknown id.</summary>
        [LiveFunction]
        public void PlaceControl(string operationSetId, string deckName, int x, int y)
        {
            int index = _IndexOf(operationSetId);
            if (index < 0) return;
            var control = operationSets[index]?.control;
            if (control == null) return;
            if (string.IsNullOrEmpty(deckName))
            {
                _PlaceOnDefaultDeck(control);
            }
            else
            {
                control.deckName = deckName;
                // Keep the tile on-grid for its (possibly 2-wide) span.
                int columns = _DeckColumns(deckName);
                control.x = Mathf.Clamp(x, 0, Mathf.Max(0, columns - Mathf.Max(1, control.w)));
                control.y = Mathf.Max(0, y);
            }
            _Broadcast();
            // The tile may have crossed decks; both files are written by the diff.
            _FlushDirtyDecks();
        }

        /// <summary>Places the control of the operation set with the given id onto the deck named
        /// <paramref name="deckName"/> at that deck's first free grid cell (row-major scan), so an added tile
        /// never lands on top of one already there. An empty <paramref name="deckName"/> means the default
        /// page. This is the "add a tile to this deck" entry point; <see cref="PlaceControl"/> takes an
        /// explicit cell and stays the drag-and-drop move. No-op for an unknown id.</summary>
        [LiveFunction]
        public void PlaceControlOnFreeCell(string operationSetId, string deckName)
        {
            int index = _IndexOf(operationSetId);
            if (index < 0) return;
            var control = operationSets[index]?.control;
            if (control == null) return;
            _PlaceOnFreeCell(control, deckName);
            _Broadcast();
            _FlushDirtyDecks();
        }

        /// <summary>Swaps the control kind (<c>DeckButton</c> / <c>DeckToggle</c> / <c>DeckSlider</c>) of
        /// the operation set with the given id, preserving its placement (deck and grid cell/span). No-op for
        /// an unknown id or type name.</summary>
        [LiveFunction]
        public void SetControlType(string operationSetId, string typeName)
        {
            int index = _IndexOf(operationSetId);
            if (index < 0) return;
            var set = operationSets[index];
            if (set == null) return;

            var next = _CreateControl(typeName);
            if (next == null) return;

            var old = set.control;
            if (old != null)
            {
                next.deckName = old.deckName;
                next.x = old.x;
                next.y = old.y;
                next.w = old.w;
                next.h = old.h;
            }
            set.control = next;
            // Enforce the new kind's fixed width after the swap (keeps it on-grid).
            _ApplyControlWidth(next);
            _Broadcast();
            _FlushDirtyDecks();
        }

        // Returns a deck name unique among all decks except <paramref name="self"/>, auto-suffixing " 2",
        // " 3", … on collision so a name stays usable as the placement key.
        private string _UniqueDeckName(string desired, Deck self)
        {
            string baseName = string.IsNullOrEmpty(desired) ? "Deck" : desired;
            string candidate = baseName;
            int n = 2;
            while (_NameTaken(candidate, self))
            {
                candidate = baseName + " " + n;
                n++;
            }
            return candidate;
        }

        private bool _NameTaken(string name, Deck self)
        {
            for (int i = 0; i < decks.Count; i++)
            {
                var p = decks[i];
                if (p != null && p != self && p.name == name) return true;
            }
            return false;
        }

        // No unplaced state: ensure a default page exists and return its name. The first deck is the default;
        // a fresh one (with a unique name) is created when the list is empty, so a new control always has a home.
        private string _EnsureDefaultDeckName()
        {
            for (int i = 0; i < decks.Count; i++)
            {
                if (decks[i] != null && !string.IsNullOrEmpty(decks[i].name))
                    return decks[i].name;
            }
            var deck = new Deck { name = _UniqueDeckName("Deck", null) };
            decks.Add(deck);
            _BroadcastDecks();
            return deck.name;
        }

        // Places the control on the default page at the first free grid cell (row-major scan).
        private void _PlaceOnDefaultDeck(DeckControl control) => _PlaceOnFreeCell(control, null);

        // Places the control on the deck named deckName at its first free grid cell (row-major scan), so a
        // tile never lands on top of one already there. An empty name means the default page (created on
        // demand), which keeps "no unplaced state" true whatever the caller passes.
        private void _PlaceOnFreeCell(DeckControl control, string deckName)
        {
            if (control == null) return;
            string target = string.IsNullOrEmpty(deckName) ? _EnsureDefaultDeckName() : deckName;
            _FindFreeCell(target, control, out int x, out int y);
            control.deckName = target;
            control.x = x;
            control.y = y;
        }

        // Finds the first grid cell on the deck where the control's span fits without overlapping another
        // tile, scanning row by row. The lowest fully-empty row is always free, so the scan is bounded there.
        private void _FindFreeCell(string deckName, DeckControl placing, out int x, out int y)
        {
            int columns = _DeckColumns(deckName);

            int w = Mathf.Clamp(placing != null ? placing.w : 1, 1, columns);
            int h = Mathf.Max(1, placing != null ? placing.h : 1);

            // The lowest fully-empty row is always free, so bound the scan there.
            int maxRow = 0;
            for (int i = 0; i < operationSets.Count; i++)
            {
                var c = operationSets[i]?.control;
                if (c != null && c != placing && c.deckName == deckName)
                    maxRow = Mathf.Max(maxRow, c.y + Mathf.Max(1, c.h));
            }

            for (int row = 0; row <= maxRow; row++)
            {
                for (int col = 0; col + w <= columns; col++)
                {
                    if (_IsAreaFree(deckName, placing, col, row, w, h)) { x = col; y = row; return; }
                }
            }
            x = 0;
            y = 0;
        }

        // True when no other control on the deck overlaps the given grid rectangle.
        private bool _IsAreaFree(string deckName, DeckControl placing, int x, int y, int w, int h)
        {
            for (int i = 0; i < operationSets.Count; i++)
            {
                var c = operationSets[i]?.control;
                if (c == null || c == placing || c.deckName != deckName) continue;
                int cw = Mathf.Max(1, c.w);
                int ch = Mathf.Max(1, c.h);
                if (x < c.x + cw && c.x < x + w && y < c.y + ch && c.y < y + h) return false;
            }
            return true;
        }

        // The logical column count of the deck with the given name (default 8 for an unknown name).
        private int _DeckColumns(string deckName)
        {
            var deck = decks.Find(p => p != null && p.name == deckName);
            return deck != null && deck.columns > 0 ? deck.columns : 8;
        }

        // Enforces a control's fixed per-kind width (see DeckControl.fixedWidth) and re-clamps x so the tile
        // stays within the deck's columns after the width changes. No type switch — each kind declares its span.
        private void _ApplyControlWidth(DeckControl control)
        {
            if (control == null) return;
            control.w = control.fixedWidth;
            int columns = _DeckColumns(control.deckName);
            if (control.x + control.w > columns) control.x = Mathf.Max(0, columns - control.w);
        }

        // Enforces the fixed per-kind tile width across all controls. Idempotent.
        private void _EnforceControlWidths()
        {
            bool any = false;
            for (int i = 0; i < operationSets.Count; i++)
            {
                var c = operationSets[i]?.control;
                if (c == null) continue;
                if (c.w != c.fixedWidth)
                {
                    _ApplyControlWidth(c);
                    any = true;
                }
            }
            if (any) _Broadcast();
        }

        // No unplaced state: make sure every control resolves to an existing deck.
        //  - empty name        → place on the default page at a free cell (genuinely unplaced).
        //  - non-empty unknown  → recreate a deck with that name and keep the control where it is, so pasting
        //                         serialized operation data automatically reconstructs the deck it referenced.
        //  - known name         → leave as is.
        private void _NormalizeControlPlacement()
        {
            bool changed = false;
            for (int i = 0; i < operationSets.Count; i++)
            {
                var control = operationSets[i]?.control;
                if (control == null) continue;

                if (string.IsNullOrEmpty(control.deckName))
                {
                    _PlaceOnDefaultDeck(control);
                    changed = true;
                }
                else if (!decks.Exists(p => p != null && p.name == control.deckName))
                {
                    decks.Add(new Deck { name = control.deckName });
                    changed = true;
                }
            }
            if (changed)
            {
                _BroadcastDecks();
                _Broadcast();
            }
        }

        /// <summary>Toggles the manual hold of the operation set with the given id. While held the set fires
        /// from <see cref="Update"/> as if its input were held active (independent of
        /// <see cref="OperationSet.enabled"/>); toggling off releases it. Lets the remote app fire a set from
        /// its card without a bound input. The broadcast lets the card reflect the new hold state.</summary>
        [LiveFunction]
        public void ToggleOperationSet(string operationSetId)
        {
            int index = _IndexOf(operationSetId);
            if (index < 0) return;

            var set = operationSets[index];
            if (set == null) return;

            set.SetHeld(!set.held);
            // Turning a grouped toggle on clears its groupmates (toggle-style radio); no-op otherwise.
            if (set.held) ApplyExclusiveGroup(operationSets, index);
            _Broadcast();
        }

        /// <summary>Sets the manual hold of the operation set with the given id to an explicit state. Used by the
        /// remote app's momentary (Button-mode) card, which holds on press and releases on pointer-up: an
        /// idempotent set avoids the desync a flip (<see cref="ToggleOperationSet"/>) would suffer if a press
        /// or release event were missed. No-op (and no broadcast) when already in the requested state.</summary>
        [LiveFunction]
        public void SetOperationSetHeld(string operationSetId, bool held)
        {
            int index = _IndexOf(operationSetId);
            if (index < 0) return;

            var set = operationSets[index];
            if (set == null || set.held == held) return;

            set.SetHeld(held);
            if (held) ApplyExclusiveGroup(operationSets, index);
            _Broadcast();
        }

        /// <summary>Sets the manual value override (0..1, clamped) of the operation set with the given id, from
        /// the remote app's Value-mode slider card. While set the set fires from <see cref="Update"/> with
        /// this value (sticky, overriding the bound input), independent of <see cref="OperationSet.enabled"/>.
        /// No broadcast: <see cref="OperationSet"/>'s manual value is <c>[NonSerialized]</c> (absent from the
        /// operationSets payload), and the slider reads its value back through the polled
        /// <see cref="operationSetValues"/>; broadcasting per drag frame would be pure overhead.</summary>
        [LiveFunction]
        public void SetOperationSetValue(string operationSetId, float value)
        {
            int index = _IndexOf(operationSetId);
            if (index < 0) return;

            var set = operationSets[index];
            if (set == null) return;

            set.SetManualValue(Mathf.Clamp01(value));
        }

        // -------------------------------------------------------
        // Deck files (*.deck.json) — one file is one deck (one tab)
        // -------------------------------------------------------

        /// <summary>
        /// Rebuilds the decks from the project's deck files. One tab is one file, so this is where tabs
        /// come from: every <c>*.deck.json</c> the project crawl found becomes a deck, named after its file.
        ///
        /// Only the set of files matters here. A file already loaded is left alone (its in-memory state,
        /// including a manual hold in the middle of a show, must survive an unrelated crawl); files that
        /// appeared are read, files that disappeared take their deck and its operation sets with them.
        /// </summary>
        internal void SyncDeckFiles()
        {
            // The apply below restores this object, which calls back into here through the broadcast.
            if (!_deckFilesActive) return;

            var projectPath = ProjectManager.projectPath;
            bool projectChanged = !string.Equals(projectPath, _syncedProjectPath, StringComparison.Ordinal);
            _syncedProjectPath = projectPath;
            if (projectChanged)
            {
                // Every deck belonged to the previous project. Custom-scope members are invisible to the
                // reset a project switch performs, so drop them here or they follow the user across.
                _deckFilePaths.Clear();
                _writtenDeckFiles.Clear();
            }

            var desired = _DiscoverDeckFiles();
            if (!projectChanged && _MatchesLoadedDeckFiles(desired)) return;

            _ApplyDeckFiles(desired);
        }

        // The deck files the project crawl knows about, as (deck name, absolute path) in tab order.
        // Names come from the file names and are made unique, since two folders may hold the same name.
        private static List<KeyValuePair<string, string>> _DiscoverDeckFiles()
        {
            var result = new List<KeyValuePair<string, string>>();
            var manager = ExternalAssetManager.current;
            if (manager == null) return result;

            var paths = new List<string>();
            var view = manager.assetsView;
            for (int i = 0; i < view.Count; i++)
            {
                if (view[i] is DeckAsset deck && !string.IsNullOrEmpty(deck.filePath)) paths.Add(deck.filePath);
            }
            // The catalog's order follows the crawl; sort so the tabs do not shuffle between runs.
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Count; i++)
            {
                var name = AssetTypeRegistry.DeriveName(paths[i]);
                if (string.IsNullOrEmpty(name)) name = "Deck";
                var unique = name;
                int n = 2;
                while (!used.Add(unique)) unique = name + " " + (n++);
                result.Add(new KeyValuePair<string, string>(unique, paths[i]));
            }
            return result;
        }

        // True when the loaded decks already are exactly these files, in this order.
        private bool _MatchesLoadedDeckFiles(List<KeyValuePair<string, string>> desired)
        {
            if (desired.Count != decks.Count) return false;
            for (int i = 0; i < desired.Count; i++)
            {
                var deck = decks[i];
                if (deck == null || deck.name != desired[i].Key) return false;
                if (!_deckFilePaths.TryGetValue(deck.name, out var path) ||
                    !string.Equals(path, desired[i].Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        // Builds the whole model (decks + operation sets) from the given files and applies it in one
        // restore. Going through the serializer rather than mutating the lists keeps a single code path
        // for turning a file's JSON into OperationSet objects — the same one the live scene uses.
        private void _ApplyDeckFiles(List<KeyValuePair<string, string>> desired)
        {
            var handle = liveObject;
            if (!handle.HasValue) return;

            var current = JObject.Parse(LiveObjectSnapshot.Capture(handle.Value, PersistScope.Custom));
            var currentSets = current["operationSets"] as JArray ?? new JArray();

            var newDecks = new JArray();
            var newSets = new JArray();
            var newPaths = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < desired.Count; i++)
            {
                var name = desired[i].Key;
                var path = desired[i].Value;

                int columns;
                JArray sets;
                if (_deckFilePaths.TryGetValue(name, out var loadedPath) &&
                    string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    // Already loaded: keep what is in memory rather than re-reading the file.
                    columns = _DeckColumns(name);
                    sets = _SetsOnDeck(currentSets, name);
                }
                else if (!_TryReadDeckFile(path, name, out columns, out sets))
                {
                    // Unreadable file: leave the tab out entirely rather than showing an empty one that
                    // would overwrite the file on the next edit.
                    continue;
                }

                newDecks.Add(new JObject { ["@type"] = "Deck", ["name"] = name, ["columns"] = columns });
                for (int s = 0; s < sets.Count; s++) newSets.Add(sets[s]);
                newPaths[name] = path;
            }

            var payload = new JObject
            {
                ["@type"] = "OperationManager",
                ["operationSets"] = newSets,
                ["decks"] = newDecks,
            };

            _applyingDeckFiles = true;
            try
            {
                LiveObjectSnapshot.Restore(payload.ToString(Formatting.None), handle.Value);
            }
            finally
            {
                _applyingDeckFiles = false;
            }

            _deckFilePaths.Clear();
            foreach (var pair in newPaths) _deckFilePaths[pair.Key] = pair.Value;

            // What is on disk is what was just applied, so nothing counts as an unwritten edit. Taking the
            // baseline from the model (rather than from the file text) makes it exact: a file written by
            // another build may differ in formatting without differing in content.
            _RebaseDeckFiles();

            _Broadcast();
            _BroadcastDecks();
        }

        // Reads one deck file. Returns false (already logged) when it cannot be used.
        private static bool _TryReadDeckFile(string fullPath, string deckName, out int columns, out JArray sets)
        {
            columns = 0;
            sets = null;

            string json;
            try { json = File.ReadAllText(fullPath); }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to read the deck file '{fullPath}': {e.Message}");
                return false;
            }

            if (!DeckFile.TryParse(json, fullPath, out var fileColumns, out var setsJson)) return false;

            columns = fileColumns > 0 ? fileColumns : 8;
            try { sets = JArray.Parse(setsJson); }
            catch { sets = new JArray(); }
            // The file decides which deck its sets are on; the stored deckName is only a leftover of where
            // they were when written (and is wrong outright for a file copied in from another project).
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] is JObject set && set["control"] is JObject control) control["deckName"] = deckName;
            }
            return true;
        }

        // The serialized sets placed on one deck, in order.
        private static JArray _SetsOnDeck(JArray sets, string deckName)
        {
            var result = new JArray();
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] is JObject set &&
                    set["control"] is JObject control &&
                    string.Equals(control["deckName"]?.Value<string>(), deckName, StringComparison.Ordinal))
                {
                    result.Add(set);
                }
            }
            return result;
        }

        /// <summary>
        /// Writes every deck whose contents differ from what its file holds. This is the whole save story:
        /// there is no save button and no unsaved state, because a deck is a file and every edit lands in it.
        ///
        /// Called from the functions that author decks and from the property-changed hook, so an edit made
        /// through the generic property REST (renaming a set, editing an operation) is covered too.
        /// </summary>
        private void _FlushDirtyDecks()
        {
            // A manager that was never enabled is not part of a running app — a unit test's bare instance,
            // or a scene template being deserialized. It has no business writing into the user's project.
            if (!_deckFilesActive) return;

            var payloads = _BuildDeckPayloads();
            if (payloads == null) return;

            bool created = false;
            foreach (var pair in payloads)
            {
                if (_writtenDeckFiles.TryGetValue(pair.Key, out var written) &&
                    string.Equals(written, pair.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                var path = _EnsureDeckFilePath(pair.Key);
                if (string.IsNullOrEmpty(path)) continue;

                bool isNew = !File.Exists(path);
                if (!_WriteDeckFile(path, pair.Value)) continue;
                _writtenDeckFiles[pair.Key] = pair.Value;
                created |= isNew;
            }

            // A file that did not exist before has to reach the project catalog, or the next crawl would
            // see a deck with no asset entry behind it and drop the tab.
            if (created) ProjectManager.RecrawlProject();
        }

        // The file text each deck would be written as, keyed by deck name.
        private Dictionary<string, string> _BuildDeckPayloads()
        {
            var handle = liveObject;
            if (!handle.HasValue) return null;

            var current = JObject.Parse(LiveObjectSnapshot.Capture(handle.Value, PersistScope.Custom));
            var sets = current["operationSets"] as JArray ?? new JArray();

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < decks.Count; i++)
            {
                var deck = decks[i];
                if (deck == null || string.IsNullOrEmpty(deck.name)) continue;
                result[deck.name] = DeckFile.BuildJson(
                    deck.columns, _SetsOnDeck(sets, deck.name).ToString(Formatting.None));
            }
            return result;
        }

        // Adopts the current model as "what the files hold", without writing anything.
        private void _RebaseDeckFiles()
        {
            _writtenDeckFiles.Clear();
            var payloads = _BuildDeckPayloads();
            if (payloads == null) return;
            foreach (var pair in payloads) _writtenDeckFiles[pair.Key] = pair.Value;
        }

        // The file a deck is, creating the mapping for a deck that does not have one yet (added from the
        // remote app). Null when no project is open to write into.
        private string _EnsureDeckFilePath(string deckName)
        {
            if (_deckFilePaths.TryGetValue(deckName, out var known) && !string.IsNullOrEmpty(known)) return known;

            var projectPath = ProjectManager.projectPath;
            if (string.IsNullOrEmpty(projectPath))
            {
                Debug.LogError("[LiveStudio] No project folder is open to write the deck into.");
                return null;
            }

            var path = Path.Combine(projectPath, DeckFile.Subfolder, deckName + DeckFile.Extension);
            _deckFilePaths[deckName] = path;
            return path;
        }

        private static bool _WriteDeckFile(string fullPath, string json)
        {
            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to write the deck file '{fullPath}': {e.Message}");
                return false;
            }
        }

        // Deletes a deck's file and forgets it. The deck itself is removed by the caller.
        private void _DeleteDeckFile(string deckName)
        {
            if (!_deckFilesActive) return;
            if (_deckFilePaths.TryGetValue(deckName, out var path) && !string.IsNullOrEmpty(path))
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LiveStudio] Failed to delete the deck file '{path}': {e.Message}");
                }
            }
            _deckFilePaths.Remove(deckName);
            _writtenDeckFiles.Remove(deckName);
        }

        // Renames a deck's file to follow the deck's new name. No-op when the deck has no file yet
        // (renamed before the first write); the file is then created under the new name.
        private void _MoveDeckFile(string fromName, string toName)
        {
            if (!_deckFilesActive) return;
            if (!_deckFilePaths.TryGetValue(fromName, out var fromPath) || string.IsNullOrEmpty(fromPath))
            {
                return;
            }

            _deckFilePaths.Remove(fromName);
            _writtenDeckFiles.TryGetValue(fromName, out var written);
            _writtenDeckFiles.Remove(fromName);

            var dir = Path.GetDirectoryName(fromPath);
            var toPath = string.IsNullOrEmpty(dir)
                ? toName + DeckFile.Extension
                : Path.Combine(dir, toName + DeckFile.Extension);

            try
            {
                if (File.Exists(fromPath)) File.Move(fromPath, toPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveStudio] Failed to rename the deck file '{fromPath}': {e.Message}");
                return;
            }

            _deckFilePaths[toName] = toPath;
            if (written != null) _writtenDeckFiles[toName] = written;
        }

        private void _OnCatalogChanged() => SyncDeckFiles();

        // Operation 要素の追加/削除/型選択は、RemoteApp の汎用配列「+」(elementTypeOptions による
        // 型選択メニュー) と汎用削除でまかなう。専用の Add/RemoveOperation 関数は持たない。

        // Rebuilds enabled-set property edits that change input wiring (input type / binding) into a
        // fresh input map, and writes the deck the edit landed in.
        //
        // This is the autosave path for everything the remote app edits through the generic property REST
        // (a set's name / group / enabled, an operation's target, a slider's range) — the dedicated
        // functions call the flush themselves.
        private void _OnPropertyChanged(LiveProperty property, object oldValue)
        {
            if (!_initialized) return;

            if (property.PathContains("input")) _RebuildInputMap();

            // The hold and the manual value are what the operator does during a show, not what they
            // authored; writing a file per button press would put disk IO in the middle of the performance.
            // They still ride along into the file the next time something is actually edited.
            if (property.PathContains("held") || property.PathContains("_manualValue")) return;

            if (property.PathContains("operationSets") || property.PathContains("decks")) _FlushDirtyDecks();
        }

        // Only AddOperationSet assigns an id; sets authored directly in a scene / prop bundle load with the
        // default empty id. An empty id makes every id-addressed function (RemoveOperationSet /
        // ToggleOperationSet / SetOperationSetHeld / ...) a silent no-op via _IndexOf, and _RebuildInputMap
        // skips the set entirely. Assign a stable id to any set missing one so it is fully addressable.
        private void _EnsureOperationSetIds()
        {
            for (int i = 0; i < operationSets.Count; i++)
            {
                var set = operationSets[i];
                if (set != null && string.IsNullOrEmpty(set.id))
                {
                    set.id = System.Guid.NewGuid().ToString();
                }
            }
        }

        // Tears down the previous map and binds every current input into a fresh one. Building a new map
        // discards stale operations in one step; inputs re-create their operations in Setup.
        private void _RebuildInputMap()
        {
            _TeardownInputMap();

            _map = new InputActionMap("Operations");
            for (int i = 0; i < operationSets.Count; i++)
            {
                var set = operationSets[i];
                if (set?.input == null || string.IsNullOrEmpty(set.id)) continue;
                set.input.Setup(_map, _OperationName(set.id));
            }
            _map.Enable();
        }

        private void _TeardownInputMap()
        {
            if (_map == null) return;
            _map.Disable();
            _map.Dispose();
            _map = null;
        }

        private static string _OperationName(string operationSetId) => "OperationSet." + operationSetId;

        /// <summary>
        /// Enforces toggle-style exclusivity within a named group: clears every other set sharing the
        /// winner's non-empty group so only the winner stays on. Does nothing unless the winner is a grouped
        /// <see cref="InputMode.Toggle"/> set, so ungrouped, button (momentary), and value sets never knock
        /// others off. Clearing a groupmate drops both its manual hold and its latched keyboard toggle, and
        /// is idempotent on sets that are already off. Static and list-based so it is unit-testable without
        /// play mode.
        /// </summary>
        internal static void ApplyExclusiveGroup(List<OperationSet> sets, int winnerIndex)
        {
            if (sets == null || winnerIndex < 0 || winnerIndex >= sets.Count) return;

            var winner = sets[winnerIndex];
            if (!_IsExclusiveSet(winner)) return;

            string group = winner.group;
            for (int i = 0; i < sets.Count; i++)
            {
                if (i == winnerIndex) continue;
                var other = sets[i];
                if (other == null || other.group != group) continue;

                other.SetHeld(false);
                other.input?.SetToggleState(false);
            }
        }

        // A set participates in group exclusivity only when it has a non-empty group and a Toggle-mode
        // control: the radio behavior is meaningful only for latched (on/off) sets, not momentary buttons.
        private static bool _IsExclusiveSet(OperationSet set)
            => set != null
               && !string.IsNullOrEmpty(set.group)
               && set.control != null
               && set.control.mode == InputMode.Toggle;

        private int _IndexOf(string operationSetId)
        {
            if (string.IsNullOrEmpty(operationSetId)) return -1;
            for (int i = 0; i < operationSets.Count; i++)
            {
                if (operationSets[i] != null && operationSets[i].id == operationSetId) return i;
            }
            return -1;
        }

        private void _Broadcast() => LivePropertyBroadcast.BroadcastProperty(this, "operationSets");

        private void _BroadcastDecks() => LivePropertyBroadcast.BroadcastProperty(this, "decks");
    }
}
