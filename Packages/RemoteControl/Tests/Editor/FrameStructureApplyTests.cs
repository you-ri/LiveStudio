// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl.Frames;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Making the world match an inventory.
    ///
    /// Capturing an inventory only says what was there. Applying one is what makes a replay a replay:
    /// scrub back past a spawn and the thing has to go, scrub forward and it has to come back. Doing
    /// that by assignment rather than by reconcile is the failure this fixture is shaped around --
    /// it leaves the spawn standing, and every later frame then writes state into a world with one
    /// object too many.
    /// </summary>
    [TestFixture]
    public class FrameStructureApplyTests
    {
        /// <summary>An object that knows what made it, which is what the capture half reads.</summary>
        [LiveClass]
        public class Made : Lamp, ILiveMadeFromRecipe
        {
            public string recipeKey => kRecipe;
        }

        [LiveClass]
        public class Lamp : ILiveObject
        {
            public string name { get; set; } = string.Empty;

            public LiveObjectHandle? liveObject => LiveObjectRegistry.FindById(name);

            string ILiveObject.id => name;

            public bool disposed;

            [LiveField] public float intensity;

            public void OnEnable() { }
            public void OnDisable() { }
            public void OnDispose() { }
            public void Update() { }
            public void Reset() { }
        }

        /// <summary>A maker, counting what it was asked to do so the reconcile can be checked.</summary>
        private sealed class LampRecipe : ILiveRecipe
        {
            public int created;
            public int destroyed;

            /// <summary>When true, stands for an asset that is not loaded: it cannot make one now.</summary>
            public bool unavailable;

            public ILiveObject Create(string id, string typeName)
            {
                if (unavailable) return null;

                created++;
                return new Lamp { name = id };
            }

            public void Destroy(ILiveObject instance)
            {
                destroyed++;
                if (instance is Lamp lamp) lamp.disposed = true;
            }
        }

        private const string kRecipe = "test:lamp";

        private LampRecipe _recipe;
        private StructureBlock _structure;
        private FrameSymbolTable _symbols;

        [SetUp]
        public void StartClean()
        {
            LiveObjectRegistry.ClearAll();
            LiveClass.RegisterFromAttributes<Lamp>();
            LiveClass.RegisterFromAttributes<Made>();

            FrameGate.ResetState("[test] cleared");
            FrameGate.SetClock(new FrameCounterClock(FrameRate.FPS60));

            _recipe = new LampRecipe();
            LiveRecipes.Clear();
            LiveRecipes.Register(kRecipe, _recipe);

            _structure = new StructureBlock();
            _symbols = new FrameSymbolTable();
        }

        [TearDown]
        public void Finish()
        {
            LiveObjectRegistry.ClearAll();
            LiveRecipes.Clear();
            LiveStructureSystem.ForgetMade();
            LiveStructureSystem.applyOnSuppliedFrames = false;

            FrameGate.ResetState("[test] cleared");
            FrameGate.RestoreDefaultClock();

            _structure.Dispose();
        }

        private void Listed(string id, string recipe = kRecipe)
        {
            _structure.AddOrUpdate(_symbols.Intern(id), _symbols.Intern(nameof(Lamp)),
                FrameSymbolTable.kNone,
                recipe == null ? FrameSymbolTable.kNone : _symbols.Intern(recipe));
        }

        [Test]
        public void SomethingListedAndMissing_IsMade()
        {
            Listed("lamp-a");

            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(1, LiveStructureSystem.createdCount);
            Assert.IsTrue(LiveObjectRegistry.TryFindById("lamp-a", out _),
                "registered under the recorded id, since that is what the state lane addresses");
        }

        [Test]
        public void SomethingAlreadyThere_IsLeftAlone()
        {
            // Applying the same keyframe twice must not reload what is already standing. This is the
            // case that makes seeking usable at all: every seek applies an inventory.
            Listed("lamp-a");
            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            var first = LiveObjectRegistry.FindById("lamp-a");
            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(0, LiveStructureSystem.createdCount);
            Assert.AreEqual(1, _recipe.created);
            Assert.AreEqual(first?.target, LiveObjectRegistry.FindById("lamp-a")?.target,
                "the same object, not a replacement");
        }

        [Test]
        public void SomethingNoLongerListed_IsTakenAway()
        {
            Listed("lamp-a");
            Listed("lamp-b");
            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            // Scrubbing back to before lamp-b was spawned.
            _structure.Remove(_symbols.Intern("lamp-b"));
            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(1, LiveStructureSystem.destroyedCount);
            Assert.AreEqual(1, _recipe.destroyed);
            Assert.IsFalse(LiveObjectRegistry.TryFindById("lamp-b", out _));
            Assert.IsTrue(LiveObjectRegistry.TryFindById("lamp-a", out _), "the other one stays");
        }

        [Test]
        public void SomethingThisNeverMade_IsNotTakenAway()
        {
            // A replay runs in a scene that has things of its own in it. A recording is a record of
            // what it watched, not a claim about everything that may exist -- so an object it never
            // mentioned has to survive an apply that does not list it.
            LiveObjectRegistry.Create(new Lamp { name = "scene-lamp" }, "scene-lamp");

            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(0, LiveStructureSystem.destroyedCount);
            Assert.IsTrue(LiveObjectRegistry.TryFindById("scene-lamp", out _));
        }

        [Test]
        public void SomethingWithNoRecipe_IsNotMadeAndIsNotAnError()
        {
            // An object that was in the scene from the start is recorded so its values have an owner,
            // not so it can be stood up somewhere else.
            Listed("lamp-a", recipe: null);

            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(0, LiveStructureSystem.createdCount);
            Assert.AreEqual(0, LiveStructureSystem.unresolvedCount);
        }

        [Test]
        public void ARecipeNothingHereKnows_IsCountedRatherThanThrown()
        {
            Listed("lamp-a", recipe: "test:missing");
            Listed("lamp-b");

            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(1, LiveStructureSystem.unresolvedCount);
            Assert.AreEqual(1, LiveStructureSystem.createdCount,
                "one object that cannot be rebuilt does not stop the rest of the world from being");
        }

        [Test]
        public void AMakerThatCannotMakeOneNow_IsCountedTheSameWay()
        {
            // An asset that is not loaded yet. The recipe exists; it just cannot produce right now.
            _recipe.unavailable = true;
            Listed("lamp-a");

            LiveStructureSystem.ApplyFrom(_structure, _symbols);

            Assert.AreEqual(0, LiveStructureSystem.createdCount);
            Assert.AreEqual(1, LiveStructureSystem.unresolvedCount);
        }

        [Test]
        public void WhatMadeAnObject_IsRecordedWithIt()
        {
            // Capture is the other half: without the key going in, nothing above can come back out.
            var made = new Made { name = "lamp-a" };
            LiveObjectRegistry.Create(made, "lamp-a");

            LiveStructureSystem.CaptureInto(_structure, _symbols);

            var index = _structure.IndexOf(_symbols.Intern("lamp-a"));
            Assert.GreaterOrEqual(index, 0);
            Assert.AreEqual(kRecipe, _symbols.Resolve(_structure[index].recipeId));
        }

        [Test]
        public void ASuppliedFrame_IsOnlyActedOnWhenSomebodyAskedForThat()
        {
            // A viewer watching a replay wants to see what the recording holds without it
            // rearranging the scene being watched in, so this is off unless a replayer turns it on.
            Assert.IsFalse(LiveStructureSystem.applyOnSuppliedFrames);
        }
    }
}
