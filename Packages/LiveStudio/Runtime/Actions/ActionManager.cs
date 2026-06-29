// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting.APIUpdating;

using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// The general "fire and act" base feature of LiveStudio: a list of <see cref="ActionSet"/>s the user
    /// authors from the remote app, each binding one input <see cref="InputSource"/> to an ordered set of
    /// <see cref="ActionBase"/>s. Each frame every enabled set evaluates its input and runs its actions in
    /// order.
    ///
    /// A plain serializable <see cref="IExposedObject"/> (like <see cref="StageManager"/> /
    /// <see cref="ExternalAssetManager"/>), stored in the scene's <c>RemoteControlBehaviour._objects</c>
    /// through its <c>[SerializeReference]</c> list, so the authored sets persist in the scene.
    /// </summary>
    [Serializable]
    [ExposedClass(Icon = "bolt", Category = "Action", HideInScene = true)]
    [MovedFrom(false, null, null, "TriggerManager")]
    public class ActionManager : IExposedObject, IExposedDeserializeCallback
    {
        const string kId = "c4e8b2d6-7a91-4f53-8e0c-1d9a6b3f2e74";

        // The active manager, so actions / sources can reach it. Set in OnEnable, cleared in OnDisable.
        // Reset on subsystem registration for safety when Domain Reload is disabled.
        [NonSerialized]
        private static ActionManager _current;

        public static ActionManager current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _InitializeCurrent() => _current = null;

        public string name { get; set; } = "Action Manager";

        public ExposedObjectHandle? exposedObject => ExposedObjectRegistry.FindByTarget(this);

        public string id => kId;

        /// <summary>The authored action sets. Polymorphic input/actions serialize via SerializeReference.</summary>
        [SerializeReference, Select]
        [ExposedField]
        public List<ActionSet> actionSets = new List<ActionSet>();

        /// <summary>The control panels: named grids of <see cref="PanelControl"/> tiles the remote app lays
        /// out, each operating an <see cref="ActionSet"/> by id. Persisted with the manager in the scene.
        /// Tiles are added/removed/moved through the generic array REST; only whole panels need add/remove
        /// functions here.</summary>
        [ExposedField]
        public List<Panel> panels = new List<Panel>();

        // The shared input map all KeyInputSources create their actions in. Rebuilt when the set of
        // inputs changes or an input's binding/type changes. Runtime-only.
        [NonSerialized]
        private InputActionMap _map;

        [NonSerialized]
        private bool _initialized;

        // The control path captured by the most recent StartKeyCapture (empty while waiting / on cancel /
        // timeout). The add-action dialog polls `capturedBinding` to show the key before committing.
        [NonSerialized]
        private string _capturedBinding = string.Empty;

        public void OnEnable()
        {
            _current = this;

            ExposedObjectRegistry.Create<ActionManager>(this, kId);
            ExposedClass.Get<ActionManager>().onPropertyChanged += _OnPropertyChanged;

            _RebuildInputMap();

            _initialized = true;
        }

        public void OnDisable()
        {
            _initialized = false;

            ExposedClass.Get<ActionManager>().onPropertyChanged -= _OnPropertyChanged;

            _TeardownInputMap();

            ExposedObjectRegistry.FindByTarget(this)?.Unregister();

            if (_current == this) _current = null;
        }

        public void OnDispose() => OnDisable();

        public void Update()
        {
            if (!_initialized) return;
            if (!Application.isPlaying) return;

            for (int i = 0; i < actionSets.Count; i++)
            {
                var set = actionSets[i];
                if (set == null) continue;

                // Snapshot the previous frame's hold before overwriting it, so the helper can detect the
                // rising/falling edge of the manual hold.
                bool wasHeld = set.heldPrev;
                set.heldPrev = set.held;

                if (!TryGetFiringContext(set, wasHeld, out var context))
                {
                    set.lastValue = 0f;
                    continue;
                }

                // Record this frame's firing output so the remote app can poll it (actionSetValues).
                set.lastValue = context.value;

                // A set that just latched on (rising active edge) clears its groupmates so only one set in a
                // named group stays on. No-op for ungrouped or non-toggle winners (see ApplyExclusiveGroup),
                // so this is cheap for the common case.
                if (context.pressed && context.active)
                {
                    ApplyExclusiveGroup(actionSets, i);
                }

                var actions = set.actions;
                if (actions == null) continue;
                for (int j = 0; j < actions.Count; j++)
                {
                    actions[j]?.Apply(in context);
                }
            }
        }

        /// <summary>
        /// Computes a set's firing context for this frame given the previous frame's hold state. Returns
        /// false (skip, value 0) when the set is disabled or has no input and is not held. Pure aside from
        /// advancing the input source's edge state, so it is unit-testable without play mode.
        /// </summary>
        /// <param name="set">The action set to evaluate. Must not be null.</param>
        /// <param name="wasHeld">The set's <see cref="ActionSet.held"/> on the previous frame.</param>
        internal static bool TryGetFiringContext(ActionSet set, bool wasHeld, out ActionContext context)
        {
            // A button-mode set commits its one-shot trigger on release, so a held press must not trigger
            // yet; other modes commit on the press edge. Mirrors InputSource.Evaluate for the manual hold.
            bool isButton = set.input != null && set.input.mode == InputMode.Button;

            if (!float.IsNaN(set.manualValue))
            {
                // Manual value from the remote app's Value-mode slider. Takes precedence over the bound
                // input and works even when disabled (the user dragged it explicitly), like the manual hold.
                // Edges stay false: SetPropertyAction's float path reads only context.value, and exclusivity
                // needs context.pressed — so a Value set never disturbs group radios. 0.5 mirrors
                // InputSource.kThreshold (protected there, so inlined) for bool-property targets.
                float manual = Mathf.Clamp01(set.manualValue);
                context = new ActionContext(manual, pressed: false, released: false,
                    active: manual >= 0.5f, triggered: false);
                return true;
            }

            if (set.held)
            {
                // Manual hold: fire as if the input were held active. Overrides the bound input and works
                // even when the set is disabled, since the user triggered it explicitly. Rising edge on the
                // first held frame; button defers its trigger to the release frame.
                bool rising = !wasHeld;
                context = new ActionContext(1f, pressed: rising, released: false, active: true,
                    triggered: isButton ? false : rising);
                return true;
            }

            if (wasHeld)
            {
                // Hold released this frame: a single falling edge, then back to normal next frame. Button
                // commits its one-shot trigger here.
                context = new ActionContext(0f, pressed: false, released: true, active: false,
                    triggered: isButton);
                return true;
            }

            if (!set.enabled || set.input == null)
            {
                context = default;
                return false;
            }

            context = set.input.Evaluate();
            return true;
        }

        /// <summary>Per-set firing output (0..1) in <see cref="actionSets"/> order, for the remote app to
        /// poll and light its cards while an input (or the manual hold) is firing. Read-only and hidden;
        /// reflects the value recorded by the most recent <see cref="Update"/>. Polled rather than pushed
        /// over SSE so it costs nothing while the Actions page is not open.</summary>
        [ExposedProperty, Hide]
        public float[] actionSetValues
        {
            get
            {
                var values = new float[actionSets.Count];
                for (int i = 0; i < actionSets.Count; i++)
                {
                    values[i] = actionSets[i]?.lastValue ?? 0f;
                }
                return values;
            }
        }

        public void Reset() { }

        public void OnAfterExposedDeserialize()
        {
            // A live-scene restore replaces the action sets list; rebuild the input map so the restored
            // inputs are bound. Idempotent, so harmless if it also fires on an unrelated property write.
            _RebuildInputMap();
            // No unplaced state: a restored (or older) scene may carry controls with no panel; place them on
            // the default page so they stay reachable now that the unplaced tray is gone. Idempotent (no-op
            // once everything is placed).
            _PlaceUnplacedControls();
            // PanelSlider tiles are fixed at 2 cells wide; enforce on restored/older scenes. Idempotent.
            _EnforceControlWidths();
        }

        /// <summary>Adds a new action set (default key input, no actions) and rebuilds the input map.</summary>
        [ExposedFunction]
        public void AddActionSet() => AddActionSet(new KeyInputSource());

        /// <summary>Adds a pre-built action set from the given input and actions, rebuilds the input map and
        /// returns the created set. Generic and feature-agnostic: callers (e.g. the expression binding bridge)
        /// supply whatever concrete <see cref="InputSource"/> / <see cref="ActionBase"/>s they need without
        /// this manager knowing about them.</summary>
        public ActionSet AddActionSet(InputSource input, params ActionBase[] actions)
            => _AddActionSet(input, null, actions);

        // Shared creation. A null/empty name keeps the generic default; the bind-a-control flows pass the
        // target's function/property name so the new set reads meaningfully in the remote app's list.
        private ActionSet _AddActionSet(InputSource input, string name, ActionBase[] actions, string group = null, PanelControl control = null)
        {
            var set = new ActionSet
            {
                id = Guid.NewGuid().ToString(),
                name = string.IsNullOrEmpty(name) ? "Action Set" : name,
                enabled = true,
                group = string.IsNullOrEmpty(group) ? string.Empty : group,
                input = input ?? new KeyInputSource(),
                actions = actions != null ? new List<ActionBase>(actions) : new List<ActionBase>(),
                control = control ?? new PanelPush(),
            };
            // PanelSlider tiles are fixed at 2 cells wide; size before placing so the free cell fits.
            _ApplyControlWidth(set.control);
            // No unplaced state: every new control is placed on the default page at a free cell.
            _PlaceOnDefaultPanel(set.control);
            actionSets.Add(set);
            _RebuildInputMap();
            _Broadcast();
            return set;
        }

        // The default panel control kind for a new set, derived from its input mode (changeable later from
        // the remote app via SetControlType): Toggle→checkbox, Value→slider, otherwise momentary push.
        private static PanelControl _DefaultControlForMode(InputMode mode)
        {
            if (mode == InputMode.Toggle) return new PanelCheckbox();
            if (mode == InputMode.Value) return new PanelSlider();
            return new PanelPush();
        }

        // Creates a fresh concrete control of the named kind, or null for an unknown name.
        private static PanelControl _CreateControl(string typeName)
        {
            if (typeName == "PanelCheckbox") return new PanelCheckbox();
            if (typeName == "PanelSlider") return new PanelSlider();
            if (typeName == "PanelPush") return new PanelPush();
            return null;
        }

        // Builds a key input already bound to the given control path (empty = unbound). The add-action dialog
        // captures the key first (see StartKeyCapture) and commits it here, so the set is born bound.
        private static KeyInputSource _KeyInput(InputMode mode, string binding)
        {
            var input = new KeyInputSource { mode = mode };
            if (!string.IsNullOrEmpty(binding)) input.SetInitialBinding(binding);
            return input;
        }

        /// <summary>Creates an action set that invokes a no-argument <c>[ExposedFunction]</c> (named
        /// <paramref name="functionName"/>) on the object with id <paramref name="targetId"/>, bound to the
        /// momentary key <paramref name="binding"/> (a control path; empty = unbound). The set is named
        /// <paramref name="name"/> (the function's display name; falls back to the generic default when empty).
        /// Returns the new set's id. Drives the "bind this function button to a key" flow.</summary>
        [ExposedFunction]
        public string AddFunctionAction(string targetId, string functionName, string name, string binding)
        {
            var set = _AddActionSet(
                _KeyInput(InputMode.Button, binding),
                name,
                new ActionBase[] { new InvokeFunctionAction { targetId = targetId, functionName = functionName } });
            return set.id;
        }

        /// <summary>Creates an action set that drives the property <paramref name="propertyPath"/> on the object
        /// with id <paramref name="targetId"/>, bound to the key <paramref name="binding"/> (a control path;
        /// empty = unbound) in the given <paramref name="mode"/> (<see cref="InputMode"/> name, case-insensitive;
        /// unrecognized/empty falls back to <see cref="InputMode.Toggle"/>). Toggle latches on/off per press;
        /// Button is momentary (on while held). The set is named <paramref name="name"/> (the property's display
        /// name; falls back to the generic default when empty). The optional <paramref name="group"/> assigns an
        /// exclusivity group (empty = ungrouped); Toggle-mode sets sharing a non-empty group act as a radio.
        /// Returns the new set's id. Drives the "bind this control to a key" flow.</summary>
        [ExposedFunction]
        public string AddPropertyAction(string targetId, string propertyPath, string mode, string name, string binding, string group)
        {
            var inputMode = System.Enum.TryParse<InputMode>(mode, ignoreCase: true, out var m)
                ? m
                : InputMode.Toggle;
            var set = _AddActionSet(
                _KeyInput(inputMode, binding),
                name,
                new ActionBase[] { new SetPropertyAction { targetId = targetId, propertyPath = propertyPath } },
                group,
                _DefaultControlForMode(inputMode));
            return set.id;
        }

        /// <summary>The control path captured by the most recent <see cref="StartKeyCapture"/> (empty while
        /// waiting, or if it was cancelled / timed out). The remote app's add-action dialog polls this to show
        /// the captured key before committing the new set. Runtime-only.</summary>
        [ExposedProperty, Hide]
        public string capturedBinding => _capturedBinding;

        /// <summary>Listens for the next key/button on the Studio machine and stores it in
        /// <see cref="capturedBinding"/>, creating no action set. Lets the add-action dialog capture an input
        /// before the user commits (Add), so the set can be born already bound. Hidden so the generic UI does
        /// not surface it as a bare button (it needs the dialog's key-capture feedback).</summary>
        [ExposedFunction, Hide]
        public void StartKeyCapture() => _StartKeyCaptureAsync();

        // Captures into a throwaway map/action so no action set is created and the shared input map is left
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

        /// <summary>Removes the action set with the given id and rebuilds the input map.</summary>
        [ExposedFunction]
        public void RemoveActionSet(string actionSetId)
        {
            int index = _IndexOf(actionSetId);
            if (index < 0) return;
            actionSets.RemoveAt(index);
            _RebuildInputMap();
            _Broadcast();
        }

        /// <summary>Adds a new control panel with a unique auto-generated name and returns that name. The
        /// remote app then adds tiles by placing controls' <see cref="PanelControl.panelName"/> onto it.</summary>
        [ExposedFunction]
        public string AddPanel()
        {
            var panel = new Panel { name = _UniquePanelName("Panel", null) };
            panels.Add(panel);
            _BroadcastPanels();
            return panel.name;
        }

        /// <summary>Removes the panel with the given name and moves any controls that were on it to the default
        /// page (no unplaced state; a fresh default page is created if this was the last panel). No-op if the
        /// name is unknown.</summary>
        [ExposedFunction]
        public void RemovePanel(string panelName)
        {
            if (string.IsNullOrEmpty(panelName)) return;
            int index = panels.FindIndex(p => p != null && p.name == panelName);
            if (index < 0) return;
            panels.RemoveAt(index);

            bool anyMoved = false;
            for (int i = 0; i < actionSets.Count; i++)
            {
                var control = actionSets[i]?.control;
                if (control != null && control.panelName == panelName)
                {
                    _PlaceOnDefaultPanel(control);
                    anyMoved = true;
                }
            }

            _BroadcastPanels();
            if (anyMoved) _Broadcast();
        }

        /// <summary>Renames the panel currently named <paramref name="panelName"/> to <paramref name="newName"/>,
        /// auto-suffixing on collision so panel names stay unique, and updates every control placed on it so the
        /// reference follows the rename. Returns the resulting (possibly suffixed) name; returns the original
        /// name unchanged for an unknown current name, an empty new name, or a no-op rename.</summary>
        [ExposedFunction]
        public string RenamePanel(string panelName, string newName)
        {
            if (string.IsNullOrEmpty(panelName) || string.IsNullOrEmpty(newName)) return panelName;
            int index = panels.FindIndex(p => p != null && p.name == panelName);
            if (index < 0) return panelName;

            string unique = _UniquePanelName(newName, panels[index]);
            if (unique == panelName) return panelName; // no effective change
            panels[index].name = unique;

            // Controls reference panels by name; follow the rename so their tiles stay on this page.
            bool anyMoved = false;
            for (int i = 0; i < actionSets.Count; i++)
            {
                var control = actionSets[i]?.control;
                if (control != null && control.panelName == panelName)
                {
                    control.panelName = unique;
                    anyMoved = true;
                }
            }

            _BroadcastPanels();
            if (anyMoved) _Broadcast();
            return unique;
        }

        /// <summary>Places (or moves) the control of the action set with the given id onto the panel named
        /// <paramref name="panelName"/> at grid cell (<paramref name="x"/>, <paramref name="y"/>). An empty
        /// <paramref name="panelName"/> falls back to the default page at a free cell (no unplaced state).
        /// No-op for an unknown id.</summary>
        [ExposedFunction]
        public void PlaceControl(string actionSetId, string panelName, int x, int y)
        {
            int index = _IndexOf(actionSetId);
            if (index < 0) return;
            var control = actionSets[index]?.control;
            if (control == null) return;
            if (string.IsNullOrEmpty(panelName))
            {
                _PlaceOnDefaultPanel(control);
            }
            else
            {
                control.panelName = panelName;
                // Keep the tile on-grid for its (possibly 2-wide) span.
                int columns = _PanelColumns(panelName);
                control.x = Mathf.Clamp(x, 0, Mathf.Max(0, columns - Mathf.Max(1, control.w)));
                control.y = Mathf.Max(0, y);
            }
            _Broadcast();
        }

        /// <summary>Swaps the control kind (<c>PanelPush</c> / <c>PanelCheckbox</c> / <c>PanelSlider</c>) of
        /// the action set with the given id, preserving its placement (panel and grid cell/span). No-op for
        /// an unknown id or type name.</summary>
        [ExposedFunction]
        public void SetControlType(string actionSetId, string typeName)
        {
            int index = _IndexOf(actionSetId);
            if (index < 0) return;
            var set = actionSets[index];
            if (set == null) return;

            var next = _CreateControl(typeName);
            if (next == null) return;

            var old = set.control;
            if (old != null)
            {
                next.panelName = old.panelName;
                next.x = old.x;
                next.y = old.y;
                next.w = old.w;
                next.h = old.h;
            }
            set.control = next;
            // PanelSlider tiles are fixed at 2 cells wide; enforce after the kind swap (keeps it on-grid).
            _ApplyControlWidth(next);
            _Broadcast();
        }

        // Returns a panel name unique among all panels except <paramref name="self"/>, auto-suffixing " 2",
        // " 3", … on collision so a name stays usable as the placement key.
        private string _UniquePanelName(string desired, Panel self)
        {
            string baseName = string.IsNullOrEmpty(desired) ? "Panel" : desired;
            string candidate = baseName;
            int n = 2;
            while (_NameTaken(candidate, self))
            {
                candidate = baseName + " " + n;
                n++;
            }
            return candidate;
        }

        private bool _NameTaken(string name, Panel self)
        {
            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (p != null && p != self && p.name == name) return true;
            }
            return false;
        }

        // No unplaced state: ensure a default page exists and return its name. The first panel is the default;
        // a fresh one (with a unique name) is created when the list is empty, so a new control always has a home.
        private string _EnsureDefaultPanelName()
        {
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i] != null && !string.IsNullOrEmpty(panels[i].name))
                    return panels[i].name;
            }
            var panel = new Panel { name = _UniquePanelName("Panel", null) };
            panels.Add(panel);
            _BroadcastPanels();
            return panel.name;
        }

        // Places the control on the default page at the first free grid cell (row-major scan).
        private void _PlaceOnDefaultPanel(PanelControl control)
        {
            if (control == null) return;
            string panelName = _EnsureDefaultPanelName();
            _FindFreeCell(panelName, control, out int x, out int y);
            control.panelName = panelName;
            control.x = x;
            control.y = y;
        }

        // Finds the first grid cell on the panel where the control's span fits without overlapping another
        // tile, scanning row by row. The lowest fully-empty row is always free, so the scan is bounded there.
        private void _FindFreeCell(string panelName, PanelControl placing, out int x, out int y)
        {
            int columns = _PanelColumns(panelName);

            int w = Mathf.Clamp(placing != null ? placing.w : 1, 1, columns);
            int h = Mathf.Max(1, placing != null ? placing.h : 1);

            // The lowest fully-empty row is always free, so bound the scan there.
            int maxRow = 0;
            for (int i = 0; i < actionSets.Count; i++)
            {
                var c = actionSets[i]?.control;
                if (c != null && c != placing && c.panelName == panelName)
                    maxRow = Mathf.Max(maxRow, c.y + Mathf.Max(1, c.h));
            }

            for (int row = 0; row <= maxRow; row++)
            {
                for (int col = 0; col + w <= columns; col++)
                {
                    if (_IsAreaFree(panelName, placing, col, row, w, h)) { x = col; y = row; return; }
                }
            }
            x = 0;
            y = 0;
        }

        // True when no other control on the panel overlaps the given grid rectangle.
        private bool _IsAreaFree(string panelName, PanelControl placing, int x, int y, int w, int h)
        {
            for (int i = 0; i < actionSets.Count; i++)
            {
                var c = actionSets[i]?.control;
                if (c == null || c == placing || c.panelName != panelName) continue;
                int cw = Mathf.Max(1, c.w);
                int ch = Mathf.Max(1, c.h);
                if (x < c.x + cw && c.x < x + w && y < c.y + ch && c.y < y + h) return false;
            }
            return true;
        }

        // The logical column count of the panel with the given name (default 8 for an unknown name).
        private int _PanelColumns(string panelName)
        {
            var panel = panels.Find(p => p != null && p.name == panelName);
            return panel != null && panel.columns > 0 ? panel.columns : 8;
        }

        // PanelSlider tiles are fixed at 2 cells wide; every other kind is 1 wide. Re-clamps x so the tile
        // stays within the panel's columns after the width changes.
        private void _ApplyControlWidth(PanelControl control)
        {
            if (control == null) return;
            control.w = control is PanelSlider ? 2 : 1;
            int columns = _PanelColumns(control.panelName);
            if (control.x + control.w > columns) control.x = Mathf.Max(0, columns - control.w);
        }

        // Enforces the fixed per-kind tile width across all controls (slider = 2, others = 1). Idempotent.
        private void _EnforceControlWidths()
        {
            bool any = false;
            for (int i = 0; i < actionSets.Count; i++)
            {
                var c = actionSets[i]?.control;
                if (c == null) continue;
                int desired = c is PanelSlider ? 2 : 1;
                if (c.w != desired)
                {
                    _ApplyControlWidth(c);
                    any = true;
                }
            }
            if (any) _Broadcast();
        }

        // No unplaced state: place any control with no panel — or one whose name no panel has (e.g. an older
        // scene that still carried a GUID) — onto the default page.
        private void _PlaceUnplacedControls()
        {
            bool anyPlaced = false;
            for (int i = 0; i < actionSets.Count; i++)
            {
                var control = actionSets[i]?.control;
                if (control == null) continue;
                bool placed = !string.IsNullOrEmpty(control.panelName)
                    && panels.Exists(p => p != null && p.name == control.panelName);
                if (!placed)
                {
                    _PlaceOnDefaultPanel(control);
                    anyPlaced = true;
                }
            }
            if (anyPlaced) _Broadcast();
        }

        /// <summary>Toggles the manual hold of the action set with the given id. While held the set fires
        /// from <see cref="Update"/> as if its input were held active (independent of
        /// <see cref="ActionSet.enabled"/>); toggling off releases it. Lets the remote app fire a set from
        /// its card without a bound input. The broadcast lets the card reflect the new hold state.</summary>
        [ExposedFunction]
        public void ToggleActionSet(string actionSetId)
        {
            int index = _IndexOf(actionSetId);
            if (index < 0) return;

            var set = actionSets[index];
            if (set == null) return;

            set.SetHeld(!set.held);
            // Turning a grouped toggle on clears its groupmates (toggle-style radio); no-op otherwise.
            if (set.held) ApplyExclusiveGroup(actionSets, index);
            _Broadcast();
        }

        /// <summary>Sets the manual hold of the action set with the given id to an explicit state. Used by the
        /// remote app's momentary (Button-mode) card, which holds on press and releases on pointer-up: an
        /// idempotent set avoids the desync a flip (<see cref="ToggleActionSet"/>) would suffer if a press
        /// or release event were missed. No-op (and no broadcast) when already in the requested state.</summary>
        [ExposedFunction]
        public void SetActionSetHeld(string actionSetId, bool held)
        {
            int index = _IndexOf(actionSetId);
            if (index < 0) return;

            var set = actionSets[index];
            if (set == null || set.held == held) return;

            set.SetHeld(held);
            if (held) ApplyExclusiveGroup(actionSets, index);
            _Broadcast();
        }

        /// <summary>Sets the manual value override (0..1, clamped) of the action set with the given id, from
        /// the remote app's Value-mode slider card. While set the set fires from <see cref="Update"/> with
        /// this value (sticky, overriding the bound input), independent of <see cref="ActionSet.enabled"/>.
        /// No broadcast: <see cref="ActionSet"/>'s manual value is <c>[NonSerialized]</c> (absent from the
        /// actionSets payload), and the slider reads its value back through the polled
        /// <see cref="actionSetValues"/>; broadcasting per drag frame would be pure overhead.</summary>
        [ExposedFunction]
        public void SetActionSetValue(string actionSetId, float value)
        {
            int index = _IndexOf(actionSetId);
            if (index < 0) return;

            var set = actionSets[index];
            if (set == null) return;

            set.SetManualValue(Mathf.Clamp01(value));
        }

        // Action 要素の追加/削除/型選択は、RemoteApp の汎用配列「+」(elementTypeOptions による
        // 型選択メニュー) と汎用削除でまかなう。専用の Add/RemoveAction 関数は持たない。

        // Rebuilds enabled-set property edits that change input wiring (input type / binding) into a
        // fresh input map. Action edits do not affect input, so they are ignored here.
        private void _OnPropertyChanged(ExposedProperty property, object oldValue)
        {
            if (!_initialized) return;
            if (!property.PathContains("input")) return;
            _RebuildInputMap();
        }

        // Tears down the previous map and binds every current input into a fresh one. Building a new map
        // discards stale actions in one step; inputs re-create their actions in Setup.
        private void _RebuildInputMap()
        {
            _TeardownInputMap();

            _map = new InputActionMap("Actions");
            for (int i = 0; i < actionSets.Count; i++)
            {
                var set = actionSets[i];
                if (set?.input == null || string.IsNullOrEmpty(set.id)) continue;
                set.input.Setup(_map, _ActionName(set.id));
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

        private static string _ActionName(string actionSetId) => "ActionSet." + actionSetId;

        /// <summary>
        /// Enforces toggle-style exclusivity within a named group: clears every other set sharing the
        /// winner's non-empty group so only the winner stays on. Does nothing unless the winner is a grouped
        /// <see cref="InputMode.Toggle"/> set, so ungrouped, button (momentary), and value sets never knock
        /// others off. Clearing a groupmate drops both its manual hold and its latched keyboard toggle, and
        /// is idempotent on sets that are already off. Static and list-based so it is unit-testable without
        /// play mode.
        /// </summary>
        internal static void ApplyExclusiveGroup(List<ActionSet> sets, int winnerIndex)
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
        // input: the radio behavior is meaningful only for latched (on/off) sets, not momentary buttons.
        private static bool _IsExclusiveSet(ActionSet set)
            => set != null
               && !string.IsNullOrEmpty(set.group)
               && set.input != null
               && set.input.mode == InputMode.Toggle;

        private int _IndexOf(string actionSetId)
        {
            if (string.IsNullOrEmpty(actionSetId)) return -1;
            for (int i = 0; i < actionSets.Count; i++)
            {
                if (actionSets[i] != null && actionSets[i].id == actionSetId) return i;
            }
            return -1;
        }

        private void _Broadcast() => ExposedPropertyBroadcast.BroadcastProperty(this, "actionSets");

        private void _BroadcastPanels() => ExposedPropertyBroadcast.BroadcastProperty(this, "panels");
    }
}
