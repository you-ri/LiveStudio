// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>A live object held inside another one, with state of its own.</summary>
    [LiveClass("StateWalkKnob")]
    public partial class StateWalkKnob
    {
        [LiveField(lane = FrameLane.State)]
        private float _turn;

        public float turn { get => _turn; set => _turn = value; }
    }

    /// <summary>An owner that holds one. What a camera holding a controller looks like, in small.</summary>
    [LiveClass("StateWalkOwner")]
    public partial class StateWalkOwner
    {
        [LiveField(lane = FrameLane.State)]
        private int _channel;

        [LiveField]
        public StateWalkKnob knob = new StateWalkKnob();

        public int channel { get => _channel; set => _channel = value; }
    }

    /// <summary>
    /// The convention this codebase writes exposed members in: a hidden field holding the value and
    /// a property giving it its behaviour.
    /// </summary>
    [LiveClass("StateWalkDimmer")]
    public partial class StateWalkDimmer
    {
        [LiveField(lane = FrameLane.State), Hide]
        [FormerlyNamedAs("level")]
        private float _level;

        /// <summary>Times the setter ran, so a test can tell a write through it from one past it.</summary>
        public int applied;

        [LiveProperty]
        public float level
        {
            get => _level;
            set
            {
                _level = value;
                applied++;
            }
        }
    }

    /// <summary>
    /// The roster the state lane walks: registered objects that have an id, and the live objects
    /// nested inside them.
    /// </summary>
    public class LiveStateWalkTests
    {
        private LiveObjectHandle? _registered;

        [SetUp]
        public void ClearGate()
        {
            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));
        }

        [TearDown]
        public void ReleaseClearGate()
        {
            _registered?.Unregister();
            _registered = null;

            FrameGate.sink = null;
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        private void _Register(object target, string id)
            => _registered = LiveObjectRegistry.Create(target.GetType(), target, id);

        [Test]
        public void ANestedLiveObject_IsCarriedUnderTheOwnersIdAndTheMemberHoldingIt()
        {
            // The address the event lane would use for a write to it, minus the property beyond it:
            // one way of saying where something is, rather than two.
            var owner = new StateWalkOwner { channel = 3 };
            owner.knob.turn = 0.25f;
            _Register(owner, "walk-owner");

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 11);

            var knobs = state.Find<StateWalkKnob.LiveStateBlock>();
            Assert.IsNotNull(knobs, "the nested object's state was not carried at all");

            var index = knobs.IndexOf(FrameGate.symbols.Intern("walk-owner/knob"));
            Assert.GreaterOrEqual(index, 0, "the nested object was not carried under owner id + member name");
            Assert.AreEqual(0.25f, knobs[index].value._turn);
        }

        [Test]
        public void TheOwnerIsCarriedToo_AndUnderItsOwnId()
        {
            var owner = new StateWalkOwner { channel = 7 };
            _Register(owner, "walk-owner");

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 11);

            var owners = state.Find<StateWalkOwner.LiveStateBlock>();
            var index = owners.IndexOf(FrameGate.symbols.Intern("walk-owner"));

            Assert.GreaterOrEqual(index, 0);
            Assert.AreEqual(7, owners[index].value._channel);
        }

        [Test]
        public void ANestedLiveObject_ComesBackOnApply()
        {
            var recorded = new StateWalkOwner { channel = 2 };
            recorded.knob.turn = 0.75f;
            _Register(recorded, "walk-owner");

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            recorded.channel = 0;
            recorded.knob.turn = 0f;

            LiveStateSystem.ApplyFrom(state);

            Assert.AreEqual(2, recorded.channel);
            Assert.AreEqual(0.75f, recorded.knob.turn, "the nested object was not written back");
        }

        [Test]
        public void AnObjectWithNoId_IsNotCarried_AndIsCounted()
        {
            // Exposed and writable, but with no identity for a replay to address. Carrying it would
            // mean every such object sharing one address; counting it is what keeps that visible.
            var owner = new StateWalkOwner { channel = 5 };
            var handle = LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Get<StateWalkOwner>(), owner);

            try
            {
                using var state = new StateBlockSet();
                LiveStateSystem.CaptureInto(state, time: 0);

                var owners = state.Find<StateWalkOwner.LiveStateBlock>();
                for (int i = 0; owners != null && i < owners.count; i++)
                {
                    Assert.AreNotEqual(5, owners[i].value._channel, "an object with no id was carried anyway");
                }

                Assert.GreaterOrEqual(LiveStateSystem.unaddressableObjectCount, 1);
            }
            finally
            {
                handle.Unregister();
            }
        }

        [Test]
        public void AShadowFieldAndItsProperty_AreOneMemberInTheBlock_NamedAfterTheProperty()
        {
            // Both faces of one value. Carrying the field as well would put the same value in the
            // frame twice, and the runtime reads the lane off the field for the same reason.
            var fields = typeof(StateWalkDimmer.LiveStateBlock).GetFields();

            CollectionAssert.AreEquivalent(
                new[] { "level" },
                System.Array.ConvertAll(fields, f => f.Name));
        }

        [Test]
        public void AShadowFieldIsAppliedThroughItsProperty_SoTheWriteHasItsEffect()
        {
            var recorded = new StateWalkDimmer { level = 0.5f };
            _Register(recorded, "walk-dimmer");

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var restored = new StateWalkDimmer();
            var bridge = StateBridgeRegistry.Find(typeof(StateWalkDimmer));

            Assert.IsTrue(bridge.Apply(restored, FrameGate.symbols.Intern("walk-dimmer"), state));
            Assert.AreEqual(0.5f, restored.level);
            Assert.AreEqual(1, restored.applied, "the value was written past the property, so its effect never ran");
        }

        [Test]
        public void TheTransformProxy_CarriesWhereTheObjectActuallyIs_AndPutsItBack()
        {
            // Animation and parenting move an object without anything writing the exposed member,
            // so a lane that carried the last written value would carry a world standing still.
            var go = new GameObject("state-walk-target");
            var proxy = new LiveGameObjectWithTransform(go);
            proxy.OnEnable();

            try
            {
                go.transform.position = new Vector3(1f, 2f, 3f);

                using var state = new StateBlockSet();
                LiveStateSystem.CaptureInto(state, time: 0);

                var blocks = state.Find<LiveGameObjectWithTransform.LiveStateBlock>();
                var index = blocks.IndexOf(FrameGate.symbols.Intern(proxy.id));

                Assert.GreaterOrEqual(index, 0, "the proxy's transform was not carried");
                Assert.AreEqual(new Vector3(1f, 2f, 3f), blocks[index].value.transform.position);

                go.transform.position = new Vector3(9f, 9f, 9f);
                LiveStateSystem.ApplyFrom(state);

                Assert.AreEqual(new Vector3(1f, 2f, 3f), go.transform.position,
                    "applying the state did not move the real Transform");
            }
            finally
            {
                proxy.OnDisable();
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
