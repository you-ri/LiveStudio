// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

// Sources these tests submit inputs as. Declared like any other source, so the tests exercise the
// same resolution path production code takes rather than a special case for tests.
[assembly: FrameSource("test")]
[assembly: FrameSource("unit-test")]
[assembly: FrameSource("batch")]

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
                frame.Add(new InputRecord(i, InputKind.PropertyWrite, 0, 0, InputFlags.None));
            }
            buffer.Commit(frameNumber);
        }

        [Test]
        public void TryRead_BeforeAnythingIsCommitted_SaysNotYet()
        {
            using var buffer = new InputFrameBuffer(4);

            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryRead(0, new InputFrame()));
        }

        [Test]
        public void TryRead_CommittedFrame_IsFoundWithItsInputs()
        {
            using var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0, inputCount: 3);

            using var destination = new InputFrame();

            Assert.AreEqual(FrameLookup.Found, buffer.TryRead(0, destination));
            Assert.AreEqual(0, destination.frameNumber);
            Assert.AreEqual(3, destination.inputCount);
            Assert.AreEqual(kRate60, destination.frameRate);
        }

        [Test]
        public void TryRead_AheadOfTheProducer_SaysNotYet()
        {
            using var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0);

            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryRead(1, new InputFrame()));
        }

        [Test]
        public void TryRead_FrameThatFellOutOfTheRing_SaysEvicted()
        {
            // Evicted and NotYetCommitted have to stay apart: one means wait, the other means the
            // reader can never catch up and has to resynchronise.
            using var buffer = new InputFrameBuffer(4);
            for (long i = 0; i <= 4; i++) _Commit(buffer, i);

            Assert.AreEqual(FrameLookup.Evicted, buffer.TryRead(0, new InputFrame()));
            Assert.AreEqual(FrameLookup.Found, buffer.TryRead(4, new InputFrame()));
        }

        [Test]
        public void TryReadLatest_ReturnsTheNewestCommittedFrame()
        {
            using var buffer = new InputFrameBuffer(4);
            using var empty = new InputFrame();

            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryReadLatest(empty));

            _Commit(buffer, 0);
            _Commit(buffer, 1);

            using var destination = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, buffer.TryReadLatest(destination));
            Assert.AreEqual(1, destination.frameNumber);
        }

        [Test]
        public void TryRead_TimecodeAtAnotherRate_IsRefusedRatherThanAnswered()
        {
            using var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0);

            var otherRate = new FrameRate(1, 30);
            var timecode = new Timecode(0, otherRate);

            Assert.AreEqual(FrameLookup.RateMismatch,
                buffer.TryRead(timecode, otherRate, new InputFrame()));
        }

        [Test]
        public void TryRead_TimecodeAtTheSameRate_FindsTheFrame()
        {
            using var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 90);

            var timecode = new Timecode(90, kRate60);

            Assert.AreEqual(FrameLookup.Found,
                buffer.TryRead(timecode, kRate60, new InputFrame()));
        }

        [Test]
        public void Reset_DropsEverythingHeld()
        {
            using var buffer = new InputFrameBuffer(4);
            _Commit(buffer, 0);
            buffer.Reset();

            Assert.AreEqual(0, buffer.frameCount);
            Assert.AreEqual(FrameLookup.NotYetCommitted, buffer.TryRead(0, new InputFrame()));
        }
    }

    public class FrameGateTests
    {
        [SetUp]
        public void ClearGate()
        {
            // The editor heartbeat pumps this gate continuously, so each test starts and ends from
            // a known state instead of inheriting whatever the editor left behind.
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
            // The editor heartbeat pumps this gate continuously, so each test starts and ends from
            // a known state instead of inheriting whatever the editor left behind.
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
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

            using var frame = new InputFrame();
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

            using var frame = new InputFrame();
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

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.IsTrue(frame[0].payloadTruncated);
        }

        [Test]
        public void SubmitAsync_OnTheMainThread_AppliesInlineAndCountsTheHole()
        {
            // Waiting for a frame head from the thread that runs frame heads would deadlock, so the
            // gate applies it immediately -- and says so, because it is a gap in the ordering.
            var before = FrameGate.bypassedCount;

            var task = FrameGate.SubmitAsync(InputKind.PropertyWrite, "test", "PUT", "/live/a", null,
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
                new InputDescriptor(InputKind.PropertyWrite, "PUT", "/live/object/cam/fov", "35"),
                new InputDescriptor(InputKind.PropertyWrite, "PUT", "/live/object/cam/near", "0.1"),
                new InputDescriptor(InputKind.FunctionCall, "POST", "/live/function/reset", "{}"),
            };

            var task = FrameGate._Enqueue(operations, "batch", () => { applied++; return true; });

            FrameGate.Pump();

            Assert.IsTrue(task.IsCompleted);
            Assert.AreEqual(1, applied, "a group applies as one unit, not once per operation");

            using var frame = new InputFrame();
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
                new InputDescriptor(InputKind.PropertyWrite, "PUT", "/live/a", "1"),
                new InputDescriptor(InputKind.PropertyWrite, "PUT", "/live/b", "2"),
            };

            var task = FrameGate._Enqueue<bool>(operations, "batch",
                () => throw new InvalidOperationException("boom"));

            LogAssert.Expect(LogType.Error, new Regex("Frame input #.*failed"));
            FrameGate.Pump();

            Assert.IsTrue(task.IsFaulted);

            using var frame = new InputFrame();
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
            var record = new InputRecord(1, InputKind.FunctionCall, 0, 1,
                InputFlags.Faulted | InputFlags.PayloadTruncated);

            Assert.IsTrue(record.faulted);
            Assert.IsTrue(record.payloadTruncated);

            var clean = new InputRecord(2, InputKind.PropertyWrite, 0, 1, InputFlags.None);

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

    public class FrameSourceTests
    {
        [SetUp]
        public void ClearGate()
        {
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
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        [Test]
        public void DeclaredSources_StartWithUnknownAndAreSorted()
        {
            var declared = FrameGate.declaredSources;

            Assert.Greater(declared.Count, 1);
            Assert.AreEqual(FrameSource.kUnknown, declared[0], "unknown has to be first so its id is fixed");
            CollectionAssert.Contains(declared, "test");
            CollectionAssert.Contains(declared, "unit-test");

            for (int i = 2; i < declared.Count; i++)
            {
                Assert.Less(string.CompareOrdinal(declared[i - 1], declared[i]), 0,
                    "declared sources have to be in a fixed order or their ids move between runs");
            }
        }

        [Test]
        public void ResolveSource_ThrowsForAnUndeclaredName()
        {
            Assert.Throws<ArgumentException>(() => FrameGate.ResolveSource("never-declared"));
            Assert.IsFalse(FrameGate.TryResolveSource("never-declared", out _));
        }

        [Test]
        public void ResolveSource_KeepsTheSameIdAcrossAReset()
        {
            // What makes it safe to resolve a source once into a static field: the gate wipes its
            // symbol table on reset, and a handle that moved would then name a different source.
            var before = FrameGate.ResolveSource("test");
            var otherBefore = FrameGate.ResolveSource("unit-test");

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            Assert.AreEqual(before, FrameGate.ResolveSource("test"));
            Assert.AreEqual(otherBefore, FrameGate.ResolveSource("unit-test"));
            Assert.AreNotEqual(before, otherBefore);
        }

        [Test]
        public void Submit_WithAnUndeclaredSource_RecordsItAsUnknownAndReportsItOnce()
        {
            LogAssert.Expect(LogType.Warning, new Regex("not declared"));

            FrameGate._Enqueue(InputKind.PropertyWrite, "stray", "/live/a", "1", () => true);

            // Reported once, not per input: a caller that has not been migrated would otherwise
            // fill the console at its own request rate.
            FrameGate._Enqueue(InputKind.PropertyWrite, "stray", "/live/b", "2", () => true);

            FrameGate.Pump();

            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.AreEqual(2, frame.inputCount);
            Assert.AreEqual(FrameSource.kUnknown, FrameGate.symbols.Resolve(frame[0].sourceId));
        }

        [Test]
        public void SubmitGroup_WithAnUnresolvedHandle_Throws()
        {
            var operations = new[] { new InputDescriptor(InputKind.PropertyWrite, "PUT", "/live/a", "1") };

            Assert.Throws<ArgumentException>(
                () => FrameGate.SubmitGroupAsync(operations, default(FrameSource), () => true));
        }
    }

    public class FrameHeadHandlerTests
    {
        [SetUp]
        public void ClearGate()
        {
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
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        [Test]
        public void FrameHeadHandler_RunsAfterTheFrameInputsAreApplied()
        {
            // Input then state: an input can change the structure, and a state block only means
            // something against the structure it belongs to.
            var order = new List<string>();

            void Handler(ref Frame frame) => order.Add("state");
            FrameGate.AddFrameHeadHandler(Handler);

            try
            {
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "1",
                    () => { order.Add("input"); return true; });

                FrameGate.Pump();
            }
            finally
            {
                FrameGate.RemoveFrameHeadHandler(Handler);
            }

            CollectionAssert.AreEqual(new[] { "input", "state" }, order);
        }

        [Test]
        public void FrameHeadHandler_IsAddedOnlyOnceAndStopsAfterRemoval()
        {
            var calls = 0;
            void Handler(ref Frame frame) => calls++;

            FrameGate.AddFrameHeadHandler(Handler);
            FrameGate.AddFrameHeadHandler(Handler);

            FrameGate.Pump();
            Assert.AreEqual(1, calls);

            FrameGate.RemoveFrameHeadHandler(Handler);
            FrameGate.Pump();
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void FrameHeadHandler_ReceivesThisFramesPositionAndInputs()
        {
            long seenNumber = -1;
            var seenInputs = -1;
            Timecode seenTimecode = default;

            void Handler(ref Frame frame)
            {
                seenNumber = frame.frameNumber;
                seenInputs = frame.inputs != null ? frame.inputs.inputCount : -1;
                seenTimecode = frame.timecode;
            }

            FrameGate.AddFrameHeadHandler(Handler);

            try
            {
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "1", () => true);
                FrameGate.Pump();
            }
            finally
            {
                FrameGate.RemoveFrameHeadHandler(Handler);
            }

            Assert.GreaterOrEqual(seenNumber, 0);
            Assert.AreEqual(1, seenInputs, "the handler sees the inputs already applied this frame");
            Assert.AreEqual(new Timecode(seenNumber, FrameGate.clock.frameRate), seenTimecode);
        }

        [Test]
        public void FrameHeadHandler_ThatThrows_DoesNotStopTheOthersOrTheFrame()
        {
            LogAssert.Expect(LogType.Error, new Regex("Frame head handler failed"));

            var reached = false;
            void Failing(ref Frame frame) => throw new InvalidOperationException("boom");
            void Following(ref Frame frame) => reached = true;

            FrameGate.AddFrameHeadHandler(Failing);
            FrameGate.AddFrameHeadHandler(Following);

            try
            {
                FrameGate.Pump();
            }
            finally
            {
                FrameGate.RemoveFrameHeadHandler(Failing);
                FrameGate.RemoveFrameHeadHandler(Following);
            }

            Assert.IsTrue(reached, "one failing producer must not stop the others");

            // The frame still commits, or every caller waiting on it would be stranded.
            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
        }
    }

    public class RepeatedWriteDiagnosticTests
    {
        [SetUp]
        public void ClearGate()
        {
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
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        [Test]
        public void RepeatedWritesInOneFrame_AreCountedButNothingIsDropped()
        {
            for (int i = 0; i < 3; i++)
            {
                var value = i.ToString();
                FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/object/cam/fov", value,
                    () => true);
            }

            FrameGate.Pump();

            // The record stays exact: folding these away would make a replay fire one callback
            // where the live run fired three.
            using var frame = new InputFrame();
            Assert.AreEqual(FrameLookup.Found, FrameGate.buffer.TryReadLatest(frame));
            Assert.AreEqual(3, frame.inputCount);

            Assert.AreEqual(2, FrameGate.repeatedWriteCount, "first write is not a repeat");
            Assert.AreEqual("/live/object/cam/fov", FrameGate.lastRepeatedTarget);
        }

        [Test]
        public void WritesToDifferentTargets_AreNotCountedAsRepeats()
        {
            FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "1", () => true);
            FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/b", "2", () => true);

            FrameGate.Pump();

            Assert.AreEqual(0, FrameGate.repeatedWriteCount);
        }

        [Test]
        public void TheSameTargetInSeparateFrames_IsNotARepeat()
        {
            FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "1", () => true);
            FrameGate.Pump();

            FrameGate._Enqueue(InputKind.PropertyWrite, "test", "/live/a", "2", () => true);
            FrameGate.Pump();

            Assert.AreEqual(0, FrameGate.repeatedWriteCount,
                "one write per frame is the normal case, not a signal");
        }

        [Test]
        public void FunctionCalls_AreNotCountedAsRepeatedWrites()
        {
            FrameGate._Enqueue(InputKind.FunctionCall, "test", "/live/camera/reset", "{}", () => true);
            FrameGate._Enqueue(InputKind.FunctionCall, "test", "/live/camera/reset", "{}", () => true);

            FrameGate.Pump();

            Assert.AreEqual(0, FrameGate.repeatedWriteCount,
                "a call means something every time it happens");
        }
    }

    public class FrameLaneAttributeTests
    {
        private class Declared
        {
            [LiveField]
            public float untagged;

            [LiveField(lane = FrameLane.State)]
            public Vector3 position;

            [LiveField(lane = FrameLane.None)]
            public Vector2Int windowPosition;

            [LiveProperty(lane = FrameLane.State)]
            public float intensity { get; set; }
        }

        [Test]
        public void UntaggedMember_DefaultsToTheInputLane()
        {
            var field = typeof(Declared).GetField(nameof(Declared.untagged));
            var attribute = (LiveFieldAttribute)Attribute.GetCustomAttribute(field, typeof(LiveFieldAttribute));

            Assert.AreEqual(FrameLane.Input, attribute.lane);
        }

        [Test]
        public void DeclaredLane_IsReadBackFromTheAttribute()
        {
            var position = typeof(Declared).GetField(nameof(Declared.position));
            var window = typeof(Declared).GetField(nameof(Declared.windowPosition));
            var intensity = typeof(Declared).GetProperty(nameof(Declared.intensity));

            Assert.AreEqual(FrameLane.State,
                ((LiveFieldAttribute)Attribute.GetCustomAttribute(position, typeof(LiveFieldAttribute))).lane);
            Assert.AreEqual(FrameLane.None,
                ((LiveFieldAttribute)Attribute.GetCustomAttribute(window, typeof(LiveFieldAttribute))).lane);
            Assert.AreEqual(FrameLane.State,
                ((LivePropertyAttribute)Attribute.GetCustomAttribute(intensity, typeof(LivePropertyAttribute))).lane);
        }
    }
}
