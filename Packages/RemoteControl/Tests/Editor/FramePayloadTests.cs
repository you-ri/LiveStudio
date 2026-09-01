// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using System.Text;
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

            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            Assert.IsTrue(EventPayload.TryPack(typeof(float), value, bytes, out var written));
            Assert.AreEqual(4, written, "a float is four bytes, not four characters");

            Assert.IsTrue(EventPayload.TryUnpack(typeof(float), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (float)read, 0f, "exactly, not nearly");
        }

        [Test]
        public void AStructOfFloats_IsItsOwnWidth()
        {
            var value = new Vector3(1f, -2.5f, 3.25f);

            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            Assert.IsTrue(EventPayload.TryPack(typeof(Vector3), value, bytes, out var written));
            Assert.AreEqual(12, written);

            Assert.IsTrue(EventPayload.TryUnpack(typeof(Vector3), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (Vector3)read);
        }

        [Test]
        public void ABool_IsOneByte_NotTheInteropFour()
        {
            // Marshal would say four. What is written is what the type occupies in memory, because
            // that is what the reader lays back out.
            Assert.AreEqual(1, EventPayload.SizeOf(typeof(bool)));

            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            Assert.IsTrue(EventPayload.TryPack(typeof(bool), true, bytes, out var written));
            Assert.AreEqual(1, written);

            Assert.IsTrue(EventPayload.TryUnpack(typeof(bool), bytes.Slice(0, written), out var read));
            Assert.IsTrue((bool)read);
        }

        [Test]
        public void AnEnum_KeepsItsValue_NotItsName()
        {
            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            Assert.IsTrue(EventPayload.TryPack(typeof(Facing), Facing.Right, bytes, out var written));

            Assert.IsTrue(EventPayload.TryUnpack(typeof(Facing), bytes.Slice(0, written), out var read));
            Assert.AreEqual(Facing.Right, (Facing)read);
        }

        [Test]
        public void AString_SaysItsOwnLengthBeforeItsCharacters()
        {
            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            Assert.IsTrue(EventPayload.TryWriteString("あい", bytes, out var written));

            // Two bytes of length, then six of UTF-8: no terminator to scan for.
            Assert.AreEqual(EventPayload.kLengthPrefixSize + 6, written);
            Assert.AreEqual(6, BitConverter.ToUInt16(bytes.Slice(0, 2).ToArray(), 0));

            Assert.AreEqual("あい", EventPayload.ReadString(bytes.Slice(0, written)));
        }

        [Test]
        public void AnEmptyString_IsStillAStringRatherThanNothing()
        {
            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            Assert.IsTrue(EventPayload.TryWriteString(string.Empty, bytes, out var written));

            Assert.AreEqual(EventPayload.kLengthPrefixSize, written);
            Assert.AreEqual(string.Empty, EventPayload.ReadString(bytes.Slice(0, written)));
        }

        [Test]
        public void AStringLongerThanTheRecord_IsCutOnACharacterBoundary()
        {
            // Kept readable rather than ending in half a rune: what survives is still the value,
            // just less of it.
            var text = new string('あ', EventRecord.kPayloadCapacity);

            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            Assert.IsFalse(EventPayload.TryWriteString(text, bytes, out var written));

            var read = EventPayload.ReadString(bytes.Slice(0, written));
            Assert.Less(read.Length, text.Length);
            StringAssert.StartsWith(read, text);
        }

        [Test]
        public void APrefixLongerThanWhatIsThere_ReadsOnlyWhatIsThere()
        {
            // A file cut off mid-record would otherwise have its last string read past its own end.
            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
            EventPayload.TryWriteString("abcdef", bytes, out var written);

            Assert.AreEqual("abc", EventPayload.ReadString(bytes.Slice(0, written - 3)));
        }

        [Test]
        public void APropertyThatDeclaresAMaximum_NeedsNoneOfThat()
        {
            // A FixedString is unmanaged, so it packs at its own width like any other value and its
            // characters stay in the record. That is what declaring a bound buys.
            var value = new Unity.Collections.FixedString32Bytes("ai");

            Assert.AreEqual(32, EventPayload.SizeOf(typeof(Unity.Collections.FixedString32Bytes)));

            Span<byte> bytes = stackalloc byte[EventRecord.kPayloadCapacity];
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

            var task = FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "35.0",
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
            Assert.AreEqual(4, record.payloadLength, "not the six characters of \"35.0\"");

            var bytes = new byte[record.payloadLength];
            record.CopyPayloadTo(bytes);
            Assert.AreEqual(35f, BitConverter.ToSingle(bytes, 0), 0f);
        }

        [LiveClass("DeferredViewSubject")]
        public class DeferredViewSubject
        {
            public string requested = string.Empty;

            /// <summary>What the getter reports until something else catches up.</summary>
            public string reported = "old";

            [LiveProperty]
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
                FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, body,
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

                var bytes = new byte[record.payloadLength];
                record.CopyPayloadTo(bytes);
                Assert.AreEqual("new", EventPayload.ReadString(bytes),
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

            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "{\"value\":\"ai\"}",
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

            var bytes = new byte[record.payloadLength];
            record.CopyPayloadTo(bytes);
            Assert.AreEqual("ai", EventPayload.ReadString(bytes), "the value, not its JSON form");
        }

        [Test]
        public void AWriteThatSaysNothing_KeepsTheRequestTextAsItsPayload()
        {
            // The fallback that makes the typed path safe to leave out: a target whose type has no
            // layout still records something a replay can use.
            const string target = "/live/object/avatar/name";

            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, "\"ai\"", () => true);
            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual(EventPayload.kRequestTypeName, FrameGate.symbols.Resolve(record.payloadTypeId));

            var bytes = new byte[record.payloadLength];
            record.CopyPayloadTo(bytes);
            Assert.AreEqual("\"ai\"", EventPayload.ReadString(bytes));
        }

        [Test]
        public void StampingOutsideAFrameHead_DoesNothing()
        {
            // The write path is reachable without the gate. It must not have to know that.
            Assert.DoesNotThrow(
                () => FrameGate.StampAppliedPayload("/live/object/cam/fov", typeof(float), 1f));
        }

        [Test]
        public void AValueThatReplacesCutShortText_IsNoLongerMarkedCutShort()
        {
            const string target = "/live/object/cam/fov";

            FrameGate._Enqueue(EventKind.PropertyWrite, "test", target, new string('x', 4000),
                () =>
                {
                    FrameGate.StampAppliedPayload(target, typeof(float), 12f);
                    return true;
                });

            FrameGate.Pump();

            using var frame = new EventFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.IsFalse(frame[0].payloadTruncated,
                "the value that replaced the text was not cut short, so the mark would be a lie");
        }

        // ---- What the lanes are allowed to carry ----

        [Test]
        public void EverythingTheLanesCarry_IsUnmanaged()
        {
            // NativeArray<T> already refuses anything else at compile time, so this is here to say
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
        public void ARecord_CostsItsPayloadAndLittleElse()
        {
            // Guards against a field creeping in beside the payload: the bookkeeping is a handful
            // of ids, and a record that grew past that would be carrying something it should not.
            var size = UnsafeUtility.SizeOf<EventRecord>();

            Assert.GreaterOrEqual(size, EventRecord.kPayloadCapacity);
            Assert.LessOrEqual(size, EventRecord.kPayloadCapacity + 64,
                "the payload is what a record is for; everything else is ids");
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

                var record = new EventRecord(1, EventKind.PropertyWrite, symbols.Intern("rest"),
                    symbols.Intern("/live/object/cam/fov"), EventFlags.None, symbols.Intern("PUT"));

                Span<byte> packed = stackalloc byte[EventRecord.kPayloadCapacity];
                EventPayload.TryPack(typeof(float), 35f, packed, out var written);
                record.SetPayload(packed.Slice(0, written), symbols.Intern("System.Single"));

                events.Add(record);

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
                Assert.AreEqual(1, player.events.Count);

                var record = player.events[0];
                Assert.AreEqual("System.Single", player.Resolve(record.payloadTypeId));

                var bytes = new byte[record.payloadLength];
                record.CopyPayloadTo(bytes);
                Assert.AreEqual(35f, BitConverter.ToSingle(bytes, 0), 0f);
            }
        }
    }
}
