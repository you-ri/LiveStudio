// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Unity.Collections.LowLevel.Unsafe;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// An event records its value as bytes rather than as the text that carried it.
    ///
    /// A slider sends the same property sixty times a second. Kept as digits, every one of those
    /// costs a parse to read back and loses the last bits on the way; kept as bytes it is the same
    /// memory the property holds, and a viewer can walk it with the machinery it walks state with.
    ///
    /// The value kept is the one the write asked for. Reading it back out of the property instead
    /// looks equivalent and is not: a getter is free to be a view over something the write only
    /// starts, and one caught mid-reconcile reports the value that is on its way out.
    /// </summary>
    [TestFixture]
    public class FramePayloadTests
    {
        [SetUp]
        public void StartClean()
        {
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }

        [TearDown]
        public void Finish()
        {
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        private enum Facing
        {
            Left = 0,
            Right = 7,
        }

        // ---- EventPayload: laying values out ----

        [Test]
        public void AFloat_SurvivesTheRoundTripExactly()
        {
            // 0.1f has no exact decimal form, which is the case digits get wrong and bytes do not.
            const float value = 0.1f;

            Span<byte> bytes = stackalloc byte[16];
            Assert.IsTrue(EventPayload.TryPack(typeof(float), value, bytes, out var written));
            Assert.AreEqual(4, written, "a float is four bytes, not four characters");

            Assert.IsTrue(EventPayload.TryUnpack(typeof(float), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (float)read, 0f, "exactly, not nearly");
        }

        [Test]
        public void AStructOfFloats_IsItsOwnWidth()
        {
            var value = new Vector3(1f, -2.5f, 3.25f);

            Span<byte> bytes = stackalloc byte[16];
            Assert.IsTrue(EventPayload.TryPack(typeof(Vector3), value, bytes, out var written));
            Assert.AreEqual(12, written);

            Assert.IsTrue(EventPayload.TryUnpack(typeof(Vector3), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (Vector3)read);
        }

        [Test]
        public void AKnownType_IsWrittenWithoutGoingThroughObject()
        {
            // The same bytes the boxed path lays down, for a caller that knows the type.
            var value = new Vector3(1f, -2.5f, 3.25f);

            Span<byte> bytes = stackalloc byte[12];
            EventPayload.Write(in value, bytes);

            Assert.IsTrue(EventPayload.TryUnpack(typeof(Vector3), bytes, out var read));
            Assert.AreEqual(value, (Vector3)read);
        }

        [Test]
        public void ABool_IsOneByte_NotTheInteropFour()
        {
            // Marshal would say four. What is written is what the type occupies in memory, because
            // that is what the reader lays back out.
            Assert.AreEqual(1, EventPayload.SizeOf(typeof(bool)));

            Span<byte> bytes = stackalloc byte[16];
            Assert.IsTrue(EventPayload.TryPack(typeof(bool), true, bytes, out var written));
            Assert.AreEqual(1, written);

            Assert.IsTrue(EventPayload.TryUnpack(typeof(bool), bytes.Slice(0, written), out var read));
            Assert.IsTrue((bool)read);
        }

        [Test]
        public void AnEnum_KeepsItsValue_NotItsName()
        {
            Span<byte> bytes = stackalloc byte[16];
            Assert.IsTrue(EventPayload.TryPack(typeof(Facing), Facing.Right, bytes, out var written));

            Assert.IsTrue(EventPayload.TryUnpack(typeof(Facing), bytes.Slice(0, written), out var read));
            Assert.AreEqual(Facing.Right, (Facing)read);
        }

        [Test]
        public void AString_IsItsUtf8_AndTheRecordSaysHowLong()
        {
            // No prefix and no terminator: the record carries the length, so the bytes are the text
            // and nothing else.
            Assert.AreEqual(6, EventPayload.ByteCountOf("あい"));

            Span<byte> bytes = stackalloc byte[16];
            var written = EventPayload.WriteString("あい", bytes);

            Assert.AreEqual(6, written);
            Assert.AreEqual("あい", EventPayload.ReadString(bytes.Slice(0, written)));
        }

        [Test]
        public void AnEmptyString_IsNoBytes_AndReadsBackEmpty()
        {
            Assert.AreEqual(0, EventPayload.ByteCountOf(string.Empty));
            Assert.AreEqual(string.Empty, EventPayload.ReadString(ReadOnlySpan<byte>.Empty));
        }

        [Test]
        public void APropertyThatDeclaresAMaximum_PacksAtItsOwnWidth()
        {
            // A FixedString is unmanaged, so it packs at its own width like any other value and its
            // characters stay in the record.
            var value = new Unity.Collections.FixedString32Bytes("ai");

            Assert.AreEqual(32, EventPayload.SizeOf(typeof(Unity.Collections.FixedString32Bytes)));

            Span<byte> bytes = stackalloc byte[64];
            Assert.IsTrue(EventPayload.TryPack(
                typeof(Unity.Collections.FixedString32Bytes), value, bytes, out var written));
            Assert.AreEqual(32, written);

            Assert.IsTrue(EventPayload.TryUnpack(
                typeof(Unity.Collections.FixedString32Bytes), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (Unity.Collections.FixedString32Bytes)read);
        }

        [Test]
        public void ATypeThisBuildDoesNotHave_ResolvesToNothingRatherThanThrowing()
        {
            // A recording can name a type that has since been removed. Refusing to read the rest of
            // the file over that would be worse than saying which one is missing.
            Assert.IsNull(EventPayload.Resolve("Nowhere.NoSuchType"));
        }

        // ---- The record ----

        [Test]
        public void AWriteThatSaysWhatItApplied_RecordsTheValue_NotTheRequest()
        {
            const string target = "/live/object/cam/fov";

            var task = FrameGate._Enqueue(EventKind.Set, "test", target, "35.0",
                () =>
                {
                    // Stands in for the property write: by this point the target has been resolved,
                    // so its type is known and the value that landed can be read back.
                    FrameGate.StampAppliedPayload(target, typeof(float), 35f);
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();
            Assert.IsTrue(task.IsCompleted);

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual("System.Single", FrameGate.symbols.Resolve(record.payloadTypeId));
            Assert.AreEqual(4, record.payloadLength, "not the four characters of \"35.0\"");
            Assert.AreEqual(35f, BitConverter.ToSingle(frame.PayloadOf(in record).ToArray(), 0), 0f);
        }

        [Test]
        public void AKnownTypeStamp_RecordsTheSameValue()
        {
            const string target = "/live/object/cam/fov";

            FrameGate._Enqueue(EventKind.Set, "test", target, "35.0",
                () =>
                {
                    FrameGate.StampAppliedPayload(target, 35f);
                    return true;
                });

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual("System.Single", FrameGate.symbols.Resolve(record.payloadTypeId));
            Assert.AreEqual(35f, BitConverter.ToSingle(frame.PayloadOf(in record).ToArray(), 0), 0f);
        }

        [LiveClass("DeferredViewSubject")]
        public class DeferredViewSubject
        {
            public string requested = string.Empty;

            /// <summary>What the getter reports until something else catches up.</summary>
            public string reported = "old";

            // A view over state held elsewhere: nothing saves it, so it is off the frame unless it
            // says otherwise (FrameLaneRules). Said here, as ExternalAvatarSource.selectedAvatar
            // says it in production -- the record this test reads exists only because of it.
            [LiveProperty(lane = FrameLane.Event)]
            public string selection
            {
                get => reported;
                set => requested = value;
            }
        }

        [Test]
        public void AWriteToAViewThatHasNotCaughtUp_RecordsWhatWasAsked()
        {
            // The shape that produced a wrong recording: selecting an avatar raises the chosen asset
            // and leaves turning the old one off to a reconcile that runs later, so the getter still
            // named the previous avatar when the write reached it. Read back, the record said the
            // avatar had been re-selected; replayed, it put the wrong one back.
            LiveClass.RegisterFromAttributes<DeferredViewSubject>();

            var subject = new DeferredViewSubject();
            var handle = LiveObjectRegistry.Create(typeof(DeferredViewSubject), subject, "deferred-view");

            const string target = "/live/object/deferred-view/selection";
            const string body = "{\"value\":\"new\"}";

            try
            {
                FrameGate._Enqueue(EventKind.Set, "test", target, body,
                    () => LiveObjectHandler.ApplyRecordedOperation(
                        null, DefaultLiveObjectResolver.Instance, "PUT", target, body, out _, out _),
                    verb: "PUT");

                FrameGate.Pump();

                Assert.AreEqual("new", subject.requested, "the write did not reach the property");
                Assert.AreEqual("old", subject.reported, "the view is meant to be stale here");

                using var frame = new EventFrame();
                Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

                var record = frame[0];
                Assert.AreEqual(EventPayload.kStringTypeName, FrameGate.symbols.Resolve(record.payloadTypeId));
                Assert.AreEqual("new", EventPayload.ReadString(frame.PayloadOf(in record)),
                    "the record kept what the getter still reported rather than what was asked for");
            }
            finally
            {
                handle?.Unregister();
            }
        }

        [Test]
        public void AStringProperty_RecordsItsValue_NotTheJsonThatCarriedIt()
        {
            const string target = "/live/object/avatar/name";

            FrameGate._Enqueue(EventKind.Set, "test", target, "{\"value\":\"ai\"}",
                () =>
                {
                    FrameGate.StampAppliedPayload(target, typeof(string), "ai");
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual(EventPayload.kStringTypeName, FrameGate.symbols.Resolve(record.payloadTypeId));
            Assert.AreEqual("ai", EventPayload.ReadString(frame.PayloadOf(in record)), "the value, not its JSON form");
        }

        [Test]
        public void AWriteThatSaysNothing_KeepsTheRequestTextAsItsPayload()
        {
            // The fallback that makes the typed path safe to leave out: a target whose type has no
            // layout still records something a replay can use.
            const string target = "/live/object/avatar/name";

            FrameGate._Enqueue(EventKind.Set, "test", target, "\"ai\"", () => true);
            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual(EventPayload.kRequestTypeName, FrameGate.symbols.Resolve(record.payloadTypeId));
            Assert.AreEqual("\"ai\"", EventPayload.ReadString(frame.PayloadOf(in record)));
        }

        [Test]
        public void StampingOutsideAFrameHead_DoesNothing()
        {
            // The write path is reachable without the gate. It must not have to know that.
            Assert.DoesNotThrow(
                () => FrameGate.StampAppliedPayload("/live/object/cam/fov", typeof(float), 1f));
            Assert.DoesNotThrow(() => FrameGate.StampAppliedPayload("/live/object/cam/fov", 1f));
        }

        [Test]
        public void AValueThatReplacesTheRequestText_IsWhatTheRecordCarries()
        {
            const string target = "/live/object/cam/fov";

            FrameGate._Enqueue(EventKind.Set, "test", target, new string('x', 4000),
                () =>
                {
                    FrameGate.StampAppliedPayload(target, typeof(float), 12f);
                    return true;
                });

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(4, frame[0].payloadLength, "the stamped value, not the request it replaced");
            Assert.AreEqual(12f, BitConverter.ToSingle(frame.PayloadAt(0).ToArray(), 0), 0f);
        }

        // ---- What the lanes are allowed to carry ----

        [Test]
        public void EverythingTheLanesCarry_IsUnmanaged()
        {
            // The native lists already refuse anything else at compile time, so this is here to say
            // it out loud: a managed field added to one of these would be caught as a build error
            // with no explanation of why it is not allowed.
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<EventRecord>(), "the evt lane");
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<ObjectEntry>(), "the structure lane");
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<StateElement<float>>(), "the state lane");
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<FrameSource>(), "carried by every state element");
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<Timecode>());
            Assert.IsTrue(UnsafeUtility.IsUnmanaged<FrameRate>());
        }

        [Test]
        public void ARecord_IsAHandfulOfIds()
        {
            // The value lives in the frame's arena, so a record is its bookkeeping and nothing else.
            // One that grew past this would be carrying something it should not.
            Assert.LessOrEqual(UnsafeUtility.SizeOf<EventRecord>(), 48);
        }

        // ---- Through the file ----

        [Test]
        public void ATypedPayload_ComesBackOutOfARecordingAsTheSameBytes()
        {
            var stream = new MemoryStream();
            var symbols = new FrameSymbolTable();

            using (var events = new EventFrame())
            using (var writer = new FrameRecordWriter(
                stream,
                new FrameRecordHeader { frameRate = FrameRate.FPS60, engineId = "unity", buildId = "test" },
                leaveOpen: true))
            {
                events.Reset(0, FrameRate.FPS60);

                var record = new EventRecord(1, EventKind.Set, symbols.Intern("rest"),
                    symbols.Intern("/live/object/cam/fov"), EventFlags.None, symbols.Intern("PUT"))
                {
                    payloadTypeId = symbols.Intern("System.Single"),
                };

                Span<byte> packed = stackalloc byte[4];
                EventPayload.Write(35f, packed);
                events.Add(in record, packed);

                var frame = new Frame { frameNumber = 0, frameRate = FrameRate.FPS60, events = events };
                writer.BeginFrame(in frame, symbols);
                writer.WriteEvents(events, symbols);
                writer.EndFrame();
                writer.Close(symbols);
            }

            stream.Position = 0;

            using (var player = new FrameRecordPlayer(stream))
            {
                Assert.IsTrue(player.Advance());
                Assert.AreEqual(1, player.events.eventCount);

                var record = player.events[0];
                Assert.AreEqual("System.Single", player.Resolve(record.payloadTypeId));
                Assert.AreEqual(35f, BitConverter.ToSingle(player.events.PayloadOf(in record).ToArray(), 0), 0f);
            }
        }
    }
}
