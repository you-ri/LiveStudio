// Copyright (c) You-Ri, 2026
using NUnit.Framework;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// The lane follows from the persistence: what the live scene saves, the frame carries; what it
    /// does not save, the frame leaves alone -- unless the member says otherwise, out loud.
    ///
    /// Pure, so it is tested as a table. The registration path (<c>LiveClass</c>) and the generator
    /// (<c>StateBlockEmitter</c>) both apply this rule; their tests check that they call it, this one
    /// checks what it says.
    /// </summary>
    public class FrameLaneRulesTests
    {
        [Test]
        public void ASavedMemberThatSaysNothing_IsRecordedWhenItChanges()
        {
            Assert.AreEqual(FrameLane.Event,
                FrameLaneRules.Resolve(null, isPersistable: true, PersistScope.Scene, typeIsOffFrame: false));
        }

        [Test]
        public void AMemberSavedToTheProjectSettings_IsOffTheFrame()
        {
            Assert.AreEqual(FrameLane.None,
                FrameLaneRules.Resolve(null, isPersistable: true, PersistScope.Project, typeIsOffFrame: false));
        }

        [Test]
        public void AMemberTheOwnerSavesItself_IsOffTheFrame()
        {
            Assert.AreEqual(FrameLane.None,
                FrameLaneRules.Resolve(null, isPersistable: true, PersistScope.Custom, typeIsOffFrame: false));
        }

        [Test]
        public void AMemberNothingSaves_IsOffTheFrame()
        {
            Assert.AreEqual(FrameLane.None,
                FrameLaneRules.Resolve(null, isPersistable: false, PersistScope.Scene, typeIsOffFrame: false));
        }

        /// <summary>
        /// The exception: a pose, an expression weight, which avatar is out. Not in the file, in
        /// the take -- and only because it said so.
        /// </summary>
        [Test]
        public void AnUnsavedMemberThatAsksForALane_GetsIt()
        {
            Assert.AreEqual(FrameLane.State,
                FrameLaneRules.Resolve(FrameLane.State, isPersistable: false, PersistScope.Scene, typeIsOffFrame: false));
            Assert.AreEqual(FrameLane.Event,
                FrameLaneRules.Resolve(FrameLane.Event, isPersistable: false, PersistScope.Scene, typeIsOffFrame: false));
            Assert.AreEqual(FrameLane.State,
                FrameLaneRules.Resolve(FrameLane.State, isPersistable: true, PersistScope.Project, typeIsOffFrame: false));
        }

        [Test]
        public void ASavedMemberThatAsksForTheStateLane_GetsIt()
        {
            Assert.AreEqual(FrameLane.State,
                FrameLaneRules.Resolve(FrameLane.State, isPersistable: true, PersistScope.Scene, typeIsOffFrame: false));
        }

        /// <summary>
        /// None is never something a member gets by asking. Refused upstream and treated here as
        /// unsaid, so a saved member cannot leave the frame by declaration -- the scene file would
        /// describe a world the recording disagrees with.
        /// </summary>
        [Test]
        public void ADeclaredNone_CannotTakeASavedMemberOffTheFrame()
        {
            Assert.AreEqual(FrameLane.Event,
                FrameLaneRules.Resolve(FrameLane.None, isPersistable: true, PersistScope.Scene, typeIsOffFrame: false));
        }

        [Test]
        public void ATypeOffTheFrame_TakesEveryMemberWithIt()
        {
            Assert.AreEqual(FrameLane.None,
                FrameLaneRules.Resolve(FrameLane.State, isPersistable: true, PersistScope.Scene, typeIsOffFrame: true));
            Assert.AreEqual(FrameLane.None,
                FrameLaneRules.Resolve(null, isPersistable: true, PersistScope.Scene, typeIsOffFrame: true));
        }

        [Test]
        public void OnlyTheSceneCountsAsSaved()
        {
            Assert.IsTrue(FrameLaneRules.IsSavedToScene(true, PersistScope.Scene));
            Assert.IsFalse(FrameLaneRules.IsSavedToScene(true, PersistScope.Project));
            Assert.IsFalse(FrameLaneRules.IsSavedToScene(true, PersistScope.Custom));
            Assert.IsFalse(FrameLaneRules.IsSavedToScene(false, PersistScope.Scene));
        }
    }
}
