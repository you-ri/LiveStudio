// Copyright (c) You-Ri, 2026

using NUnit.Framework;
using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;

// Declares the source name the test double publishes under. Resolving a name nothing declared
// throws, and the declaration belongs to the assembly that submits frames -- here, this one.
[assembly: FrameSource("test-motion")]

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Recording and replay belong to <see cref="MotionSourceBase"/>, not to any one source.
    ///
    /// This is the thing that changed when the state lane moved down here: a source only has to know
    /// how to sample, and it is recorded and replayed by existing. Before, the code that put a pose
    /// on the frame lived in the VirgoMotion source, so a second source would have been silently
    /// absent from every take -- and a machine replaying one had to load the capture machinery to
    /// get the pose back out. The source below knows nothing about any of that.
    /// </summary>
    public class MotionSourceRecordingTests
    {
        private const string kOwnerAddress = "test-motion-source";

        private GameObject _go;
        private TestMotionSource _source;
        private LiveGameObject _wrapper;

        [SetUp]
        public void SetUp()
        {
            FrameGate.source = null;
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            _go = new GameObject("Test Motion Source") { hideFlags = HideFlags.HideAndDontSave };

            // The frame is filed under the id of the source's GameObject, so the registry has to hold
            // a proxy for it -- without one the source has no address and publishes nowhere.
            _wrapper = new LiveGameObject(_go);
            _wrapper.ReplaceId(kOwnerAddress);
            _wrapper.OnEnable();

            _source = _go.AddComponent<TestMotionSource>();
        }

        [TearDown]
        public void TearDown()
        {
            FrameGate.source = null;

            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
            _source = null;

            _wrapper?.OnDisable();
            _wrapper = null;

            LiveObjectRegistry.ClearAll();
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>
        /// A source that only implements sampling is on the state lane, under its own address.
        /// </summary>
        [Test]
        public void ASourceThatOnlySamples_IsPutOnTheStateLane()
        {
            _source.sample.pose.AsMuscle(7) = 0.33f;
            _source.hasSample = true;

            var frame = _LiveFrame();
            _source.OnFrameHead(ref frame);

            var block = frame.state.Find<AvatarAnimationData>();
            Assert.IsNotNull(block, "nothing was published for the pose type");

            int index = block.IndexOfOwner(FrameGate.symbols.Intern(kOwnerAddress));
            Assert.GreaterOrEqual(index, 0, "the frame was not filed under the source's own address");
            Assert.AreEqual(0.33f, block[index].value.pose.AsMuscle(7), 1e-5f);
        }

        /// <summary>
        /// What reaches the avatar is placed; what is recorded is not. Keeping the two apart is what
        /// lets the reference point be changed and the take drawn again.
        /// </summary>
        [Test]
        public void WhatIsRecorded_IsThePoseBeforePlacement()
        {
            _source.hasSample = true;
            _source.sample.root.position = Vector3.zero;
            _source.placement = Matrix4x4.Translate(new Vector3(5f, 0f, 0f));

            var frame = _LiveFrame();
            _source.OnFrameHead(ref frame);

            var block = frame.state.Find<AvatarAnimationData>();
            int index = block.IndexOfOwner(FrameGate.symbols.Intern(kOwnerAddress));

            Assert.AreEqual(Vector3.zero, block[index].value.root.position,
                "the placement was folded into the recording, which makes the take usable only from where it was shot");
            Assert.AreEqual(new Vector3(5f, 0f, 0f), _source.frameData.root.position,
                "the placement did not reach the avatar");
        }

        /// <summary>
        /// With nothing to sample the previous frame stands. A source that has not received anything
        /// yet must not blank the avatar.
        /// </summary>
        [Test]
        public void WithNothingToSample_TheLastFrameStands()
        {
            _source.hasSample = true;
            _source.sample.pose.AsMuscle(2) = 0.75f;
            var frame = _LiveFrame();
            _source.OnFrameHead(ref frame);

            _source.hasSample = false;
            frame = _LiveFrame();
            _source.OnFrameHead(ref frame);

            Assert.AreEqual(0.75f, _source.frameData.pose.AsMuscle(2), 1e-5f,
                "the avatar was blanked when the source had nothing for this frame");
        }

        /// <summary>A live frame with the lanes a frame head is given.</summary>
        private static Frame _LiveFrame()
            => new Frame
            {
                frameNumber = 1,
                frameRate = FrameRate.FPS60,
                state = new StateBlockSet(),
                symbols = FrameGate.symbols,
                isSupplied = false,
            };

        /// <summary>
        /// A source with nothing behind it: it hands over whatever it was told to and knows nothing
        /// about frames, recordings or the lane.
        /// </summary>
        private sealed class TestMotionSource : MotionSourceBase
        {
            public AvatarAnimationData sample;
            public bool hasSample;
            public Matrix4x4 placement = Matrix4x4.identity;

            protected override FrameSource frameSource => FrameGate.ResolveSource("test-motion");

            protected override Matrix4x4 PlacementMatrix() => placement;

            protected override bool TrySample(out AvatarAnimationData sampled, out long sampledFrom)
            {
                sampled = sample;
                sampledFrom = 1;
                return hasSample;
            }
        }
    }
}
