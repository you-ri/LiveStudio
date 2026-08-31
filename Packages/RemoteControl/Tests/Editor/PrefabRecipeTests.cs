// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Standing a spawned object back up from a recording.
    ///
    /// The inventory says what existed; on its own that is not enough to make it again. This is the
    /// other half: the object names the prefab it came from, and a replay makes it from the same
    /// prefab under the same id -- so a scrub back past a spawn takes it away, and a scrub forward
    /// brings it back rather than leaving a world one object short.
    /// </summary>
    [TestFixture]
    public class PrefabRecipeTests
    {
        private const string kPrefabKey = "test-prefab-guid";
        private const string kId = "spawned-1";
        private const string kTypeName = "GameObjectWithTransform";

        private GameObject _prefab;
        private LiveObjectContainer _container;
        private LiveObjectContainer _previousMain;
        private StructureBlock _structure;
        private FrameSymbolTable _symbols;

        [SetUp]
        public void StartClean()
        {
            _prefab = new GameObject("recipe-prefab");
            PrefabRegistry.Register(kPrefabKey, _prefab);

            _previousMain = LiveObjectContainer.main;
            _container = new LiveObjectContainer("recipe-tests", new List<ILiveObject>());
            LiveObjectContainer.main = _container;

            PrefabRecipe.Install();
            LiveStructureSystem.ForgetMade();

            _structure = new StructureBlock();
            _symbols = new FrameSymbolTable();
        }

        [TearDown]
        public void Finish()
        {
            LiveStructureSystem.ForgetMade();

            foreach (var obj in new List<ILiveObject>(_container.objects))
            {
                obj.OnDispose();
                var go = (obj as LiveUnityObjectBase)?.reference as GameObject;
                if (go != null) Object.DestroyImmediate(go);
            }

            LiveObjectContainer.main = _previousMain;
            _container = null;

            if (_prefab != null) Object.DestroyImmediate(_prefab);
            _prefab = null;

            _structure.Dispose();
        }

        /// <summary>Spawns one the way a factory does: instance, wrapper, source key, container.</summary>
        private LiveGameObjectWithTransform _Spawn(string id)
        {
            var instance = Object.Instantiate(_prefab);
            var wrapper = new LiveGameObjectWithTransform(instance) { prefabSourceKey = kPrefabKey };

            wrapper.ReplaceId(id);
            wrapper.OnEnable();
            _container.AddLiveObject(wrapper);
            return wrapper;
        }

        private void _Remove(ILiveObject wrapper)
        {
            wrapper.OnDispose();
            _container.RemoveLiveObject(wrapper);

            var go = (wrapper as LiveUnityObjectBase)?.reference as GameObject;
            if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void AProxy_NamesThePrefabItWasMadeFrom()
        {
            // The same key a saved scene writes as @prefab. Asked of the object rather than derived
            // from its type, because two objects of one type can come from different prefabs.
            var wrapper = _Spawn(kId);

            Assert.AreEqual(kPrefabKey, ((ILiveMadeFromRecipe)wrapper).recipeKey);
        }

        [Test]
        public void TheInventory_CarriesTheKeyThatCanRebuildIt()
        {
            _Spawn(kId);

            LiveStructureSystem.CaptureInto(_structure, _symbols);

            var index = _IndexOf(kId);
            Assert.GreaterOrEqual(index, 0, "the spawned object is not in the inventory");
            Assert.AreEqual(kPrefabKey, _symbols.Resolve(_structure[index].recipeId));
            Assert.AreEqual(kTypeName, _symbols.Resolve(_structure[index].typeId));
        }

        [Test]
        public void SomethingRecordedAndGone_IsMadeAgainFromItsPrefab()
        {
            var wrapper = _Spawn(kId);
            LiveStructureSystem.CaptureInto(_structure, _symbols);
            _Remove(wrapper);

            Assert.IsFalse(LiveObjectRegistry.TryFindById(kId, out _), "the object was not actually taken away");

            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(1, LiveStructureSystem.createdCount);
            Assert.AreEqual(0, LiveStructureSystem.unresolvedCount);

            Assert.IsTrue(LiveObjectRegistry.TryFindById(kId, out var handle),
                "the object was not stood back up under its recorded id");
            Assert.IsInstanceOf<LiveGameObjectWithTransform>(handle.target);
            Assert.IsNotNull(((LiveUnityObjectBase)handle.target).reference,
                "the wrapper came back without anything behind it");
        }

        [Test]
        public void ApplyingTheSameInventoryTwice_DoesNotMakeASecondOne()
        {
            // What makes a replay usable: every frame applies the inventory, and a keyframe applied
            // twice must not reload the world.
            var wrapper = _Spawn(kId);
            LiveStructureSystem.CaptureInto(_structure, _symbols);
            _Remove(wrapper);

            LiveStructureSystem.ApplyFrom(_structure, _symbols);
            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(0, LiveStructureSystem.createdCount, "the second apply made another one");
        }

        [Test]
        public void ScrubbingBackPastTheSpawn_TakesItAway()
        {
            var wrapper = _Spawn(kId);
            LiveStructureSystem.CaptureInto(_structure, _symbols);
            _Remove(wrapper);

            LiveStructureSystem.ApplyFrom(_structure, _symbols);
            Assert.IsTrue(LiveObjectRegistry.TryFindById(kId, out var rebuilt));
            var go = ((LiveUnityObjectBase)rebuilt.target).reference as GameObject;

            // The frame before the spawn: an inventory that does not list it.
            using (var earlier = new StructureBlock())
            {
                LiveStructureSystem.ApplyFrom(earlier, _symbols);
            }

            Assert.AreEqual(1, LiveStructureSystem.destroyedCount);
            Assert.IsFalse(LiveObjectRegistry.TryFindById(kId, out _));
            Assert.IsTrue(go == null, "the object it was wrapping is still in the scene");
        }

        [Test]
        public void SomethingThisReplayDidNotMake_IsLeftAlone()
        {
            // A recording is a record of what it watched, not a claim about everything that exists.
            var standing = _Spawn("was-here-all-along");

            using (var empty = new StructureBlock())
            {
                LiveStructureSystem.ApplyFrom(empty, _symbols);
            }

            Assert.AreEqual(0, LiveStructureSystem.destroyedCount);
            Assert.IsTrue(LiveObjectRegistry.TryFindById("was-here-all-along", out _));

            _Remove(standing);
        }

        [Test]
        public void AKeyNothingCanFind_IsCountedRatherThanThrown()
        {
            // An external bundle that is not loaded. One object that cannot be rebuilt must not stop
            // the rest of the world from being.
            _structure.AddOrUpdate(_symbols.Intern("missing-1"), _symbols.Intern(kTypeName),
                FrameSymbolTable.kNone, _symbols.Intern("no-such-prefab"));

            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(1, LiveStructureSystem.unresolvedCount);
            Assert.AreEqual(0, LiveStructureSystem.createdCount);
        }

        private int _IndexOf(string id)
        {
            var interned = _symbols.Intern(id);
            for (int i = 0; i < _structure.count; i++)
            {
                if (_structure[i].id == interned) return i;
            }
            return -1;
        }
    }
}
