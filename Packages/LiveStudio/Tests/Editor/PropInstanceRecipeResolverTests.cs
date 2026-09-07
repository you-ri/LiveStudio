// Copyright (c) You-Ri, 2026

using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using Lilium.RemoteControl;
using Lilium.RemoteControl.Frames;
using Lilium.RemoteControl.LiveScene;

namespace Lilium.LiveStudio.EditorTests
{
    /// <summary>
    /// Fetching the prefab a recorded prop instance needs, so a replay can stand it back up.
    ///
    /// A take carries the key an instance was made from, and the maker that rebuilds it can only
    /// answer for a prefab already in hand. A built-in prop is (its catalogue reads from Resources);
    /// an external <c>*.prop.lsb</c> prop is not -- its prefab has to come out of a bundle first, and
    /// nothing was doing that read. The instance was simply never rebuilt, counted as unresolved on
    /// every frame of the replay and reported nowhere.
    ///
    /// What this fixture pins down is the shape of the fix as much as the fix: this resolver fetches
    /// and registers, and never creates. Creating is the reconcile's, which is looking at the
    /// inventory of the frame it is on -- so an instance scrubbed away mid-read is never made at all.
    /// </summary>
    public class PropInstanceRecipeResolverTests
    {
        private const string kKey = "Props/test.prop.lsb";

        /// <summary>A prop whose bundle read the test drives by hand.</summary>
        private sealed class FakeProp : AssetBase, IInstantiableProp
        {
            public TaskCompletionSource<GameObject> pending;
            public Task<GameObject> answer = Task.FromResult<GameObject>(null);
            public int loads;

            public bool supportsInstancing => true;

            public string instanceKey => kKey;

            public Task<GameObject> LoadInstancePrefabAsync()
            {
                loads++;
                return pending != null ? pending.Task : answer;
            }

            public override Task LoadAsync(AssetLoadContext context) => Task.CompletedTask;

            public override void Unload(AssetLoadContext context) { }
        }

        private static readonly FieldInfo kAssetsField = typeof(ExternalAssetManager)
            .GetField("assets", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo kCurrentField = typeof(ExternalAssetManager)
            .GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);

        // The backing field of the static event, so a test can say "a crawl happened" without running one.
        private static readonly FieldInfo kCatalogChangedField = typeof(ExternalAssetManager)
            .GetField("onCatalogChanged", BindingFlags.Static | BindingFlags.NonPublic);

        private FakeProp _prop;
        private GameObject _prefab;

        [SetUp]
        public void StartClean()
        {
            Assert.IsNotNull(kAssetsField, "ExternalAssetManager.assets was renamed; update this test");
            Assert.IsNotNull(kCurrentField, "ExternalAssetManager._current was renamed; update this test");

            PrefabRecipe.Install();
            PropInstanceRecipeResolver.Install();

            _prop = new FakeProp { id = Guid.NewGuid().ToString(), name = "Test Prop" };
            _prefab = new GameObject("test-prop-prefab");

            var manager = new ExternalAssetManager();
            kAssetsField.SetValue(manager, new AssetBase[] { _prop });
            kCurrentField.SetValue(null, manager);
        }

        [TearDown]
        public void Finish()
        {
            kCurrentField.SetValue(null, null);
            PropInstanceRecipeResolver.Reset();

            if (_prefab != null) UnityEngine.Object.DestroyImmediate(_prefab);
            _prefab = null;

            Assert.AreEqual(0, FrameGate.supplyHoldCount, "a hold was left standing");
        }

        [Test]
        public void AKeyNoAssetClaims_IsLeftToOtherResolvers()
        {
            // Several resolvers stand at once and the first non-null answer wins, so one that
            // answered for everything would silence the rest.
            Assert.IsFalse(LiveRecipes.TryGet("nothing/owns/this", out _));
            Assert.AreEqual(0, _prop.loads, "a key this manager does not claim started a read");
            Assert.AreEqual(0, FrameGate.supplyHoldCount);
        }

        [Test]
        public void APrefabAlreadyInHand_IsNotFetchedAgain()
        {
            PrefabRegistry.Register(kKey, _prefab);

            Assert.IsTrue(LiveRecipes.TryGet(kKey, out var recipe));
            Assert.IsNotNull(recipe, "the maker for a registered prefab");
            Assert.AreEqual(0, _prop.loads, "the bundle was opened for a prefab already in memory");
        }

