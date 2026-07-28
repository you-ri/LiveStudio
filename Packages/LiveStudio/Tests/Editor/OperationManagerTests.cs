// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using NUnit.Framework;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Tests the firing logic <see cref="OperationManager.Update"/> drives each frame, exercised through the
    /// pure <see cref="OperationManager.TryGetFiringContext"/> helper (so no play mode is needed), plus the
    /// <see cref="OperationManager.operationSetValues"/> poll surface the remote app reads. Reuses
    /// <see cref="FakeInputSource"/> from InputSourceTests to drive input without a real device.
    /// </summary>
    public class OperationManagerTests
    {
        [Test]
        public void Held_RisingEdge_FiresFullPress()
        {
            var set = new OperationSet { enabled = true, input = new FakeInputSource() };
            set.SetHeld(true);

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(1f, context.value, 1e-4f);
            Assert.IsTrue(context.pressed, "rising edge on the first held frame");
            Assert.IsFalse(context.released);
            Assert.IsTrue(context.active);
        }

        [Test]
        public void Held_Continued_NoEdge()
        {
            var set = new OperationSet { enabled = true, input = new FakeInputSource() };
            set.SetHeld(true);

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: true, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(1f, context.value, 1e-4f);
            Assert.IsFalse(context.pressed, "no new edge while still held");
            Assert.IsTrue(context.active);
        }

        [Test]
        public void Hold_Released_FiresFallingEdgeWithZeroValue()
        {
            // held is now false, but it was held last frame: one falling edge with value 0.
            var set = new OperationSet { enabled = true, input = new FakeInputSource() };

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: true, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0f, context.value, 1e-4f);
            Assert.IsFalse(context.pressed);
            Assert.IsTrue(context.released, "falling edge on the release frame");
            Assert.IsFalse(context.active);
        }

        [Test]
        public void InputDriven_PassesThroughValue()
        {
            var set = new OperationSet
            {
                enabled = true,
                control = new DeckSlider(),
                input = new FakeInputSource { raw = 0.7f },
            };

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.7f, context.value, 1e-4f, "value mode forwards the raw 0..1 input");
            Assert.IsTrue(context.active, "0.7 is above the activation threshold");
        }

        [Test]
        public void DisabledAndNotHeld_Skips()
        {
            var set = new OperationSet { enabled = false, input = new FakeInputSource { raw = 1f } };

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out _);

            Assert.IsFalse(fired, "a disabled set that is not held does not fire");
        }

        [Test]
        public void NoInputAndNotHeld_Skips()
        {
            var set = new OperationSet { enabled = true, input = null };

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out _);

            Assert.IsFalse(fired, "a set without an input source does not fire");
        }

        [Test]
        public void DisabledButHeld_StillFires()
        {
            // Manual hold overrides the enabled flag, since the user triggered it explicitly.
            var set = new OperationSet { enabled = false, input = new FakeInputSource() };
            set.SetHeld(true);

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(1f, context.value, 1e-4f);
        }

        [Test]
        public void ApplyExclusiveGroup_ClearsHeldGroupmate()
        {
            var winner = new OperationSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            var loser = new OperationSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            winner.SetHeld(true);
            loser.SetHeld(true);

            OperationManager.ApplyExclusiveGroup(new List<OperationSet> { winner, loser }, 0);

            Assert.IsTrue(winner.held, "the winner stays on");
            Assert.IsFalse(loser.held, "a groupmate's manual hold is cleared");
        }

        [Test]
        public void ApplyExclusiveGroup_ClearsLatchedKeyboardToggleOfGroupmate()
        {
            var winner = new OperationSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            var loser = new OperationSet
            {
                group = "g",
                control = new DeckToggle(),
                input = new FakeInputSource { raw = 1f },
            };

            // Latch the loser on with a keyboard rising edge.
            Assert.AreEqual(1f, loser.input.Evaluate(InputMode.Toggle).value, 1e-4f);

            OperationManager.ApplyExclusiveGroup(new List<OperationSet> { winner, loser }, 0);

            // The latched toggle is cleared; the still-held key no longer reads as on (no new rising edge).
            Assert.AreEqual(0f, loser.input.Evaluate(InputMode.Toggle).value, 1e-4f);
        }

        [Test]
        public void ApplyExclusiveGroup_LeavesOtherGroupsAndUngroupedUntouched()
        {
            var winner = new OperationSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            var otherGroup = new OperationSet { group = "h", input = new FakeInputSource() };
            var ungrouped = new OperationSet { group = "", input = new FakeInputSource() };
            otherGroup.SetHeld(true);
            ungrouped.SetHeld(true);

            OperationManager.ApplyExclusiveGroup(
                new List<OperationSet> { winner, otherGroup, ungrouped }, 0);

            Assert.IsTrue(otherGroup.held, "a different group is unaffected");
            Assert.IsTrue(ungrouped.held, "ungrouped sets are unaffected");
        }

        [Test]
        public void ApplyExclusiveGroup_UngroupedWinner_DoesNothing()
        {
            var winner = new OperationSet { group = "", input = new FakeInputSource() };
            var other = new OperationSet { group = "", input = new FakeInputSource() };
            other.SetHeld(true);

            OperationManager.ApplyExclusiveGroup(new List<OperationSet> { winner, other }, 0);

            Assert.IsTrue(other.held, "no group means no exclusivity");
        }

        [Test]
        public void ApplyExclusiveGroup_ButtonModeWinner_DoesNotClearGroupmates()
        {
            var winner = new OperationSet { group = "g", control = new DeckButton(), input = new FakeInputSource() };
            var other = new OperationSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            other.SetHeld(true);

            OperationManager.ApplyExclusiveGroup(new List<OperationSet> { winner, other }, 0);

            Assert.IsTrue(other.held, "a momentary (button) winner does not enforce exclusivity");
        }

        [Test]
        public void HeldButton_TriggersOnReleaseNotPress()
        {
            // A button-mode set held via the remote app commits its one-shot trigger on release.
            var set = new OperationSet
            {
                enabled = true,
                control = new DeckButton(),
                input = new FakeInputSource(),
            };

            set.SetHeld(true);
            OperationManager.TryGetFiringContext(set, wasHeld: false, out var press);
            Assert.IsTrue(press.pressed);
            Assert.IsFalse(press.triggered, "a held button does not trigger on press");

            set.SetHeld(false);
            OperationManager.TryGetFiringContext(set, wasHeld: true, out var release);
            Assert.IsTrue(release.released);
            Assert.IsTrue(release.triggered, "a held button triggers on release");
        }

        [Test]
        public void HeldButton_PressAndReleaseCollapsedInOneFrame_StillTriggersOnce()
        {
            // A momentary deck button sends held=true then held=false as two separate REST calls. If both land
            // between two Update frames, the manager never observes held as true (wasHeld stays false), and the
            // release one-shot used to be silently dropped (intermittent "no reaction" on fast taps). The
            // rising-edge pulse latched by SetHeld makes the release still fire exactly once.
            var set = new OperationSet
            {
                enabled = true,
                control = new DeckButton(),
                input = new FakeInputSource(),
            };

            // Press then release before any frame observed the hold: held ends false, but the pulse latched.
            set.SetHeld(true);
            set.SetHeld(false);

            OperationManager.TryGetFiringContext(set, wasHeld: false, out var collapsed);
            Assert.IsTrue(collapsed.released, "the collapsed press is seen as a release");
            Assert.IsTrue(collapsed.triggered, "the button's one-shot still fires when the press collapsed in one frame");

            // The manager consumes the pulse each frame; the next frame must not re-fire the one-shot.
            set.heldPulse = false;
            OperationManager.TryGetFiringContext(set, wasHeld: false, out var next);
            Assert.IsFalse(next.triggered, "the collapsed press fires exactly once, not on subsequent frames");
        }

        [Test]
        public void HeldToggle_TriggersOnPress()
        {
            // Non-button (toggle) sets keep triggering on the press edge (when turned on).
            var set = new OperationSet
            {
                enabled = true,
                control = new DeckToggle(),
                input = new FakeInputSource(),
            };
            set.SetHeld(true);

            OperationManager.TryGetFiringContext(set, wasHeld: false, out var press);
            Assert.IsTrue(press.triggered, "a toggle triggers when it turns on");
        }

        [Test]
        public void AddOperationSet_WithInputAndActions_AddsConfiguredSetAndReturnsIt()
        {
            var manager = new OperationManager();
            var input = new FakeInputSource();
            var action = new SetPropertyOperation { targetId = "obj", propertyPath = "expressions/Happy/weight" };

            var set = manager.AddOperationSet(input, action);

            Assert.IsNotNull(set);
            Assert.AreSame(input, set.input, "the supplied input is used as-is");
            Assert.AreEqual(1, set.operations.Count);
            Assert.AreSame(action, set.operations[0], "the supplied action is used as-is");
            Assert.IsFalse(string.IsNullOrEmpty(set.id), "a fresh id is assigned");
            Assert.IsTrue(set.enabled);
            Assert.AreSame(set, manager.operationSets[manager.operationSets.Count - 1], "the set is appended");
        }

        [Test]
        public void OnAfterLiveDeserialize_BackfillsIdOnSetAuthoredWithoutOne()
        {
            // A set authored directly in a scene / prop bundle (not via AddOperationSet) loads with the
            // default empty id, which makes every id-addressed function a silent no-op. Restore must
            // backfill a stable id so the set is addressable.
            var manager = new OperationManager();
            var set = new OperationSet { enabled = true, control = new DeckToggle() };
            Assert.AreEqual(string.Empty, set.id, "an authored set starts with the default empty id");
            manager.operationSets.Add(set);

            manager.OnAfterLiveDeserialize();

            Assert.IsFalse(string.IsNullOrEmpty(set.id), "restore assigns a stable id to the set");
        }

        [Test]
        public void RemoveOperationSet_RemovesSetAuthoredWithoutId_AfterBackfill()
        {
            // Regression: a DeckToggle set authored without an id could not be removed because the empty
            // id never matched in _IndexOf. After the id backfill, deleting by the assigned id works.
            var manager = new OperationManager();
            manager.operationSets.Add(new OperationSet { enabled = true, control = new DeckToggle() });
            manager.OnAfterLiveDeserialize();

            var id = manager.operationSets[0].id;
            manager.RemoveOperationSet(id);

            Assert.AreEqual(0, manager.operationSets.Count, "the backfilled id addresses the set for removal");
        }

        [Test]
        public void AddFunctionOperation_CreatesButtonSetWithInvokeAction_AndReturnsId()
        {
            var manager = new OperationManager();

            var id = manager.AddFunctionOperation("obj-1", "DoThing", "Do Thing", "<Keyboard>/a");

            Assert.AreEqual(1, manager.operationSets.Count);
            var set = manager.operationSets[0];
            Assert.AreEqual(id, set.id, "the returned id addresses the created set");
            Assert.AreEqual("Do Thing", set.name, "the set is named after the bound function");
            Assert.IsInstanceOf<KeyInputSource>(set.input);
            Assert.AreEqual(InputMode.Button, set.control.mode, "a function bind is momentary");
            Assert.AreEqual("<Keyboard>/a", ((KeyInputSource)set.input).binding,
                "the set is born bound to the captured key");
            Assert.AreEqual(1, set.operations.Count);
            var action = set.operations[0] as InvokeFunctionOperation;
            Assert.IsNotNull(action);
            Assert.AreEqual("obj-1", action.targetId);
            Assert.AreEqual("DoThing", action.functionName);
            Assert.AreEqual(string.Empty, action.argsJson,
                "the no-argument overload leaves argsJson empty");
        }

        [Test]
        public void AddFunctionOperation_StoresArgsJsonOnInvokeOperation()
        {
            var manager = new OperationManager();

            var id = manager.AddFunctionOperation("obj-1", "SetValue", "Set Value", "", "[42]");

            var set = manager.operationSets[0];
            Assert.AreEqual(id, set.id);
            var action = set.operations[0] as InvokeFunctionOperation;
            Assert.IsNotNull(action);
            Assert.AreEqual("SetValue", action.functionName);
            Assert.AreEqual("[42]", action.argsJson,
                "the positional arguments are stored on the operation for replay");
        }

        [Test]
        public void AddPropertyOperation_CreatesSetWithSetPropertyOperation_AndReturnsId()
        {
            var manager = new OperationManager();

            var id = manager.AddPropertyOperation("obj-2", "useSpout", "Toggle", "Use Spout", "", "");

            Assert.AreEqual(1, manager.operationSets.Count);
            var set = manager.operationSets[0];
            Assert.AreEqual(id, set.id);
            Assert.AreEqual("Use Spout", set.name, "the set is named after the bound property");
            Assert.AreEqual(string.Empty, set.group, "an empty group leaves the set ungrouped");
            Assert.IsInstanceOf<KeyInputSource>(set.input);
            Assert.AreEqual(string.Empty, ((KeyInputSource)set.input).binding,
                "an empty binding leaves the set unbound");
            Assert.AreEqual(1, set.operations.Count);
            var action = set.operations[0] as SetPropertyOperation;
            Assert.IsNotNull(action);
            Assert.AreEqual("obj-2", action.targetId);
            Assert.AreEqual("useSpout", action.propertyPath);
        }

        [Test]
        public void AddPropertyOperation_AssignsGroupWhenProvided()
        {
            var manager = new OperationManager();

            manager.AddPropertyOperation(
                "obj", "expressions[Joy]/weight", "Toggle", "Joy", "", "Expression");

            var set = manager.operationSets[manager.operationSets.Count - 1];
            Assert.AreEqual("Expression", set.group,
                "a non-empty group is assigned to the new set (exclusivity radio)");
        }

        [Test]
        public void AddPropertyOperation_MapsModeArgument()
        {
            var manager = new OperationManager();

            Assert.AreEqual(InputMode.Toggle, _ModeOfAdded(manager, "Toggle"),
                "Toggle latches on/off");
            Assert.AreEqual(InputMode.Button, _ModeOfAdded(manager, "Button"),
                "Button is momentary");
            Assert.AreEqual(InputMode.Button, _ModeOfAdded(manager, "button"),
                "mode is case-insensitive");
            Assert.AreEqual(InputMode.Toggle, _ModeOfAdded(manager, ""),
                "empty falls back to Toggle");
            Assert.AreEqual(InputMode.Toggle, _ModeOfAdded(manager, "nonsense"),
                "unrecognized falls back to Toggle");

            // Local helper: adds one and returns the new set's behaviour mode (carried by its control kind).
            static InputMode _ModeOfAdded(OperationManager mgr, string mode)
            {
                var id = mgr.AddPropertyOperation("obj", "prop", mode, "Prop", "", "");
                var set = mgr.operationSets[mgr.operationSets.Count - 1];
                Assert.AreEqual(id, set.id);
                return set.control.mode;
            }
        }

        [Test]
        public void ManualValue_OverridesInputWithThatValue()
        {
            // A Value-mode slider value fires with that value, taking precedence over the bound input.
            var set = new OperationSet
            {
                enabled = true,
                input = new FakeInputSource { raw = 0.2f },
            };
            set.SetManualValue(0.6f);

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.6f, context.value, 1e-4f, "the manual value overrides the bound input");
            Assert.IsTrue(context.active, "0.6 is at/above the 0.5 threshold");
            Assert.IsFalse(context.pressed, "the slider carries no edges");
            Assert.IsFalse(context.triggered, "the slider carries no edges");
        }

        [Test]
        public void ManualValue_Clamps()
        {
            var high = new OperationSet { enabled = true, input = new FakeInputSource() };
            high.SetManualValue(1.5f);
            OperationManager.TryGetFiringContext(high, wasHeld: false, out var hi);
            Assert.AreEqual(1f, hi.value, 1e-4f, "values above 1 clamp to 1");

            var low = new OperationSet { enabled = true, input = new FakeInputSource() };
            low.SetManualValue(-0.5f);
            OperationManager.TryGetFiringContext(low, wasHeld: false, out var lo);
            Assert.AreEqual(0f, lo.value, 1e-4f, "values below 0 clamp to 0");
            Assert.IsFalse(lo.active, "a clamped-zero manual value is below the threshold");
        }

        [Test]
        public void ManualValue_DefaultNone_FallsThroughToInput()
        {
            // A fresh set has no manual value (the -1 sentinel), so the bound input drives it as before.
            var set = new OperationSet
            {
                enabled = true,
                control = new DeckSlider(),
                input = new FakeInputSource { raw = 0.7f },
            };

            Assert.IsFalse(set.hasManualValue, "a fresh set has no manual value override");

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.7f, context.value, 1e-4f, "with no manual value the input still drives the set");
        }

        [Test]
        public void ManualValue_OverridesEvenWhenDisabled()
        {
            // Like the manual hold, an explicit slider value fires regardless of the enabled flag.
            var set = new OperationSet { enabled = false, input = new FakeInputSource { raw = 0f } };
            set.SetManualValue(0.4f);

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.4f, context.value, 1e-4f);
        }

        [Test]
        public void SetManualValue_ZeroCountsAsOverride()
        {
            // The -1 sentinel (was NaN) means "no override"; a value of 0 is a real, persisted override
            // (a slider dragged fully to the left), so it must not be mistaken for "none".
            var set = new OperationSet();
            Assert.IsFalse(set.hasManualValue, "a fresh set has no manual value override");

            set.SetManualValue(0f);

            Assert.IsTrue(set.hasManualValue, "a zero manual value is a real override, not the no-override sentinel");
            Assert.AreEqual(0f, set.manualValue, 1e-4f);
        }

        [Test]
        public void SetOperationSetValue_AppliesClampedManualValueToTargetSet()
        {
            var manager = new OperationManager();
            var id = manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "");
            var set = manager.operationSets[manager.operationSets.Count - 1];

            manager.SetOperationSetValue(id, 1.5f);

            Assert.AreEqual(1f, set.manualValue, 1e-4f, "the manual value is set and clamped to 0..1");
        }

        [Test]
        public void SetOperationSetValue_UnknownId_IsNoOp()
        {
            var manager = new OperationManager();
            var id = manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "");
            var set = manager.operationSets[manager.operationSets.Count - 1];

            manager.SetOperationSetValue("does-not-exist", 0.5f);

            Assert.IsFalse(set.hasManualValue, "an unknown id leaves every set's manual value untouched");
        }

        [Test]
        public void OperationSetValues_ReflectsLastValueInOrderWithNullsAsZero()
        {
            var a = new OperationSet { lastValue = 0.3f };
            var b = new OperationSet { lastValue = 1f };
            var manager = new OperationManager
            {
                operationSets = new List<OperationSet> { a, null, b },
            };

            var values = manager.operationSetValues;

            Assert.AreEqual(3, values.Length);
            Assert.AreEqual(0.3f, values[0], 1e-4f);
            Assert.AreEqual(0f, values[1], 1e-4f, "null entries report 0");
            Assert.AreEqual(1f, values[2], 1e-4f);
        }

        [Test]
        public void AddDeck_AppendsDeckWithUniqueNameAndReturnsIt()
        {
            var manager = new OperationManager();

            var name = manager.AddDeck();

            Assert.AreEqual(1, manager.decks.Count);
            var deck = manager.decks[0];
            Assert.AreEqual(name, deck.name, "the returned name addresses the created deck");
            Assert.IsFalse(string.IsNullOrEmpty(deck.name), "a name is assigned");
        }

        [Test]
        public void AddDeck_AssignsUniqueNames()
        {
            var manager = new OperationManager();

            var first = manager.AddDeck();
            var second = manager.AddDeck();

            Assert.AreEqual(2, manager.decks.Count);
            Assert.AreEqual("Deck", first);
            Assert.AreEqual("Deck 2", second, "a colliding default name is auto-suffixed");
        }

        [Test]
        public void RemoveDeck_RemovesMatchingDeck()
        {
            var manager = new OperationManager();
            var keep = manager.AddDeck();
            var drop = manager.AddDeck();

            manager.RemoveDeck(drop);

            Assert.AreEqual(1, manager.decks.Count);
            Assert.AreEqual(keep, manager.decks[0].name, "the other deck is untouched");
        }

        [Test]
        public void RemoveDeck_UnknownName_IsNoOp()
        {
            var manager = new OperationManager();
            manager.AddDeck();

            manager.RemoveDeck("does-not-exist");

            Assert.AreEqual(1, manager.decks.Count, "an unknown name removes nothing");
        }

        [Test]
        public void AddFunctionOperation_DefaultsControlToPush()
        {
            var manager = new OperationManager();

            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");

            var set = manager.operationSets[manager.operationSets.Count - 1];
            Assert.AreEqual(id, set.id);
            Assert.IsInstanceOf<DeckButton>(set.control, "a function bind defaults to a momentary push tile");
            Assert.AreEqual(1, manager.decks.Count, "a default page is auto-created for the new control");
            Assert.AreEqual(manager.decks[0].name, set.control.deckName,
                "a new control is placed on the default page (no unplaced state)");
            Assert.AreEqual(0, set.control.x);
            Assert.AreEqual(0, set.control.y);
        }

        [Test]
        public void AddPropertyOperation_DefaultsControlKindFromMode()
        {
            var manager = new OperationManager();

            manager.AddPropertyOperation("obj", "useSpout", "Toggle", "Spout", "", "");
            Assert.IsInstanceOf<DeckToggle>(
                manager.operationSets[manager.operationSets.Count - 1].control,
                "a toggle property defaults to a checkbox tile");

            manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "");
            Assert.IsInstanceOf<DeckSlider>(
                manager.operationSets[manager.operationSets.Count - 1].control,
                "a value property defaults to a slider tile");

            manager.AddPropertyOperation("obj", "fire", "Button", "Fire", "", "");
            Assert.IsInstanceOf<DeckButton>(
                manager.operationSets[manager.operationSets.Count - 1].control,
                "a button property defaults to a push tile");
        }

        [Test]
        public void AddPropertyOperation_ValueMode_InheritsSliderMinMax()
        {
            var manager = new OperationManager();

            manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "", 10f, 90f);

            var slider = manager.operationSets[manager.operationSets.Count - 1].control as DeckSlider;
            Assert.IsNotNull(slider, "a value property defaults to a slider tile");
            Assert.AreEqual(10f, slider.min, 1e-4f, "the source slider's min is inherited");
            Assert.AreEqual(90f, slider.max, 1e-4f, "the source slider's max is inherited");
        }

        [Test]
        public void AddPropertyOperation_ValueMode_DefaultsToIdentityRange()
        {
            var manager = new OperationManager();

            manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "");

            var slider = manager.operationSets[manager.operationSets.Count - 1].control as DeckSlider;
            Assert.IsNotNull(slider);
            Assert.AreEqual(0f, slider.min, 1e-4f, "without a source range the tile keeps the identity 0..1 range");
            Assert.AreEqual(1f, slider.max, 1e-4f);
        }

        [Test]
        public void AddPropertyOperation_NonSliderMode_IgnoresMinMax()
        {
            var manager = new OperationManager();

            // A toggle property maps to a DeckToggle, which carries no value range; min/max are simply ignored.
            manager.AddPropertyOperation("obj", "useSpout", "Toggle", "Spout", "", "", 10f, 90f);

            Assert.IsInstanceOf<DeckToggle>(manager.operationSets[manager.operationSets.Count - 1].control);
        }

        [Test]
        public void TryGetFiringContext_DeckSliderMapsValueOntoRange()
        {
            // A slider with a [10, 90] range maps its normalized 0.25 manual value onto 10 + 0.25*80 = 30.
            var set = new OperationSet
            {
                enabled = true,
                control = new DeckSlider { min = 10f, max = 90f },
                input = new FakeInputSource(),
            };
            set.SetManualValue(0.25f);

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.25f, context.value, 1e-4f, "value stays normalized so gauges are unaffected");
            Assert.AreEqual(10f, context.valueMin, 1e-4f);
            Assert.AreEqual(90f, context.valueMax, 1e-4f);
            Assert.AreEqual(30f, context.MappedValue, 1e-4f, "MappedValue maps the 0..1 value onto [min, max]");
        }

        [Test]
        public void TryGetFiringContext_NonSliderControl_KeepsIdentityRange()
        {
            // A non-slider control (default DeckButton) keeps the identity 0..1 range, so MappedValue == value.
            var set = new OperationSet { enabled = true, input = new FakeInputSource { raw = 0.7f } };

            bool fired = OperationManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0f, context.valueMin, 1e-4f);
            Assert.AreEqual(1f, context.valueMax, 1e-4f);
            Assert.AreEqual(context.value, context.MappedValue, 1e-4f, "the identity range leaves the value unchanged");
        }

        [Test]
        public void PlaceControl_SetsDeckAndCell()
        {
            var manager = new OperationManager();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");

            manager.PlaceControl(id, "deck-1", 3, 2);

            var control = manager.operationSets[0].control;
            Assert.AreEqual("deck-1", control.deckName);
            Assert.AreEqual(3, control.x);
            Assert.AreEqual(2, control.y);
        }

        [Test]
        public void PlaceControl_EmptyDeckName_PlacesOnDefaultPage()
        {
            var manager = new OperationManager();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");
            // AddFunctionOperation auto-created the default page and placed the control there.
            var defaultDeckName = manager.decks[0].name;
            manager.PlaceControl(id, "deck-1", 1, 1);

            manager.PlaceControl(id, "", 0, 0);

            Assert.AreEqual(defaultDeckName, manager.operationSets[0].control.deckName,
                "an empty deck name falls back to the default page (no unplaced state)");
        }

        [Test]
        public void PlaceControlOnFreeCell_SkipsOccupiedCells()
        {
            var manager = new OperationManager();
            var deck = manager.AddDeck();
            var occupying = manager.AddFunctionOperation("obj", "A", "A", "");
            manager.PlaceControl(occupying, deck, 0, 0);
            var id = manager.AddFunctionOperation("obj", "B", "B", "");

            manager.PlaceControlOnFreeCell(id, deck);

            var control = manager.operationSets[1].control;
            Assert.AreEqual(deck, control.deckName);
            Assert.AreEqual(1, control.x, "an added tile takes the first free cell instead of overlapping");
            Assert.AreEqual(0, control.y);
        }

        [Test]
        public void PlaceControlOnFreeCell_SliderSkipsGapNarrowerThanItsSpan()
        {
            var manager = new OperationManager();
            var deck = manager.AddDeck();
            var left = manager.AddFunctionOperation("obj", "A", "A", "");
            var right = manager.AddFunctionOperation("obj", "B", "B", "");
            manager.PlaceControl(left, deck, 0, 0);
            manager.PlaceControl(right, deck, 2, 0);
            var slider = manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "");

            manager.PlaceControlOnFreeCell(slider, deck);

            var control = manager.operationSets[2].control;
            Assert.AreEqual(2, control.w, "a slider tile is 2 cells wide");
            Assert.AreEqual(3, control.x,
                "the single free cell between the two tiles cannot hold a 2-wide tile");
            Assert.AreEqual(0, control.y);
        }

        [Test]
        public void PlaceControlOnFreeCell_EmptyDeckName_UsesDefaultPage()
        {
            var manager = new OperationManager();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");
            var defaultDeckName = manager.decks[0].name;
            manager.PlaceControl(id, "deck-1", 1, 1);

            manager.PlaceControlOnFreeCell(id, "");

            var control = manager.operationSets[0].control;
            Assert.AreEqual(defaultDeckName, control.deckName,
                "an empty deck name falls back to the default page (no unplaced state)");
            Assert.AreEqual(0, control.x);
            Assert.AreEqual(0, control.y);
        }

        [Test]
        public void SetControlType_SwapsKindPreservingPlacement()
        {
            var manager = new OperationManager();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");
            manager.PlaceControl(id, "deck-1", 4, 5);

            manager.SetControlType(id, "DeckSlider");

            var control = manager.operationSets[0].control;
            Assert.IsInstanceOf<DeckSlider>(control, "the kind is swapped");
            Assert.AreEqual("deck-1", control.deckName, "placement is preserved across the swap");
            Assert.AreEqual(4, control.x);
            Assert.AreEqual(5, control.y);
        }

        [Test]
        public void SetControlType_UnknownType_IsNoOp()
        {
            var manager = new OperationManager();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");

            manager.SetControlType(id, "NotAControl");

            Assert.IsInstanceOf<DeckButton>(manager.operationSets[0].control,
                "an unknown type leaves the existing control untouched");
        }

        [Test]
        public void RemoveDeck_MovesControlsToDefaultPage()
        {
            var manager = new OperationManager();
            var keep = manager.AddDeck(); // "Deck" (becomes the default = first deck)
            var drop = manager.AddDeck(); // "Deck 2"
            var a = manager.AddFunctionOperation("obj", "A", "A", "");
            var b = manager.AddFunctionOperation("obj", "B", "B", "");
            manager.PlaceControl(a, drop, 0, 0);
            manager.PlaceControl(b, keep, 1, 0);

            manager.RemoveDeck(drop);

            // No unplaced state: the control on the removed deck moves to the default page (first remaining).
            Assert.AreEqual(1, manager.decks.Count, "only the kept deck remains");
            Assert.AreEqual(keep, manager.operationSets[0].control.deckName,
                "a control on the removed deck moves to the default page");
            Assert.AreEqual(keep, manager.operationSets[1].control.deckName,
                "a control on the kept deck is untouched");
        }

        [Test]
        public void AddOperationSet_AutoCreatesDefaultPageAndPlacesAtFirstCell()
        {
            var manager = new OperationManager();

            manager.AddOperationSet(new KeyInputSource());

            Assert.AreEqual(1, manager.decks.Count, "the first add auto-creates the default page");
            var control = manager.operationSets[0].control;
            Assert.AreEqual(manager.decks[0].name, control.deckName, "placed on the default page");
            Assert.AreEqual(0, control.x);
            Assert.AreEqual(0, control.y);
        }

        [Test]
        public void AddOperationSet_SecondControl_GoesToNextFreeCell()
        {
            var manager = new OperationManager();

            manager.AddOperationSet(new KeyInputSource());
            manager.AddOperationSet(new KeyInputSource());

            Assert.AreEqual(1, manager.decks.Count, "both share the same default page");
            var first = manager.operationSets[0].control;
            var second = manager.operationSets[1].control;
            Assert.AreEqual(0, first.x);
            Assert.AreEqual(0, first.y);
            Assert.AreEqual(1, second.x, "the second tile takes the next free cell");
            Assert.AreEqual(0, second.y);
        }

        [Test]
        public void AddPropertyOperation_SliderTileIsTwoCellsWide()
        {
            var manager = new OperationManager();

            manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "");

            var control = manager.operationSets[0].control;
            Assert.IsInstanceOf<DeckSlider>(control, "a value property defaults to a slider tile");
            Assert.AreEqual(2, control.w, "a slider tile is fixed at 2 cells wide");
        }

        [Test]
        public void SetControlType_SliderWidthIsTwo_OtherKindsOne()
        {
            var manager = new OperationManager();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");
            Assert.AreEqual(1, manager.operationSets[0].control.w, "a push tile is 1 wide");

            manager.SetControlType(id, "DeckSlider");
            Assert.AreEqual(2, manager.operationSets[0].control.w, "swapping to slider widens to 2 cells");

            manager.SetControlType(id, "DeckButton");
            Assert.AreEqual(1, manager.operationSets[0].control.w, "swapping away from slider narrows back to 1");
        }

        [Test]
        public void AddOperationSet_SliderTakesTwoCells_NextTileSkipsThem()
        {
            var manager = new OperationManager();
            // First a slider (2 wide at 0,0), then a push: the push must skip the slider's two cells.
            manager.AddPropertyOperation("obj", "weight", "Value", "Weight", "", "");
            manager.AddOperationSet(new KeyInputSource());

            var slider = manager.operationSets[0].control;
            var push = manager.operationSets[1].control;
            Assert.AreEqual(0, slider.x);
            Assert.AreEqual(2, slider.w);
            Assert.AreEqual(2, push.x, "the next tile starts after the 2-wide slider");
            Assert.AreEqual(0, push.y);
        }

        [Test]
        public void OnAfterLiveDeserialize_PlacesUnplacedControls()
        {
            var manager = new OperationManager();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");
            // Simulate a restored/older scene where the control carries no deck.
            manager.operationSets[0].control.deckName = string.Empty;

            manager.OnAfterLiveDeserialize();

            Assert.IsFalse(string.IsNullOrEmpty(manager.operationSets[0].control.deckName),
                "a control with no deck is placed on the default page after restore");
        }

        [Test]
        public void OnAfterLiveDeserialize_UnknownDeckName_RecreatesDeck()
        {
            var manager = new OperationManager();
            manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");
            // Simulate pasted serialized action data referencing a deck that does not exist yet.
            manager.operationSets[0].control.deckName = "Combat";

            manager.OnAfterLiveDeserialize();

            Assert.IsTrue(manager.decks.Exists(p => p.name == "Combat"),
                "a missing referenced deck is recreated by name so pasted data brings its deck along");
            Assert.AreEqual("Combat", manager.operationSets[0].control.deckName,
                "the control keeps its deck reference rather than being moved");
        }

        [Test]
        public void OnAfterLiveDeserialize_DuplicateUnknownName_CreatesOneDeck()
        {
            var manager = new OperationManager();
            manager.AddFunctionOperation("obj", "A", "A", "");
            manager.AddFunctionOperation("obj", "B", "B", "");
            manager.operationSets[0].control.deckName = "Combat";
            manager.operationSets[1].control.deckName = "Combat";

            manager.OnAfterLiveDeserialize();

            Assert.AreEqual(1, manager.decks.FindAll(p => p.name == "Combat").Count,
                "controls referencing the same missing deck create exactly one deck");
        }

        [Test]
        public void RenameDeck_PropagatesToControls()
        {
            var manager = new OperationManager();
            var name = manager.AddDeck();
            var id = manager.AddFunctionOperation("obj", "DoThing", "Do Thing", "");
            manager.PlaceControl(id, name, 0, 0);

            manager.RenameDeck(name, "Main");

            Assert.AreEqual("Main", manager.decks[0].name, "the deck is renamed");
            Assert.AreEqual("Main", manager.operationSets[0].control.deckName,
                "a control on the renamed deck follows the rename");
        }

        [Test]
        public void RenameDeck_CollisionAutoSuffixes()
        {
            var manager = new OperationManager();
            manager.AddDeck(); // "Deck"
            var second = manager.AddDeck(); // "Deck 2"

            manager.RenameDeck(second, "Deck");

            Assert.AreEqual("Deck 2", manager.decks[1].name,
                "renaming onto an existing name auto-suffixes to stay unique");
        }

        [Test]
        public void RenameDeck_SameName_NoOp()
        {
            var manager = new OperationManager();
            var name = manager.AddDeck();

            manager.RenameDeck(name, name);

            Assert.AreEqual(name, manager.decks[0].name, "renaming to the same name is a no-op");
        }
    }
}
