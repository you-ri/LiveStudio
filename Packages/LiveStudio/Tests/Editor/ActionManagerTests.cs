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
                input = new FakeInputSource { mode = InputMode.Value, raw = 0.7f },
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
            var winner = new ActionSet { group = "g", input = new FakeInputSource { mode = InputMode.Toggle } };
            var loser = new ActionSet { group = "g", input = new FakeInputSource { mode = InputMode.Toggle } };
            winner.SetHeld(true);
            loser.SetHeld(true);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, loser }, 0);

            Assert.IsTrue(winner.held, "the winner stays on");
            Assert.IsFalse(loser.held, "a groupmate's manual hold is cleared");
        }

        [Test]
        public void ApplyExclusiveGroup_ClearsLatchedKeyboardToggleOfGroupmate()
        {
            var winner = new ActionSet { group = "g", input = new FakeInputSource { mode = InputMode.Toggle } };
            var loser = new ActionSet
            {
                group = "g",
                input = new FakeInputSource { mode = InputMode.Toggle, raw = 1f },
            };

            // Latch the loser on with a keyboard rising edge.
            Assert.AreEqual(1f, loser.input.Evaluate().value, 1e-4f);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, loser }, 0);

            // The latched toggle is cleared; the still-held key no longer reads as on (no new rising edge).
            Assert.AreEqual(0f, loser.input.Evaluate().value, 1e-4f);
        }

        [Test]
        public void ApplyExclusiveGroup_LeavesOtherGroupsAndUngroupedUntouched()
        {
            var winner = new ActionSet { group = "g", input = new FakeInputSource { mode = InputMode.Toggle } };
            var otherGroup = new ActionSet { group = "h", input = new FakeInputSource { mode = InputMode.Toggle } };
            var ungrouped = new ActionSet { group = "", input = new FakeInputSource { mode = InputMode.Toggle } };
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
            var winner = new ActionSet { group = "", input = new FakeInputSource { mode = InputMode.Toggle } };
            var other = new ActionSet { group = "", input = new FakeInputSource { mode = InputMode.Toggle } };
            other.SetHeld(true);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, other }, 0);

            Assert.IsTrue(other.held, "no group means no exclusivity");
        }

        [Test]
        public void ApplyExclusiveGroup_ButtonModeWinner_DoesNotClearGroupmates()
        {
            var winner = new ActionSet { group = "g", input = new FakeInputSource { mode = InputMode.Button } };
            var other = new ActionSet { group = "g", input = new FakeInputSource { mode = InputMode.Toggle } };
            other.SetHeld(true);

            ActionManager.ApplyExclusiveGroup(new List<ActionSet> { winner, other }, 0);

            Assert.IsTrue(other.held, "a momentary (button) winner does not enforce exclusivity");
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
    }
}
