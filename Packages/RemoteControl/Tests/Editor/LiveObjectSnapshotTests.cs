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
    /// Verifies the public <see cref="LiveObjectSnapshot"/> facade: capturing a single exposed
    /// object's persistable values to JSON and restoring them onto a different handle for the same
    /// type. This underpins persisting a prop's parameters across an unload/reload cycle, where the
    /// captured snapshot is reapplied to a freshly instantiated object.
    /// </summary>
    [TestFixture]
    public class LiveObjectSnapshotTests
    {
        #region Test Classes

        public enum SnapBone { None, Head, Hips }

        /// <summary>Value-typed exposed fields plus one non-persistable field.</summary>
        [Serializable]
        [LiveClass("SnapValue")]
        public class SnapValue
        {
            [LiveField]
            public int number;

            [LiveField]
            public Vector3 vector;

            [LiveField]
            public SnapBone bone;

            [LiveField(persistable = false)]
            public int runtimeOnly;
        }

        /// <summary>Tracks whether the deserialize callback re-applies state after a restore.</summary>
        [Serializable]
        [LiveClass("SnapCallback")]
        public class SnapCallback : ILiveDeserializeCallback
        {
            [LiveField]
            public int value;

            [NonSerialized]
            public int appliedCount;

            void ILiveDeserializeCallback.OnAfterLiveDeserialize()
            {
                appliedCount++;
            }
        }

        #endregion

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            LiveClass.RegisterFromAttributes<SnapValue>();
            LiveClass.RegisterFromAttributes<SnapCallback>();

            foreach (var obj in LiveObjectRegistry.instances.ToList()) obj.Unregister();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in LiveObjectRegistry.instances.ToList()) obj.Unregister();
        }

        [Test]
        public void Capture_Then_Restore_RoundTripsValueTypes()
        {
            var source = new SnapValue { number = 5, vector = new Vector3(1f, 2f, 3f), bone = SnapBone.Head };
            var sourceHandle = LiveObjectRegistry.Create<SnapValue>(source, "snap-src").Value;

            var json = LiveObjectSnapshot.Capture(sourceHandle);

            var target = new SnapValue();
            var targetHandle = LiveObjectRegistry.Create<SnapValue>(target, "snap-dst").Value;
            var changed = LiveObjectSnapshot.Restore(json, targetHandle);

            Assert.IsTrue(changed, "Restore should report that values were applied.");
            Assert.AreEqual(5, target.number);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), target.vector);
            Assert.AreEqual(SnapBone.Head, target.bone);
        }

        [Test]
        public void Restore_InvokesAfterDeserializeCallback()
        {
            var source = new SnapCallback { value = 7 };
            var sourceHandle = LiveObjectRegistry.Create<SnapCallback>(source, "snap-cb-src").Value;
            var json = LiveObjectSnapshot.Capture(sourceHandle);

            var target = new SnapCallback();
            var targetHandle = LiveObjectRegistry.Create<SnapCallback>(target, "snap-cb-dst").Value;
            LiveObjectSnapshot.Restore(json, targetHandle);

            Assert.AreEqual(7, target.value);
            Assert.GreaterOrEqual(target.appliedCount, 1,
                "Restore should invoke ILiveDeserializeCallback so the target can re-apply state.");
        }

        [Test]
        public void Capture_ExcludesNonPersistableFields()
        {
            var source = new SnapValue { number = 1, runtimeOnly = 99 };
            var sourceHandle = LiveObjectRegistry.Create<SnapValue>(source, "snap-np").Value;

            var json = LiveObjectSnapshot.Capture(sourceHandle);
            var parsed = JObject.Parse(json);

            Assert.IsNull(parsed["runtimeOnly"],
                "forPersistence capture must omit fields marked persistable = false.");
            Assert.IsNotNull(parsed["number"], "Persistable fields should still be captured.");
        }
    }
}
