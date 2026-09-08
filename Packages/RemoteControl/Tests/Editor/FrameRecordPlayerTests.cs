// Copyright (c) You-Ri, 2026
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    public class FrameRecordPlayerTests
    {
        private struct Beam
        {
            public float intensity;
        }

        private struct Pose
        {
            public float x;
        }

        [SetUp]
        public void ClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }

        /// <summary>
        /// Puts the live clock back. The gate is process-wide, so a counter clock left behind
        /// here counts pumps for whoever runs next -- and for the editor session after the run,
        /// where it makes the timecode advance at whatever rate the editor happens to tick at.
        /// </summary>
        [TearDown]
        public void ReleaseClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>Records a few frames through the real gate and hands back the bytes.</summary>
        private static byte[] Record(int frames, FrameHeadDelegate producer = null, System.Action beforePump = null)
        {
            var stream = new MemoryStream();
            var recorder = new FrameRecorder();

            if (producer != null) FrameGate.AddFrameHeadHandler(producer);
            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < frames; i++)
                {
                    beforePump?.Invoke();
                    FrameGate.Pump();
                }
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
                if (producer != null) FrameGate.RemoveFrameHeadHandler(producer);
            }

            return stream.ToArray();
        }

        [Test]
        public void Advance_WalksEveryFrameInOrder()
        {
            var bytes = Record(4);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                long previous = -1;
                var frames = 0;

                while (player.Advance())
                {
                    Assert.Greater(player.frameNumber, previous);
                    previous = player.frameNumber;
                    frames++;
                }

                Assert.AreEqual(4, frames);
                Assert.IsTrue(player.atEnd);
            }
        }

        [Test]
        public void State_ComesBackAtTheValueItHadOnThatFrame()
        {
            var intensity = 0f;

            void Producer(ref Frame frame)
            {
                frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = intensity;
                intensity += 1f;
            }

            var bytes = Record(3, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                // The block has to exist before a recording can be played into it.
                var block = player.state.GetOrCreate<Beam>();

                Assert.IsTrue(player.Advance());
                Assert.AreEqual(0f, block[0].value.intensity);

                Assert.IsTrue(player.Advance());
                Assert.AreEqual(1f, block[0].value.intensity);

                Assert.IsTrue(player.Advance());
                Assert.AreEqual(2f, block[0].value.intensity);
            }
        }

        /// <summary>
        /// The whole point of the mask, seen from the object.
        ///
        /// A take made before a member existed says nothing about it, and the bytes in its place are
        /// zero. Writing that zero is not "leaving it at its default" -- a struct default is zero,
        /// and zero is a real value: it empties a name, puts a field of view at nothing, turns an
        /// override whose "none" is minus one into an override with zero. So the members the take
        /// did not carry are not written at all, and what the object already holds survives the
        /// replay.
        /// </summary>
        [Test]
        public void AMemberAddedAfterTheTake_KeepsWhatTheObjectAlreadyHas()
        {
            var name = typeof(Lantern).FullName;
            var mine = StateSchemaRegistry.Find(name);
            Assert.IsNotNull(mine, "the generator declared no description, so there is nothing to test");

            byte[] bytes;
            try
            {
                // The recording is made by a build that had only the first member.
                StateSchemaRegistry.Declare(name,
                    new StateSchema(mine.metaSize, mine.stride, new[] { mine.members[0] }));

                bytes = Record(1, (ref Frame frame) =>
                {
                    ref var element = ref frame.state
                        .GetOrCreate<Lantern.LiveStateBlock>(name).GetOrCreate(7);
                    element.value.intensity = 3f;
                });
            }
            finally
            {
                StateSchemaRegistry.Declare(name, mine);
            }

            LogAssert.Expect(LogType.Warning, new Regex("laid out differently"));

            using var player = new FrameRecordPlayer(new MemoryStream(bytes));
            player.state.GetOrCreate<Lantern.LiveStateBlock>(name);

            Assert.IsTrue(player.Advance());

            var lantern = new Lantern { intensity = 0f, range = 12f };
            var bridge = StateBridgeRegistry.Find(typeof(Lantern));
            Assert.IsNotNull(bridge);
            Assert.IsTrue(bridge.Apply(lantern, 7, player.state, player.symbols));

            Assert.AreEqual(3f, lantern.intensity, "the member the take carried was not put back");
            Assert.AreEqual(12f, lantern.range,
                "a member the take never carried was written with the zero standing in for it");
        }

        /// <summary>Two members of the same width, which is the case a width check cannot see.</summary>
        private struct Lamp
        {
            public float intensity;
            public float range;
        }

        private static StateSchema LampSchema(params StateSchemaMember[] members)
            => new StateSchema(16, 16 + 8, members);

        private static StateSchemaMember Member(string name, int offset)
            => new StateSchemaMember(name, typeof(float).FullName, offset, 4);

        /// <summary>
        /// A member that moved is followed by its name, not by where it used to sit.
        ///
        /// This is the failure a width check cannot see: two builds that disagree about the order of
        /// two members of the same size produce elements that measure alike, so the width passes and
        /// every value lands in the wrong member -- which looks like values rather than like an
        /// error. It used to be refused, which cost the whole type its state. Described member by
        /// member, it does not have to be either.
        /// </summary>
        [Test]
        public void AMemberThatMoved_IsReadBackByItsName()
        {
            StateSchemaRegistry.Declare(typeof(Lamp).FullName,
                LampSchema(Member("intensity", 0), Member("range", 4)));

            byte[] bytes;
            try
            {
                bytes = Record(1, (ref Frame frame) =>
                {
                    ref var element = ref frame.state.GetOrCreate<Lamp>().GetOrCreate(1);
                    element.value.intensity = 5f;
                    element.value.range = 9f;
                });
            }
            finally
            {
                StateSchemaRegistry.Remove(typeof(Lamp).FullName);
            }

            // The same two members, the other way round: what a swap between two builds looks like.
            // The struct itself has not moved, so what lands in the field at offset zero is the
            // member this build calls "range" -- which is the recorded 9, proving the copy followed
            // the name rather than the position.
            StateSchemaRegistry.Declare(typeof(Lamp).FullName,
                LampSchema(Member("range", 0), Member("intensity", 4)));

            try
            {
                using var player = new FrameRecordPlayer(new MemoryStream(bytes));
                var block = player.state.GetOrCreate<Lamp>();

                Assert.IsTrue(player.Advance());
                Assert.AreEqual(1, block.count, "the type was refused instead of being rearranged");
                Assert.AreEqual(9f, block[0].value.intensity, "the member at offset zero is not the one named there");
                Assert.AreEqual(5f, block[0].value.range);
            }
            finally
            {
                StateSchemaRegistry.Remove(typeof(Lamp).FullName);
            }
        }

        /// <summary>
        /// A member added after the take was made is left out of the mask, so nothing writes it.
        ///
        /// The bytes in its place are zero, and zero is a value like any other -- it would empty a
        /// name and put a field of view at nothing. What the object already holds is the only
        /// honest answer, and saying so is the mask's whole job.
        /// </summary>
        [Test]
        public void AMemberTheRecordingNeverCarried_IsLeftOutOfTheMask()
        {
            StateSchemaRegistry.Declare(typeof(Lamp).FullName, LampSchema(Member("intensity", 0)));

            byte[] bytes;
            try
            {
                bytes = Record(1, (ref Frame frame) =>
                    frame.state.GetOrCreate<Lamp>().GetOrCreate(1).value.intensity = 5f);
            }
            finally
            {
                StateSchemaRegistry.Remove(typeof(Lamp).FullName);
            }

            StateSchemaRegistry.Declare(typeof(Lamp).FullName,
                LampSchema(Member("intensity", 0), Member("range", 4)));

            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("laid out differently"));

                using var player = new FrameRecordPlayer(new MemoryStream(bytes));
                var block = player.state.GetOrCreate<Lamp>();

                Assert.IsTrue(player.Advance());
                Assert.AreEqual(5f, block[0].value.intensity, "the member both builds have was not put back");
                Assert.AreEqual(0b01UL, block.appliedMemberMask,
                    "the member the recording never carried is inside the mask, so it would be written");
            }
            finally
            {
                StateSchemaRegistry.Remove(typeof(Lamp).FullName);
            }
        }

        /// <summary>
        /// A producer that hand-registers a struct declares no layout, and neither side claiming
        /// one is a pass. Refusing on the strength of an absent claim would break every such
        /// producer to catch nothing.
        /// </summary>
        [Test]
        public void StateWithNoLayoutDeclared_IsReadAsBefore()
        {
            var bytes = Record(1, (ref Frame frame) =>
                frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = 5f);

            using var player = new FrameRecordPlayer(new MemoryStream(bytes));
            var block = player.state.GetOrCreate<Beam>();

            Assert.IsTrue(player.Advance());
            Assert.AreEqual(5f, block[0].value.intensity);
        }

        [Test]
        public void StateOfAnUnknownType_IsReportedRatherThanSilentlyDropped()
        {
            LogAssert.Expect(LogType.Warning, new Regex("which nothing here holds"));

            void Producer(ref Frame frame)
                => frame.state.GetOrCreate<Pose>().GetOrCreate(1).value.x = 1f;

            var bytes = Record(2, Producer);

            // Stands in for a machine that has never held this type. Recording it here announced it,
            // and an announced type is given a block on playback rather than reported -- which is the
            // point of announcing. Forget it again to get back to the case this test is about.
            StateTypeRegistry.Clear();

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                // No block can be made for Pose: the replay is missing part of the world, and saying
                // so is the difference between a hole and an empty scene nobody questions.
                while (player.Advance()) { }

                CollectionAssert.Contains(player.unknownStateTypes, typeof(Pose).FullName);
            }
        }

        [Test]
        public void StateOfAnAnnouncedType_GetsABlockWithoutOneBeingMadeFirst()
        {
            // The other half, and the one that actually bit: a take carried a pose every frame and
            // the replay showed none of it, because the playing side had a producer for the type but
            // had not happened to publish one yet. Nothing was wrong with the file.
            void Producer(ref Frame frame)
                => frame.state.GetOrCreate<Pose>().GetOrCreate(7).value.x = 3f;

            var bytes = Record(2, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                while (player.Advance()) { }

                CollectionAssert.IsEmpty(player.unknownStateTypes,
                    "an announced type has somewhere to go");

                var block = player.state.Find<Pose>();
                Assert.IsNotNull(block, "the player has to be able to make the block from the name");
                Assert.AreEqual(1, block.count);
                Assert.AreEqual(3f, block[0].value.x);
            }
        }

        [Test]
        public void Inputs_AreHandedOverPerFrameForWhoeverAppliesThem()
        {
            var pending = 0;
            var bytes = Record(3, beforePump: () =>
            {
                var value = (pending++).ToString();
                FrameGate._Enqueue(EventKind.Set, "test", "/live/object/cam/fov", value,
                    () => true);
            });

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                var total = 0;
                while (player.Advance())
                {
                    for (int i = 0; i < player.events.Count; i++)
                    {
                        var record = player.events[i];
                        Assert.AreEqual(EventKind.Set, record.kind);
                        Assert.AreEqual("/live/object/cam/fov", player.Resolve(record.targetId));
                        Assert.AreEqual("test", player.Resolve(record.sourceId));
                        total++;
                    }
                }

                Assert.AreEqual(3, total);
            }
        }

        /// <summary>
        /// A take made before EventKind lost StructureChange (2) on 2026-09-08 still holds that
        /// value, and the reader is the one point that knows what it meant.
        ///
        /// Written as a raw cast rather than patched into the bytes afterwards: a C# enum is not a
        /// closed set, so the writer takes (EventKind)2 and puts it on disk exactly as a build from
        /// before the change did.
        /// </summary>
        [Test]
        public void ARetiredKind_ComesBackAsTheKindItBehavedLike()
        {
            var written = false;
            var bytes = Record(2, beforePump: () =>
            {
                if (written) return;

                written = true;
                FrameGate._Enqueue((EventKind)2, "test", "/live/scene/export", "{}", () => true);
            });

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                Assert.IsTrue(player.Advance());
                Assert.AreEqual(1, player.events.Count);
                Assert.AreEqual(EventKind.Call, player.events[0].kind,
                    "2 was StructureChange, and five of its six routes were calls");
                Assert.AreEqual("/live/scene/export", player.Resolve(player.events[0].targetId));
            }
        }

        /// <summary>
        /// Nothing ever wrote 3 (RegisteredSource), but a reader still has to land an unknown value
        /// on a kind the rest of the code has a case for rather than pass it through.
        /// </summary>
        [Test]
        public void AKindNoBuildEverWrote_ComesBackAsSomethingReadable()
        {
            var written = false;
            var bytes = Record(2, beforePump: () =>
            {
                if (written) return;

                written = true;
                FrameGate._Enqueue((EventKind)3, "test", "/live/a", "1", () => true);
            });

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                Assert.IsTrue(player.Advance());
                Assert.AreEqual(1, player.events.Count);
                Assert.AreEqual(EventKind.Set, player.events[0].kind);
            }
        }

        [Test]
        public void Inputs_DoNotLeakFromOneFrameToTheNext()
        {
            var written = false;
            var bytes = Record(3, beforePump: () =>
            {
                if (written) return;

                written = true;
                FrameGate._Enqueue(EventKind.Set, "test", "/live/a", "1", () => true);
            });

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                Assert.IsTrue(player.Advance());
                Assert.AreEqual(1, player.events.Count);

                Assert.IsTrue(player.Advance());
                Assert.AreEqual(0, player.events.Count, "a frame with no events has none");
            }
        }

        [Test]
        public void Seek_LandsOnTheFrameAndResolvesIdsFromTheTail()
        {
            var intensity = 0f;

            void Producer(ref Frame frame)
            {
                frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = intensity;
                intensity += 1f;
            }

            var bytes = Record(6, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                var block = player.state.GetOrCreate<Beam>();
                Assert.IsTrue(player.canSeek);

                Assert.IsTrue(player.TrySeek(4));
                Assert.AreEqual(4, player.frameNumber);
                Assert.AreEqual(4f, block[0].value.intensity);

                // Backwards too: state is whatever that frame carried, not what came before it.
                Assert.IsTrue(player.TrySeek(1));
                Assert.AreEqual(1f, block[0].value.intensity);

                // A jump skips the entries that name the ids, so the state landing at all is proof
                // the table came from the tail instead.
                Assert.AreEqual(1, block.count);
            }
        }

        [Test]
        public void Seek_WithoutATail_IsRefusedRatherThanGuessed()
        {
            var stream = new MemoryStream();
            var recorder = new FrameRecorder();
            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            for (int i = 0; i < 3; i++) FrameGate.Pump();

            FrameGate.sink = null;

            using (var player = new FrameRecordPlayer(new MemoryStream(stream.ToArray())))
            {
                Assert.IsFalse(player.canSeek);
                Assert.IsFalse(player.TrySeek(1));

                // Walking still works, which is the point of the entries carrying their own length.
                var frames = 0;
                while (player.Advance()) frames++;
                Assert.AreEqual(3, frames);
            }
        }

        [Test]
        public void Structure_IsReconciledSoASpawnDisappearsWhenScrubbedBackPast()
        {
            var frames = 0;

            void Producer(ref Frame frame)
            {
                frame.structure.AddOrUpdate(1, 10, FrameSymbolTable.kNone);

                // Something is spawned partway through the run.
                if (frames >= 2) frame.structure.AddOrUpdate(2, 10, FrameSymbolTable.kNone);
                frames++;
            }

            var bytes = Record(4, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                while (player.Advance()) { }
                Assert.AreEqual(2, player.structure.count, "both objects exist by the end");

                // Scrubbing back past the spawn has to take it away again. Applying the inventory as
                // an assignment would leave it standing, which is the failure this guards.
                Assert.IsTrue(player.TrySeek(0));
                Assert.AreEqual(1, player.structure.count);
                Assert.AreEqual(1, player.structure[0].id);
            }
        }

        [Test]
        public void Rewind_DropsWhatWasBuiltUp()
        {
            void Producer(ref Frame frame)
            {
                frame.structure.AddOrUpdate(1, 10, FrameSymbolTable.kNone);
                frame.state.GetOrCreate<Beam>().GetOrCreate(1).value.intensity = 5f;
            }

            var bytes = Record(2, Producer);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                player.state.GetOrCreate<Beam>();
                while (player.Advance()) { }

                Assert.AreEqual(1, player.structure.count);

                player.Rewind();

                Assert.AreEqual(0, player.structure.count);
                Assert.AreEqual(-1, player.frameNumber);
                Assert.IsFalse(player.atEnd);
            }
        }

        [Test]
        public void Header_TellsWhichBuildTheRecordingCameFrom()
        {
            var bytes = Record(1);

            using (var player = new FrameRecordPlayer(new MemoryStream(bytes)))
            {
                StringAssert.StartsWith("unity-", player.header.engineId);
                Assert.IsNotEmpty(player.header.buildId);
            }
        }
    }
}
