// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio.Tests
{
    /// <summary>
    /// Which of the avatar controller's members belong to the avatar rather than to the scene, and what
    /// that costs a take.
    /// <para>
    /// The settings below are about one model — its meshes, its animator's parameters, its blend shapes —
    /// so they travel in that avatar's own preset file and are not written to the live scene. Saying it
    /// here as a test rather than trusting the declarations keeps a member from quietly moving back: a
    /// member that returns to the scene starts being carried over onto whichever avatar comes next, and
    /// one that leaves the frame without saying so is lost from every take that changed it.
    /// </para>
    /// </summary>
    public class AvatarOwnedSettingsTests
    {
        private static LivePropertyType _Member(string name)
        {
            var member = LiveClass.Get<AvatarController>().FindProperty(name);
            Assert.IsNotNull(member, $"'{name}' is no longer an exposed member of AvatarController");
            return member;
        }

        [TestCase("_expressionConfig")]
        [TestCase("meshStateOverrides")]
        [TestCase("animationParameterOverrides")]
        [TestCase("_avatarLayer")]
        [TestCase("_lockLowerBodyPose")]
        [TestCase("_bodyOverrideClip")]
        public void AnAvatarOwnedSetting_IsSavedWithTheAvatarAndNotWithTheScene(string name)
        {
            var member = _Member(name);

            Assert.AreEqual(PersistScope.Custom, member.persistScope);
            Assert.IsTrue(member.isPersistable, "still saved -- by the avatar's own file");
            Assert.IsFalse(FrameLaneRules.IsSavedToScene(member.isPersistable, member.persistScope));
        }

        [TestCase("meshStateOverrides", FrameLane.Event)]
        [TestCase("animationParameterOverrides", FrameLane.Event)]
        [TestCase("_lockLowerBodyPose", FrameLane.Event)]
        [TestCase("_avatarLayer", FrameLane.State)]
        public void ASettingTheOperatorChangesMidShow_IsStillCarriedByATake(string name, FrameLane expected)
        {
            Assert.AreEqual(expected, _Member(name).lane);
        }
    }
}
