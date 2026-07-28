// Copyright (c) You-Ri, 2026
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Lilium.RemoteControl;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// Verifies the cursor semantics remote apps depend on: a client must never miss a change it has
    /// not seen, and must never be handed the same change twice once it has advanced past it.
    /// </summary>
    public class LiveChangeLogTests
    {
        private readonly List<string> _buffer = new List<string>();

        [SetUp]
        public void SetUp()
        {
            LiveChangeLog.Clear();
            _buffer.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            LiveChangeLog.Clear();
        }

        [Test]
        public void Clear_LeavesRevisionAtZero()
        {
            LiveChangeLog.Record("a");
            LiveChangeLog.Clear();

            Assert.AreEqual(0, LiveChangeLog.revision);
            Assert.AreEqual(0, LiveChangeLog.GetChangesSince(0, _buffer));
            Assert.IsEmpty(_buffer);
        }

        [Test]
        public void GetChangesSince_Zero_ReturnsEverythingRecorded()
        {
            LiveChangeLog.Record("a");
            LiveChangeLog.Record("b");

            var revision = LiveChangeLog.GetChangesSince(0, _buffer);

            Assert.AreEqual(2, revision);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _buffer);
        }

        [Test]
        public void GetChangesSince_CurrentRevision_ReturnsNothing()
        {
            LiveChangeLog.Record("a");
            var revision = LiveChangeLog.GetChangesSince(0, _buffer);

            LiveChangeLog.GetChangesSince(revision, _buffer);

            Assert.IsEmpty(_buffer);
        }

        [Test]
        public void GetChangesSince_ReturnsOnlyIdsRecordedAfterTheCursor()
        {
            LiveChangeLog.Record("a");
            var revision = LiveChangeLog.GetChangesSince(0, _buffer);
            LiveChangeLog.Record("b");

            LiveChangeLog.GetChangesSince(revision, _buffer);

            CollectionAssert.AreEqual(new[] { "b" }, _buffer);
        }

        [Test]
        public void Record_SameIdTwice_ReportsItOnceAtTheLatestRevision()
        {
            LiveChangeLog.Record("a");
            LiveChangeLog.Record("a");

            var revision = LiveChangeLog.GetChangesSince(0, _buffer);

            Assert.AreEqual(2, revision, "every record bumps the revision");
            CollectionAssert.AreEqual(new[] { "a" }, _buffer, "but an id is only reported once");
        }

        [Test]
        public void Record_AfterCursor_ResurfacesAnAlreadySeenId()
        {
            LiveChangeLog.Record("a");
            var revision = LiveChangeLog.GetChangesSince(0, _buffer);

            // The object changed again — a client that already refetched it must be told to refetch.
            LiveChangeLog.Record("a");
            LiveChangeLog.GetChangesSince(revision, _buffer);

            CollectionAssert.AreEqual(new[] { "a" }, _buffer);
        }

        [Test]
        public void Record_IgnoresNullAndEmptyIds()
        {
            LiveChangeLog.Record(null);
            LiveChangeLog.Record(string.Empty);

            Assert.AreEqual(0, LiveChangeLog.revision);
            LiveChangeLog.GetChangesSince(0, _buffer);
            Assert.IsEmpty(_buffer);
        }

        [Test]
        public void Record_FromManyThreads_AssignsEveryChangeADistinctRevision()
        {
            const int kThreads = 8;
            const int kPerThread = 250;

            var tasks = new Task[kThreads];
            for (int t = 0; t < kThreads; t++)
            {
                int threadIndex = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < kPerThread; i++)
                    {
                        LiveChangeLog.Record($"obj-{threadIndex}-{i}");
                    }
                });
            }
            Task.WaitAll(tasks);

            // Distinct ids and one revision each: nothing was lost to a race.
            Assert.AreEqual(kThreads * kPerThread, LiveChangeLog.revision);
            LiveChangeLog.GetChangesSince(0, _buffer);
            Assert.AreEqual(kThreads * kPerThread, _buffer.Count);
        }
    }
}
