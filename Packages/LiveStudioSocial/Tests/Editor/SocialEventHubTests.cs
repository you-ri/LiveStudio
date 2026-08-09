// Copyright (c) You-Ri, 2026

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lilium.LiveStudio.Social.EditorTests
{
    /// <summary>
    /// Tests <see cref="SocialEventHub"/>: the frame-stable publish, the bounded queue's drop-oldest
    /// policy, intake normalization, and the reset that stands in for a domain reload. Frames are driven
    /// through the hub's internal frame seam because <c>Time.frameCount</c> does not advance in edit mode.
    /// </summary>
    public class SocialEventHubTests
    {
        private int _frame;

        [SetUp]
        public void SetUp()
        {
            SocialEventHub.Reset();
            _frame = 1;
            SocialEventHub.frameProvider = () => _frame;
        }

        [TearDown]
        public void TearDown()
        {
            // Restores the Unity frame counter too, so a leaked seam cannot follow the editor into play mode.
            SocialEventHub.Reset();
        }

        private static SocialEvent _Chat(string message) => new SocialEvent
        {
            source = SocialEventSources.Test,
            type = SocialEventTypes.Chat,
            message = message,
        };

        private static List<string> _Messages(IReadOnlyList<SocialEvent> events)
        {
            var messages = new List<string>(events.Count);
            for (int i = 0; i < events.Count; i++) messages.Add(events[i].message);
            return messages;
        }

        [Test]
        public void CurrentEvents_IsStableWithinAFrame()
        {
            SocialEventHub.Enqueue(_Chat("a"));

            CollectionAssert.AreEqual(new[] { "a" }, _Messages(SocialEventHub.currentEvents));

            // Arriving mid-frame must not change what this frame's consumers see: an input source that
            // already ran and one that runs later have to agree.
            SocialEventHub.Enqueue(_Chat("b"));
            CollectionAssert.AreEqual(new[] { "a" }, _Messages(SocialEventHub.currentEvents));

            _frame++;
            CollectionAssert.AreEqual(new[] { "b" }, _Messages(SocialEventHub.currentEvents));
        }

        [Test]
        public void CurrentEvents_EmptiesWhenNothingArrives()
        {
            SocialEventHub.Enqueue(_Chat("a"));
            Assert.AreEqual(1, SocialEventHub.currentEvents.Count);

            _frame++;
            Assert.AreEqual(0, SocialEventHub.currentEvents.Count, "last frame's events must not linger");
        }

        [Test]
        public void CurrentEvents_KeepsUnreadEventsUntilSomeoneReads()
        {
            // Nobody touches the hub for several frames; the lazy pump means the events wait rather than
            // being lost to a swap no consumer observed.
            SocialEventHub.Enqueue(_Chat("a"));
            _frame += 5;

            CollectionAssert.AreEqual(new[] { "a" }, _Messages(SocialEventHub.currentEvents));
        }

        [Test]
        public void Enqueue_PreservesArrivalOrder()
        {
            SocialEventHub.Enqueue(_Chat("a"));
            SocialEventHub.Enqueue(_Chat("b"));
            SocialEventHub.Enqueue(_Chat("c"));

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, _Messages(SocialEventHub.currentEvents));
        }

        [Test]
        public void Overflow_DropsOldestAndCountsThem()
        {
            const int kOverflow = 44;
            int total = SocialEventHub.kQueueCapacity + kOverflow;
            for (int i = 0; i < total; i++) SocialEventHub.Enqueue(_Chat(i.ToString()));

            var events = SocialEventHub.currentEvents;
            Assert.AreEqual(SocialEventHub.kQueueCapacity, events.Count);
            Assert.AreEqual(total, SocialEventHub.totalReceived, "received counts everything accepted");
            Assert.AreEqual(kOverflow, SocialEventHub.totalDropped, "drops are counted, never silent");

            // The survivors are the newest ones: the oldest events fell off the front.
            Assert.AreEqual(kOverflow.ToString(), events[0].message);
            Assert.AreEqual((total - 1).ToString(), events[events.Count - 1].message);
        }

        [Test]
        public void OnEvent_FiresOncePerEventInOrderAfterPublish()
        {
            var seen = new List<string>();
            SocialEventHub.onEvent += e => seen.Add(e.message);

            SocialEventHub.Enqueue(_Chat("a"));
            SocialEventHub.Enqueue(_Chat("b"));
            Assert.IsEmpty(seen, "nothing fires before the frame's list is published");

            var events = SocialEventHub.currentEvents;
            CollectionAssert.AreEqual(new[] { "a", "b" }, seen);

            // A second read in the same frame must not re-fire.
            _ = SocialEventHub.currentEvents;
            CollectionAssert.AreEqual(new[] { "a", "b" }, seen);
            Assert.AreEqual(2, events.Count);
        }

        [Test]
        public void Enqueue_NormalizesMissingFields()
        {
            SocialEventHub.Enqueue(new SocialEvent());

            var e = SocialEventHub.currentEvents[0];
            Assert.AreEqual(string.Empty, e.source);
            Assert.AreEqual(string.Empty, e.type);
            Assert.AreEqual(string.Empty, e.id);
            Assert.AreEqual(string.Empty, e.message);
            Assert.AreEqual(string.Empty, e.currency);
            Assert.IsNotNull(e.user, "consumers read user flags without a null check every frame");
            Assert.IsFalse(e.user.isModerator);
            Assert.IsFalse(e.user.isMember);
            Assert.IsFalse(e.user.isOwner);
        }

        [Test]
        public void Enqueue_NormalizesMissingUserFields()
        {
            // A feeder that sends only role flags leaves id and name absent. Filters index into them
            // every frame, so they have to survive that without a null check.
            SocialEventHub.Enqueue(new SocialEvent { user = new SocialUser { isMember = true } });

            var user = SocialEventHub.currentEvents[0].user;
            Assert.AreEqual(string.Empty, user.id);
            Assert.AreEqual(string.Empty, user.name);
            Assert.IsTrue(user.isMember, "normalization must not clobber what the feeder did send");
        }

        [Test]
        public void Enqueue_TruncatesOverlongMessage()
        {
            SocialEventHub.Enqueue(_Chat(new string('x', SocialEvent.kMaxMessageLength + 100)));

            Assert.AreEqual(SocialEvent.kMaxMessageLength, SocialEventHub.currentEvents[0].message.Length);
        }

        [Test]
        public void Enqueue_KeepsMessageAtTheLimitIntact()
        {
            SocialEventHub.Enqueue(_Chat(new string('x', SocialEvent.kMaxMessageLength)));

            Assert.AreEqual(SocialEvent.kMaxMessageLength, SocialEventHub.currentEvents[0].message.Length);
        }

        [Test]
        public void Enqueue_DoesNotSplitASurrogatePairWhenTruncating()
        {
            // Land an emoji astride the limit: filler up to one unit short, then a 2-unit character.
            string message = new string('x', SocialEvent.kMaxMessageLength - 1) + "\U0001F389";
            SocialEventHub.Enqueue(_Chat(message));

            string truncated = SocialEventHub.currentEvents[0].message;
            Assert.AreEqual(SocialEvent.kMaxMessageLength - 1, truncated.Length,
                "the pair is dropped whole rather than cut in half");
            Assert.IsFalse(char.IsHighSurrogate(truncated[truncated.Length - 1]),
                "a trailing lone surrogate would be an ill-formed string");
        }

        [Test]
        public void OnEvent_SurvivesAThrowingSubscriber()
        {
            var seen = new List<string>();
            SocialEventHub.onEvent += _ => throw new InvalidOperationException("subscriber blew up");
            SocialEventHub.onEvent += e => seen.Add(e.message);

            SocialEventHub.Enqueue(_Chat("a"));
            SocialEventHub.Enqueue(_Chat("b"));

            LogAssert.ignoreFailingMessages = true;
            var events = SocialEventHub.currentEvents;
            LogAssert.ignoreFailingMessages = false;

            // The throw must not abandon the frame: both events still publish, and the exception does
            // not escape into the caller that pumped the hub.
            CollectionAssert.AreEqual(new[] { "a", "b" }, _Messages(events));
            Assert.IsEmpty(seen, "a subscriber after the thrower is skipped for that event (documented limit)");
        }

        [Test]
        public void OnEvent_SubscriberCanEnqueueWithoutDisturbingTheFrame()
        {
            SocialEventHub.onEvent += e =>
            {
                if (e.message == "a") SocialEventHub.Enqueue(_Chat("echo"));
            };

            SocialEventHub.Enqueue(_Chat("a"));
            CollectionAssert.AreEqual(new[] { "a" }, _Messages(SocialEventHub.currentEvents),
                "an event enqueued from a subscriber belongs to the next frame, not this one");

            _frame++;
            CollectionAssert.AreEqual(new[] { "echo" }, _Messages(SocialEventHub.currentEvents));
        }

        [Test]
        public void Publish_GivesEveryEventInAFrameTheSameTime()
        {
            SocialEventHub.Enqueue(_Chat("a"));
            SocialEventHub.Enqueue(_Chat("b"));

            var events = SocialEventHub.currentEvents;
            Assert.AreEqual(events[0].receivedTime, events[1].receivedTime, 0.0,
                "one publish, one timestamp — consumers compare these against a cooldown");
        }

        [Test]
        public void Enqueue_RejectsNullWithoutCounting()
        {
            LogAssert.Expect(LogType.Error, "[Social] Enqueue was given a null event.");
            SocialEventHub.Enqueue(null);

            Assert.AreEqual(0, SocialEventHub.totalReceived);
            Assert.AreEqual(0, SocialEventHub.currentEvents.Count);
        }

        [Test]
        public void Publish_StampsReceivedTime()
        {
            SocialEventHub.Enqueue(_Chat("a"));

            double stamped = SocialEventHub.currentEvents[0].receivedTime;
            Assert.Greater(stamped, 0.0, "the stamp must come from the Unity clock, not stay at its default");
            Assert.AreEqual(Time.unscaledTimeAsDouble, stamped, 0.5);
        }

        [Test]
        public void Enqueue_IsSafeFromWorkerThreads()
        {
            const int kThreads = 4;
            const int kPerThread = 50;

            var threads = new Thread[kThreads];
            for (int t = 0; t < kThreads; t++)
            {
                int id = t;
                threads[t] = new Thread(() =>
                {
                    for (int i = 0; i < kPerThread; i++) SocialEventHub.Enqueue(_Chat($"{id}-{i}"));
                });
                threads[t].Start();
            }
            for (int t = 0; t < kThreads; t++) threads[t].Join();

            Assert.AreEqual(kThreads * kPerThread, SocialEventHub.totalReceived);
            Assert.AreEqual(0, SocialEventHub.totalDropped, "the batch fits inside the queue bound");
            Assert.AreEqual(kThreads * kPerThread, SocialEventHub.currentEvents.Count);
        }

        [Test]
        public void Enqueue_IsSafeWhileTheMainThreadSwapsBuffers()
        {
            // The previous test joins before reading; this one deliberately overlaps intake with the
            // buffer swap, the only moment worker and main thread contend for the same lists.
            const int kTotal = 5000;
            int published = 0;

            var worker = new Thread(() =>
            {
                for (int i = 0; i < kTotal; i++)
                {
                    SocialEventHub.Enqueue(_Chat("x"));
                    if ((i & 63) == 0) Thread.Sleep(0);   // let the main thread get its swaps in
                }
            });
            worker.Start();

            while (worker.IsAlive)
            {
                published += SocialEventHub.currentEvents.Count;
                _frame++;
                Thread.Sleep(0);
            }
            worker.Join();

            // Drain what arrived after the last swap.
            published += SocialEventHub.currentEvents.Count;

            Assert.AreEqual(kTotal, SocialEventHub.totalReceived);
            Assert.AreEqual(kTotal - SocialEventHub.totalDropped, published,
                "every accepted event is either published exactly once or counted as dropped");
            Assert.Greater(published, 0, "the test did not actually overlap intake with swaps");
        }

        [Test]
        public void Reset_ClearsEventsCountersAndSubscribers()
        {
            bool fired = false;
            SocialEventHub.onEvent += _ => fired = true;
            SocialEventHub.Enqueue(_Chat("a"));
            _ = SocialEventHub.currentEvents;
            Assert.IsTrue(fired);

            SocialEventHub.Reset();
            // Reset restores the Unity frame counter, so re-install the test seam as SetUp does.
            SocialEventHub.frameProvider = () => _frame;

            Assert.AreEqual(0, SocialEventHub.totalReceived);
            Assert.AreEqual(0, SocialEventHub.totalDropped);
            Assert.AreEqual(0, SocialEventHub.currentEvents.Count);

            fired = false;
            SocialEventHub.Enqueue(_Chat("b"));
            _frame++;
            _ = SocialEventHub.currentEvents;
            Assert.IsFalse(fired, "subscribers from before the reset must not survive it");
        }
    }
}
