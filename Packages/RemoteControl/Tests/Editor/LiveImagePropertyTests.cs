// Copyright (c) You-Ri, 2026
using System.Linq;
using NUnit.Framework;
using Lilium.RemoteControl;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Image members ([ImagePreview] on a <see cref="LiveImageData"/> getter) fold to their own
    /// address on every JSON read and never invoke the getter there — the getter renders a frame,
    /// so listing an object must not run it. The bytes are only pulled by a direct property GET
    /// (LiveObjectHandler's binary branch). These tests pin the fold on both JSON paths
    /// (whole-object and single-property), the getter-invocation contract, and the older
    /// string-address style staying untouched.
    /// </summary>
    [TestFixture]
    public class LiveImagePropertyTests
    {
        [LiveClass("ImageFixture")]
        public class ImageFixture
        {
            public int getterCalls;

            [LiveField] public int plain = 7;

            [LiveProperty, ImagePreview]
            public LiveImageData preview
            {
                get
                {
                    getterCalls++;
                    return new LiveImageData(new byte[] { 1, 2, 3 }, "image/png");
                }
            }

            // Older style: [ImagePreview] on a string member — the value IS an address served by
            // some other route, and the getter is safe to call. Serialization must not change.
            [LiveProperty, ImagePreview]
            public string legacyPreview => "/live/somewhere/else";
        }

        private TestLiveObjectResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            LiveClass.Clear();
            foreach (var obj in LiveObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
            _resolver = new TestLiveObjectResolver();
            LiveClass.RegisterFromAttributes<ImageFixture>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in LiveObjectRegistry.instances.ToList())
            {
                obj.Unregister();
            }
        }

        private static LiveObjectHandle _CreateHandle(ImageFixture target, string id = "img-1")
            => new LiveObjectHandle(id, LiveClass.Find(typeof(ImageFixture)), target);

        [Test]
        public void FullSerialization_FoldsImageMemberToAddress_WithoutInvokingGetter()
        {
            var target = new ImageFixture();
            var handle = _CreateHandle(target);

            var json = LivePropertySerializer.SerializeFullToJObject(handle, _resolver);

            Assert.AreEqual("/live/object/img-1/preview", (string)json["preview"]);
            Assert.AreEqual("/live/somewhere/else", (string)json["legacyPreview"]);
            Assert.AreEqual(7, (int)json["plain"]);
            Assert.AreEqual(0, target.getterCalls, "listing an object must not render its picture");
        }

        [Test]
        public void SingleProperty_FoldsToAddress_AndNeverReportsChanged()
        {
            var target = new ImageFixture();
            var handle = _CreateHandle(target);

            var property = handle.FindProperty("preview");
            Assert.IsTrue(property.HasValue);

            var json = LivePropertySerializer.ToJObject(property.Value, _resolver);

            Assert.AreEqual("/live/object/img-1/preview", (string)json["value"]);
            Assert.AreEqual("img-1", (string)json["id"]);
            Assert.AreEqual("preview", (string)json["path"]);
            Assert.AreEqual(false, (bool)json["changed"], "a picture is derived state; it has no baseline");
            Assert.AreEqual(0, target.getterCalls);
        }

        [Test]
        public void Persistence_NeverCarriesImageMember()
        {
            var target = new ImageFixture();
            var handle = _CreateHandle(target);

            var json = LivePropertySerializer.SerializeFullToJObject(handle, _resolver, forPersistence: true);

            Assert.IsNull(json["preview"], "an image member is derived and must not reach scene.json");
            Assert.AreEqual(0, target.getterCalls);
        }

        [Test]
        public void IsImageProperty_RequiresBothAttributeAndValueType()
        {
            var liveClass = LiveClass.Find(typeof(ImageFixture));
            var types = liveClass.propertyTypes.ToDictionary(p => p.name);

            Assert.IsTrue(LivePropertySerializer.IsImageProperty(types["preview"]));
            Assert.IsFalse(LivePropertySerializer.IsImageProperty(types["legacyPreview"]),
                "a string [ImagePreview] member keeps the older call-the-getter style");
            Assert.IsFalse(LivePropertySerializer.IsImageProperty(types["plain"]));
        }

        [Test]
        public void GetterRuns_OnlyWhenValueIsPulledDirectly()
        {
            // The one sanctioned getter call: what the handler's binary branch does.
            var target = new ImageFixture();
            var handle = _CreateHandle(target);

            var property = handle.FindProperty("preview");
            var value = (LiveImageData)property.Value.GetValue();

            Assert.IsTrue(value.isValid);
            Assert.AreEqual("image/png", value.mimeType);
            Assert.AreEqual(1, target.getterCalls);
        }
    }
}
