// Copyright (c) You-Ri, 2026
using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// A delta taken for one <see cref="PersistScope"/> holds that scope's members and no others, and the
    /// defaults it is taken against are the ones of that scope. This is what lets an owner keep its own
    /// members in its own file (an avatar's settings) while the live scene keeps the rest of the same
    /// object, without either writing the other's values.
    /// </summary>
    public class ScopedDeltaSerializationTests
    {
        // internal, not private: the generator writes the declaration order of every [LiveClass] into a
        // file of its own, which cannot reach a private nested type.
        [LiveClass("ScopedDeltaSubject")]
        internal class Subject
        {
            [LiveField] public int sceneValue = 1;

            [LiveField(persistScope = PersistScope.Custom)] public int ownedValue = 10;

            [LiveField(persistScope = PersistScope.Custom)] public string ownedText = "a";
        }

        private Subject _subject;
        private LiveObjectHandle _handle;

        [SetUp]
        public void SetUp()
        {
            _subject = new Subject();
            _handle = LiveObjectRegistry.GetOrCreateWithoutId(LiveClass.Get<Subject>(), _subject);
            LiveObjectDefaultRegistry.CaptureDefaults(_handle, DefaultLiveObjectResolver.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            LiveObjectDefaultRegistry.Remove(_handle);
        }

        [Test]
        public void EachScopesDelta_HoldsOnlyItsOwnChangedMembers()
        {
            _subject.sceneValue = 2;
            _subject.ownedValue = 20;

            var scene = JObject.Parse(LiveObjectSnapshot.CaptureDelta(_handle));
            var owned = JObject.Parse(LiveObjectSnapshot.CaptureDelta(_handle, PersistScope.Custom));

            Assert.AreEqual(2, scene["sceneValue"]?.Value<int>());
            Assert.IsNull(scene["ownedValue"]);
            Assert.AreEqual(20, owned["ownedValue"]?.Value<int>());
            Assert.IsNull(owned["sceneValue"]);
        }

        [Test]
        public void AnUnchangedMember_IsInNeitherDelta()
        {
            _subject.ownedValue = 20;

            var scene = JObject.Parse(LiveObjectSnapshot.CaptureDelta(_handle));
            var owned = JObject.Parse(LiveObjectSnapshot.CaptureDelta(_handle, PersistScope.Custom));

            Assert.IsNull(scene["sceneValue"]);
            Assert.IsNull(owned["ownedText"]);
        }

        [Test]
        public void RestoringOneScopesDefaults_LeavesTheOtherScopeAlone()
        {
            _subject.sceneValue = 2;
            _subject.ownedValue = 20;
            _subject.ownedText = "b";

            var defaults = LiveObjectSnapshot.CaptureScopedDefaults(_handle, PersistScope.Custom);
            Assert.IsNotNull(defaults);
            LiveObjectSnapshot.Restore(defaults, _handle);

            Assert.AreEqual(10, _subject.ownedValue);
            Assert.AreEqual("a", _subject.ownedText);
            Assert.AreEqual(2, _subject.sceneValue, "the live scene's member is not this owner's to reset");
        }
    }
}