        [Test]
        public void APropAlreadyRead_ResolvesOnTheSameFrame()
        {
            // A bundle read once hands back a finished task. Costing the replay a stall for something
            // that is in memory would be a stall for nothing.
            _prop.answer = Task.FromResult(_prefab);

            Assert.IsTrue(LiveRecipes.TryGet(kKey, out var recipe));
            Assert.IsNotNull(recipe);
            Assert.AreEqual(0, FrameGate.supplyHoldCount, "a finished read held the replay anyway");

            Assert.IsTrue(PrefabRegistry.TryFind(kKey, out var found));
            Assert.AreSame(_prefab, found, "the maker has to be able to find it afterwards");
        }

        [UnityTest]
        public IEnumerator AReadStillRunning_HoldsTheReplayAndIsNotStartedTwice()
        {
            _prop.pending = new TaskCompletionSource<GameObject>();

            Assert.IsFalse(LiveRecipes.TryGet(kKey, out _), "nothing can be made until it lands");
            Assert.AreEqual(1, FrameGate.supplyHoldCount,
                "how long a bundle takes is a property of this machine, not of the take");

            // The reconcile runs at every frame head. Asking again must not open the bundle twice or
            // take a second hold that the one completion cannot balance.
            Assert.IsFalse(LiveRecipes.TryGet(kKey, out _));
            Assert.AreEqual(1, _prop.loads);
            Assert.AreEqual(1, FrameGate.supplyHoldCount);

            _prop.pending.SetResult(_prefab);
            yield return _Landed();
        }

        [UnityTest]
        public IEnumerator AReadThatLands_GivesBackTheHoldAndBringsThePrefab()
        {
            _prop.pending = new TaskCompletionSource<GameObject>();

            Assert.IsFalse(LiveRecipes.TryGet(kKey, out _));
            Assert.AreEqual(1, FrameGate.supplyHoldCount);

            _prop.pending.SetResult(_prefab);
            yield return _Landed();

            Assert.AreEqual(0, FrameGate.supplyHoldCount, "the replay was never let go");
            Assert.IsTrue(PrefabRegistry.TryFind(kKey, out var found));
            Assert.AreSame(_prefab, found);

            // And from here the key is the prefab maker's, which is what actually rebuilds the object.
            Assert.IsTrue(LiveRecipes.TryGet(kKey, out var recipe));
            Assert.IsNotNull(recipe);
        }

        [Test]
        public void AReadThatFound_Nothing_IsNotRetriedEveryFrame()
        {
            // A bundle file that is gone is reported by the loader as an error. Retrying at every
            // frame head would turn one broken prop into a console full of the same line.
            _prop.answer = Task.FromResult<GameObject>(null);

            Assert.IsFalse(LiveRecipes.TryGet(kKey, out _));
            Assert.IsFalse(LiveRecipes.TryGet(kKey, out _));

            Assert.AreEqual(1, _prop.loads);
        }

        [Test]
        public void ACrawl_MakesAFailedReadWorthTryingAgain()
        {
            // The catalogue changing is the only thing that can make the answer different -- the file
            // that was missing is back, or the asset that owns the key has only now registered.
            _prop.answer = Task.FromResult<GameObject>(null);
            Assert.IsFalse(LiveRecipes.TryGet(kKey, out _));
            Assert.AreEqual(1, _prop.loads);

            _RaiseCatalogChanged();
            _prop.answer = Task.FromResult(_prefab);

            Assert.IsTrue(LiveRecipes.TryGet(kKey, out var recipe));
            Assert.IsNotNull(recipe);
            Assert.AreEqual(2, _prop.loads);
        }

        /// <summary>
        /// Waits for the read's continuation, which runs on the main thread -- the next editor update.
        /// </summary>
        private static IEnumerator _Landed()
        {
            for (int i = 0; i < 120 && FrameGate.supplyHoldCount > 0; i++) yield return null;
        }

        private static void _RaiseCatalogChanged()
        {
            Assert.IsNotNull(kCatalogChangedField,
                "ExternalAssetManager.onCatalogChanged was renamed; update this test");

            (kCatalogChangedField.GetValue(null) as Action)?.Invoke();
        }
    }
}
