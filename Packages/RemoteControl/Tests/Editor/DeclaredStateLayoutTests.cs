// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Editor.LiveDataViewer;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Reading a declared type's element back as values.
    ///
    /// A generated block is a struct, so anything holding its bytes can ask reflection where each
    /// field is. A declared block has no such type -- the element is a payload and the declaration
    /// says what is in it -- so a viewer walking the type would describe the exposed component
    /// instead and show nothing at all. The declaration is the map, and these are the rows it makes.
    /// </summary>
    [TestFixture]
    public class DeclaredStateLayoutTests
    {
        /// <summary>A plain type with no attributes on it, as an asset-declared type would be.</summary>
        public class Fixture
        {
            public float intensity;
            public bool on;
            public Color tint;
            public LightShadows shadows;
        }

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveDataValueLayout.Clear();
        }

        [TearDown]
        public void Finish()
        {
            LiveObjectRegistry.ClearAll();
            StateBridgeRegistry.Unregister(typeof(Fixture));
            LiveDataValueLayout.Clear();
        }

        private static DeclaredStateBridge Declare(params string[] names)
        {
            var members = new LivePropertyDefine[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                members[i] = new LivePropertyDefine
                {
                    name = names[i],
                    path = names[i],
                    lane = FrameLane.State,
                };
            }

            var bridge = DeclaredStateBridge.Build(LiveClass.Register(typeof(Fixture), nameof(Fixture), members));
            StateBridgeRegistry.Register(bridge);
            return bridge;
        }

        [Test]
        public void TheRowsFollowTheDeclaration_AndTheBytesTheyPointAtHoldTheValues()
        {
            var bridge = Declare("intensity", "on", "tint", "shadows");

            var subject = new Fixture
            {
                intensity = 2.75f,
                on = true,
                tint = new Color(0.25f, 0.5f, 0.75f, 1f),
                shadows = LightShadows.Soft,
            };
            var handle = LiveObjectRegistry.Create(subject, "layout-fixture");
            Assert.IsNotNull(handle);

            using var state = new StateBlockSet();
            bridge.Capture(subject, 1, state, default, 0, FrameGate.symbols);

            var block = state.FindDeclared(typeof(Fixture));
            var value = new byte[block.elementSize - block.metaSize];
            block.CopyValueTo(0, value);

            var layout = LiveDataValueLayout.For(typeof(Fixture));

            // Nothing leads the payload. The first declared member sits at offset zero now that the
            // hash of the declaration is gone -- the description says what the block holds, member
            // by member, and says it for a generated block the same way.
            Assert.AreEqual("intensity", layout[0].label);
            Assert.AreEqual(0, layout[0].offset);
            Assert.IsNotNull(bridge.schemaSignature);

            Assert.AreEqual(2.75f, System.BitConverter.ToSingle(value, _OffsetOf(layout, "intensity")));
            Assert.AreNotEqual(0, System.BitConverter.ToInt32(value, _OffsetOf(layout, "on")));
            Assert.AreEqual(0.25f, System.BitConverter.ToSingle(value, _OffsetOf(layout, "tint")));
            Assert.AreEqual((int)LightShadows.Soft,
                System.BitConverter.ToInt32(value, _OffsetOf(layout, "shadows")));
        }

        [Test]
        public void AColourIsOneRow_TheWayItIsForAGeneratedBlock()
        {
            Declare("tint");

            var layout = LiveDataValueLayout.For(typeof(Fixture));
            var tint = layout.Find(f => f.label == "tint");

            Assert.IsNotNull(tint, "the declared value is not described at all");
            Assert.AreEqual(typeof(Color), tint.type, "it was broken up into its floats");
            Assert.IsFalse(tint.isHeading);
        }

        [Test]
        public void EditingTheDeclaration_ChangesTheRows()
        {
            // The layout is cached per type, and a declaration can be edited while the window is
            // open. A cache kept across that points every row at the wrong bytes, which reads as
            // values rather than as a stale cache.
            Declare("intensity");
            Assert.AreEqual(1, LiveDataValueLayout.For(typeof(Fixture)).Count, "one declared value");

            Declare("intensity", "on", "tint");

            Assert.AreEqual(3, LiveDataValueLayout.For(typeof(Fixture)).Count,
                "the rows still describe the declaration that has been replaced");
        }

        private static int _OffsetOf(System.Collections.Generic.List<ValueField> layout, string label)
        {
            var field = layout.Find(f => f.label == label);
            Assert.IsNotNull(field, $"'{label}' is missing from the layout");
            return field.offset;
        }
    }
}
