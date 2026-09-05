// Copyright (c) You-Ri, 2026
using System;

using NUnit.Framework;

using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A value chosen from a list. The selector is what says the value comes from a set, and that
    /// is the whole declaration -- nothing here asks for a width or for a table.
    /// </summary>
    [LiveClass("TextTablePicker")]
    public class TextTablePicker
    {
        [LiveField(lane = FrameLane.State), StringSelector(nameof(choices))]
        public string pick = string.Empty;

        /// <summary>Text of the same kind, kept to a width. The contrast the fixture is about.</summary>
        [LiveField(lane = FrameLane.State, textCapacity = 32)]
        public string typed = string.Empty;

        [LiveProperty, Hide]
        public string[] choices => new[] { string.Empty, "one", "two" };
    }

    /// <summary>
    /// Text drawn from a vocabulary travels as the id the frame's table gave it.
    ///
    /// The four bytes are not the point. A fixed width is a claim about every value the member will
    /// ever hold, and a value that outgrows it is left out of the frame rather than shortened -- so
    /// the claim turns out to be wrong on somebody's rig, halfway through a take, silently. An id
    /// has no length to outgrow.
    /// </summary>
    [TestFixture]
    public class LiveTextTableTests
    {
        private const string kId = "text-table-subject";

        private TextTablePicker _subject;
        private LiveObjectHandle? _handle;

        /// <summary>Longer than the widest fixed slot (256 bytes), and a plausible bone path.</summary>
        private static string _TooLongForAnyWidth()
            => "Armature/" + string.Join("/", System.Linq.Enumerable.Repeat("SomeRatherLongBoneName", 20));

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<TextTablePicker>();

            FrameGate.ResetState("[test] cleared");
            LiveFixedStringStats.Reset();
            LiveTextIdStats.Reset();

            _subject = new TextTablePicker();
            _handle = LiveObjectRegistry.Create(typeof(TextTablePicker), _subject, kId);
        }

        [TearDown]
        public void Finish()
        {
            _handle?.Unregister();
            _handle = null;

            LiveObjectRegistry.ClearAll();
            FrameGate.ResetState("[test] cleared");
        }

        [Test]
        public void AValueChosenFromAList_ComesBack()
        {
            _subject.pick = "two";

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            _subject.pick = string.Empty;
            LiveStateSystem.ApplyFrom(state, FrameGate.symbols);

            Assert.AreEqual("two", _subject.pick);
        }

        [Test]
        public void AValueLongerThanAnyWidth_StillComesBack()
        {
            // The reason for the table. The widest slot the block has is 256 bytes; this is past it,
            // and a fixed width would drop it -- leaving the member saying nothing at all.
            var long_ = _TooLongForAnyWidth();
            Assert.Greater(System.Text.Encoding.UTF8.GetByteCount(long_), 256, "the fixture is not testing what it says");

            _subject.pick = long_;

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            _subject.pick = string.Empty;
            LiveStateSystem.ApplyFrom(state, FrameGate.symbols);

            Assert.AreEqual(long_, _subject.pick);
        }

        [Test]
        public void TheSameValueOnEveryFrame_InternsOnce()
        {
            // What makes the table cheap: the string is in the file once however many frames repeat
            // it, where a width costs its own bytes per object per frame.
            _subject.pick = "one";

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var after = FrameGate.symbols.count;
            for (int i = 0; i < 10; i++) LiveStateSystem.CaptureInto(state, time: i);

            Assert.AreEqual(after, FrameGate.symbols.count, "the table grew for a value that never changed");
        }

        [Test]
        public void AWidthDeclaredOutright_StillMeansAWidth()
        {
            // The opt-out. Saying textCapacity is how a member keeps the fixed slot, and with it the
            // ceiling: a value past the width is passed over rather than shortened.
            _subject.typed = _TooLongForAnyWidth();

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            _subject.typed = "short";
            LiveStateSystem.ApplyFrom(state, FrameGate.symbols);

            Assert.AreEqual("short", _subject.typed, "a value that outgrew its width must not overwrite what is there");
            Assert.Greater(LiveFixedStringStats.unrepresentableCount, 0, "the drop went uncounted");
        }

        [Test]
        public void AnIdTheTableCannotResolve_LeavesTheMemberAlone()
        {
            // A file cut short mid-write names ids its table never got. Reading those as empty would
            // clear the member, and clearing a reference is a change where saying nothing is not.
            _subject.pick = "two";

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            _subject.pick = "one";

            // The owner is in this table so its row is found; the value's id is not, which is the
            // shape a file cut short has -- rows referring to symbols that never got written.
            var partial = new FrameSymbolTable();
            partial.SetAt(FrameGate.symbols.Intern(kId), kId);

            LiveStateSystem.ApplyFrom(state, partial);

            Assert.AreEqual("one", _subject.pick, "an unresolvable id overwrote a value that was there");
            Assert.Greater(LiveTextIdStats.unresolvedCount, 0, "the miss went uncounted");
        }

        [Test]
        public void NullAndEmpty_StayApart()
        {
            // Neither can be interned (the table answers kNone for both), so they are encoded rather
            // than looked up. A replay that turned one into the other would clear a member that was
            // only ever blank -- or set one that was never set at all.
            var symbols = new FrameSymbolTable();

            var fromNull = LiveTextId.From(null, symbols);
            var fromEmpty = LiveTextId.From(string.Empty, symbols);

            Assert.AreNotEqual(fromNull, fromEmpty);
            Assert.IsTrue(fromNull.hasValue);
            Assert.IsTrue(fromEmpty.hasValue);
            Assert.IsFalse(default(LiveTextId).hasValue, "a zeroed block must not read as a value");

            Assert.IsTrue(fromNull.TryGetValue("something", symbols, out var toNull));
            Assert.IsNull(toNull);
            Assert.IsFalse(fromNull.TryGetValue(null, symbols, out _), "already null, so nothing to write");

            Assert.IsTrue(fromEmpty.TryGetValue(null, symbols, out var toEmpty));
            Assert.AreEqual(string.Empty, toEmpty);
            Assert.IsFalse(fromEmpty.TryGetValue(string.Empty, symbols, out _), "already empty, so nothing to write");
        }

        [Test]
        public void AValueThatHasNotMoved_IsNotWrittenAgain()
        {
            // The state lane says every member on every frame. Writing through a setter each time
            // would run whatever that setter does -- loading an asset, pairing a device -- sixty
            // times a second for a value standing still.
            var symbols = new FrameSymbolTable();
            var id = LiveTextId.From("two", symbols);

            Assert.IsFalse(id.TryGetValue("two", symbols, out _));
            Assert.IsTrue(id.TryGetValue("one", symbols, out var value));
            Assert.AreEqual("two", value);
        }
    }
}
