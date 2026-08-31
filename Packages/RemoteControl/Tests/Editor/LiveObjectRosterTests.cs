// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// An exposed scene component: not registered, found when asked for, and answering to its type
    /// name. The kind of object a lane walking only the registry never sees.
    /// </summary>
    [LiveClass("RosterProbe")]
    public partial class RosterProbe : MonoBehaviour
    {
        [LiveField(lane = FrameLane.State)]
        private float _reading;

        public float reading { get => _reading; set => _reading = value; }
    }

    /// <summary>
    /// Which objects a frame carries: the registry, plus the exposed scene components that answer
    /// to their type name -- the same address REST resolves them by.
    /// </summary>
    public class LiveObjectRosterTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        [SetUp]
        public void StartClean()
        {
            LiveObjectRoster.Clear();
        }

        [TearDown]
        public void Finish()
        {
            for (int i = 0; i < _made.Count; i++)
            {
                if (_made[i] != null) Object.DestroyImmediate(_made[i]);
            }
            _made.Clear();

            LiveObjectRoster.Clear();
        }

        private RosterProbe _Place(string name)
        {
            var go = new GameObject(name, typeof(RosterProbe));
            _made.Add(go);
            return go.GetComponent<RosterProbe>();
        }

        [Test]
        public void AnExposedSceneComponent_IsCarriedUnderItsTypeName()
        {
            var probe = _Place("roster-probe");
            probe.reading = 4.5f;

            LiveObjectRoster.Refresh();

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var blocks = state.Find<RosterProbe.LiveStateBlock>();
            Assert.IsNotNull(blocks, "the scene component's state was not carried at all");

            var index = blocks.IndexOf(FrameGate.symbols.Intern("RosterProbe"));
            Assert.GreaterOrEqual(index, 0, "it was not carried under the name it answers to");
            Assert.AreEqual(4.5f, blocks[index].value._reading);
        }

        [Test]
        public void TwoOfATypeAreCarriedByNeither_BecauseOneNameCannotSayWhich()
        {
            // Both answer to "RosterProbe". Carrying them both under it would put two objects at one
            // address and record whichever was walked last.
            _Place("probe-a").reading = 1f;
            _Place("probe-b").reading = 2f;

            LiveObjectRoster.Refresh();

            Assert.AreEqual(0, _CountInRoster("RosterProbe"));
        }

        [Test]
        public void OneRegisteredWithAnIdOfItsOwn_IsLeftToTheRegistry()
        {
            // A request that reached it first registers it, and the registry walk carries it from
            // then on. Keeping it here as well would write the same object twice.
            var probe = _Place("roster-probe");
            var handle = LiveObjectRegistry.Create(typeof(RosterProbe), probe, "roster-probe-id");

            try
            {
                LiveObjectRoster.Refresh();

                Assert.AreEqual(0, _CountInRoster("RosterProbe"));
            }
            finally
            {
                handle?.Unregister();
            }
        }

        [Test]
        public void AStateValue_ComesBackOnApply()
        {
            var probe = _Place("roster-probe");
            probe.reading = 9f;

            LiveObjectRoster.Refresh();

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            probe.reading = 0f;
            LiveStateSystem.ApplyFrom(state);

            Assert.AreEqual(9f, probe.reading);
        }

        private static int _CountInRoster(string id)
        {
            var entries = LiveObjectRoster.sceneComponents;
            var count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].id == id) count++;
            }
            return count;
        }
    }
}
