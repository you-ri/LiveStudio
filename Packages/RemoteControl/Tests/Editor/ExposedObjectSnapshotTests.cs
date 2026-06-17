// Copyright (c) You-Ri, 2026
using System;
using System.Linq;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies the public <see cref="ExposedObjectSnapshot"/> facade: capturing a single exposed
    /// object's persistable values to JSON and restoring them onto a different handle for the same
    /// type. This underpins persisting a prop's parameters across an unload/reload cycle, where the
    /// captured snapshot is reapplied to a freshly instantiated object.
    /// </summary>
    [TestFixture]
    public class ExposedObjectSnapshotTests
    {
        #region Test Classes

        public enum SnapBone { None, Head, Hips }

        /// <summary>Value-typed exposed fields plus one non-persistable field.</summary>
        [Serializable]
        [ExposedClass("SnapValue")]
        public class SnapValue
        {
            [ExposedField]
            public int number;

            [ExposedField]
            public Vector3 vector;

            [ExposedField]
            public SnapBone bone;

            [ExposedField(persistable = false)]
            public int runtimeOnly;
        }

        /// <summary>Tracks whether the deserialize callback re-applies state after a restore.</summary>
        [Serializable]
        [ExposedClass("SnapCallback")]
        public class SnapCallback : IExposedDeserializeCallback
        {
            [ExposedField]
            public int value;

            [NonSerialized]
            public int appliedCount;

            void IExposedDeserializeCallback.OnAfterExposedDeserialize()
            {
                appliedCount++;
            }
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            ExposedClass.RegisterFromAttributes<SnapValue>();
            ExposedClass.RegisterFromAttributes<SnapCallback>();

            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
        }

        [Test]
        public void Capture_Then_Restore_RoundTripsValueTypes()
        {
            var source = new SnapValue { number = 5, vector = new Vector3(1f, 2f, 3f), bone = SnapBone.Head };
            var sourceHandle = ExposedObjectRegistry.Create<SnapValue>(source, "snap-src").Value;

            var json = ExposedObjectSnapshot.Capture(sourceHandle);

            var target = new SnapValue();
            var targetHandle = ExposedObjectRegistry.Create<SnapValue>(target, "snap-dst").Value;
            var changed = ExposedObjectSnapshot.Restore(json, targetHandle);

            Assert.IsTrue(changed, "Restore should report that values were applied.");
            Assert.AreEqual(5, target.number);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), target.vector);
            Assert.AreEqual(SnapBone.Head, target.bone);
        }

        [Test]
        public void Restore_InvokesAfterDeserializeCallback()
        {
            var source = new SnapCallback { value = 7 };
            var sourceHandle = ExposedObjectRegistry.Create<SnapCallback>(source, "snap-cb-src").Value;
            var json = ExposedObjectSnapshot.Capture(sourceHandle);

            var target = new SnapCallback();
            var targetHandle = ExposedObjectRegistry.Create<SnapCallback>(target, "snap-cb-dst").Value;
            ExposedObjectSnapshot.Restore(json, targetHandle);

            Assert.AreEqual(7, target.value);
            Assert.GreaterOrEqual(target.appliedCount, 1,
                "Restore should invoke IExposedDeserializeCallback so the target can re-apply state.");
        }

        [Test]
        public void Capture_ExcludesNonPersistableFields()
        {
            var source = new SnapValue { number = 1, runtimeOnly = 99 };
            var sourceHandle = ExposedObjectRegistry.Create<SnapValue>(source, "snap-np").Value;

            var json = ExposedObjectSnapshot.Capture(sourceHandle);
            var parsed = JObject.Parse(json);

            Assert.IsNull(parsed["runtimeOnly"],
                "forPersistence capture must omit fields marked persistable = false.");
            Assert.IsNotNull(parsed["number"], "Persistable fields should still be captured.");
        }
    }
}
