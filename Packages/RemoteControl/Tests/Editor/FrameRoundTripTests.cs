// Copyright (c) You-Ri, 2026
using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;
using UnityEngine;

using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>An element of a recorded collection: a key, a value, and an effect.</summary>
    [LiveClass("RoundTripRow")]
    public partial class RoundTripRow
    {
        [LiveField, LiveKey]
        public string name;

        [LiveField(lane = FrameLane.State)]
        public int weight;

        [LiveField(onApplied = nameof(ApplyVisible))]
        public bool visible = true;

        /// <summary>Where the effect lands. Not exposed, so only <see cref="ApplyVisible"/> moves it.</summary>
        public bool shown = true;

        public void ApplyVisible() => shown = visible;
    }

    /// <summary>A registered object holding a keyed collection.</summary>
    [LiveClass("RoundTripOwner")]
    public partial class RoundTripOwner
    {
        [LiveField(lane = FrameLane.State)]
        public int channel;

        [LiveField]
        public List<RoundTripRow> rows = new List<RoundTripRow>();
    }

    /// <summary>
    /// An exposed component holding a collection of its own.
    ///
    /// The shape mesh overrides had when they found that the state lane stopped at a component
    /// instead of carrying what it held.
    /// </summary>
    [LiveClass("RoundTripPanel")]
    public partial class RoundTripPanel : MonoBehaviour
    {
        [LiveField(lane = FrameLane.State)]
        public int level;

        [LiveField]
        public List<RoundTripRow> rows = new List<RoundTripRow>();
    }

    /// <summary>An exposed component that holds another exposed GameObject.</summary>
    [LiveClass("RoundTripLink")]
    public partial class RoundTripLink : MonoBehaviour
    {
        [LiveField(lane = FrameLane.State)]
        public int hops;

        [LiveField]
        public LiveGameObject target;
    }

    /// <summary>
    /// A take is written through the gate and played back through the gate, into a run that
    /// numbers its symbols differently.
    ///
    /// The other frame fixtures reach past the gate: they capture into a block and hand the apply
    /// side the recording's table themselves. That is the half that was never wrong. Every one of
    /// the four faults that made a replay do nothing lived on the path between the two -- which
    /// table the frame head chooses, how deep the walk goes, whether a written value has an effect,
    /// and which lane carries a change of shape -- so the fixture that catches them has to be the
    /// one that lets the gate choose.
    /// </summary>
    [TestFixture]
    public class FrameRoundTripTests
    {
        private const string kOwnerId = "round-trip-owner";

        /// <summary>Accepts whatever the event lane hands it. The event lane is not what is under test.</summary>
        private sealed class NullApplier : IEventApplier
        {
            public int count;

            public bool Apply(in ReplayEvent evt, out string error)
            {
                count++;
                error = null;
                return true;
            }
        }

        private RoundTripOwner _owner;
        private LiveObjectHandle? _handle;
        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<LiveGameObject> _proxies = new List<LiveGameObject>();

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<RoundTripRow>();
            LiveClass.RegisterFromAttributes<RoundTripOwner>();
            LiveClass.RegisterFromAttributes<RoundTripPanel>();
            LiveClass.RegisterFromAttributes<RoundTripLink>();

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            LiveStructureSystem.Retain();
            LiveStateSystem.Retain();

            _owner = new RoundTripOwner();
            _handle = LiveObjectRegistry.Create(typeof(RoundTripOwner), _owner, kOwnerId);
        }

        [TearDown]
        public void Finish()
        {
            LiveStructureSystem.applyOnSuppliedFrames = false;
            LiveStateSystem.Release();
            LiveStructureSystem.Release();

            for (int i = 0; i < _proxies.Count; i++) _proxies[i].OnDisable();
            _proxies.Clear();

            LiveObjectRoster.Clear();

            for (int i = 0; i < _scene.Count; i++)
            {
                if (_scene[i] != null) UnityEngine.Object.DestroyImmediate(_scene[i]);
            }
            _scene.Clear();

            _handle?.Unregister();
            _handle = null;

            LiveObjectRegistry.ClearAll();
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>Stands up an exposed GameObject the walk can reach, cleaned up by the teardown.</summary>
        private LiveGameObject _ExposeGameObject(string name, Action<GameObject> build)
        {
            var go = new GameObject(name);
            _scene.Add(go);

            build?.Invoke(go);

            var proxy = new LiveGameObject(go);
            proxy.OnEnable();
            _proxies.Add(proxy);

            LiveObjectRoster.Refresh();
            return proxy;
        }

        /// <summary>Writes frames of the world as it runs, the way a take is written.</summary>
        private byte[] _Record(int frames, Action beforeEach = null)
        {
            var stream = new MemoryStream();
            var recorder = new FrameRecorder();

            recorder.Start(stream, leaveOpen: true);
            FrameGate.sink = recorder;

            try
            {
                for (int i = 0; i < frames; i++)
                {
                    beforeEach?.Invoke();
                    FrameGate.Pump();
                }
            }
            finally
            {
                FrameGate.sink = null;
                recorder.Stop();
            }

            return stream.ToArray();
        }

        /// <summary>
        /// Starts the run over, the way opening a take in a fresh session does.
        ///
        /// The junk interned first is what makes this run number the same addresses differently. A
        /// replay that reads recorded ids against this run's table then finds nothing, which is the
        /// fault the whole fixture exists to keep out -- and without the shift the two tables agree
        /// by luck and it hides.
        /// </summary>
        private void _StartAnotherRun()
        {
            _handle?.Unregister();
            for (int i = 0; i < _proxies.Count; i++) _proxies[i].OnDisable();

            FrameGate.ResetState("[test] another run");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            FrameGate.symbols.Intern("a-string-the-take-does-not-use");
            FrameGate.symbols.Intern("another-one");
            FrameGate.symbols.Intern("and-a-third");

            _handle = LiveObjectRegistry.Create(typeof(RoundTripOwner), _owner, kOwnerId);
            for (int i = 0; i < _proxies.Count; i++) _proxies[i].OnEnable();

            LiveObjectRoster.Refresh();
        }

        /// <summary>Plays the take back through the gate, letting the frame head choose its table.</summary>
        private int _Replay(byte[] bytes, int frames)
        {
            var applier = new NullApplier();

            using (var replayer = new FrameReplayer(new MemoryStream(bytes), applier))
            {
                FrameGate.source = replayer;
                LiveStructureSystem.applyOnSuppliedFrames = true;

                try
                {
                    for (int i = 0; i < frames; i++) FrameGate.Pump();
                }
                finally
                {
                    FrameGate.source = null;
                    LiveStructureSystem.applyOnSuppliedFrames = false;
                }
            }

            return applier.count;
        }

        // ---------------------------------------------------------------- the four faults

        [Test]
        public void ValuesAndElements_ComeBack_ThroughTheGate()
        {
            // ① The recorded ids index the file's table. Reading them against this run's table
            // finds whatever happens to sit in those slots now, which is nothing -- silently.
            _owner.channel = 7;
            _owner.rows.Add(new RoundTripRow { name = "one", weight = 3 });
            _owner.rows.Add(new RoundTripRow { name = "two", weight = 5 });

            var bytes = _Record(4);
            _StartAnotherRun();

            _owner.channel = 0;
            _owner.rows.Clear();

            _Replay(bytes, 4);

            Assert.AreEqual(7, _owner.channel, "the owner's value never came back");
            Assert.AreEqual(2, _owner.rows.Count, "the elements were not stood back up");
            Assert.AreEqual("one", _owner.rows[0].name);
            Assert.AreEqual(3, _owner.rows[0].weight, "the element was created but its value is the default");
            Assert.AreEqual("two", _owner.rows[1].name);
            Assert.AreEqual(5, _owner.rows[1].weight);
        }

        [Test]
        public void AValueInsideAnExposedComponent_ComesBack()
        {
            // ② The two walks reach a component the same way, but only the structure lane went on
            // into what it holds. The shape came back and the values did not, so a replay stood the
            // elements up with their defaults and nothing about the take showed.
            RoundTripPanel panel = null;
            var proxy = _ExposeGameObject("round-trip-host", go => panel = go.AddComponent<RoundTripPanel>());

            panel.level = 4;
            panel.rows.Add(new RoundTripRow { name = "a", weight = 11 });
            panel.rows.Add(new RoundTripRow { name = "b", weight = 22 });

            var bytes = _Record(4);
            _StartAnotherRun();

            panel.level = 0;
            panel.rows.Clear();

            _Replay(bytes, 4);

            Assert.AreEqual(4, panel.level, "the component's own value never came back");
            Assert.AreEqual(2, panel.rows.Count, "the component's elements were not stood back up");
            Assert.AreEqual(11, panel.rows[0].weight,
                "the element exists but its value is the default -- the state lane stopped at the component");
            Assert.AreEqual(22, panel.rows[1].weight);
            Assert.IsNotNull(proxy);
        }

        [Test]
        public void AnEffectDeclaredWithOnApplied_RunsOnReplay()
        {
            // ③ The state lane writes the value and nothing else. Where the value only means
            // something because a setter acts on it, a replay puts the number back and leaves the
            // world looking the way it did before.
            _owner.rows.Add(new RoundTripRow { name = "one", visible = false });

            var bytes = _Record(4);
            _StartAnotherRun();

            _owner.rows.Clear();

            _Replay(bytes, 4);

            Assert.AreEqual(1, _owner.rows.Count);
            Assert.IsFalse(_owner.rows[0].visible, "the value did not come back");
            Assert.IsFalse(_owner.rows[0].shown, "the value came back but nothing acted on it");
        }

        [Test]
        public void AChangeOfShape_SettlesInsteadOfOscillating()
        {
            // ④ Once the inventory carries shape, leaving the same add on the event lane makes a
            // replay do it twice: the reconcile brings the collection to the recorded shape at the
            // frame head, and the event stands another element up later in the same frame.
            _owner.rows.Add(new RoundTripRow { name = "one", weight = 1 });

            var added = false;
            var bytes = _Record(6, () =>
            {
                if (added) return;

                added = true;
                _owner.rows.Add(new RoundTripRow { name = "two", weight = 2 });
            });

            _StartAnotherRun();
            _owner.rows.Clear();

            _Replay(bytes, 6);

            Assert.AreEqual(2, _owner.rows.Count,
                "the shape was carried by both lanes -- the element was stood up twice");
            CollectionAssert.AreEqual(new[] { "one", "two" },
                new[] { _owner.rows[0].name, _owner.rows[1].name });
        }

        [Test]
        public void ReplayingTheSameTakeTwice_LandsInTheSamePlace()
        {
            // Applying is a reconcile, so running it again over a world it already matches has to
            // be a no-op. Anything that grows or empties here is a rule written on one side of the
            // walk and not the other.
            _owner.channel = 9;
            _owner.rows.Add(new RoundTripRow { name = "one", weight = 3 });
            _owner.rows.Add(new RoundTripRow { name = "two", weight = 5 });

            var bytes = _Record(4);
            _StartAnotherRun();

            _owner.rows.Clear();

            _Replay(bytes, 4);
            var afterFirst = _owner.rows.Count;

            _Replay(bytes, 4);

            Assert.AreEqual(afterFirst, _owner.rows.Count, "a second pass changed the world again");
            Assert.AreEqual(2, _owner.rows.Count);
            Assert.AreEqual(9, _owner.channel);
        }

        // ---------------------------------------------------------------- the walk re-enters

        [Test]
        public void AnExposedGameObjectHeldByAComponent_DoesNotCutTheComponentWalkShort()
        {
            // Carrying what a component holds made the component loop re-entrant: a member pointing
            // at another exposed GameObject sends the walk back into the same GetComponents list.
            // Refilled underneath, the outer loop then reads the inner object's components under
            // its own index and stops at its length, and the rest of the outer object's components
            // leave the frame with nothing reporting it.
            var inner = _ExposeGameObject("round-trip-inner", go => go.AddComponent<RoundTripPanel>());

            RoundTripPanel outerPanel = null;
            _ExposeGameObject("round-trip-outer", go =>
            {
                var link = go.AddComponent<RoundTripLink>();
                link.hops = 1;
                link.target = inner;

                outerPanel = go.AddComponent<RoundTripPanel>();
                outerPanel.level = 8;
            });

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var panels = state.Find<RoundTripPanel.LiveStateBlock>();
            Assert.IsNotNull(panels, "no panel state was carried at all");

            var outerId = FrameGate.symbols.Intern(
                _proxies[1].id + "/components[RoundTripPanel]");
            var index = panels.IndexOf(outerId);

            Assert.GreaterOrEqual(index, 0,
                "the component after the one that led the walk away was dropped from the frame");
            Assert.AreEqual(8, panels[index].value.level);
        }
    }
}
