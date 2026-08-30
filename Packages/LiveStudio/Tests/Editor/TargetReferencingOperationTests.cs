// Copyright (c) You-Ri, 2026

using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Tests the generic operations that drive an arbitrary exposed object addressed by its registry id
    /// (<see cref="InvokeFunctionOperation"/> / <see cref="SetPropertyOperation"/>), created from the remote app's
    /// "bind to key" affordance. A fake exposed object stands in for a real control so no scene is needed.
    /// </summary>
    public class TargetReferencingOperationTests
    {
        const string kTargetId = "target-1";

        [LiveClass]
        public class FakeTarget
        {
            [LiveField] public bool flag;

            // A member the state lane carries, to check that a deck key writing it is treated the
            // same way a remote write to it is.
            [LiveField(lane = FrameLane.State)] public float carried;

            // Its counterpart in the input lane, so the two can be told apart in the same fixture.
            [LiveField] public float requested;
            public int invokeCount;

            // Records the last argument received by SetValue so an argument-bearing invoke can be asserted.
            public int lastValue;

            // Read-only keyed array standing in for an avatar's expressions[]: a bound action targets
            // an element's float weight by its stable key, e.g. "weights[Beta].weight".
            [LiveField] public WeightedEntry[] weights;

            [LiveFunction]
            public void DoThing() => invokeCount++;

            [LiveFunction]
            public void SetValue(int value)
            {
                lastValue = value;
                invokeCount++;
            }
        }

        [LiveClass("FakeWeighted")]
        public class WeightedEntry
        {
            private string _key = string.Empty;

            public WeightedEntry() { }

            public WeightedEntry(string key) { _key = key ?? string.Empty; }

            [LiveProperty, LiveKey] public string key => _key;

            [LiveProperty] public float weight { get; set; }

            // A nested [LiveFunction] so an InvokeFunctionOperation can target it via a property path
            // (the generic shape of StageManager's set-element WarpTo).
            [LiveFunction] public void Bump(float amount) => weight += amount;
        }

        FakeTarget _target;

        [SetUp]
        public void SetUp()
        {
            // A step here is a step, not an interval of wall time: these tests fire an operation and
            // run the frame head it posted to, twice in a row inside a millisecond. The live clock
            // would put both at the same position on the time axis and skip the second frame.
            Lilium.RemoteControl.Frames.FrameGate.SetClock(
                new Lilium.RemoteControl.Frames.FrameCounterClock(
                    Lilium.RemoteControl.FrameRate.FPS60));

            // One pump before anything is posted, so the gate counts as running.
            //
            // A posted input is applied on the spot when nothing is going to pump, and whether that
            // was true depended on whether some earlier fixture had reset the gate -- which made
            // these tests pass alone and fail in a suite.
            Lilium.RemoteControl.Frames.FrameGate.Pump();

            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<WeightedEntry>();
            LiveClass.RegisterFromAttributes<FakeTarget>();
            _target = new FakeTarget
            {
                weights = new[] { new WeightedEntry("Alpha"), new WeightedEntry("Beta") },
            };
            LiveObjectRegistry.Create(_target, kTargetId);
        }

        [TearDown]
        public void TearDown()
        {
            LiveObjectRegistry.ClearAll();
            Lilium.RemoteControl.Frames.FrameGate.RestoreDefaultClock();
        }

        /// <summary>
        /// Applies the operation and runs the frame head it posted its work to.
        ///
        /// An operation no longer writes inside Apply: it queues the write through the frame gate so
        /// it takes its place in the order with everything else that arrived, and that lands at the
        /// head of the next frame. One frame is the cost of the ordering.
        /// </summary>
        static void Fire(OperationBase operation, in OperationContext context)
        {
            operation.Apply(in context);
            Lilium.RemoteControl.Frames.FrameGate.Pump();
        }

        static OperationContext Triggered(bool active)
            => new OperationContext(active ? 1f : 0f, pressed: active, released: !active, active: active, triggered: true);

        [Test]
        public void InvokeFunctionOperation_OnTrigger_InvokesTargetFunction()
        {
            var action = new InvokeFunctionOperation { targetId = kTargetId, functionName = "DoThing" };

            Fire(action, Triggered(active: true));

            Assert.AreEqual(1, _target.invokeCount, "the bound function runs on the trigger pulse");
        }

        [Test]
        public void InvokeFunctionOperation_WithArgsJson_PassesArgumentToTarget()
        {
            var action = new InvokeFunctionOperation
            {
                targetId = kTargetId,
                functionName = "SetValue",
                argsJson = "[42]",
            };

            Fire(action, Triggered(active: true));

            Assert.AreEqual(42, _target.lastValue,
                "the stored JSON argument is deserialized to the parameter type and passed to the function");
            Assert.AreEqual(1, _target.invokeCount, "the argument-bearing function runs on the trigger pulse");
        }

        [Test]
        public void InvokeFunctionOperation_NestedFunction_ViaPropertyPath_InvokesOnElement()
        {
            // WarpTo-shaped: the function lives on a keyed array element, not the target object directly.
            var action = new InvokeFunctionOperation
            {
                targetId = kTargetId,
                functionName = "Bump",
                propertyPath = "weights/Beta",
                argsJson = "[0.25]",
            };

            Assert.IsTrue(action.valid, "valid resolves the nested function through the property path");

            Fire(action, Triggered(active: true));

            Assert.AreEqual(0.25f, _target.weights[1].weight, 1e-5f,
                "the nested function runs on the element resolved by propertyPath, with the deserialized arg");
            Assert.AreEqual(0f, _target.weights[0].weight, "the other element is untouched");
        }

        [Test]
        public void InvokeFunctionOperation_EmptyArgsJson_InvokesWithoutArguments()
        {
            // An empty argsJson must keep the original no-argument behaviour (regression guard for the
            // back-compat of the new field on operations saved before it existed).
            var action = new InvokeFunctionOperation
            {
                targetId = kTargetId,
                functionName = "DoThing",
                argsJson = string.Empty,
            };

            Fire(action, Triggered(active: true));

            Assert.AreEqual(1, _target.invokeCount, "the no-argument function still runs when argsJson is empty");
        }

        [Test]
        public void InvokeFunctionOperation_NotTriggered_DoesNothing()
        {
            var action = new InvokeFunctionOperation { targetId = kTargetId, functionName = "DoThing" };
            var context = new OperationContext(0f, pressed: false, released: false, active: false, triggered: false);

            Fire(action, context);

            Assert.AreEqual(0, _target.invokeCount, "no invoke without a trigger pulse");
        }

        [Test]
        public void InvokeFunctionOperation_Valid_TracksTargetResolution()
        {
            var resolves = new InvokeFunctionOperation { targetId = kTargetId, functionName = "DoThing" };
            Assert.IsTrue(resolves.valid, "id resolves and the function exists");

            var missingId = new InvokeFunctionOperation { targetId = "nope", functionName = "DoThing" };
            Assert.IsFalse(missingId.valid, "an unresolved id is invalid");

            var missingFn = new InvokeFunctionOperation { targetId = kTargetId, functionName = "Ghost" };
            Assert.IsFalse(missingFn.valid, "a missing function is invalid even when the id resolves");
        }

        [Test]
        public void InvokeFunctionOperation_DanglingId_OnTrigger_IsNoOp()
        {
            var action = new InvokeFunctionOperation { targetId = "nope", functionName = "DoThing" };

            Assert.DoesNotThrow(() => Fire(action, Triggered(active: true)));
            Assert.AreEqual(0, _target.invokeCount);
        }

        [Test]
        public void SetPropertyOperation_DrivesBoolFromActive()
        {
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "flag" };

            Fire(action, Triggered(active: true));
            Assert.IsTrue(_target.flag, "active writes the bool on");

            Fire(action, Triggered(active: false));
            Assert.IsFalse(_target.flag, "inactive writes the bool off");
        }

        [Test]
        public void SetPropertyOperation_Valid_TracksTargetResolution()
        {
            var resolves = new SetPropertyOperation { targetId = kTargetId, propertyPath = "flag" };
            Assert.IsTrue(resolves.valid);

            var missingId = new SetPropertyOperation { targetId = "nope", propertyPath = "flag" };
            Assert.IsFalse(missingId.valid, "an unresolved id is invalid");

            var missingProp = new SetPropertyOperation { targetId = kTargetId, propertyPath = "ghost" };
            Assert.IsFalse(missingProp.valid, "a missing property is invalid even when the id resolves");
        }

        [Test]
        public void SetPropertyOperation_DanglingId_IsNoOp()
        {
            var action = new SetPropertyOperation { targetId = "nope", propertyPath = "flag" };

            Assert.DoesNotThrow(() => Fire(action, Triggered(active: true)));
            Assert.IsFalse(_target.flag);
        }

        static OperationContext Value(float v)
            => new OperationContext(v, pressed: false, released: false, active: v > 0f, triggered: false);

        [Test]
        public void SetPropertyOperation_DrivesFloatByKey_FromContinuousValue()
        {
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights[Beta].weight" };

            Fire(action, Value(0.5f));

            Assert.AreEqual(0.5f, _target.weights[1].weight, "the float weight is written from context.value via the key path");
            Assert.AreEqual(0f, _target.weights[0].weight, "the other entry is untouched");
        }

        [Test]
        public void SetPropertyOperation_OnAStateLaneMember_LeavesNoInputRecord()
        {
            // The deck key and the remote write are the same write. Only the REST path honoured the
            // lane, so the same value recorded once or twice depending on which control was used.
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "carried" };
            var omitted = Lilium.RemoteControl.Frames.FrameGate.omittedRecordCount;

            Fire(action, Value(0.5f));

            Assert.AreEqual(0.5f, _target.carried, 1e-5f, "the write still lands");

            using var frame = new Lilium.RemoteControl.Frames.InputFrame();
            Assert.AreEqual(Lilium.RemoteControl.Frames.FrameLookup.Found,
                Lilium.RemoteControl.Frames.FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(0, frame.inputCount, "the state lane already carries it");
            Assert.AreEqual(omitted + 1, Lilium.RemoteControl.Frames.FrameGate.omittedRecordCount,
                "counted, so 'no input for this' can be told from 'the input went missing'");
        }

        [Test]
        public void SetPropertyOperation_OnAnInputLaneMember_IsStillRecorded()
        {
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "requested" };

            Fire(action, Value(0.5f));

            using var frame = new Lilium.RemoteControl.Frames.InputFrame();
            Assert.AreEqual(Lilium.RemoteControl.Frames.FrameLookup.Found,
                Lilium.RemoteControl.Frames.FrameGate.buffer.TryReadLatest(frame));

            Assert.AreEqual(1, frame.inputCount);
            Assert.AreEqual("operation",
                Lilium.RemoteControl.Frames.FrameGate.symbols.Resolve(frame[0].sourceId),
                "recorded as coming from the operator's own controls");
        }

        [Test]
        public void SetPropertyOperation_FloatKeyPath_SurvivesReorder()
        {
            var beta = _target.weights[1];
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights[Beta].weight" };

            Fire(action, Value(0.3f));
            Assert.AreEqual(0.3f, beta.weight);

            // Reorder so Beta is no longer at index 1.
            (_target.weights[0], _target.weights[1]) = (_target.weights[1], _target.weights[0]);

            Fire(action, Value(0.8f));
            Assert.AreEqual(0.8f, beta.weight, "the key drives the same Beta entry after reordering");
        }

        [Test]
        public void SetPropertyOperation_SlashKeyPath_ResolvesViaNormalization()
        {
            // The remote app sends the transport (slash) form; SetPropertyOperation normalizes it to
            // DotBracket before lookup, so "weights/Beta/weight" drives the same entry as the bracket form.
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights/Beta/weight" };

            Fire(action, Value(0.7f));

            Assert.AreEqual(0.7f, _target.weights[1].weight, "the slash key path resolves by key after normalization");
            Assert.IsTrue(action.valid, "valid also resolves the slash key path");
        }

        [Test]
        public void SetPropertyOperation_KeyPath_TracksValidResolution()
        {
            var resolves = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights[Beta].weight" };
            Assert.IsTrue(resolves.valid);

            var missingKey = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights[Zzz].weight" };
            Assert.IsFalse(missingKey.valid, "an unknown key is invalid even when the id resolves");
        }
    }
}
