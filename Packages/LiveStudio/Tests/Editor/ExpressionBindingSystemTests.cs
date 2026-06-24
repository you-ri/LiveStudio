// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using NUnit.Framework;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Tests the pure projection <see cref="ExpressionBindingSystem.CollectBindings"/>, which derives the
    /// expression bindings the remote app shows from the generic action sets without needing play mode or an
    /// active manager. Reuses <see cref="FakeInputSource"/> from InputSourceTests.
    /// </summary>
    public class ExpressionBindingSystemTests
    {
        [Test]
        public void CollectBindings_ReturnsOnlySetsWithExpressionAction()
        {
            var sets = new List<ActionSet>
            {
                new ActionSet
                {
                    id = "s1",
                    input = new FakeInputSource(),
                    actions = new List<ActionBase> { new SetExpressionAction { expression = "Happy" } },
                },
                new ActionSet
                {
                    id = "s2",
                    input = new FakeInputSource(),
                    actions = new List<ActionBase> { new SwitchAvatarAction() },
                },
                new ActionSet
                {
                    id = "s3",
                    input = new FakeInputSource(),
                    actions = new List<ActionBase>(),
                },
                null,
            };

            var result = ExpressionBindingSystem.CollectBindings(sets);

            Assert.AreEqual(1, result.Length, "only the set holding a SetExpressionAction is reported");
            Assert.AreEqual("Happy", result[0].expression);
            Assert.AreEqual("s1", result[0].setId);
        }

        [Test]
        public void CollectBindings_EmitsOneEntryPerExpressionAction()
        {
            var sets = new List<ActionSet>
            {
                new ActionSet
                {
                    id = "s1",
                    input = new FakeInputSource(),
                    actions = new List<ActionBase>
                    {
                        new SetExpressionAction { expression = "A" },
                        new SetExpressionAction { expression = "B" },
                    },
                },
            };

            var result = ExpressionBindingSystem.CollectBindings(sets);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("A", result[0].expression);
            Assert.AreEqual("B", result[1].expression);
            Assert.AreEqual("s1", result[0].setId);
            Assert.AreEqual("s1", result[1].setId);
        }

        [Test]
        public void CollectBindings_NullList_ReturnsEmpty()
        {
            var result = ExpressionBindingSystem.CollectBindings(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }
    }
}
