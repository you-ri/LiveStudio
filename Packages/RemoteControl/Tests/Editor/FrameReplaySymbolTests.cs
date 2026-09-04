// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.Frames.Recording;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>A recorded object with one value and one collection.</summary>
    [LiveClass("SymbolRow")]
    public partial class SymbolRow
    {
        [LiveField, LiveKey]
        public string name;

        [LiveField(lane = FrameLane.State)]
        public int weight;
    }

    [LiveClass("SymbolOwner")]
    public class SymbolOwner
    {
        [LiveField(lane = FrameLane.State)]
        public int channel;

        [LiveField]
        public List<SymbolRow> rows = new List<SymbolRow>();
    }

    /// <summary>
    /// A replay reads the ids of a supplied frame through the recording's own table.
    ///
    /// The numbers in a recorded frame index the table the file carries. This run interns its own
    /// strings in whatever order it happened to meet them, so the same address is a different number
    /// here -- and reading a recorded id against the live table names whatever is in that slot now.
    ///
    /// The failure was silent and total: the reconcile matched nothing, the blocks were searched
    /// under ids nobody had filed anything under, and replaying a take put nothing back while the
    /// viewer -- which does resolve through the recording -- showed the take as correct.
    /// </summary>
    [TestFixture]
    public class FrameReplaySymbolTests
    {
        private const string kOwnerId = "symbol-owner";

        private SymbolOwner _owner;
        private LiveObjectHandle? _handle;

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<SymbolRow>();
            LiveClass.RegisterFromAttributes<SymbolOwner>();

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            _owner = new SymbolOwner();
            _handle = LiveObjectRegistry.Create(typeof(SymbolOwner), _owner, kOwnerId);
        }

        [TearDown]
        public void Finish()
        {
            _handle?.Unregister();
            LiveObjectRegistry.ClearAll();
            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();
        }

        /// <summary>
        /// Writes one frame of the world as it stands, and hands back the file.
        ///
        /// Through the gate's table, the way a real take is written: the lanes are filed under the
        /// ids of the run being recorded, and the file carries that run's table alongside them.
        /// </summary>
        private byte[] _Record()
        {
            var stream = new MemoryStream();
            var symbols = FrameGate.symbols;

            using (var writer = new FrameRecordWriter(stream,
                       FrameRecorder.DescribeRun(FrameRate.FPS60, 0), leaveOpen: true))
            using (var structure = new StructureBlock())
            using (var state = new StateBlockSet())
            {
                LiveStructureSystem.CaptureInto(structure, symbols);
                LiveStateSystem.CaptureInto(state, time: 0);

                var frame = new Frame
                {
                    frameNumber = 0,
                    frameRate = FrameRate.FPS60,
                    structure = structure,
                    state = state,
                };

                writer.BeginFrame(in frame, symbols);
                writer.WriteStructure(structure, symbols, force: true);
                writer.WriteState(state, symbols);
                writer.EndFrame();
                writer.Close(symbols);
            }

            return stream.ToArray();
        }

        /// <summary>
        /// Starts the run over, the way opening a take in a fresh session does.
        ///
        /// The gate's table goes with it, and the strings interned first here are ones the take
        /// never used -- so the numbering this run hands out cannot line up with the recording's.
        /// Without the shift the two tables agree by luck and the bug hides.
        /// </summary>
        private void _StartAnotherRun()
        {
            _handle?.Unregister();

            FrameGate.ResetState("[test] another run");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            FrameGate.symbols.Intern("a-string-the-take-does-not-use");
            FrameGate.symbols.Intern("another-one");
            FrameGate.symbols.Intern("and-a-third");

            _handle = LiveObjectRegistry.Create(typeof(SymbolOwner), _owner, kOwnerId);
        }

        [Test]
        public void TheRecordingsIdsDoNotMatchThisRuns()
        {
            // The premise. If these ever agreed, the tests below would pass without the fix.
            _owner.rows.Add(new SymbolRow { name = "one", weight = 3 });

            var bytes = _Record();
            _StartAnotherRun();

            using var player = new FrameRecordPlayer(new MemoryStream(bytes));
            Assert.IsTrue(player.Advance());

            var recorded = player.IdOf(kOwnerId);
            Assert.AreNotEqual(FrameSymbolTable.kNone, recorded, "the recording never mentioned the owner");
            Assert.AreNotEqual(FrameGate.symbols.Intern(kOwnerId), recorded,
                "the two tables agree by chance -- this fixture no longer tests what it says it does");
        }

        [Test]
        public void AValueIsPutBack_ThroughTheRecordingsTable()
        {
            _owner.rows.Add(new SymbolRow { name = "one", weight = 3 });
            _owner.channel = 7;

            var bytes = _Record();
            _StartAnotherRun();

            _owner.channel = 0;

            using var player = new FrameRecordPlayer(new MemoryStream(bytes));
            Assert.IsTrue(player.Advance());

            LiveStateSystem.ApplyFrom(player.state, player.IdOf);

            Assert.AreEqual(7, _owner.channel, "the recorded value was looked for under the wrong id");
        }

        [Test]
        public void AnElementIsStoodBackUp_ThroughTheRecordingsTable()
        {
            _owner.rows.Add(new SymbolRow { name = "one" });
            _owner.rows.Add(new SymbolRow { name = "two" });

            var bytes = _Record();
            _StartAnotherRun();

            _owner.rows.Clear();

            using var player = new FrameRecordPlayer(new MemoryStream(bytes));
            Assert.IsTrue(player.Advance());

            LiveStructureSystem.ApplyFrom(player.structure, player.Resolve);

            Assert.AreEqual(2, _owner.rows.Count, "the inventory was read against the wrong table");
            Assert.AreEqual("one", _owner.rows[0].name);
            Assert.AreEqual("two", _owner.rows[1].name);
        }
    }
}
