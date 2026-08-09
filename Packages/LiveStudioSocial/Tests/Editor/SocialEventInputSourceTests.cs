// Copyright (c) You-Ri, 2026

using NUnit.Framework;

namespace Lilium.LiveStudio.Social.EditorTests
{
    /// <summary>
    /// Tests <see cref="SocialEventInputSource"/>: which events get through the declarative filters, and
    /// what the operation set then sees for each tile kind. Frames and the clock are driven through the
    /// hub's internal seams, so a whole stream of events replays deterministically without play mode.
    /// </summary>
    public class SocialEventInputSourceTests
    {
        private int _frame;
        private double _time;

        [SetUp]
        public void SetUp()
        {
            SocialEventHub.Reset();
            _frame = 1;
            _time = 0.0;
            SocialEventHub.frameProvider = () => _frame;
            SocialEventHub.timeProvider = () => _time;
        }

        [TearDown]
        public void TearDown()
        {
            SocialEventHub.Reset();
        }

        // Publishes one frame carrying the given events and returns what the source reads from it.
        private float _Frame(SocialEventInputSource source, params SocialEvent[] events)
        {
            _frame++;
            for (int i = 0; i < events.Length; i++) SocialEventHub.Enqueue(events[i]);
            return _ReadRaw(source);
        }

        // Publishes one frame and evaluates the source the way the operation manager would.
        private OperationContext _Frame(SocialEventInputSource source, InputMode mode, params SocialEvent[] events)
        {
            _frame++;
            for (int i = 0; i < events.Length; i++) SocialEventHub.Enqueue(events[i]);
            return source.Evaluate(mode);
        }

        // ReadRawValue is protected; Evaluate in Value mode passes the raw value through unchanged
        // (Mathf.Clamp01 of a value already in 0..1), so it doubles as a read of the raw output.
        private static float _ReadRaw(SocialEventInputSource source) => source.Evaluate(InputMode.Value).value;

        private static SocialEvent _Chat(string message) => new SocialEvent
        {
            source = SocialEventSources.Test,
            type = SocialEventTypes.Chat,
            message = message,
        };

        private static SocialEvent _Tip(float amount) => new SocialEvent
        {
            source = SocialEventSources.YouTube,
            type = SocialEventTypes.SuperChat,
            amount = amount,
        };

        // --- Filters ---

        [Test]
        public void WithNoFilters_AnyEventFires()
        {
            var source = new SocialEventInputSource();

            Assert.AreEqual(1f, _Frame(source, _Chat("anything")), 1e-4f);
            Assert.AreEqual(0f, _Frame(source), 1e-4f, "a frame with no events does not fire");
        }

        [Test]
        public void Source_AcceptsOnlyThatPlatform()
        {
            var source = new SocialEventInputSource { source = SocialEventSources.YouTube };

            Assert.AreEqual(0f, _Frame(source, _Chat("from test")), 1e-4f);
            Assert.AreEqual(1f, _Frame(source, new SocialEvent
            {
                source = SocialEventSources.YouTube,
                type = SocialEventTypes.Chat,
            }), 1e-4f);
        }

        [Test]
        public void Source_IsCaseInsensitive()
        {
            var source = new SocialEventInputSource { source = "YouTube" };

            Assert.AreEqual(1f, _Frame(source, new SocialEvent { source = "youtube", type = "chat" }), 1e-4f);
        }

        [Test]
        public void EventType_AcceptsOnlyThatKind()
        {
            var source = new SocialEventInputSource { eventType = SocialEventTypes.SuperChat };

            Assert.AreEqual(0f, _Frame(source, _Chat("hi")), 1e-4f);
            Assert.AreEqual(1f, _Frame(source, _Tip(100f)), 1e-4f);
        }

        [Test]
        public void Keyword_MatchesAnywhereAndIgnoresCase()
        {
            var source = new SocialEventInputSource { keyword = "!confetti" };

            Assert.AreEqual(0f, _Frame(source, _Chat("hello")), 1e-4f);
            Assert.AreEqual(1f, _Frame(source, _Chat("!confetti")), 1e-4f);
            Assert.AreEqual(1f, _Frame(source, _Chat("please !CONFETTI now")), 1e-4f,
                "a viewer writing more than the bare command still fires it");
        }

        [Test]
        public void Keyword_DoesNotThrowOnAnEventWithNoMessage()
        {
            // The hub normalizes a missing message to empty, which is exactly what the filter relies on.
            var source = new SocialEventInputSource { keyword = "!confetti" };

            Assert.AreEqual(0f, _Frame(source, _Tip(500f)), 1e-4f);
        }

