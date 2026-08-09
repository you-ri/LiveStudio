// Copyright (c) You-Ri, 2026

using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Lilium.LiveStudio.Social.EditorTests
{
    /// <summary>
    /// Tests the wire-facing rules behind <see cref="SocialEventHandler"/>: token comparison, the body
    /// size limit, and what counts as a well-formed event or batch. The handler itself is a thin shell
    /// over these — <c>HttpListenerContext</c> cannot be constructed in a test, so the decisions that
    /// produce 200 / 400 / 401 / 413 live here and are exercised directly.
    /// </summary>
    public class SocialEventIntakeTests
    {
        private int _frame;

        [SetUp]
        public void SetUp()
        {
            SocialEventHub.Reset();
            _frame = 1;
            SocialEventHub.frameProvider = () => _frame;
        }

        [TearDown]
        public void TearDown()
        {
            SocialEventHub.Reset();
        }

        private static Stream _Body(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

        /// <summary>
        /// A stream that hands back one byte at a time. <see cref="MemoryStream"/> always satisfies a read
        /// in full, so it never exercises the short-read loop that a real network stream forces.
        /// </summary>
        private sealed class DribblingStream : Stream
        {
            private readonly byte[] _bytes;
            private int _position;

            public DribblingStream(string text) => _bytes = Encoding.UTF8.GetBytes(text);

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _bytes.Length || count <= 0) return 0;
                buffer[offset] = _bytes[_position++];
                return 1;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _bytes.Length;
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // --- Token ---

        [Test]
        public void IsAuthorized_WithNoConfiguredToken_LetsAnythingThrough()
        {
            Assert.IsTrue(SocialEventIntake.IsAuthorized(null, null));
            Assert.IsTrue(SocialEventIntake.IsAuthorized(string.Empty, null));
            Assert.IsTrue(SocialEventIntake.IsAuthorized(string.Empty, "whatever"));
        }

        [Test]
        public void IsAuthorized_WithConfiguredToken_RequiresAnExactMatch()
        {
            Assert.IsTrue(SocialEventIntake.IsAuthorized("s3cret", "s3cret"));
            Assert.IsFalse(SocialEventIntake.IsAuthorized("s3cret", null));
            Assert.IsFalse(SocialEventIntake.IsAuthorized("s3cret", string.Empty));
            Assert.IsFalse(SocialEventIntake.IsAuthorized("s3cret", "wrong"));
            Assert.IsFalse(SocialEventIntake.IsAuthorized("s3cret", "S3CRET"), "comparison is case sensitive");
            Assert.IsFalse(SocialEventIntake.IsAuthorized("s3cret", "s3cretXX"), "a correct prefix is not enough");
            Assert.IsFalse(SocialEventIntake.IsAuthorized("s3cret", "s3cre"));
        }

        // --- Body limit ---

        [Test]
        public void TryReadBody_ReadsExactlyTheDeclaredLength()
        {
            const string json = "{\"source\":\"test\"}";
            var bytes = Encoding.UTF8.GetBytes(json);

            Assert.IsTrue(SocialEventIntake.TryReadBody(_Body(json), bytes.Length, out var body));
            Assert.AreEqual(json, body);
        }

        [Test]
        public void TryReadBody_DecodesMultiByteCharacters()
        {
            const string json = "{\"message\":\"おめでとう🎉\"}";
            var bytes = Encoding.UTF8.GetBytes(json);

            Assert.IsTrue(SocialEventIntake.TryReadBody(_Body(json), bytes.Length, out var body));
            Assert.AreEqual(json, body, "the length is in bytes but the result must be the original text");
        }

        [Test]
        public void TryReadBody_RefusesAnOversizedDeclaredLength_WithoutReading()
        {
            var stream = _Body("x");

            Assert.IsFalse(SocialEventIntake.TryReadBody(stream, SocialEventIntake.kMaxBodyBytes + 1, out _));
            Assert.AreEqual(0, stream.Position, "an oversized body must be refused before it is read");
        }

        [Test]
        public void TryReadBody_WithUnknownLength_ReadsUpToTheLimit()
        {
            // Chunked encoding reports -1, so the cap has to be enforced while reading.
            string text = new string('x', SocialEventIntake.kMaxBodyBytes);

            Assert.IsTrue(SocialEventIntake.TryReadBody(_Body(text), -1, out var body));
            Assert.AreEqual(SocialEventIntake.kMaxBodyBytes, body.Length);
        }

        [Test]
        public void TryReadBody_WithUnknownLength_RefusesOnceThePayloadPassesTheLimit()
        {
            string text = new string('x', SocialEventIntake.kMaxBodyBytes + 1);

            Assert.IsFalse(SocialEventIntake.TryReadBody(_Body(text), -1, out _));
        }

        [Test]
        public void TryReadBody_AcceptsExactlyTheLimit()
        {
            string text = new string('x', SocialEventIntake.kMaxBodyBytes);

            Assert.IsTrue(SocialEventIntake.TryReadBody(_Body(text), SocialEventIntake.kMaxBodyBytes, out var body),
                "the limit is inclusive");
            Assert.AreEqual(SocialEventIntake.kMaxBodyBytes, body.Length);
        }

        [Test]
        public void TryReadBody_ReassemblesAcrossShortReads()
        {
            // A real network stream returns whatever has arrived, not the whole body in one call.
            const string json = "{\"source\":\"test\",\"type\":\"chat\",\"message\":\"split across reads\"}";
            int length = Encoding.UTF8.GetByteCount(json);

            Assert.IsTrue(SocialEventIntake.TryReadBody(new DribblingStream(json), length, out var declared));
            Assert.AreEqual(json, declared, "declared-length path must keep reading until the buffer is full");

            Assert.IsTrue(SocialEventIntake.TryReadBody(new DribblingStream(json), -1, out var chunked));
            Assert.AreEqual(json, chunked, "chunked path must keep reading until the peer stops");
        }

        [Test]
        public void TryReadBody_WithAnOverstatedContentLength_StopsAtWhatArrived()
        {
            // Content-Length promised more than the client sent (a disconnect mid-request). The read must
            // end at end-of-stream rather than hang, and the result must not be padded out with the NUL
            // bytes left over in the buffer — otherwise an otherwise-valid body would fail to parse.
            const string json = "{\"source\":\"test\",\"type\":\"chat\"}";

            Assert.IsTrue(SocialEventIntake.TryReadBody(_Body(json), Encoding.UTF8.GetByteCount(json) + 20, out var body));
            Assert.AreEqual(json, body);
            Assert.IsTrue(SocialEventIntake.TrySubmitOne(body, out var error), error);
        }

        // --- Single event ---

        [Test]
        public void TrySubmitOne_AcceptsAWellFormedEventAndEnqueuesIt()
        {
            const string json = @"{
                ""source"": ""youtube"",
                ""type"": ""superchat"",
                ""id"": ""evt-1"",
                ""user"": { ""id"": ""UC1"", ""name"": ""viewer"", ""isMember"": true },
                ""message"": ""congrats"",
                ""amount"": 500,
                ""currency"": ""JPY""
            }";

            Assert.IsTrue(SocialEventIntake.TrySubmitOne(json, out var error), error);
            Assert.IsNull(error);

            var e = SocialEventHub.currentEvents[0];
            Assert.AreEqual(SocialEventSources.YouTube, e.source);
            Assert.AreEqual(SocialEventTypes.SuperChat, e.type);
            Assert.AreEqual("evt-1", e.id);
            Assert.AreEqual("viewer", e.user.name);
            Assert.IsTrue(e.user.isMember);
            Assert.AreEqual("congrats", e.message);
            Assert.AreEqual(500f, e.amount, 1e-4f);
            Assert.AreEqual("JPY", e.currency);
        }

        [Test]
        public void TrySubmitOne_AcceptsAMinimalEvent()
        {
            // Only source and type are required; the hub fills in the rest.
            Assert.IsTrue(SocialEventIntake.TrySubmitOne(@"{""source"":""test"",""type"":""chat""}", out _));

            var e = SocialEventHub.currentEvents[0];
            Assert.AreEqual(string.Empty, e.message);
            Assert.IsNotNull(e.user);
            Assert.AreEqual(0f, e.amount, 1e-4f);
        }

        [Test]
        public void TrySubmitOne_IgnoresUnknownFields()
        {
            // The contract says a reader tolerates fields it does not know, so feeders can add their own.
            Assert.IsTrue(SocialEventIntake.TrySubmitOne(
                @"{""source"":""test"",""type"":""chat"",""futureField"":{""nested"":1}}", out var error), error);
            Assert.AreEqual(1, SocialEventHub.currentEvents.Count);
        }

        [Test]
        public void TrySubmitOne_TruncatesAnOverlongMessageInsteadOfRejecting()
        {
            string message = new string('x', SocialEvent.kMaxMessageLength + 50);
            string json = "{\"source\":\"test\",\"type\":\"chat\",\"message\":\"" + message + "\"}";

            Assert.IsTrue(SocialEventIntake.TrySubmitOne(json, out _));
            Assert.AreEqual(SocialEvent.kMaxMessageLength, SocialEventHub.currentEvents[0].message.Length);
        }

        [Test]
        public void TrySubmitOne_RejectsAMissingSource()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitOne(@"{""type"":""chat""}", out var error));
            StringAssert.Contains("source", error);
            StringAssert.StartsWith("[Social]", error);
            Assert.AreEqual(0, SocialEventHub.totalReceived, "a rejected event never reaches the hub");
        }

        [Test]
        public void TrySubmitOne_RejectsABlankSource()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitOne(@"{""source"":""  "",""type"":""chat""}", out var error));
            StringAssert.Contains("source", error);
        }

        [Test]
        public void TrySubmitOne_RejectsAMissingType()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitOne(@"{""source"":""test""}", out var error));
            StringAssert.Contains("type", error);
        }

        [Test]
        public void TrySubmitOne_RejectsMalformedJson()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitOne(@"{""source"":", out var error));
            StringAssert.StartsWith("[Social]", error);
            Assert.AreEqual(0, SocialEventHub.totalReceived);
        }

        [Test]
        public void TrySubmitOne_RejectsAnEmptyBody()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitOne("", out var error));
            StringAssert.StartsWith("[Social]", error);
            Assert.IsFalse(SocialEventIntake.TrySubmitOne("   ", out _));
        }

        [Test]
        public void TrySubmitOne_RejectsAJsonNullOrAnArray()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitOne("null", out var nullError));
            StringAssert.StartsWith("[Social]", nullError);

            Assert.IsFalse(SocialEventIntake.TrySubmitOne(@"[{""source"":""test"",""type"":""chat""}]", out var arrayError));
            StringAssert.StartsWith("[Social]", arrayError);
            Assert.AreEqual(0, SocialEventHub.totalReceived, "the array endpoint is a separate path");
        }

        // --- Batch ---

        [Test]
        public void TrySubmitBatch_AcceptsEveryWellFormedEntry()
        {
            const string json = @"[
                {""source"":""test"",""type"":""chat"",""message"":""a""},
                {""source"":""test"",""type"":""chat"",""message"":""b""}
            ]";

            Assert.IsTrue(SocialEventIntake.TrySubmitBatch(json, out int accepted, out int rejected, out var error), error);
            Assert.AreEqual(2, accepted);
            Assert.AreEqual(0, rejected);

            var events = SocialEventHub.currentEvents;
            Assert.AreEqual(2, events.Count);
            Assert.AreEqual("a", events[0].message);
            Assert.AreEqual("b", events[1].message);
        }

        [Test]
        public void TrySubmitBatch_SkipsBadEntriesWithoutLosingTheGoodOnes()
        {
            // One malformed comment must not cost a feeder the rest of its batch.
            const string json = @"[
                {""source"":""test"",""type"":""chat"",""message"":""good""},
                {""type"":""chat""},
                null,
                {""source"":""test"",""message"":""no type""}
            ]";

            Assert.IsTrue(SocialEventIntake.TrySubmitBatch(json, out int accepted, out int rejected, out var error), error);
            Assert.AreEqual(1, accepted);
            Assert.AreEqual(3, rejected);
            Assert.AreEqual("good", SocialEventHub.currentEvents[0].message);
        }

        [Test]
        public void TrySubmitBatch_AcceptsAnEmptyArray()
        {
            Assert.IsTrue(SocialEventIntake.TrySubmitBatch("[]", out int accepted, out int rejected, out _));
            Assert.AreEqual(0, accepted);
            Assert.AreEqual(0, rejected);
        }

        [Test]
        public void TrySubmitBatch_RejectsABodyThatIsNotAnArray()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitBatch(
                @"{""source"":""test"",""type"":""chat""}", out _, out _, out var error));
            StringAssert.StartsWith("[Social]", error);
            Assert.AreEqual(0, SocialEventHub.totalReceived);
        }

        [Test]
        public void TrySubmitBatch_RejectsMalformedJsonAndAnEmptyBody()
        {
            Assert.IsFalse(SocialEventIntake.TrySubmitBatch("[{", out _, out _, out var malformed));
            StringAssert.StartsWith("[Social]", malformed);

            Assert.IsFalse(SocialEventIntake.TrySubmitBatch("", out _, out _, out var empty));
            StringAssert.StartsWith("[Social]", empty);
        }

        [Test]
        public void TrySubmitBatch_OverflowingTheQueueIsCountedByTheHubNotTheResponse()
        {
            // "dropped" in the response means entries this request rejected. Queue overflow is a separate
            // condition, visible through /social/status, and must not be conflated with it.
            var json = new StringBuilder("[");
            int count = SocialEventHub.kQueueCapacity + 10;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(@"{""source"":""test"",""type"":""chat""}");
            }
            json.Append(']');

            Assert.IsTrue(SocialEventIntake.TrySubmitBatch(json.ToString(), out int accepted, out int rejected, out _));
            Assert.AreEqual(count, accepted);
            Assert.AreEqual(0, rejected, "every entry was well formed");
            Assert.AreEqual(10, SocialEventHub.totalDropped);
            Assert.AreEqual(SocialEventHub.kQueueCapacity, SocialEventHub.currentEvents.Count);
        }
    }
}
