// Copyright (c) You-Ri, 2026

using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;
using UnityEngine;

using Lilium.RemoteControl;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Reproduces the reported AvatarController._avatarLayer loss on a load -> save -> reload cycle:
    /// a string override applied by the live-scene restore onto a PERSISTENT avatar-wrapper component
    /// must survive the avatar's load-complete re-baseline (AssetStateSnapshot.CaptureDefaults) and the
    /// following delta save. Mirrors LiveScenePendingStoreTests.RecaptureAfterRestore, but drives the
    /// real avatar re-baseline entry point and uses a string field defaulting to "" (as _avatarLayer).
    /// </summary>
    [TestFixture]
    public class AvatarLayerPersistenceTests
    {
        // Mirrors the shape of AvatarController: an [ExposedClass] component with a string field whose
        // default is the empty string ("" = "do not change the layer").
        [ExposedClass("TestAvatar")]
        public class TestAvatarComponent : MonoBehaviour
        {
            [ExposedField] public string layer = "";
        }

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ExposedClass.Clear();
            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            LiveScenePendingStore.Clear();
            ExposedObjectFileRegistry.Clear();

            ExposedClass.RegisterFromAttributes<ExposedGameObject>();
            ExposedClass.RegisterFromAttributes<TestAvatarComponent>();
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

        // Builds an avatar-like wrapper: a persistent GameObject with the test component, wrapped by an
        // ExposedGameObject re-keyed to objectId (mirrors the AvatarController's scene wrapper).
        private (ExposedGameObject wrapper, TestAvatarComponent comp) _BuildAvatarWrapper(string objectId, string layer)
        {
            var go = new GameObject("Main Avatar");
            _spawned.Add(go);
            var comp = go.AddComponent<TestAvatarComponent>();
            comp.layer = layer;

            var wrapper = new ExposedGameObject(go);
            wrapper.ReplaceId(objectId);
            wrapper.OnEnable();
            return (wrapper, comp);
        }

        private static string _Save(SerializeMode mode) =>
            LiveSceneSerializer.LiveSceneToJson(
                new List<ExposedObjectHandle>(ExposedObjectRegistry.instances),
                DefaultExposedObjectResolver.Instance, mode);

        // Faithful path: the avatar's load-complete re-baseline (AssetStateSnapshot.CaptureDefaults).
        [Test]
        public void AvatarRebaseline_PreservesRestoreAppliedLayerOverride()
        {
            // Save an avatar wrapper whose component override ("Water") differs from the source default ("").
            _BuildAvatarWrapper("avatar-1", layer: "Water");
            var saved = _Save(SerializeMode.Snapshot);

            foreach (var obj in ExposedObjectRegistry.instances.ToList()) obj.Unregister();
            foreach (var go in _spawned) Object.DestroyImmediate(go);
            _spawned.Clear();

            // Reload: the wrapper is a persistent scene object, so it exists at restore time and the saved
            // override is applied immediately during LiveSceneFromJson (not deferred).
            var (_, comp) = _BuildAvatarWrapper("avatar-1", layer: "");
            LiveSceneSerializer.LiveSceneFromJson(saved, DefaultExposedObjectResolver.Instance);
            Assert.AreEqual("Water", comp.layer, "sanity: the restore applies the saved layer override");

            // The avatar model finishes loading AFTER the restore; its load-complete re-baseline runs here.
            AssetStateSnapshot.CaptureDefaults(comp.gameObject);

            // A delta save must still carry the override; a blind re-baseline would have adopted "Water"
            // as the default and dropped it here (the reported avatarLayer loss).
            var delta = _Save(SerializeMode.Delta);
            StringAssert.Contains("\"layer\": \"Water\"", delta,
                "the restore-applied layer override must survive the avatar load-complete re-baseline");
        }
    }
}
