// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Elements of an exposed collection on the state lane.
    ///
    /// An operation set's slider is dragged many times a second, and it lives in a list rather than
    /// being a member of something registered -- which is what the frame could not reach before.
    /// Each element is addressed the way the rest of the codebase addresses one
    /// (<c>operationSets[0]</c>), so a recorded value has somewhere to go back to.
    /// </summary>
    public class CollectionStateLaneTests
    {
        private const string kId = "collection-state-manager";

        private OperationManager _manager;
        private LiveObjectHandle? _handle;

        [SetUp]
        public void SetUp()
        {
            _manager = new OperationManager();
            _handle = LiveObjectRegistry.Create(typeof(OperationManager), _manager, kId);
        }

        [TearDown]
        public void TearDown()
        {
            _handle?.Unregister();
            _handle = null;
            _manager = null;
        }

        private OperationSet _AddSet(string id, float value)
        {
            var set = new OperationSet { id = id };
            _manager.operationSets.Add(set);
            _manager.SetOperationSetValue(id, value);
            return set;
        }

        [Test]
        public void EachElement_IsCarriedUnderItsOwnAddress()
        {
            _AddSet("set-a", 0.25f);
            _AddSet("set-b", 0.75f);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var blocks = state.Find<OperationSet.LiveStateBlock>();
            Assert.IsNotNull(blocks, "no element of the collection was carried");

            var first = blocks.IndexOf(FrameGate.symbols.Intern(kId + "/operationSets[0]"));
            var second = blocks.IndexOf(FrameGate.symbols.Intern(kId + "/operationSets[1]"));

            Assert.GreaterOrEqual(first, 0, "the first element was not carried under owner id + slot");
            Assert.GreaterOrEqual(second, 0);

            Assert.AreEqual(0.25f, blocks[first].value._manualValue);
            Assert.AreEqual(0.75f, blocks[second].value._manualValue);
        }

        [Test]
        public void TheValuesComeBackOnApply_ElementByElement()
        {
            var a = _AddSet("set-a", 0.25f);
            var b = _AddSet("set-b", 0.75f);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            _manager.SetOperationSetValue("set-a", 0f);
            _manager.SetOperationSetValue("set-b", 0f);

            LiveStateSystem.ApplyFrom(state);

            Assert.AreEqual(0.25f, a.manualValue, 1e-6f);
            Assert.AreEqual(0.75f, b.manualValue, 1e-6f);
        }

        [Test]
        public void TheSliderIsOffTheEventLane_SoADragDoesNotCostARecordAFrame()
        {
            var liveClass = LiveClass.Get<OperationSet>();
            var member = System.Array.Find(liveClass.propertyTypes, p => p.name == "_manualValue");

            Assert.IsNotNull(member, "the slider value is not exposed any more");
            Assert.AreEqual(FrameLane.State, member.lane);
        }
    }
}
