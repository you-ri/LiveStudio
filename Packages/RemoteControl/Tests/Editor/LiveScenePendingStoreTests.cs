// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Covers the deferred-bind path for asset-backed objects (props / avatars) whose GameObject loads
    /// asynchronously after a live-scene restore:
    /// - a loaded object's component reference is written as a top-level entry keyed by exposed TYPE NAME
    ///   ("prop-1.components[TestChair]") so it survives bundle re-export / component reordering, while
    ///   legacy numeric-index keys ("prop-1.components[0]") still resolve;
    /// - entries that cannot resolve at load time are queued in <see cref="LiveScenePendingStore"/> and
    ///   applied by <see cref="LiveScenePendingStore.ApplyFor"/> once the owning asset loads;
    /// - entries that never bind round-trip verbatim on the next save.
    /// </summary>
    [TestFixture]
    public class LiveScenePendingStoreTests
    {
        // A stand-in for a prop's bone-driven component (e.g. AvatarChair): a MonoBehaviour with exposed
        // fields, discovered by ExposedGameObject._components and serialized as a top-level pending entry.
        [ExposedClass("TestChair")]
        public class TestChairComponent : MonoBehaviour
        {
            [ExposedField] public int restYaw;
            [ExposedField] public float offset;
        }

        private TestExposedObjectResolver _resolver;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            LiveScenePendingStore.Clear();
            ExposedObjectFileRegistry.Clear();

            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestChairComponent>();

            _resolver = new TestExposedObjectResolver();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            LiveScenePendingStore.Clear();
            ExposedObjectFileRegistry.Clear();
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        // Builds a prop-like wrapper: a GameObject with the test component, wrapped by an ExposedGameObject
        // re-keyed to objectId (mirrors PropAsset._Register's ctor -> ReplaceId -> OnEnable).
        private (ExposedGameObject wrapper, TestChairComponent comp) _BuildProp(string objectId, int restYaw, float offset)
        {
            var go = new GameObject("TestProp");
            _spawned.Add(go);
            var comp = go.AddComponent<TestChairComponent>();
            comp.restYaw = restYaw;
            comp.offset = offset;

            var wrapper = new ExposedGameObject(go);
            wrapper.ReplaceId(objectId);
            wrapper.OnEnable();
            return (wrapper, comp);
        }

        private string _Save() =>
            LiveSceneSerializer.LiveSceneToJson(new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver);

        private static JObject _FindEntry(string json, string sourceKey)
        {
            var objects = JObject.Parse(json)["objects"] as JArray;
            return objects?.OfType<JObject>().FirstOrDefault(o => o["@source"]?.Value<string>() == sourceKey);
        }

        // --- Phase 1: source key naming ---

        [Test]
        public void Save_ComponentReference_UsesExposedTypeNameKey_NotIndex()
        {
            _BuildProp("prop-1", restYaw: 42, offset: 1.5f);

            var json = _Save();

            Assert.IsNotNull(_FindEntry(json, "prop-1.components[TestChair]"),
                "component entry should be keyed by exposed type name");
            Assert.IsNull(_FindEntry(json, "prop-1.components[0]"),
                "component entry should NOT use the numeric index key");
        }

        [Test]
        public void Load_LegacyNumericIndexKey_StillResolves()
        {
            // A wrapper is present (as if already loaded); a legacy file references its component by index.
            var (_, comp) = _BuildProp("prop-1", restYaw: 0, offset: 0f);

            var json = new JObject
            {
                ["format"] = LiveSceneSerializer.FormatIdentifier,
                ["formatVersion"] = LiveSceneSerializer.CurrentFormatVersion,
                ["objects"] = new JArray
                {
                    new JObject
                    {
                        ["@source"] = "prop-1.components[0]",
                        ["@type"] = "TestChair",
                        ["restYaw"] = 7,
                    },
                },
            }.ToString();

            LiveSceneSerializer.LiveSceneFromJson(json, _resolver);

            Assert.AreEqual(7, comp.restYaw, "legacy components[0] index key should still resolve and apply");
        }

        // --- Phase 2: deferred bind ---

        [Test]
        public void Load_ThenApplyFor_BindsDeferredComponentEntry()
        {
            // Save an edited prop, then tear it down so the restore cannot resolve it.
            _BuildProp("prop-1", restYaw: 99, offset: 3.25f);
            var saved = _Save();
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            foreach (var go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();

            // Restore: the prop is not loaded yet, so its entries are queued rather than applied/dropped.
            LiveSceneSerializer.LiveSceneFromJson(saved, _resolver);

            // The prop "loads": a fresh wrapper + default component appear under the same id.
            var (_, comp) = _BuildProp("prop-1", restYaw: 0, offset: 0f);
            Assert.AreEqual(0, comp.restYaw, "sanity: fresh component starts at default");

            int applied = LiveScenePendingStore.ApplyFor("prop-1", _resolver);

            Assert.GreaterOrEqual(applied, 1, "ApplyFor should bind at least the component entry");
            Assert.AreEqual(99, comp.restYaw, "deferred entry should apply the saved value");
            Assert.AreEqual(3.25f, comp.offset, 1e-4f);
        }

        [Test]
        public void ApplyFor_OnlyBindsMatchingRootId_LeavesOthersQueued()
        {
            // Two saved props.
            _BuildProp("prop-1", restYaw: 11, offset: 0f);
            _BuildProp("prop-2", restYaw: 22, offset: 0f);
            var saved = _Save();
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            foreach (var go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();

            LiveSceneSerializer.LiveSceneFromJson(saved, _resolver);

            // Only prop-1 loads.
            var (_, comp1) = _BuildProp("prop-1", restYaw: 0, offset: 0f);
            LiveScenePendingStore.ApplyFor("prop-1", _resolver);
            Assert.AreEqual(11, comp1.restYaw, "prop-1 should bind");

            // prop-2 was NOT consumed: it binds only once its own wrapper appears.
            var (_, comp2) = _BuildProp("prop-2", restYaw: 0, offset: 0f);
            int applied2 = LiveScenePendingStore.ApplyFor("prop-2", _resolver);
            Assert.GreaterOrEqual(applied2, 1, "prop-2 entry should still be queued and bind now");
            Assert.AreEqual(22, comp2.restYaw, "prop-2 should bind with its saved value");
        }

        [Test]
        public void UnboundEntry_RoundTripsOnSave()
        {
            _BuildProp("prop-1", restYaw: 55, offset: 0f);
            var saved = _Save();
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            foreach (var go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();

            // Restore without loading the prop: the component entry stays queued.
            LiveSceneSerializer.LiveSceneFromJson(saved, _resolver);

            // Saving again (still unloaded) must re-emit the queued entry verbatim, not drop it.
            var resaved = _Save();

            Assert.IsNotNull(_FindEntry(resaved, "prop-1.components[TestChair]"),
                "an unbound entry should round-trip verbatim on the next save");
        }

        // --- Load-complete re-baseline must not swallow restore-applied overrides ---

        [Test]
        public void RecaptureAfterRestore_PreservesAppliedOverridesInDeltaSave()
        {
            // Save an edited prop, tear it down.
            _BuildProp("avatar-1", restYaw: 7, offset: 0f);
            var saved = _Save();
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            foreach (var go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();

            // Avatar-like: the wrapper EXISTS at restore time (persistent scene object), so the saved
            // component diff is applied immediately during LiveSceneFromJson, not deferred.
            var (_, comp) = _BuildProp("avatar-1", restYaw: 0, offset: 0f);
            LiveSceneSerializer.LiveSceneFromJson(saved, _resolver);
            Assert.AreEqual(7, comp.restYaw, "sanity: entry applies immediately when the target already exists");

            // The asset's load-complete re-baseline (AssetStateSnapshot.CaptureDefaults) runs AFTER the
            // restore. With a blind CaptureDefaults the just-applied 7 became the default and vanished
            // from the next delta save; the preserving variant must keep it dirty.
            var handle = ExposedObjectHandle.CreateUnregistered(ExposedClass.Find(typeof(TestChairComponent)), comp);
            ExposedObjectDefaultRegistry.CaptureDefaultsPreservingOverrides(handle, _resolver);

            var delta = LiveSceneSerializer.LiveSceneToJson(
                new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            var entry = _FindEntry(delta, "avatar-1.components[TestChair]");
            Assert.IsNotNull(entry, "the restore-applied override must survive the load-complete re-baseline");
            Assert.AreEqual(7, entry["restYaw"]?.Value<int>());
        }

        [Test]
        public void CaptureDefaultsPreservingOverrides_KeepsDirtyDefaults_RebaselinesCleanOnes()
        {
            var (_, comp) = _BuildProp("prop-1", restYaw: 0, offset: 1f);
            var handle = ExposedObjectHandle.CreateUnregistered(ExposedClass.Find(typeof(TestChairComponent)), comp);
            ExposedObjectDefaultRegistry.CaptureDefaults(handle, _resolver); // baseline: restYaw=0, offset=1

            comp.restYaw = 7; // overridden since the baseline (dirty)

            ExposedObjectDefaultRegistry.CaptureDefaultsPreservingOverrides(handle, _resolver);

            Assert.AreEqual(0, ExposedObjectDefaultRegistry.GetDefaultToken(handle, "restYaw")?.Value<int>(),
                "an overridden property must keep its pre-override default");
            Assert.AreEqual(1f, ExposedObjectDefaultRegistry.GetDefaultToken(handle, "offset")?.Value<float>() ?? -1f, 1e-4f,
                "an unchanged property keeps (re-adopts) its current value as the default");
        }

        [Test]
        public void CaptureDefaultsPreservingOverrides_NoPriorBaseline_BehavesLikeCaptureDefaults()
        {
            var (_, comp) = _BuildProp("prop-1", restYaw: 3, offset: 0.5f);
            var handle = ExposedObjectHandle.CreateUnregistered(ExposedClass.Find(typeof(TestChairComponent)), comp);

            // Freshly instantiated object (prop load path): no previous baseline exists.
            ExposedObjectDefaultRegistry.CaptureDefaultsPreservingOverrides(handle, _resolver);

            Assert.AreEqual(3, ExposedObjectDefaultRegistry.GetDefaultToken(handle, "restYaw")?.Value<int>(),
                "without a prior baseline the current values become the defaults, like CaptureDefaults");
        }

        [Test]
        public void ApplyFor_DoesNotPolluteDeltaBaseline()
        {
            // Save an edited prop and tear it down.
            _BuildProp("prop-1", restYaw: 8, offset: 0f);
            var saved = _Save();
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            foreach (var go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();

            LiveSceneSerializer.LiveSceneFromJson(saved, _resolver);

            // Fresh prop at defaults (restYaw=0); ApplyFor captures those as the baseline BEFORE applying 8.
            var (_, comp) = _BuildProp("prop-1", restYaw: 0, offset: 0f);
            LiveScenePendingStore.ApplyFor("prop-1", _resolver);
            Assert.AreEqual(8, comp.restYaw);

            // A delta save must still show restYaw=8 as a diff — proving ApplyFor applied with
            // captureDefaults:false and did not bake the applied value into the delta baseline.
            var delta = LiveSceneSerializer.LiveSceneToJson(
                new List<ExposedObjectHandle>(ExposedObjectRegistry.instances), _resolver, SerializeMode.Delta);
            var entry = _FindEntry(delta, "prop-1.components[TestChair]");
            Assert.IsNotNull(entry, "delta should still contain the component entry (value differs from baseline)");
            Assert.AreEqual(8, entry["restYaw"]?.Value<int>(),
                "the applied value must read as a delta, not as the captured baseline");
        }
    }
}
