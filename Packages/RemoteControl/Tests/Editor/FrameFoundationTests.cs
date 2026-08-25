// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    public class InputSymbolTableTests
    {
        [Test]
        public void Intern_SameString_ReturnsSameId()
        {
            var table = new InputSymbolTable();

            var first = table.Intern("/live/objects/camera/fieldOfView");
            var second = table.Intern("/live/objects/camera/fieldOfView");

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, table.count);
        }

        [Test]
        public void Intern_DifferentStrings_HandsOutAscendingIds()
        {
            var table = new InputSymbolTable();

            Assert.AreEqual(0, table.Intern("a"));
            Assert.AreEqual(1, table.Intern("b"));
            Assert.AreEqual(2, table.Intern("c"));
        }

        [Test]
        public void Intern_NullOrEmpty_ReturnsNoneAndAddsNothing()
        {
            var table = new InputSymbolTable();

            Assert.AreEqual(InputSymbolTable.kNone, table.Intern(null));
            Assert.AreEqual(InputSymbolTable.kNone, table.Intern(string.Empty));
            Assert.AreEqual(0, table.count);
        }

        [Test]
        public void Resolve_RoundTripsPastTheInitialCapacity()
        {
            var table = new InputSymbolTable();

            // The backing array starts at 64, so this forces it to grow at least twice.
            const int kCount = 300;
            for (int i = 0; i < kCount; i++) table.Intern($"symbol-{i}");

            Assert.AreEqual(kCount, table.count);
            for (int i = 0; i < kCount; i++)
            {
                Assert.AreEqual($"symbol-{i}", table.Resolve(i), $"id {i} did not survive growth");
            }
        }

        [Test]
        public void TryResolve_UnknownId_Fails()
        {
            var table = new InputSymbolTable();
            table.Intern("only");

            Assert.IsFalse(table.TryResolve(1, out _));
            Assert.IsFalse(table.TryResolve(InputSymbolTable.kNone, out _));
            Assert.IsFalse(table.TryResolve(-42, out _));
        }

        [Test]
        public void Reset_EmptiesTheTable()
        {
            var table = new InputSymbolTable();
            table.Intern("a");
            table.Reset();

            Assert.AreEqual(0, table.count);
            Assert.AreEqual(0, table.Intern("b"), "ids restart from zero after a reset");
        }
    }

    public class InputSequencerTests
    {
        private static PendingInput _Input(int recordCount = 1) => new PendingInput
        {
            records = new InputRecord[recordCount],
            recordCount = recordCount,
        };

        [Test]
        public void Submit_StampsInAcceptanceOrder()
        {
            var sequencer = new InputSequencer();

            var first = sequencer.Submit(_Input());
            var second = sequencer.Submit(_Input());

            Assert.AreEqual(first + 1, second);
        }

        [Test]
        public void Submit_Group_TakesAConsecutiveRunOfSequences()
        {
            // A group has to be numbered as one run: that is what lets a frame head take all of it
            // or none of it, so a bundled request cannot be split across two frames.
            var sequencer = new InputSequencer();

            var first = sequencer.Submit(_Input(3));
            var next = sequencer.Submit(_Input());

            Assert.AreEqual(first + 3, next, "the group must reserve one number per record");

            var drained = sequencer.Drain();
            Assert.AreEqual(2, drained.Count, "a group stays a single queue entry");
            Assert.AreEqual(first + 0, drained[0].records[0].sequence);
            Assert.AreEqual(first + 1, drained[0].records[1].sequence);
            Assert.AreEqual(first + 2, drained[0].records[2].sequence);
        }

        [Test]
        public void Drain_ReturnsEverythingInOrderThenComesBackEmpty()
        {
            var sequencer = new InputSequencer();
            for (int i = 0; i < 5; i++) sequencer.Submit(_Input());

            var drained = sequencer.Drain();

            Assert.AreEqual(5, drained.Count);
            for (int i = 1; i < drained.Count; i++)
            {
                Assert.Less(drained[i - 1].firstSequence, drained[i].firstSequence);
            }

            Assert.AreEqual(0, sequencer.pendingCount);
            drained.Clear();
            Assert.AreEqual(0, sequencer.Drain().Count);
        }

        [Test]
        public void Drain_ReusesItsListWithoutLosingWhatArrivedSince()
        {
            var sequencer = new InputSequencer();
            sequencer.Submit(_Input());

            var first = sequencer.Drain();
            first.Clear();

            sequencer.Submit(_Input());
            var second = sequencer.Drain();

            Assert.AreEqual(1, second.Count, "an input submitted between drains must not be dropped");
        }
    }

    public class InputFrameBufferTests
    {
        private static readonly FrameRate kRate60 = new FrameRate(1, 60);

        private static void _Commit(InputFrameBuffer buffer, long frameNumber, int inputCount = 0)
        {
            var frame = buffer.BeginFrame(frameNumber, kRate60);
            for (int i = 0; i < inputCount; i++)
            {
                frame.Add(new InputRecord(i, InputKind.PropertyWrite, 0, 0,
                    default, InputFlags.None));
            }
            buffer.Commit(frameNumber);
        }

        [Test]
        public void TryRead_BeforeAnythingIsCommitted_SaysNotYet()
        {
            var buffer = new InputFrameBuffer(4);

            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryRead(0, new InputFrame()));
        }

        [Test]
        public void TryRead_CommittedFrame_IsFoundWithItsInputs()
        {
            var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0, inputCount: 3);

            var destination = new InputFrame();

            Assert.AreEqual(FrameLookup.Found, buffer.TryRead(0, destination));
            Assert.AreEqual(0, destination.frameNumber);
            Assert.AreEqual(3, destination.inputCount);
            Assert.AreEqual(kRate60, destination.frameRate);
        }

        [Test]
        public void TryRead_AheadOfTheProducer_SaysNotYet()
        {
            var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0);

            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryRead(1, new InputFrame()));
        }

        [Test]
        public void TryRead_FrameThatFellOutOfTheRing_SaysEvicted()
        {
            // Evicted and NotYetCommitted have to stay apart: one means wait, the other means the
            // reader can never catch up and has to resynchronise.
            var buffer = new InputFrameBuffer(4);
            for (long i = 0; i <= 4; i++) _Commit(buffer, i);

            Assert.AreEqual(FrameLookup.Evicted, buffer.TryRead(0, new InputFrame()));
            Assert.AreEqual(FrameLookup.Found, buffer.TryRead(4, new InputFrame()));
        }

        [Test]
        public void TryReadLatest_ReturnsTheNewestCommittedFrame()
        {
            var buffer = new InputFrameBuffer(4);
            var empty = new InputFrame();

            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryReadLatest(empty));

            _Commit(buffer, 0);
            _Commit(buffer, 1);

            var destination = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, buffer.TryReadLatest(destination));
            Assert.AreEqual(1, destination.frameNumber);
        }

        [Test]
        public void TryRead_TimecodeAtAnotherRate_IsRefusedRatherThanAnswered()
        {
            var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0);

            var otherRate = new FrameRate(1, 30);
            var timecode = new Timecode(0, otherRate);

            Assert.AreEqual(FrameLookup.RateMismatch,
                buffer.TryRead(timecode, otherRate, new InputFrame()));
        }

        [Test]
        public void TryRead_TimecodeAtTheSameRate_FindsTheFrame()
        {
            var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 90);

            var timecode = new Timecode(90, kRate60);

            Assert.AreEqual(FrameLookup.Found,
                buffer.TryRead(timecode, kRate60, new InputFrame()));
        }

        [Test]
        public void Reset_DropsEverythingHeld()
        {
            var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0);
            buffer.Reset();

            Assert.AreEqual(0, buffer.frameCount);
            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryRead(0, new InputFrame()));
        }
    }

    public class FrameGateTests
    {
        [SetUp]
        [TearDown]
        public void ClearGate()
        {
            // The editor heartbeat pumps this gate continuously, so each test starts and ends from
            // a known state instead of inheriting whatever the editor left behind.
            FrameGate.ResetState("[test] cleared");
        }

        [Test]
        public void Enqueue_CompletesOnceThePumpAppliesIt()
        {
            var applied = false;
            var task = FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "1",
                () => { applied = true; return 42; });

            Assert.IsFalse(task.IsCompleted, "nothing should be applied before a frame head");
            Assert.IsFalse(applied);

            FrameGate.Pump();

            Assert.IsTrue(applied);
            Assert.AreEqual(42, task.Result);
        }

        [Test]
        public void Pump_AppliesInSequenceOrder()
        {
            var order = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                var captured = i;
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", $"/live/{i}", null,
                    () => { order.Add(captured); return true; });
            }

            FrameGate.Pump();

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, order);
        }

        [Test]
        public void Pump_RecordsWhatItApplied()
        {
            FrameGate._Enqueue(InputKind.FunctionCall, "unit-test", "/live/camera/reset", "{}",
                () => true);

            FrameGate.Pump();

            var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.AreEqual(1, frame.inputCount);

            var record = frame[0];
            Assert.AreEqual(InputKind.FunctionCall, record.kind);
            Assert.AreEqual("/live/camera/reset", FrameGate.symbols.Resolve(record.targetId));
            Assert.AreEqual("unit-test", FrameGate.symbols.Resolve(record.sourceId));
            Assert.IsFalse(record.faulted);
        }

        [Test]
        public void Pump_FailedInput_FaultsTheCallerAndMarksTheRecord()
        {
            var task = FrameGate._Enqueue<bool>(InputKind.PropertyWrite, "test", "/live/boom", null,
                () => throw new InvalidOperationException("boom"));

            LogAssert.Expect(LogType.Error, new Regex("Frame input #.*failed"));
            FrameGate.Pump();

            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(task.Exception.InnerException);

            var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.IsTrue(frame[0].faulted, "a failure has to be distinguishable from a no-op");
        }

        [Test]
        public void ResetState_FaultsQueuedInputsInsteadOfDroppingThem()
        {
            // Dropping them silently left the caller waiting for a frame head that would never
            // arrive, which hung the HTTP request behind it until the client gave up.
            var task = FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "1",
                () => true);

            FrameGate.ResetState("[test] restarted");

            Assert.IsTrue(task.IsFaulted, "a queued input must not be abandoned in silence");
            Assert.IsInstanceOf<OperationCanceledException>(task.Exception.InnerException);
        }

        [Test]
        public void Enqueue_OverlongPayload_IsTruncatedAndCounted()
        {
            var before = FrameGate.truncatedPayloadCount;

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/long", new string('x', 4000),
                () => true);
            FrameGate.Pump();

            Assert.AreEqual(before + 1, FrameGate.truncatedPayloadCount);

            var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.IsTrue(frame[0].payloadTruncated);
        }

        [Test]
        public void SubmitAsync_OnTheMainThread_AppliesInlineAndCountsTheHole()
        {
            // Waiting for a frame head from the thread that runs frame heads would deadlock, so the
            // gate applies it immediately -- and says so, because it is a gap in the ordering.
            var before = FrameGate.bypassedCount;

            var task = FrameGate.SubmitAsync(InputKind.PropertyWrite, "test", "/live/a", null,
                () => 7);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(7, task.Result);
            Assert.AreEqual(before + 1, FrameGate.bypassedCount);
        }

        [Test]
        public void Group_LandsWholeInOneFrameAndIsRecordedPerOperation()
        {
            var applied = 0;
            var operations = new[]
            {
                new InputDescriptor(InputKind.PropertyWrite, "/live/object/cam/fov", "35"),
                new InputDescriptor(InputKind.PropertyWrite, "/live/object/cam/near", "0.1"),
                new InputDescriptor(InputKind.FunctionCall, "/live/function/reset", "{}"),
            };

            var task = FrameGate._Enqueue(operations, "batch", () => { applied++; return true; });

            FrameGate.Pump();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(1, applied, "a group applies as one unit, not once per operation");

            var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.AreEqual(3, frame.inputCount, "each operation is recorded on its own");

            Assert.AreEqual(frame[0].sequence + 1, frame[1].sequence);
            Assert.AreEqual(frame[1].sequence + 1, frame[2].sequence);
            Assert.AreEqual("/live/object/cam/fov", FrameGate.symbols.Resolve(frame[0].targetId));
            Assert.AreEqual(InputKind.FunctionCall, frame[2].kind);
        }

        [Test]
        public void Group_ThatThrows_MarksEveryOperationFaulted()
        {
            var operations = new[]
            {
                new InputDescriptor(InputKind.PropertyWrite, "/live/a", "1"),
                new InputDescriptor(InputKind.PropertyWrite, "/live/b", "2"),
            };

            var task = FrameGate._Enqueue<bool>(operations, "batch",
                () => throw new InvalidOperationException("boom"));

            LogAssert.Expect(LogType.Error, new Regex("Frame input #.*failed"));
            FrameGate.Pump();

            Assert.IsTrue(task.IsFaulted);

            var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.IsTrue(frame[0].faulted);
            Assert.IsTrue(frame[1].faulted, "the group applied as one unit, so it failed as one");
        }

        [Test]
        public void Pump_EmptyFrame_StillCommits()
        {
            var startCount = FrameGate.buffer.frameCount;

            FrameGate.Pump();

            Assert.AreEqual(startCount + 1, FrameGate.buffer.frameCount,
                "frames have to advance even with no input, or frame numbers stop meaning time");
        }
    }

    public class InputRecordTests
    {
        // Fails to compile if the record stops being unmanaged, which is the whole reason strings
        // are interned instead of stored.
        private static void _RequireUnmanaged<T>() where T : unmanaged { }

        [Test]
        public void Record_IsUnmanaged()
        {
            Assert.DoesNotThrow(() => _RequireUnmanaged<InputRecord>());
        }

        [Test]
        public void Flags_ReadBackThroughTheirProperties()
        {
            var record = new InputRecord(1, InputKind.FunctionCall, 0, 1, default,
                InputFlags.Faulted | InputFlags.PayloadTruncated);

            Assert.IsTrue(record.faulted);
            Assert.IsTrue(record.payloadTruncated);

            var clean = new InputRecord(2, InputKind.PropertyWrite, 0, 1, default,
                InputFlags.None);

            Assert.IsFalse(clean.faulted);
            Assert.IsFalse(clean.payloadTruncated);
        }

        [Test]
        public void Payload_TooLongForTheRecord_IsTruncatedRatherThanThrowing()
        {
            var payload = default(FixedString512Bytes);
            var error = payload.CopyFromTruncated(new string('x', 4000));

            Assert.AreEqual(CopyError.Truncation, error);
            Assert.Greater(payload.Length, 0);
        }
    }
}
