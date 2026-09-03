// Copyright (c) You-Ri, 2026
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A type that puts some of its exposed members in the state lane.
    ///
    /// Partial because the generator emits the block and the two movers inside it: the convention
    /// here is a private field with the attribute on it, and a free function could not read one.
    /// </summary>
    [LiveClass("StateLaneProbe")]
    public partial class StateLaneProbe
    {
        [LiveField(lane = FrameLane.State)]
        private float _intensity;

        [LiveField(lane = FrameLane.State)]
        private Vector3 _position;

        [LiveField(lane = FrameLane.State)]
        private ProbeMode _mode;

        // Ordinary lane: changes when someone asks, so it is recorded as an event.
        [LiveField]
        private string _label = "probe";

        public float intensity { get => _intensity; set => _intensity = value; }

        public Vector3 position { get => _position; set => _position = value; }

        public ProbeMode mode { get => _mode; set => _mode = value; }

        public string label { get => _label; set => _label = value; }
    }

    public enum ProbeMode
    {
        Off = 0,
        On = 1,
        Auto = 2,
    }

    public class LiveStateBridgeTests
    {
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

        [Test]
        public void TheGenerator_EmitsABridgeForATypeInTheStateLane()
        {
            var bridge = StateBridgeRegistry.Find(typeof(StateLaneProbe));

            Assert.IsNotNull(bridge, "no bridge was generated for a type with state-lane members");
            Assert.AreEqual(typeof(StateLaneProbe), bridge.ownerType);
            Assert.AreEqual(typeof(StateLaneProbe.LiveStateBlock), bridge.blockType);
        }

        [Test]
        public void TheBlock_CarriesOnlyTheStateLaneMembers()
        {
            var fields = typeof(StateLaneProbe.LiveStateBlock).GetFields();

            // The label is in the event lane, so it is recorded when it changes rather than carried
            // every frame. Text can join the other lane now (see LiveFixedStringStateTests), but
            // only where a declaration claims a width for it; this one claims none.
            CollectionAssert.AreEquivalent(
                new[] { "_intensity", "_position", "_mode" },
                System.Array.ConvertAll(fields, f => f.Name));
        }

        [Test]
        public void TheBlockIsUnmanaged_SoAFrameCanCarryItVerbatim()
        {
            Assert.DoesNotThrow(() => _RequireUnmanaged<StateLaneProbe.LiveStateBlock>());

            // float + Vector3 + enum, with no padding to hide anything.
            Assert.AreEqual(4 + 12 + 4, Unity.Collections.LowLevel.Unsafe.UnsafeUtility
                .SizeOf<StateLaneProbe.LiveStateBlock>());
        }

        private static void _RequireUnmanaged<T>() where T : unmanaged { }

        [Test]
        public void Capture_ReadsTheObjectIntoTheBlock()
        {
            var probe = new StateLaneProbe { intensity = 0.75f, position = new Vector3(1, 2, 3), mode = ProbeMode.Auto };
            var bridge = StateBridgeRegistry.Find(typeof(StateLaneProbe));

            using var state = new StateBlockSet();
            bridge.Capture(probe, ownerId: 7, state, default, time: 42);

            var block = state.Find<StateLaneProbe.LiveStateBlock>();
            Assert.AreEqual(1, block.count);
            Assert.AreEqual(7, block[0].ownerId);
            Assert.AreEqual(42, block[0].time);
            Assert.AreEqual(0.75f, block[0].value._intensity);
            Assert.AreEqual(new Vector3(1, 2, 3), block[0].value._position);
            Assert.AreEqual(ProbeMode.Auto, block[0].value._mode);
        }

        [Test]
        public void Apply_WritesTheBlockBackOntoTheObject()
        {
            var captured = new StateLaneProbe { intensity = 2f, position = Vector3.one, mode = ProbeMode.On };
            var restored = new StateLaneProbe();

            var bridge = StateBridgeRegistry.Find(typeof(StateLaneProbe));

            using var state = new StateBlockSet();
            bridge.Capture(captured, ownerId: 1, state, default, time: 0);

            Assert.IsTrue(bridge.Apply(restored, ownerId: 1, state));

            Assert.AreEqual(2f, restored.intensity);
            Assert.AreEqual(Vector3.one, restored.position);
            Assert.AreEqual(ProbeMode.On, restored.mode);
        }

        [Test]
        public void Apply_ForAnOwnerTheSetDoesNotHave_SaysSo()
        {
            var probe = new StateLaneProbe();
            var bridge = StateBridgeRegistry.Find(typeof(StateLaneProbe));

            using var state = new StateBlockSet();

            Assert.IsFalse(bridge.Apply(probe, ownerId: 99, state));
        }

        [Test]
        public void PrepareBlocks_GivesAReplaySomewhereToPutEveryRegisteredType()
        {
            // Without this a replay reports every type as unknown until something has happened to
            // write it live first, which is exactly backwards for a machine that is only replaying.
            using var state = new StateBlockSet();
            LiveStateSystem.PrepareBlocks(state);

            Assert.IsNotNull(state.FindByTypeName(typeof(StateLaneProbe.LiveStateBlock).FullName));
        }

        [Test]
        public void ExposedState_TravelsThroughARecordingAndComesBack()
        {
            // The whole point of the generator: a member declared FrameLane.State is in the frame,
            // so a keyframe carries it and a replay puts it back.
            var probe = new StateLaneProbe { intensity = 1.5f, position = new Vector3(4, 5, 6), mode = ProbeMode.Auto };
            var bridge = StateBridgeRegistry.Find(typeof(StateLaneProbe));

            void Producer(ref Frame frame)
                => bridge.Capture(probe, ownerId: 3, frame.state, default, frame.frameNumber);

            var stream = new MemoryStream();
            var recorder = new FrameRecorder();

            FrameGate.AddFrameHeadHandler(Producer);
            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < 3; i++) FrameGate.Pump();
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
                FrameGate.RemoveFrameHeadHandler(Producer);
            }

            var restored = new StateLaneProbe();

            using (var player = new FrameRecordPlayer(new MemoryStream(stream.ToArray())))
            {
                LiveStateSystem.PrepareBlocks(player.state);
                while (player.Advance()) { }

                Assert.IsTrue(bridge.Apply(restored, ownerId: 3, player.state));
            }

            Assert.AreEqual(1.5f, restored.intensity);
            Assert.AreEqual(new Vector3(4, 5, 6), restored.position);
            Assert.AreEqual(ProbeMode.Auto, restored.mode);
        }
    }
}
