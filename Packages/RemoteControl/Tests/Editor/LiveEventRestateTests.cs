// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Values restated into a keyframe.
    ///
    /// The event lane records changes, so a value settled before the recording started appears in
    /// the file nowhere at all -- a replay of it keeps whatever the machine was already holding, and
    /// no work on the replay side can recover what was never written down. These cover the other
    /// half: each keyframe also carries the values as they stand, addressed exactly as a live write
    /// to them would be.
    /// </summary>
    public class LiveEventRestateTests
    {
        private const string kId = "restate-subject";

        [LiveClass("RestateSubject")]
        public class RestateSubject
        {
            [LiveField] public string title = string.Empty;

            [LiveField] public float weight;

            /// <summary>Off the live data: the kind of thing a spare machine differs on.</summary>
            [LiveField(lane = FrameLane.None)] public int windowX;

            /// <summary>An application result. Restating one would be injecting the answer.</summary>
            [LiveProperty] public string derived => title + "!";

            public int writes;

            private float _guarded;

            [LiveProperty]
            public float guarded
            {
                get => _guarded;
                set { writes++; _guarded = value; }
            }
        }

        /// <summary>An exposed component, reached through the GameObject that owns it.</summary>
        [LiveClass("RestateComponent")]
        public class RestateComponent : MonoBehaviour
        {
            [LiveField] public string label = string.Empty;
        }

        /// <summary>What one call handed the applier, kept past the call.</summary>
        private readonly struct Applied
        {
            public readonly string target;
            public readonly string source;
            public readonly string text;
            public readonly string payloadTypeName;
            public readonly byte[] payload;
            public readonly bool reemitted;

            public Applied(in ReplayEvent evt)
            {
                target = evt.target;
                source = evt.source;
                text = evt.payloadIsString ? evt.text : null;
                payloadTypeName = evt.payloadTypeName;
                payload = evt.payload.ToArray();
                reemitted = evt.reemitted;
            }
        }

        private sealed class CollectingApplier : IEventApplier
        {
            public readonly List<Applied> applied = new List<Applied>();

            public bool Apply(in ReplayEvent evt, out string error)
            {
                error = null;
                applied.Add(new Applied(in evt));
                return true;
            }
        }

        private RestateSubject _subject;
        private LiveObjectHandle? _handle;

        [SetUp]
        public void SetUp()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            _subject = new RestateSubject();
            _handle = LiveObjectRegistry.Create(typeof(RestateSubject), _subject, kId);
        }

        [TearDown]
        public void TearDown()
        {
            _handle?.Unregister();
            _handle = null;
            _subject = null;

            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>Records one frame with nothing submitted, so all it can hold is the restatement.</summary>
        private static byte[] RecordOneFrame() => RecordFrames(1);

        private static byte[] RecordFrames(int count)
        {
            var stream = new MemoryStream();
            var recorder = new FrameRecorder();

            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < count; i++) FrameGate.Pump();
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
            }

            return stream.ToArray();
        }

        /// <summary>
        /// Everything the first frame of a recording hands an applier. The world this runs in has
        /// live objects of its own, so tests look for their own targets rather than counting.
        /// </summary>
        private static List<Applied> ReplayFirstFrame(byte[] bytes)
        {
            var applier = new CollectingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                replayer.Advance();
            }

            return applier.applied;
        }

        private static Applied? Find(List<Applied> applied, string member)
        {
            var target = "/live/object/" + kId + "/" + member;
            for (int i = 0; i < applied.Count; i++)
            {
                if (applied[i].target == target) return applied[i];
            }

            return null;
        }

        [Test]
        public void AValueSetBeforeRecording_IsInTheFirstKeyframe()
        {
            _subject.title = "before";
            _subject.weight = 0.25f;

            var applied = ReplayFirstFrame(RecordOneFrame());

            var title = Find(applied, "title");
            Assert.IsTrue(title.HasValue, "the string member was not restated");
            Assert.AreEqual("before", title.Value.text);

            var weight = Find(applied, "weight");
            Assert.IsTrue(weight.HasValue, "the float member was not restated");
            Assert.IsTrue(EventPayload.TryUnpack(typeof(float), weight.Value.payload, out var value));
            Assert.AreEqual(0.25f, (float)value, 1e-6f);
        }

        [Test]
        public void ARestatedRecord_SaysItIsOne()
        {
            _subject.title = "before";

            var title = Find(ReplayFirstFrame(RecordOneFrame()), "title");

            Assert.IsTrue(title.HasValue);
            Assert.IsTrue(title.Value.reemitted, "a restated value is not marked as one");
            Assert.AreEqual(LiveEventRestateSystem.kSourceName, title.Value.source);
        }

        [Test]
        public void AMemberOffTheLane_OrOneThatCannotBeWritten_IsNotRestated()
        {
            _subject.windowX = 12;

            var applied = ReplayFirstFrame(RecordOneFrame());

            Assert.IsFalse(Find(applied, "windowX").HasValue,
                "a member declared FrameLane.None was carried anyway");
            Assert.IsFalse(Find(applied, "derived").HasValue,
                "a read-only member was restated, which injects the application's own result");
        }

        [Test]
        public void TurningItOff_LeavesTheKeyframeAsItWas()
        {
            _subject.title = "before";

            var stream = new MemoryStream();
            var recorder = new FrameRecorder { restateValues = false };

            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;
            try
            {
                FrameGate.Pump();
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
            }

            Assert.IsFalse(Find(ReplayFirstFrame(stream.ToArray()), "title").HasValue);
        }

        [Test]
        public void ARestatementThatMatches_DoesNotWriteAgain()
        {
            _subject.guarded = 0.5f;
            _subject.writes = 0;

            var applied = LiveObjectHandler.ApplyRecordedValue(
                null, DefaultLiveObjectResolver.Instance,
                "/live/object/" + kId + "/guarded", 0.5f,
                out var status, out var error, skipIfUnchanged: true);

            Assert.IsTrue(applied, $"the restatement was refused: {status} {error}");
            Assert.AreEqual(0, _subject.writes,
                "a restated value the world already holds went through the setter");
        }

        [Test]
        public void ARestatementThatDiffers_Writes()
        {
            _subject.guarded = 0.5f;
            _subject.writes = 0;

            var applied = LiveObjectHandler.ApplyRecordedValue(
                null, DefaultLiveObjectResolver.Instance,
                "/live/object/" + kId + "/guarded", 0.75f,
                out var status, out var error, skipIfUnchanged: true);

            Assert.IsTrue(applied, $"the restatement was refused: {status} {error}");
            Assert.AreEqual(1, _subject.writes);
            Assert.AreEqual(0.75f, _subject.guarded, 1e-6f);
        }

        /// <summary>
        /// The point of the whole thing: a scrub lands somewhere in the middle of a take and the
        /// world has to look the way it looked there, including the parts nobody touched during it.
        /// </summary>
        [Test]
        public void SeekingIntoTheMiddle_FindsTheRestatedValue()
        {
            _subject.title = "before";

            var bytes = RecordFrames(5);

            var applier = new CollectingApplier();
            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                Assert.IsTrue(replayer.TrySeek(replayer.player.firstFrameNumber + 3));

                var title = Find(applier.applied, "title");
                Assert.IsTrue(title.HasValue,
                    "seeking past the keyframe lost the value it restated");
                Assert.AreEqual("before", title.Value.text);
            }
        }

        /// <summary>
        /// An exposed component has two ways in: through the GameObject that owns it
        /// (<c>components/2/selectedAvatar</c>, which is what every client actually uses) and by its
        /// own type name. A restatement written under the second one is at an address no live write
        /// ever used, so the fold never puts the two together and a seek applies both in whatever
        /// order they happen to be in -- which is how a recorded avatar switch came back as "no
        /// avatar" while the real write sat in the same file, unread.
        /// </summary>
        [Test]
        public void AnExposedComponent_IsRestatedAtTheAddressAWriteToItUses()
        {
            // The proxy's `components` getter filters by LiveClass.Has, which does not register on
            // demand. The app registers every exposed class at startup; a test has to say so.
            LiveClass.RegisterFromAttributes<RestateComponent>();
            LiveClass.RegisterFromAttributes<LiveGameObject>();

            var host = new GameObject("restate-host");
            var component = host.AddComponent<RestateComponent>();
            component.label = "set-before";

            var proxy = new LiveGameObject(host);
            var hostHandle = new LiveObjectHandle("restate-host", LiveClass.Get(typeof(LiveGameObject)), proxy);

            try
            {
                var applied = ReplayFirstFrame(RecordOneFrame());

                // Addressed by id and suffix rather than by suffix alone: the registry is process-wide
                // and holds whatever else the run has registered, some of it with members of the same
                // name.
                string found = null;
                var byTypeName = false;
                for (int i = 0; i < applied.Count; i++)
                {
                    var target = applied[i].target;
                    if (target == null || !target.EndsWith("/label")) continue;

                    if (target.StartsWith("/live/object/restate-host/"))
                    {
                        found = target;
                        Assert.AreEqual("set-before", applied[i].text);
                    }
                    else if (target == "/live/object/RestateComponent/label")
                    {
                        byTypeName = true;
                    }
                }

                Assert.IsFalse(byTypeName,
                    "restated under its own type name, an address no live write uses");
                Assert.IsNotNull(found, "the component's member was not restated through its owner");
                StringAssert.StartsWith("/live/object/restate-host/components/", found);
            }
            finally
            {
                hostHandle.Unregister();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// A real write is never skipped for matching: it happened, and a setter is entitled to run
        /// its side effects again.
        /// </summary>
        [Test]
        public void ARealWriteThatMatches_StillWrites()
        {
            _subject.guarded = 0.5f;
            _subject.writes = 0;

            LiveObjectHandler.ApplyRecordedValue(
                null, DefaultLiveObjectResolver.Instance,
                "/live/object/" + kId + "/guarded", 0.5f,
                out _, out _);

            Assert.AreEqual(1, _subject.writes);
        }
    }
}
