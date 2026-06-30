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

        [ExposedClass]
        public class FakeTarget
        {
            [ExposedField] public bool flag;
            public int invokeCount;

            // Read-only keyed array standing in for an avatar's expressions[]: a bound action targets
            // an element's float weight by its stable key, e.g. "weights[Beta].weight".
            [ExposedField] public WeightedEntry[] weights;

            [ExposedFunction]
            public void DoThing() => invokeCount++;
        }

        [ExposedClass("FakeWeighted")]
        public class WeightedEntry
        {
            private string _key = string.Empty;

            public WeightedEntry() { }

            public WeightedEntry(string key) { _key = key ?? string.Empty; }

            [ExposedProperty, ExposedKey] public string key => _key;

            [ExposedProperty] public float weight { get; set; }
        }

        FakeTarget _target;

        [SetUp]
        public void SetUp()
        {
            ExposedObjectRegistry.ClearAll();
            ExposedClass.RegisterFromAttributes<WeightedEntry>();
            ExposedClass.RegisterFromAttributes<FakeTarget>();
            _target = new FakeTarget
            {
                weights = new[] { new WeightedEntry("Alpha"), new WeightedEntry("Beta") },
            };
            ExposedObjectRegistry.Create(_target, kTargetId);
        }

        [TearDown]
        public void TearDown()
        {
            ExposedObjectRegistry.ClearAll();
        }

        static OperationContext Triggered(bool active)
            => new OperationContext(active ? 1f : 0f, pressed: active, released: !active, active: active, triggered: true);

        [Test]
        public void InvokeFunctionOperation_OnTrigger_InvokesTargetFunction()
        {
            var action = new InvokeFunctionOperation { targetId = kTargetId, functionName = "DoThing" };

            action.Apply(Triggered(active: true));

            Assert.AreEqual(1, _target.invokeCount, "the bound function runs on the trigger pulse");
        }

        [Test]
        public void InvokeFunctionOperation_NotTriggered_DoesNothing()
        {
            var action = new InvokeFunctionOperation { targetId = kTargetId, functionName = "DoThing" };
            var context = new OperationContext(0f, pressed: false, released: false, active: false, triggered: false);

            action.Apply(context);

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

            Assert.DoesNotThrow(() => action.Apply(Triggered(active: true)));
            Assert.AreEqual(0, _target.invokeCount);
        }

        [Test]
        public void SetPropertyOperation_DrivesBoolFromActive()
        {
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "flag" };

            action.Apply(Triggered(active: true));
            Assert.IsTrue(_target.flag, "active writes the bool on");

            action.Apply(Triggered(active: false));
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

            Assert.DoesNotThrow(() => action.Apply(Triggered(active: true)));
            Assert.IsFalse(_target.flag);
        }

        static OperationContext Value(float v)
            => new OperationContext(v, pressed: false, released: false, active: v > 0f, triggered: false);

        [Test]
        public void SetPropertyOperation_DrivesFloatByKey_FromContinuousValue()
        {
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights[Beta].weight" };

            action.Apply(Value(0.5f));

            Assert.AreEqual(0.5f, _target.weights[1].weight, "the float weight is written from context.value via the key path");
            Assert.AreEqual(0f, _target.weights[0].weight, "the other entry is untouched");
        }

        [Test]
        public void SetPropertyOperation_FloatKeyPath_SurvivesReorder()
        {
            var beta = _target.weights[1];
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights[Beta].weight" };

            action.Apply(Value(0.3f));
            Assert.AreEqual(0.3f, beta.weight);

            // Reorder so Beta is no longer at index 1.
            (_target.weights[0], _target.weights[1]) = (_target.weights[1], _target.weights[0]);

            action.Apply(Value(0.8f));
            Assert.AreEqual(0.8f, beta.weight, "the key drives the same Beta entry after reordering");
        }

        [Test]
        public void SetPropertyOperation_SlashKeyPath_ResolvesViaNormalization()
        {
            // The remote app sends the transport (slash) form; SetPropertyOperation normalizes it to
            // DotBracket before lookup, so "weights/Beta/weight" drives the same entry as the bracket form.
            var action = new SetPropertyOperation { targetId = kTargetId, propertyPath = "weights/Beta/weight" };

            action.Apply(Value(0.7f));

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
