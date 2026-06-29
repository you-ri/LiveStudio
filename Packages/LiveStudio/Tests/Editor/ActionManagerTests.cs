// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using NUnit.Framework;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Tests the firing logic <see cref="ActionManager.Update"/> drives each frame, exercised through the
    /// pure <see cref="ActionManager.TryGetFiringContext"/> helper (so no play mode is needed), plus the
    /// <see cref="ActionManager.actionSetValues"/> poll surface the remote app reads. Reuses
    /// <see cref="FakeInputSource"/> from InputSourceTests to drive input without a real device.
    /// </summary>
    public class ActionManagerTests
    {
        [Test]
        public void Held_RisingEdge_FiresFullPress()
        {
            var set = new ActionSet { enabled = true, input = new FakeInputSource() };
            set.SetHeld(true);

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(1f, context.value, 1e-4f);
            Assert.IsTrue(context.pressed, "rising edge on the first held frame");
            Assert.IsFalse(context.released);
            Assert.IsTrue(context.active);
        }

        [Test]
        public void Held_Continued_NoEdge()
        {
            var set = new ActionSet { enabled = true, input = new FakeInputSource() };
            set.SetHeld(true);

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: true, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(1f, context.value, 1e-4f);
            Assert.IsFalse(context.pressed, "no new edge while still held");
            Assert.IsTrue(context.active);
        }

        [Test]
        public void Hold_Released_FiresFallingEdgeWithZeroValue()
        {
            // held is now false, but it was held last frame: one falling edge with value 0.
            var set = new ActionSet { enabled = true, input = new FakeInputSource() };

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: true, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0f, context.value, 1e-4f);
            Assert.IsFalse(context.pressed);
            Assert.IsTrue(context.released, "falling edge on the release frame");
            Assert.IsFalse(context.active);
        }

        [Test]
        public void InputDriven_PassesThroughValue()
        {
            var set = new ActionSet
            {
                enabled = true,
                control = new DeckSlider(),
                input = new FakeInputSource { raw = 0.7f },
            };

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.7f, context.value, 1e-4f, "value mode forwards the raw 0..1 input");
            Assert.IsTrue(context.active, "0.7 is above the activation threshold");
        }

        [Test]
        public void DisabledAndNotHeld_Skips()
        {
            var set = new ActionSet { enabled = false, input = new FakeInputSource { raw = 1f } };

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out _);

            Assert.IsFalse(fired, "a disabled set that is not held does not fire");
        }

        [Test]
        public void NoInputAndNotHeld_Skips()
        {
            var set = new ActionSet { enabled = true, input = null };

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out _);

            Assert.IsFalse(fired, "a set without an input source does not fire");
        }

        [Test]
        public void DisabledButHeld_StillFires()
        {
            // Manual hold overrides the enabled flag, since the user triggered it explicitly.
            var set = new ActionSet { enabled = false, input = new FakeInputSource() };
            set.SetHeld(true);

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(1f, context.value, 1e-4f);
        }

        [Test]
        public void ApplyExclusiveGroup_ClearsHeldGroupmate()
        {
            var winner = new ActionSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            var loser = new ActionSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            winner.SetHeld(true);
            loser.SetHeld(true);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, loser }, 0);

            Assert.IsTrue(winner.held, "the winner stays on");
            Assert.IsFalse(loser.held, "a groupmate's manual hold is cleared");
        }

        [Test]
        public void ApplyExclusiveGroup_ClearsLatchedKeyboardToggleOfGroupmate()
        {
            var winner = new ActionSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            var loser = new ActionSet
            {
                group = "g",
                control = new DeckToggle(),
                input = new FakeInputSource { raw = 1f },
            };

            // Latch the loser on with a keyboard rising edge.
            Assert.AreEqual(1f, loser.input.Evaluate(InputMode.Toggle).value, 1e-4f);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, loser }, 0);

            // The latched toggle is cleared; the still-held key no longer reads as on (no new rising edge).
            Assert.AreEqual(0f, loser.input.Evaluate(InputMode.Toggle).value, 1e-4f);
        }

        [Test]
        public void ApplyExclusiveGroup_LeavesOtherGroupsAndUngroupedUntouched()
        {
            var winner = new ActionSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            var otherGroup = new ActionSet { group = "h", input = new FakeInputSource() };
            var ungrouped = new ActionSet { group = "", input = new FakeInputSource() };
            otherGroup.SetHeld(true);
            ungrouped.SetHeld(true);

            ActionManager.ApplyExclusiveGroup(
                new List<ActionSet> { winner, otherGroup, ungrouped }, 0);

            Assert.IsTrue(otherGroup.held, "a different group is unaffected");
            Assert.IsTrue(ungrouped.held, "ungrouped sets are unaffected");
        }

        [Test]
        public void ApplyExclusiveGroup_UngroupedWinner_DoesNothing()
        {
            var winner = new ActionSet { group = "", input = new FakeInputSource() };
            var other = new ActionSet { group = "", input = new FakeInputSource() };
            other.SetHeld(true);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, other }, 0);

            Assert.IsTrue(other.held, "no group means no exclusivity");
        }

        [Test]
        public void ApplyExclusiveGroup_ButtonModeWinner_DoesNotClearGroupmates()
        {
            var winner = new ActionSet { group = "g", control = new DeckButton(), input = new FakeInputSource() };
            var other = new ActionSet { group = "g", control = new DeckToggle(), input = new FakeInputSource() };
            other.SetHeld(true);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, other }, 0);

            Assert.IsTrue(other.held, "a momentary (button) winner does not enforce exclusivity");
        }

        [Test]
        public void HeldButton_TriggersOnReleaseNotPress()
        {
            // A button-mode set held via the remote app commits its one-shot trigger on release.
            var set = new ActionSet
            {
                enabled = true,
                control = new DeckButton(),
                input = new FakeInputSource(),
            };

            set.SetHeld(true);
            ActionManager.TryGetFiringContext(set, wasHeld: false, out var press);
            Assert.IsTrue(press.pressed);
            Assert.IsFalse(press.triggered, "a held button does not trigger on press");

            set.SetHeld(false);
            ActionManager.TryGetFiringContext(set, wasHeld: true, out var release);
            Assert.IsTrue(release.released);
            Assert.IsTrue(release.triggered, "a held button triggers on release");
        }

        [Test]
        public void HeldToggle_TriggersOnPress()
        {
            // Non-button (toggle) sets keep triggering on the press edge (when turned on).
            var set = new ActionSet
            {
                enabled = true,
                control = new DeckToggle(),
                input = new FakeInputSource(),
            };
            set.SetHeld(true);

            ActionManager.TryGetFiringContext(set, wasHeld: false, out var press);
            Assert.IsTrue(press.triggered, "a toggle triggers when it turns on");
        }

        [Test]
        public void AddActionSet_WithInputAndActions_AddsConfiguredSetAndReturnsIt()
        {
            var manager = new ActionManager();
            var input = new FakeInputSource();
            var action = new SetPropertyAction { targetId = "obj", propertyPath = "expressions/Happy/weight" };

            var set = manager.AddActionSet(input, action);

            Assert.IsNotNull(set);
            Assert.AreSame(input, set.input, "the supplied input is used as-is");
            Assert.AreEqual(1, set.actions.Count);
            Assert.AreSame(action, set.actions[0], "the supplied action is used as-is");
            Assert.IsFalse(string.IsNullOrEmpty(set.id), "a fresh id is assigned");
            Assert.IsTrue(set.enabled);
            Assert.AreSame(set, manager.actionSets[manager.actionSets.Count - 1], "the set is appended");
        }

        [Test]
        public void AddFunctionAction_CreatesButtonSetWithInvokeAction_AndReturnsId()
        {
            var manager = new ActionManager();

            var id = manager.AddFunctionAction("obj-1", "DoThing", "Do Thing", "<Keyboard>/a");

            Assert.AreEqual(1, manager.actionSets.Count);
            var set = manager.actionSets[0];
            Assert.AreEqual(id, set.id, "the returned id addresses the created set");
            Assert.AreEqual("Do Thing", set.name, "the set is named after the bound function");
            Assert.IsInstanceOf<KeyInputSource>(set.input);
            Assert.AreEqual(InputMode.Button, set.control.mode, "a function bind is momentary");
            Assert.AreEqual("<Keyboard>/a", ((KeyInputSource)set.input).binding,
                "the set is born bound to the captured key");
            Assert.AreEqual(1, set.actions.Count);
            var action = set.actions[0] as InvokeFunctionAction;
            Assert.IsNotNull(action);
            Assert.AreEqual("obj-1", action.targetId);
            Assert.AreEqual("DoThing", action.functionName);
        }

        [Test]
        public void AddPropertyAction_CreatesSetWithSetPropertyAction_AndReturnsId()
        {
            var manager = new ActionManager();

            var id = manager.AddPropertyAction("obj-2", "useSpout", "Toggle", "Use Spout", "", "");

            Assert.AreEqual(1, manager.actionSets.Count);
            var set = manager.actionSets[0];
            Assert.AreEqual(id, set.id);
            Assert.AreEqual("Use Spout", set.name, "the set is named after the bound property");
            Assert.AreEqual(string.Empty, set.group, "an empty group leaves the set ungrouped");
            Assert.IsInstanceOf<KeyInputSource>(set.input);
            Assert.AreEqual(string.Empty, ((KeyInputSource)set.input).binding,
                "an empty binding leaves the set unbound");
            Assert.AreEqual(1, set.actions.Count);
            var action = set.actions[0] as SetPropertyAction;
            Assert.IsNotNull(action);
            Assert.AreEqual("obj-2", action.targetId);
            Assert.AreEqual("useSpout", action.propertyPath);
        }

        [Test]
        public void AddPropertyAction_AssignsGroupWhenProvided()
        {
            var manager = new ActionManager();

            manager.AddPropertyAction(
                "obj", "expressions[Joy]/weight", "Toggle", "Joy", "", "Expression");

            var set = manager.actionSets[manager.actionSets.Count - 1];
            Assert.AreEqual("Expression", set.group,
                "a non-empty group is assigned to the new set (exclusivity radio)");
        }

        [Test]
        public void AddPropertyAction_MapsModeArgument()
        {
            var manager = new ActionManager();

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
            static InputMode _ModeOfAdded(ActionManager mgr, string mode)
            {
                var id = mgr.AddPropertyAction("obj", "prop", mode, "Prop", "", "");
                var set = mgr.actionSets[mgr.actionSets.Count - 1];
                Assert.AreEqual(id, set.id);
                return set.control.mode;
            }
        }

        [Test]
        public void ManualValue_OverridesInputWithThatValue()
        {
            // A Value-mode slider value fires with that value, taking precedence over the bound input.
            var set = new ActionSet
            {
                enabled = true,
                input = new FakeInputSource { raw = 0.2f },
            };
            set.SetManualValue(0.6f);

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.6f, context.value, 1e-4f, "the manual value overrides the bound input");
            Assert.IsTrue(context.active, "0.6 is at/above the 0.5 threshold");
            Assert.IsFalse(context.pressed, "the slider carries no edges");
            Assert.IsFalse(context.triggered, "the slider carries no edges");
        }

        [Test]
        public void ManualValue_Clamps()
        {
            var high = new ActionSet { enabled = true, input = new FakeInputSource() };
            high.SetManualValue(1.5f);
            ActionManager.TryGetFiringContext(high, wasHeld: false, out var hi);
            Assert.AreEqual(1f, hi.value, 1e-4f, "values above 1 clamp to 1");

            var low = new ActionSet { enabled = true, input = new FakeInputSource() };
            low.SetManualValue(-0.5f);
            ActionManager.TryGetFiringContext(low, wasHeld: false, out var lo);
            Assert.AreEqual(0f, lo.value, 1e-4f, "values below 0 clamp to 0");
            Assert.IsFalse(lo.active, "a clamped-zero manual value is below the threshold");
        }

        [Test]
        public void ManualValue_DefaultNaN_FallsThroughToInput()
        {
            // A fresh set has no manual value (NaN), so the bound input drives it as before.
            var set = new ActionSet
            {
                enabled = true,
                control = new DeckSlider(),
                input = new FakeInputSource { raw = 0.7f },
            };

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.7f, context.value, 1e-4f, "with no manual value the input still drives the set");
        }

        [Test]
        public void ManualValue_OverridesEvenWhenDisabled()
        {
            // Like the manual hold, an explicit slider value fires regardless of the enabled flag.
            var set = new ActionSet { enabled = false, input = new FakeInputSource { raw = 0f } };
            set.SetManualValue(0.4f);

            bool fired = ActionManager.TryGetFiringContext(set, wasHeld: false, out var context);

            Assert.IsTrue(fired);
            Assert.AreEqual(0.4f, context.value, 1e-4f);
        }

        [Test]
        public void SetActionSetValue_AppliesClampedManualValueToTargetSet()
        {
            var manager = new ActionManager();
            var id = manager.AddPropertyAction("obj", "weight", "Value", "Weight", "", "");
            var set = manager.actionSets[manager.actionSets.Count - 1];

            manager.SetActionSetValue(id, 1.5f);

            Assert.AreEqual(1f, set.manualValue, 1e-4f, "the manual value is set and clamped to 0..1");
        }

        [Test]
        public void SetActionSetValue_UnknownId_IsNoOp()
        {
            var manager = new ActionManager();
            var id = manager.AddPropertyAction("obj", "weight", "Value", "Weight", "", "");
            var set = manager.actionSets[manager.actionSets.Count - 1];

            manager.SetActionSetValue("does-not-exist", 0.5f);

            Assert.IsTrue(float.IsNaN(set.manualValue), "an unknown id leaves every set's manual value untouched");
        }

        [Test]
        public void ActionSetValues_ReflectsLastValueInOrderWithNullsAsZero()
        {
            var a = new ActionSet { lastValue = 0.3f };
            var b = new ActionSet { lastValue = 1f };
            var manager = new ActionManager
            {
                actionSets = new List<ActionSet> { a, null, b },
            };

            var values = manager.actionSetValues;

            Assert.AreEqual(3, values.Length);
            Assert.AreEqual(0.3f, values[0], 1e-4f);
            Assert.AreEqual(0f, values[1], 1e-4f, "null entries report 0");
            Assert.AreEqual(1f, values[2], 1e-4f);
        }

        [Test]
        public void AddDeck_AppendsDeckWithUniqueNameAndReturnsIt()
        {
            var manager = new ActionManager();

            var name = manager.AddDeck();

            Assert.AreEqual(1, manager.decks.Count);
            var deck = manager.decks[0];
            Assert.AreEqual(name, deck.name, "the returned name addresses the created deck");
            Assert.IsFalse(string.IsNullOrEmpty(deck.name), "a name is assigned");
        }

        [Test]
        public void AddDeck_AssignsUniqueNames()
        {
            var manager = new ActionManager();

            var first = manager.AddDeck();
            var second = manager.AddDeck();

            Assert.AreEqual(2, manager.decks.Count);
            Assert.AreEqual("Deck", first);
            Assert.AreEqual("Deck 2", second, "a colliding default name is auto-suffixed");
        }

        [Test]
        public void RemoveDeck_RemovesMatchingDeck()
        {
            var manager = new ActionManager();
            var keep = manager.AddDeck();
            var drop = manager.AddDeck();

            manager.RemoveDeck(drop);

            Assert.AreEqual(1, manager.decks.Count);
            Assert.AreEqual(keep, manager.decks[0].name, "the other deck is untouched");
        }

        [Test]
        public void RemoveDeck_UnknownName_IsNoOp()
        {
            var manager = new ActionManager();
            manager.AddDeck();

            manager.RemoveDeck("does-not-exist");

            Assert.AreEqual(1, manager.decks.Count, "an unknown name removes nothing");
        }

        [Test]
        public void AddFunctionAction_DefaultsControlToPush()
        {
            var manager = new ActionManager();

            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");

            var set = manager.actionSets[manager.actionSets.Count - 1];
            Assert.AreEqual(id, set.id);
            Assert.IsInstanceOf<DeckButton>(set.control, "a function bind defaults to a momentary push tile");
            Assert.AreEqual(1, manager.decks.Count, "a default page is auto-created for the new control");
            Assert.AreEqual(manager.decks[0].name, set.control.deckName,
                "a new control is placed on the default page (no unplaced state)");
            Assert.AreEqual(0, set.control.x);
            Assert.AreEqual(0, set.control.y);
        }

        [Test]
        public void AddPropertyAction_DefaultsControlKindFromMode()
        {
            var manager = new ActionManager();

            manager.AddPropertyAction("obj", "useSpout", "Toggle", "Spout", "", "");
            Assert.IsInstanceOf<DeckToggle>(
                manager.actionSets[manager.actionSets.Count - 1].control,
                "a toggle property defaults to a checkbox tile");

            manager.AddPropertyAction("obj", "weight", "Value", "Weight", "", "");
            Assert.IsInstanceOf<DeckSlider>(
                manager.actionSets[manager.actionSets.Count - 1].control,
                "a value property defaults to a slider tile");

            manager.AddPropertyAction("obj", "fire", "Button", "Fire", "", "");
            Assert.IsInstanceOf<DeckButton>(
                manager.actionSets[manager.actionSets.Count - 1].control,
                "a button property defaults to a push tile");
        }

        [Test]
        public void PlaceControl_SetsDeckAndCell()
        {
            var manager = new ActionManager();
            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");

            manager.PlaceControl(id, "deck-1", 3, 2);

            var control = manager.actionSets[0].control;
            Assert.AreEqual("deck-1", control.deckName);
            Assert.AreEqual(3, control.x);
            Assert.AreEqual(2, control.y);
        }

        [Test]
        public void PlaceControl_EmptyDeckName_PlacesOnDefaultPage()
        {
            var manager = new ActionManager();
            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");
            // AddFunctionAction auto-created the default page and placed the control there.
            var defaultDeckName = manager.decks[0].name;
            manager.PlaceControl(id, "deck-1", 1, 1);

            manager.PlaceControl(id, "", 0, 0);

            Assert.AreEqual(defaultDeckName, manager.actionSets[0].control.deckName,
                "an empty deck name falls back to the default page (no unplaced state)");
        }

        [Test]
        public void SetControlType_SwapsKindPreservingPlacement()
        {
            var manager = new ActionManager();
            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");
            manager.PlaceControl(id, "deck-1", 4, 5);

            manager.SetControlType(id, "DeckSlider");

            var control = manager.actionSets[0].control;
            Assert.IsInstanceOf<DeckSlider>(control, "the kind is swapped");
            Assert.AreEqual("deck-1", control.deckName, "placement is preserved across the swap");
            Assert.AreEqual(4, control.x);
            Assert.AreEqual(5, control.y);
        }

        [Test]
        public void SetControlType_UnknownType_IsNoOp()
        {
            var manager = new ActionManager();
            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");

            manager.SetControlType(id, "NotAControl");

            Assert.IsInstanceOf<DeckButton>(manager.actionSets[0].control,
                "an unknown type leaves the existing control untouched");
        }

        [Test]
        public void RemoveDeck_MovesControlsToDefaultPage()
        {
            var manager = new ActionManager();
            var keep = manager.AddDeck(); // "Deck" (becomes the default = first deck)
            var drop = manager.AddDeck(); // "Deck 2"
            var a = manager.AddFunctionAction("obj", "A", "A", "");
            var b = manager.AddFunctionAction("obj", "B", "B", "");
            manager.PlaceControl(a, drop, 0, 0);
            manager.PlaceControl(b, keep, 1, 0);

            manager.RemoveDeck(drop);

            // No unplaced state: the control on the removed deck moves to the default page (first remaining).
            Assert.AreEqual(1, manager.decks.Count, "only the kept deck remains");
            Assert.AreEqual(keep, manager.actionSets[0].control.deckName,
                "a control on the removed deck moves to the default page");
            Assert.AreEqual(keep, manager.actionSets[1].control.deckName,
                "a control on the kept deck is untouched");
        }

        [Test]
        public void AddActionSet_AutoCreatesDefaultPageAndPlacesAtFirstCell()
        {
            var manager = new ActionManager();

            manager.AddActionSet(new KeyInputSource());

            Assert.AreEqual(1, manager.decks.Count, "the first add auto-creates the default page");
            var control = manager.actionSets[0].control;
            Assert.AreEqual(manager.decks[0].name, control.deckName, "placed on the default page");
            Assert.AreEqual(0, control.x);
            Assert.AreEqual(0, control.y);
        }

        [Test]
        public void AddActionSet_SecondControl_GoesToNextFreeCell()
        {
            var manager = new ActionManager();

            manager.AddActionSet(new KeyInputSource());
            manager.AddActionSet(new KeyInputSource());

            Assert.AreEqual(1, manager.decks.Count, "both share the same default page");
            var first = manager.actionSets[0].control;
            var second = manager.actionSets[1].control;
            Assert.AreEqual(0, first.x);
            Assert.AreEqual(0, first.y);
            Assert.AreEqual(1, second.x, "the second tile takes the next free cell");
            Assert.AreEqual(0, second.y);
        }

        [Test]
        public void AddPropertyAction_SliderTileIsTwoCellsWide()
        {
            var manager = new ActionManager();

            manager.AddPropertyAction("obj", "weight", "Value", "Weight", "", "");

            var control = manager.actionSets[0].control;
            Assert.IsInstanceOf<DeckSlider>(control, "a value property defaults to a slider tile");
            Assert.AreEqual(2, control.w, "a slider tile is fixed at 2 cells wide");
        }

        [Test]
        public void SetControlType_SliderWidthIsTwo_OtherKindsOne()
        {
            var manager = new ActionManager();
            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");
            Assert.AreEqual(1, manager.actionSets[0].control.w, "a push tile is 1 wide");

            manager.SetControlType(id, "DeckSlider");
            Assert.AreEqual(2, manager.actionSets[0].control.w, "swapping to slider widens to 2 cells");

            manager.SetControlType(id, "DeckButton");
            Assert.AreEqual(1, manager.actionSets[0].control.w, "swapping away from slider narrows back to 1");
        }

        [Test]
        public void AddActionSet_SliderTakesTwoCells_NextTileSkipsThem()
        {
            var manager = new ActionManager();
            // First a slider (2 wide at 0,0), then a push: the push must skip the slider's two cells.
            manager.AddPropertyAction("obj", "weight", "Value", "Weight", "", "");
            manager.AddActionSet(new KeyInputSource());

            var slider = manager.actionSets[0].control;
            var push = manager.actionSets[1].control;
            Assert.AreEqual(0, slider.x);
            Assert.AreEqual(2, slider.w);
            Assert.AreEqual(2, push.x, "the next tile starts after the 2-wide slider");
            Assert.AreEqual(0, push.y);
        }

        [Test]
        public void OnAfterExposedDeserialize_PlacesUnplacedControls()
        {
            var manager = new ActionManager();
            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");
            // Simulate a restored/older scene where the control carries no deck.
            manager.actionSets[0].control.deckName = string.Empty;

            manager.OnAfterExposedDeserialize();

            Assert.IsFalse(string.IsNullOrEmpty(manager.actionSets[0].control.deckName),
                "a control with no deck is placed on the default page after restore");
        }

        [Test]
        public void OnAfterExposedDeserialize_UnknownDeckName_RecreatesDeck()
        {
            var manager = new ActionManager();
            manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");
            // Simulate pasted serialized action data referencing a deck that does not exist yet.
            manager.actionSets[0].control.deckName = "Combat";

            manager.OnAfterExposedDeserialize();

            Assert.IsTrue(manager.decks.Exists(p => p.name == "Combat"),
                "a missing referenced deck is recreated by name so pasted data brings its deck along");
            Assert.AreEqual("Combat", manager.actionSets[0].control.deckName,
                "the control keeps its deck reference rather than being moved");
        }

        [Test]
        public void OnAfterExposedDeserialize_DuplicateUnknownName_CreatesOneDeck()
        {
            var manager = new ActionManager();
            manager.AddFunctionAction("obj", "A", "A", "");
            manager.AddFunctionAction("obj", "B", "B", "");
            manager.actionSets[0].control.deckName = "Combat";
            manager.actionSets[1].control.deckName = "Combat";

            manager.OnAfterExposedDeserialize();

            Assert.AreEqual(1, manager.decks.FindAll(p => p.name == "Combat").Count,
                "controls referencing the same missing deck create exactly one deck");
        }

        [Test]
        public void RenameDeck_PropagatesToControls()
        {
            var manager = new ActionManager();
            var name = manager.AddDeck();
            var id = manager.AddFunctionAction("obj", "DoThing", "Do Thing", "");
            manager.PlaceControl(id, name, 0, 0);

            manager.RenameDeck(name, "Main");

            Assert.AreEqual("Main", manager.decks[0].name, "the deck is renamed");
            Assert.AreEqual("Main", manager.actionSets[0].control.deckName,
                "a control on the renamed deck follows the rename");
        }

        [Test]
        public void RenameDeck_CollisionAutoSuffixes()
        {
            var manager = new ActionManager();
            manager.AddDeck(); // "Deck"
            var second = manager.AddDeck(); // "Deck 2"

            manager.RenameDeck(second, "Deck");

            Assert.AreEqual("Deck 2", manager.decks[1].name,
                "renaming onto an existing name auto-suffixes to stay unique");
        }

        [Test]
        public void RenameDeck_SameName_NoOp()
        {
            var manager = new ActionManager();
            var name = manager.AddDeck();

            manager.RenameDeck(name, name);

            Assert.AreEqual(name, manager.decks[0].name, "renaming to the same name is a no-op");
        }
    }
}