        [Test]
        public void MinAmount_RejectsSmallerTips()
        {
            var source = new SocialEventInputSource { minAmount = 500f };

            Assert.AreEqual(0f, _Frame(source, _Tip(499f)), 1e-4f);
            Assert.AreEqual(1f, _Frame(source, _Tip(500f)), 1e-4f, "the minimum is inclusive");
            Assert.AreEqual(0f, _Frame(source, _Chat("no amount at all")), 1e-4f);
        }

        [Test]
        public void UserFilter_MemberOnly_AcceptsMembersModeratorsAndTheOwner()
        {
            var source = new SocialEventInputSource { userFilter = SocialUserFilter.MemberOnly };

            Assert.AreEqual(0f, _Frame(source, _Chat("a")), 1e-4f, "a plain viewer is rejected");

            var member = _Chat("b");
            member.user = new SocialUser { isMember = true };
            Assert.AreEqual(1f, _Frame(source, member), 1e-4f);

            var moderator = _Chat("c");
            moderator.user = new SocialUser { isModerator = true };
            Assert.AreEqual(1f, _Frame(source, moderator), 1e-4f, "a moderator counts as a member");

            var owner = _Chat("d");
            owner.user = new SocialUser { isOwner = true };
            Assert.AreEqual(1f, _Frame(source, owner), 1e-4f, "the streamer is not locked out of their own perks");
        }

        [Test]
        public void UserFilter_ModeratorOnly_RejectsPlainMembers()
        {
            var source = new SocialEventInputSource { userFilter = SocialUserFilter.ModeratorOnly };

            var member = _Chat("a");
            member.user = new SocialUser { isMember = true };
            Assert.AreEqual(0f, _Frame(source, member), 1e-4f);

            var moderator = _Chat("b");
            moderator.user = new SocialUser { isModerator = true };
            Assert.AreEqual(1f, _Frame(source, moderator), 1e-4f);

            var owner = _Chat("c");
            owner.user = new SocialUser { isOwner = true };
            Assert.AreEqual(1f, _Frame(source, owner), 1e-4f, "the streamer outranks their own moderators");
        }

        [Test]
        public void EventType_IsCaseInsensitive()
        {
            var source = new SocialEventInputSource { eventType = "SuperChat" };

            Assert.AreEqual(1f, _Frame(source, new SocialEvent { source = "test", type = "superchat" }), 1e-4f);
        }

        [Test]
        public void ANaNAmount_DoesNotSlipPastTheMinimum()
        {
            // NaN compares false against everything, so an unguarded minimum would let it through and the
            // value would then be written to a property forever (NaN never equals what is already there).
            // The hub turns it into 0 on intake; this pins that the filter can rely on it.
            var source = new SocialEventInputSource { minAmount = 100f };

            var broken = _Tip(float.NaN);
            Assert.AreEqual(0f, _Frame(source, broken), 1e-4f);
            Assert.AreEqual(0f, broken.amount, 1e-4f, "the amount was sanitized rather than left as NaN");
        }

        [Test]
        public void Filters_CombineAsAnd()
        {
            var source = new SocialEventInputSource
            {
                source = SocialEventSources.YouTube,
                eventType = SocialEventTypes.SuperChat,
                minAmount = 500f,
            };

            Assert.AreEqual(0f, _Frame(source, _Tip(100f)), 1e-4f, "right platform and kind, amount too small");
            Assert.AreEqual(1f, _Frame(source, _Tip(1000f)), 1e-4f);
        }

        [Test]
        public void AMatchingEventAmongNonMatchingOnesStillFires()
        {
            var source = new SocialEventInputSource { keyword = "!confetti" };

            Assert.AreEqual(1f, _Frame(source, _Chat("hello"), _Chat("!confetti"), _Chat("bye")), 1e-4f);
        }

        // --- Pulse shape ---

        [Test]
        public void Firing_LastsExactlyOneFrame()
        {
            var source = new SocialEventInputSource();

            Assert.AreEqual(1f, _Frame(source, _Chat("a")), 1e-4f);
            Assert.AreEqual(0f, _Frame(source), 1e-4f, "the pulse does not carry into the next frame");
        }

        // --- Tile kinds ---

