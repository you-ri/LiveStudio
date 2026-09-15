// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine.Profiling;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A frame in steady state allocates nothing: live, recording, or replaying.
    ///
    /// The frame head runs sixty times a second for as long as the application is up, and anything
    /// it leaves for the collector turns into a hitch at an arbitrary moment of a live show. Warmed
    /// up first -- lists reach their high-water mark, ids get interned, class layouts get described
    /// -- because that is a cost paid once per run, not per frame.
    ///
    /// Counted with the profiler's own GC.Alloc marker, which is what Unity's
    /// <c>Is.Not.AllocatingGCMemory</c> reads too; counting it here puts the number in the message.
    /// The per-lane tests say which part allocates when the whole-frame ones do not hold.
    /// </summary>
    public class FrameAllocationTests
    {
        // Past the gate's ring of retained frames (16), so every slot has been used once before
        // anything is measured: a slot's first frame sizes its storage, which is a cost per run.
        private const int kWarmUpFrames = 24;
        private const int kMeasuredFrames = 16;

        private readonly List<LiveObjectHandle?> _handles = new List<LiveObjectHandle?>();
        private bool _retained;
        private bool _declared;

        private static readonly Action _nothing = () => { };

        [SetUp]
        public void ClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }

        [TearDown]
        public void Release()
        {
            if (_retained)
            {
                LiveStateSystem.Release();
                LiveStructureSystem.Release();
                _retained = false;
            }

            for (int i = 0; i < _handles.Count; i++) _handles[i]?.Unregister();
            _handles.Clear();

            if (_declared)
            {
                StateTypes.Unregister(typeof(DeclaredLamp));
                _declared = false;
            }

            FrameGate.sink = null;
            FrameGate.source = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        [Test]
        public void TheCounterSeesAllocations()
        {
            // Without this, a counter that measures nothing would pass every test below.
            Assert.Greater(_CountAllocations(() => GC.KeepAlive(new object[1])), 0);
        }

        [Test]
        public void AnEmptyFrame_DoesNotAllocate()
        {
            _Pump(kWarmUpFrames);

            _AssertNoAllocations(() => _Pump(kMeasuredFrames));
        }

        [Test]
        public void CapturingState_DoesNotAllocate()
        {
            _RegisterWorld();
            var state = FrameGate.state;
            for (int i = 0; i < kWarmUpFrames; i++) LiveStateSystem.CaptureInto(state, i);

            _AssertNoAllocations(() =>
            {
                for (int i = 0; i < kMeasuredFrames; i++) LiveStateSystem.CaptureInto(state, i);
            });
        }

        [Test]
        public void ApplyingState_DoesNotAllocate()
        {
            _RegisterWorld();
            var state = FrameGate.state;
            LiveStateSystem.CaptureInto(state, 0);
            for (int i = 0; i < kWarmUpFrames; i++) LiveStateSystem.ApplyFrom(state, FrameGate.symbols);

            _AssertNoAllocations(() =>
            {
                for (int i = 0; i < kMeasuredFrames; i++) LiveStateSystem.ApplyFrom(state, FrameGate.symbols);
            });
        }

        [Test]
        public void CapturingStructure_DoesNotAllocate()
        {
            _RegisterWorld();
            var structure = FrameGate.structure;
            for (int i = 0; i < kWarmUpFrames; i++) LiveStructureSystem.CaptureInto(structure, FrameGate.symbols);

            _AssertNoAllocations(() =>
            {
                for (int i = 0; i < kMeasuredFrames; i++) LiveStructureSystem.CaptureInto(structure, FrameGate.symbols);
            });
        }

        [Test]
        public void ApplyingStructure_DoesNotAllocate()
        {
            _RegisterWorld();
            var structure = FrameGate.structure;
            LiveStructureSystem.CaptureInto(structure, FrameGate.symbols);
            for (int i = 0; i < kWarmUpFrames; i++) LiveStructureSystem.ApplyFrom(structure, FrameGate.symbols);

            _AssertNoAllocations(() =>
            {
                for (int i = 0; i < kMeasuredFrames; i++) LiveStructureSystem.ApplyFrom(structure, FrameGate.symbols);
            });
        }

        [Test]
        public void ALiveFrame_CarryingTheWorld_DoesNotAllocate()
        {
            _RegisterWorld();
            _Retain();
            _Pump(kWarmUpFrames);

            _AssertNoAllocations(() => _Pump(kMeasuredFrames));
        }

        [Test]
        public void Recording_DoesNotAllocate()
        {
            _RegisterWorld();
            _Retain();

            // Sized so the stream itself never grows: what is measured is the recorder, not the
            // buffer standing in for a file.
            using var stream = new MemoryStream(4 * 1024 * 1024);
            using var recorder = new FrameRecorder();

            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            _Pump(kWarmUpFrames);

            _AssertNoAllocations(() => _Pump(kMeasuredFrames));

            FrameGate.sink = null;
            recorder.Stop();
        }

        [Test]
        public void Replaying_DoesNotAllocate()
        {
            _RegisterWorld();
            _Retain();

            // Long enough for the warm-up, the uncounted pass and the counted one, with the take
            // still running at the end.
            var take = _Record(kWarmUpFrames + 2 * kMeasuredFrames + 8);

            using var replayer = new FrameReplayer(new MemoryStream(take), new NothingApplier());
            FrameGate.source = replayer;

            _Pump(kWarmUpFrames);

            _AssertNoAllocations(() => _Pump(kMeasuredFrames));

            FrameGate.source = null;
        }

        [Test]
        public void Posting_DoesNotAllocateInTheGate()
        {
            // A producer inside the frame -- a deck key, a fader -- posting a delegate it keeps. What
            // the gate does with it on the way to the frame head is what is measured.
            var source = FrameGate.ResolveSource("test");

            for (int i = 0; i < kWarmUpFrames; i++)
            {
                FrameGate._Post(EventKind.Set, source, "PUT", "/live/object/alloc/value", _nothing);
                FrameGate.Pump();
            }

            _AssertNoAllocations(() =>
            {
                for (int i = 0; i < kMeasuredFrames; i++)
                {
                    FrameGate._Post(EventKind.Set, source, "PUT", "/live/object/alloc/value", _nothing);
                    FrameGate.Pump();
                }
            });
        }

        [Test]
        public void CapturingDeclaredState_DoesNotAllocate()
        {
            // A type declared by an asset rather than generated for: its members are read through
            // the declaration, which answered in object -- a box per member per object per frame.
            var lamp = new DeclaredLamp { level = 0.5f, on = true, offset = UnityEngine.Vector3.one };
            lamp.tint = UnityEngine.Color.red;
            var bridge = _DeclareLamp(lamp);
            var state = new StateBlockSet();

            try
            {
                for (int i = 0; i < kWarmUpFrames; i++) bridge.Capture(lamp, 1, state, default, i, FrameGate.symbols);

                _AssertNoAllocations(() =>
                {
                    for (int i = 0; i < kMeasuredFrames; i++) bridge.Capture(lamp, 1, state, default, i, FrameGate.symbols);
                });
            }
            finally
            {
                state.Dispose();
            }
        }

        [Test]
        public void ApplyingDeclaredStateThatHasNotMoved_DoesNotAllocate()
        {
            // The replay side of the same thing: every member compared with what is recorded, every
            // frame, and nothing written because nothing moved.
            var lamp = new DeclaredLamp { level = 0.5f, on = true, offset = UnityEngine.Vector3.one };
            lamp.tint = UnityEngine.Color.red;
            var bridge = _DeclareLamp(lamp);
            var state = new StateBlockSet();

            try
            {
                bridge.Capture(lamp, 1, state, default, 0, FrameGate.symbols);
                lamp.tintWrites = 0;

                for (int i = 0; i < kWarmUpFrames; i++) bridge.Apply(lamp, 1, state, FrameGate.symbols);

                _AssertNoAllocations(() =>
                {
                    for (int i = 0; i < kMeasuredFrames; i++) bridge.Apply(lamp, 1, state, FrameGate.symbols);
                });

                Assert.AreEqual(0, lamp.tintWrites, "an unchanged value was written back");
                Assert.AreEqual(0.5f, lamp.level);
                Assert.IsTrue(lamp.on);
            }
            finally
            {
                state.Dispose();
            }
        }

        private static void _AssertNoAllocations(Action action)
        {
            // Once before it is counted. The first run of a new lambda pays for its own compilation
            // and for the string literals it names, which is a cost of the test and not of the frame.
            action();

            var count = _CountAllocations(action);
            Assert.AreEqual(0, count, $"{count} GC allocation(s) over {kMeasuredFrames} frames");
        }

        private static int _CountAllocations(Action action)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }

            return recorder.sampleBlockCount;
        }

        private static void _Pump(int frames)
        {
            for (int i = 0; i < frames; i++) FrameGate.Pump();
        }

        private void _Retain()
        {
            LiveStructureSystem.Retain();
            LiveStateSystem.Retain();
            _retained = true;
        }

        /// <summary>
        /// A small world with each shape the frame carries: an owner holding a nested object, the
        /// field-behind-a-property convention, and a keyed collection.
        /// </summary>
        private void _RegisterWorld()
        {
            LiveClass.RegisterFromAttributes<Board>();
            LiveClass.RegisterFromAttributes<Tile>();

            var owner = new StateWalkOwner { channel = 3 };
            owner.knob.turn = 0.5f;
            _handles.Add(LiveObjectRegistry.Create(owner.GetType(), owner, "alloc-owner"));

            var dimmer = new StateWalkDimmer { level = 0.25f };
            _handles.Add(LiveObjectRegistry.Create(dimmer.GetType(), dimmer, "alloc-dimmer"));

            var board = new Board();
            board.tiles.Add(new Tile { id = "tile-a", value = 1f });
            board.tiles.Add(new Tile { id = "tile-b", value = 2f });
            _handles.Add(LiveObjectRegistry.Create(typeof(Board), board, "alloc-board"));
        }

        /// <summary>
        /// Declares the lamp the way an asset does -- no attributes, every member on the state lane --
        /// and registers one.
        /// </summary>
        private DeclaredStateBridge _DeclareLamp(DeclaredLamp lamp)
        {
            var liveClass = LiveClass.Register(typeof(DeclaredLamp), nameof(DeclaredLamp), new[]
            {
                new LivePropertyDefine { name = "level", path = "level", lane = FrameLane.State },
                new LivePropertyDefine { name = "on", path = "on", lane = FrameLane.State },
                new LivePropertyDefine { name = "offset", path = "offset", lane = FrameLane.State },
                new LivePropertyDefine { name = "tint", path = "tint", lane = FrameLane.State },
            });

            _declared = true;
            _handles.Add(LiveObjectRegistry.Create(typeof(DeclaredLamp), lamp, "alloc-lamp"));

            return DeclaredStateBridge.Build(liveClass);
        }

        /// <summary>
        /// An undecorated type, as an asset would declare one: fields of each width worth telling
        /// apart -- a bool is four bytes in a slot and one in memory -- and a property.
        /// </summary>
        public class DeclaredLamp
        {
            public float level;
            public bool on;
            public UnityEngine.Vector3 offset;

            public int tintWrites;
            private UnityEngine.Color _tint;

            public UnityEngine.Color tint
            {
                get => _tint;
                set
                {
                    _tint = value;
                    tintWrites++;
                }
            }
        }

        private static byte[] _Record(int frames)
        {
            using var stream = new MemoryStream();
            using var recorder = new FrameRecorder();

            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;
            _Pump(frames);
            FrameGate.sink = null;
            recorder.Stop();

            return stream.ToArray();
        }

        private sealed class NothingApplier : IEventApplier
        {
            public bool Apply(in ReplayEvent evt, out string error)
            {
                error = null;
                return true;
            }
        }
    }
}
