// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>Something holding a collection of exposed objects.</summary>
    [LiveClass]
    public class Board
    {
        [LiveField] public List<Tile> tiles = new List<Tile>();
    }

    /// <summary>
    /// One element, keyed by its own id and carrying a value every frame.
    ///
    /// Top-level and partial so the generated block lands inside it (<c>Tile.LiveStateBlock</c>).
    /// A nested type gets its block beside itself instead, under a name of its own.
    /// </summary>
    [LiveClass]
    public partial class Tile
    {
        [LiveField, LiveKey] public string id = string.Empty;

        [LiveField(lane = FrameLane.State)] public float value;
    }

    /// <summary>
    /// Elements of an exposed collection on the state lane.
    ///
    /// A value that lives in a list rather than as a member of something registered is what the
    /// frame could not reach before. Each element is addressed the way the rest of the codebase
    /// addresses one (<c>tiles[tile-a]</c>), so a recorded value has somewhere to go back to.
    ///
    /// By the element's key rather than its position, because a position moves every value one along
    /// the moment something is inserted above it.
    ///
    /// ⚠ This used to be written against OperationManager / OperationSet, which was the first thing
    /// to need it. That desk is now off the frame entirely (it is a setting of the operator's
    /// machine), so the mechanism is exercised here against a fixture of its own -- it belongs to
    /// this package anyway, and tying it to whichever feature happened to use it first is what let a
    /// lane decision elsewhere take the coverage with it.
    /// </summary>
    public class CollectionStateLaneTests
    {
        private const string kId = "collection-state-owner";

        private Board _board;
        private LiveObjectHandle? _handle;

        [SetUp]
        public void SetUp()
        {
            LiveClass.RegisterFromAttributes<Board>();
            LiveClass.RegisterFromAttributes<Tile>();

            _board = new Board();
            _handle = LiveObjectRegistry.Create(typeof(Board), _board, kId);
        }

        [TearDown]
        public void TearDown()
        {
            _handle?.Unregister();
            _handle = null;
            _board = null;
        }

        private Tile _AddTile(string id, float value)
        {
            var tile = new Tile { id = id, value = value };
            _board.tiles.Add(tile);
            return tile;
        }

        [Test]
        public void EachElement_IsCarriedUnderItsOwnAddress()
        {
            _AddTile("tile-a", 0.25f);
            _AddTile("tile-b", 0.75f);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            var blocks = state.Find<Tile.LiveStateBlock>();
            Assert.IsNotNull(blocks, "no element of the collection was carried");

            var first = blocks.IndexOf(FrameGate.symbols.Intern(kId + "/tiles[tile-a]"));
            var second = blocks.IndexOf(FrameGate.symbols.Intern(kId + "/tiles[tile-b]"));

            Assert.GreaterOrEqual(first, 0, "the first element was not carried under owner id + key");
            Assert.GreaterOrEqual(second, 0);

            Assert.AreEqual(0.25f, blocks[first].value.value);
            Assert.AreEqual(0.75f, blocks[second].value.value);
        }

        [Test]
        public void TheValuesComeBackOnApply_ElementByElement()
        {
            var a = _AddTile("tile-a", 0.25f);
            var b = _AddTile("tile-b", 0.75f);

            using var state = new StateBlockSet();
            LiveStateSystem.CaptureInto(state, time: 0);

            a.value = 0f;
            b.value = 0f;

            LiveStateSystem.ApplyFrom(state);

            Assert.AreEqual(0.25f, a.value, 1e-6f);
            Assert.AreEqual(0.75f, b.value, 1e-6f);
        }
    }
}