        [Test]
        public void ButtonTile_RunsTheOperationOncePerEvent()
        {
            var source = new SocialEventInputSource();

            // A button commits on the falling edge, so the event frame arms it and the next frame fires.
            var onEvent = _Frame(source, InputMode.Button, _Chat("a"));
            Assert.IsTrue(onEvent.pressed);
            Assert.IsFalse(onEvent.triggered);

            var afterEvent = _Frame(source, InputMode.Button);
            Assert.IsTrue(afterEvent.released);
            Assert.IsTrue(afterEvent.triggered, "one event, one run");

            var quiet = _Frame(source, InputMode.Button);
            Assert.IsFalse(quiet.triggered, "and it does not keep running");
        }

        [Test]
        public void ToggleTile_FlipsOncePerEvent()
        {
            var source = new SocialEventInputSource();

            Assert.AreEqual(1f, _Frame(source, InputMode.Toggle, _Chat("a")).value, 1e-4f);
            Assert.AreEqual(1f, _Frame(source, InputMode.Toggle).value, 1e-4f, "the latch holds while quiet");
            Assert.AreEqual(0f, _Frame(source, InputMode.Toggle, _Chat("b")).value, 1e-4f);
        }

        [Test]
        public void ToggleTile_SeveralEventsInOneFrameFlipOnce()
        {
            // The frame is the resolution: a burst of comments is one flip, not a race that lands wherever.
            // Two events on purpose — an odd count would read as "on" under either behaviour and the test
            // would pass even if it flipped per event.
            var source = new SocialEventInputSource();

            Assert.AreEqual(1f, _Frame(source, InputMode.Toggle, _Chat("a"), _Chat("b")).value, 1e-4f);
        }

        [Test]
        public void ButtonTile_MergesMatchesOnConsecutiveFrames()
        {
            // Matches on back-to-back frames read as one long press, so busy chat fires once rather than
            // once per comment. Locked in deliberately: a confetti cannon should not run at 60 Hz, and
            // cooldownSeconds is the knob for an explicit rate.
            var source = new SocialEventInputSource();

            Assert.IsFalse(_Frame(source, InputMode.Button, _Chat("a")).triggered);
            Assert.IsFalse(_Frame(source, InputMode.Button, _Chat("b")).triggered, "still held down");
            Assert.IsFalse(_Frame(source, InputMode.Button, _Chat("c")).triggered);

            Assert.IsTrue(_Frame(source, InputMode.Button).triggered, "one firing once the chat pauses");
        }

        [Test]
        public void ToggleTile_MergesMatchesOnConsecutiveFrames()
        {
            var source = new SocialEventInputSource();

            Assert.AreEqual(1f, _Frame(source, InputMode.Toggle, _Chat("a")).value, 1e-4f);
            Assert.AreEqual(1f, _Frame(source, InputMode.Toggle, _Chat("b")).value, 1e-4f,
                "a continuing burst does not flip it back off");
        }

        [Test]
        public void SliderTile_WithValueFromAmount_HoldsTheNormalizedAmount()
        {
            var source = new SocialEventInputSource
            {
                valueFromAmount = true,
                minAmount = 0f,
                amountRange = 1000f,
            };

            Assert.AreEqual(0.5f, _Frame(source, _Tip(500f)), 1e-4f);
            Assert.AreEqual(0.5f, _Frame(source), 1e-4f, "the value holds instead of snapping back to 0");
            Assert.AreEqual(1f, _Frame(source, _Tip(1000f)), 1e-4f);
            Assert.AreEqual(1f, _Frame(source, _Tip(5000f)), 1e-4f, "amounts past the range clamp");
        }

        [Test]
        public void ValueFromAmount_MapsFromMinAmountNotFromZero()
        {
            var source = new SocialEventInputSource
            {
                valueFromAmount = true,
                minAmount = 500f,
                amountRange = 1500f,
            };

            Assert.AreEqual(0f, _Frame(source, _Tip(500f)), 1e-4f, "the smallest accepted tip is the low end");
            Assert.AreEqual(0.5f, _Frame(source, _Tip(1000f)), 1e-4f);
        }

        [Test]
        public void ValueFromAmount_WithADegenerateRange_ReadsFull()
        {
            // amountRange at or below minAmount has no span to map onto; treat every accepted tip as full
            // rather than dividing by zero.
            var source = new SocialEventInputSource
            {
                valueFromAmount = true,
                minAmount = 500f,
                amountRange = 500f,
            };

            Assert.AreEqual(1f, _Frame(source, _Tip(500f)), 1e-4f);
        }

