// Copyright (c) You-Ri, 2026
using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// An input records the value it applied, not the request that asked for it.
    ///
    /// A slider sends the same property sixty times a second. Kept as digits, every one of those
    /// costs a parse to read back and loses the last bits on the way; kept as bytes it is the same
    /// memory the property holds, and a viewer can walk it with the machinery it walks state with.
    /// </summary>
    [TestFixture]
    public class FramePayloadTests
    {
        [SetUp]
        public void StartClean() => FrameGate.ResetState("[test] cleared");

        [TearDown]
        public void Finish() => FrameGate.ResetState("[test] cleared");

        private enum Facing
        {
            Left = 0,
            Right = 7,
        }

        // ---- InputPayload: laying values out ----

        [Test]
        public void AFloat_SurvivesTheRoundTripExactly()
        {
            // 0.1f has no exact decimal form, which is the case digits get wrong and bytes do not.
            const float value = 0.1f;

            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsTrue(InputPayload.TryPack(typeof(float), value, bytes, out var written));
            Assert.AreEqual(4, written, "a float is four bytes, not four characters");

            Assert.IsTrue(InputPayload.TryUnpack(typeof(float), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (float)read, 0f, "exactly, not nearly");
        }

        [Test]
        public void AStructOfFloats_IsItsOwnWidth()
        {
            var value = new Vector3(1f, -2.5f, 3.25f);

            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsTrue(InputPayload.TryPack(typeof(Vector3), value, bytes, out var written));
            Assert.AreEqual(12, written);

            Assert.IsTrue(InputPayload.TryUnpack(typeof(Vector3), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (Vector3)read);
        }

        [Test]
        public void ABool_IsOneByte_NotTheInteropFour()
        {
            // Marshal would say four. What is written is what the type occupies in memory, because
            // that is what the reader lays back out.
            Assert.AreEqual(1, InputPayload.SizeOf(typeof(bool)));

            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsTrue(InputPayload.TryPack(typeof(bool), true, bytes, out var written));
            Assert.AreEqual(1, written);

            Assert.IsTrue(InputPayload.TryUnpack(typeof(bool), bytes.Slice(0, written), out var read));
            Assert.IsTrue((bool)read);
        }

        [Test]
        public void AnEnum_KeepsItsValue_NotItsName()
        {
            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsTrue(InputPayload.TryPack(typeof(Facing), Facing.Right, bytes, out var written));

            Assert.IsTrue(InputPayload.TryUnpack(typeof(Facing), bytes.Slice(0, written), out var read));
            Assert.AreEqual(Facing.Right, (Facing)read);
        }

        [Test]
        public void AString_SaysItsOwnLengthBeforeItsCharacters()
        {
            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsTrue(InputPayload.TryWriteString("あい", bytes, out var written));

            // Two bytes of length, then six of UTF-8: no terminator to scan for.
            Assert.AreEqual(InputPayload.kLengthPrefixSize + 6, written);
            Assert.AreEqual(6, BitConverter.ToUInt16(bytes.Slice(0, 2).ToArray(), 0));

            Assert.AreEqual("あい", InputPayload.ReadString(bytes.Slice(0, written)));
        }

        [Test]
        public void AnEmptyString_IsStillAStringRatherThanNothing()
        {
            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsTrue(InputPayload.TryWriteString(string.Empty, bytes, out var written));

            Assert.AreEqual(InputPayload.kLengthPrefixSize, written);
            Assert.AreEqual(string.Empty, InputPayload.ReadString(bytes.Slice(0, written)));
        }

        [Test]
        public void AStringLongerThanTheRecord_IsCutOnACharacterBoundary()
        {
            // Kept readable rather than ending in half a rune: what survives is still the value,
            // just less of it.
            var text = new string('あ', InputRecord.kPayloadCapacity);

            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsFalse(InputPayload.TryWriteString(text, bytes, out var written));

            var read = InputPayload.ReadString(bytes.Slice(0, written));
            Assert.Less(read.Length, text.Length);
            StringAssert.StartsWith(read, text);
        }

        [Test]
        public void APrefixLongerThanWhatIsThere_ReadsOnlyWhatIsThere()
        {
            // A file cut off mid-record would otherwise have its last string read past its own end.
            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            InputPayload.TryWriteString("abcdef", bytes, out var written);

            Assert.AreEqual("abc", InputPayload.ReadString(bytes.Slice(0, written - 3)));
        }

        [Test]
        public void APropertyThatDeclaresAMaximum_NeedsNoneOfThat()
        {
            // A FixedString is unmanaged, so it packs at its own width like any other value and its
            // characters stay in the record. That is what declaring a bound buys.
            var value = new Unity.Collections.FixedString32Bytes("ai");

            Assert.AreEqual(32, InputPayload.SizeOf(typeof(Unity.Collections.FixedString32Bytes)));

            Span<byte> bytes = stackalloc byte[InputRecord.kPayloadCapacity];
            Assert.IsTrue(InputPayload.TryPack(
                typeof(Unity.Collections.FixedString32Bytes), value, bytes, out var written));
            Assert.AreEqual(32, written);

            Assert.IsTrue(InputPayload.TryUnpack(
                typeof(Unity.Collections.FixedString32Bytes), bytes.Slice(0, written), out var read));
            Assert.AreEqual(value, (Unity.Collections.FixedString32Bytes)read);
        }

        [Test]
        public void ATypeThisBuildDoesNotHave_ResolvesToNothingRatherThanThrowing()
        {
            // A recording can name a type that has since been removed. Refusing to read the rest of
            // the file over that would be worse than saying which one is missing.
            Assert.IsNull(InputPayload.Resolve("Nowhere.NoSuchType"));
        }

        // ---- The record ----

        [Test]
        public void AWriteThatSaysWhatItApplied_RecordsTheValue_NotTheRequest()
        {
            const string target = "/live/object/cam/fov";

            var task = FrameGate._Enqueue(InputKind.PropertyWrite, "test", target, "35.0",
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

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual("System.Single", FrameGate.symbols.Resolve(record.payloadTypeId));
            Assert.AreEqual(4, record.payloadLength, "not the six characters of \"35.0\"");

            var bytes = new byte[record.payloadLength];
            record.CopyPayloadTo(bytes);
            Assert.AreEqual(35f, BitConverter.ToSingle(bytes, 0), 0f);
        }

        [Test]
        public void AStringProperty_RecordsItsValue_NotTheJsonThatCarriedIt()
        {
            const string target = "/live/object/avatar/name";

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", target, "{\"value\":\"ai\"}",
                () =>
                {
                    FrameGate.StampAppliedPayload(target, typeof(string), "ai");
                    return true;
                },
                verb: "PUT");

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual(InputPayload.kStringTypeName, FrameGate.symbols.Resolve(record.payloadTypeId));

            var bytes = new byte[record.payloadLength];
            record.CopyPayloadTo(bytes);
            Assert.AreEqual("ai", InputPayload.ReadString(bytes), "the value, not its JSON form");
        }

        [Test]
        public void AWriteThatSaysNothing_KeepsTheRequestTextAsItsPayload()
        {
            // The fallback that makes the typed path safe to leave out: a target whose type has no
            // layout still records something a replay can use.
            const string target = "/live/object/avatar/name";

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", target, "\"ai\"", () => true);
            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            var record = frame[0];
            Assert.AreEqual(InputPayload.kRequestTypeName, FrameGate.symbols.Resolve(record.payloadTypeId));

            var bytes = new byte[record.payloadLength];
            record.CopyPayloadTo(bytes);
            Assert.AreEqual("\"ai\"", InputPayload.ReadString(bytes));
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

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", target, new string('x', 4000),
                () =>
                {
                    FrameGate.StampAppliedPayload(target, typeof(float), 12f);
                    return true;
                });

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));

            Assert.IsFalse(frame[0].payloadTruncated,
                "the value that replaced the text was not cut short, so the mark would be a lie");
        }

        // ---- Through the file ----

        [Test]
        public void ATypedPayload_ComesBackOutOfARecordingAsTheSameBytes()
        {
            var stream = new MemoryStream();
            var symbols = new InputSymbolTable();

            using (var inputs = new InputFrame())
            using (var writer = new FrameRecordWriter(
                stream,
                new FrameRecordHeader { frameRate = FrameRate.FPS60, engineId = "unity", buildId = "test" },
                leaveOpen: true))
            {
                inputs.Reset(0, FrameRate.FPS60);

                var record = new InputRecord(1, InputKind.PropertyWrite, symbols.Intern("rest"),
                    symbols.Intern("/live/object/cam/fov"), InputFlags.None, symbols.Intern("PUT"));

                Span<byte> packed = stackalloc byte[InputRecord.kPayloadCapacity];
                InputPayload.TryPack(typeof(float), 35f, packed, out var written);
                record.SetPayload(packed.Slice(0, written), symbols.Intern("System.Single"));

                inputs.Add(record);

                var frame = new Frame { frameNumber = 0, frameRate = FrameRate.FPS60, inputs = inputs };
                writer.BeginFrame(in frame, symbols);
                writer.WriteInputs(inputs, symbols);
                writer.EndFrame();
                writer.Close(symbols);
            }

            stream.Position = 0;

            using (var player = new FrameRecordPlayer(stream))
            {
                Assert.IsTrue(player.Advance());
                Assert.AreEqual(1, player.inputs.Count);

                var record = player.inputs[0];
                Assert.AreEqual("System.Single", player.Resolve(record.payloadTypeId));

                var bytes = new byte[record.payloadLength];
                record.CopyPayloadTo(bytes);
                Assert.AreEqual(35f, BitConverter.ToSingle(bytes, 0), 0f);
            }
        }
    }
}
