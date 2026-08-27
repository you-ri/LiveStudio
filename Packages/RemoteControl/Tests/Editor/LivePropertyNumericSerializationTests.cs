// Copyright (c) You-Ri, 2026

using NUnit.Framework;
using Newtonsoft.Json.Linq;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Numeric types must come back as numbers.
    ///
    /// A type the writer accepts but the reader has no case for does not fail -- it falls through to
    /// the plain-object path, where JsonUtility returns "{}" and the value leaves as a bare type stub
    /// (<c>{"@type":"Int64"}</c>). The value is gone and nothing says so. Found on a frame counter
    /// exposed as a long, which reached the remote app with no number in it.
    /// </summary>
    [TestFixture]
    public class LivePropertyNumericSerializationTests
    {
        private static JToken _Serialize(object value)
            => JObject.Parse(LivePropertySerializer.ToJson(value))["value"];

        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(9007199254740993L)]
        public void Long_SerializesAsANumber(long value)
        {
            var token = _Serialize(value);

            Assert.AreEqual(JTokenType.Integer, token.Type, "a long must not degrade to a type stub");
            Assert.AreEqual(value, token.Value<long>());
        }

        [Test]
        public void Short_SerializesAsANumber()
        {
            var token = _Serialize((short)-42);

            Assert.AreEqual(JTokenType.Integer, token.Type);
            Assert.AreEqual(-42, token.Value<short>());
        }

        [Test]
        public void Byte_SerializesAsANumber()
        {
            var token = _Serialize((byte)200);

            Assert.AreEqual(JTokenType.Integer, token.Type);
            Assert.AreEqual(200, token.Value<byte>());
        }

        [Test]
        public void EveryNumericTypeTheWriterAcceptsSurvivesARoundTrip()
        {
            // The reader's set has to match the writer's, or a value can be written and then read
            // back as nothing. These are the numeric types DeserializeUnityType accepts.
            object[] values = { 1, 1f, 1.0, 1L, (short)1, (byte)1 };

            foreach (var value in values)
            {
                var token = _Serialize(value);
                Assert.IsTrue(
                    token.Type == JTokenType.Integer || token.Type == JTokenType.Float,
                    $"{value.GetType().Name} came back as {token.Type}, not a number");
            }
        }
    }
}