        [Test]
        public void ValueFromAmount_StartsAtZeroBeforeAnythingArrives()
        {
            var source = new SocialEventInputSource { valueFromAmount = true };

            Assert.AreEqual(0f, _Frame(source), 1e-4f);
        }

        [Test]
        public void ValueFromAmount_TakesTheFirstMatchInAFrame()
        {
            // Chat order is the order the viewers saw. Picking the largest instead would make the earlier,
            // smaller tip look ignored.
            var source = new SocialEventInputSource { valueFromAmount = true, amountRange = 1000f };

            Assert.AreEqual(0.1f, _Frame(source, _Tip(100f), _Tip(900f)), 1e-4f);
        }

        [Test]
        public void ValueFromAmount_TurnedOnLaterStartsFromZero()
        {
            // Firing as a pulse must not leave a full-strength value behind in the latch, or switching the
            // set to a slider tile would show 1.0 out of nowhere.
            var source = new SocialEventInputSource();

            Assert.AreEqual(1f, _Frame(source, _Chat("a")), 1e-4f);

            source.valueFromAmount = true;
            Assert.AreEqual(0f, _Frame(source), 1e-4f);
        }

        [Test]
        public void ValueFromAmount_HoldsThroughACooldown()
        {
            // A tip arriving during the cooldown is ignored, and ignoring it means the slider keeps the
            // value it already had rather than dropping to 0.
            var source = new SocialEventInputSource
            {
                valueFromAmount = true,
                amountRange = 1000f,
                cooldownSeconds = 5f,
            };

            _time = 10.0;
            Assert.AreEqual(0.5f, _Frame(source, _Tip(500f)), 1e-4f);

            _time = 11.0;
            Assert.AreEqual(0.5f, _Frame(source, _Tip(1000f)), 1e-4f, "the second tip is inside the cooldown");

            _time = 16.0;
            Assert.AreEqual(1f, _Frame(source, _Tip(1000f)), 1e-4f);
        }

        // --- Cooldown ---

        [Test]
        public void Cooldown_IgnoresRepeatsUntilItExpires()
        {
            var source = new SocialEventInputSource { cooldownSeconds = 5f };

            _time = 10.0;
            Assert.AreEqual(1f, _Frame(source, _Chat("a")), 1e-4f);

            _time = 12.0;
            Assert.AreEqual(0f, _Frame(source, _Chat("b")), 1e-4f, "still inside the cooldown");

            _time = 14.9;
            Assert.AreEqual(0f, _Frame(source, _Chat("c")), 1e-4f);

            _time = 15.0;
            Assert.AreEqual(1f, _Frame(source, _Chat("d")), 1e-4f, "the cooldown has elapsed");
        }

        [Test]
        public void Cooldown_OfZeroFiresEveryTime()
        {
            var source = new SocialEventInputSource();

            _time = 10.0;
            Assert.AreEqual(1f, _Frame(source, _Chat("a")), 1e-4f);
            Assert.AreEqual(1f, _Frame(source, _Chat("b")), 1e-4f, "same timestamp, still fires");
        }

        [Test]
        public void Cooldown_DoesNotStartUntilAnEventIsAccepted()
        {
            // A brand new source must fire on its very first event however early in the session it is.
            var source = new SocialEventInputSource { cooldownSeconds = 60f };

            _time = 0.0;
            Assert.AreEqual(1f, _Frame(source, _Chat("a")), 1e-4f);
        }

        [Test]
        public void Cooldown_IsNotStartedByAnEventTheFiltersRejected()
        {
            var source = new SocialEventInputSource { keyword = "!confetti", cooldownSeconds = 60f };

            _time = 10.0;
            Assert.AreEqual(0f, _Frame(source, _Chat("unrelated")), 1e-4f);

            _time = 11.0;
            Assert.AreEqual(1f, _Frame(source, _Chat("!confetti")), 1e-4f,
                "chatter that never matched must not eat the cooldown");
        }

        [Test]
        public void Cooldown_TakesTheFirstMatchInAFrameAndSkipsTheRest()
        {
            var source = new SocialEventInputSource { cooldownSeconds = 5f };

            _time = 10.0;
            Assert.AreEqual(1f, _Frame(source, _Chat("a"), _Chat("b"), _Chat("c")), 1e-4f);

            _time = 11.0;
            Assert.AreEqual(0f, _Frame(source, _Chat("d")), 1e-4f);
        }
    }
}
