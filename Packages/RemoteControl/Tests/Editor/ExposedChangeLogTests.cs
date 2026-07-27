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
    public class ExposedChangeLogTests
    {
        private readonly List<string> _buffer = new List<string>();

        [SetUp]
        public void SetUp()
        {
            ExposedChangeLog.Clear();
            _buffer.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ExposedChangeLog.Clear();
        }

        [Test]
        public void Clear_LeavesRevisionAtZero()
        {
            ExposedChangeLog.Record("a");
            ExposedChangeLog.Clear();

            Assert.AreEqual(0, ExposedChangeLog.revision);
            Assert.AreEqual(0, ExposedChangeLog.GetChangesSince(0, _buffer));
            Assert.IsEmpty(_buffer);
        }

        [Test]
        public void GetChangesSince_Zero_ReturnsEverythingRecorded()
        {
            ExposedChangeLog.Record("a");
            ExposedChangeLog.Record("b");

            var revision = ExposedChangeLog.GetChangesSince(0, _buffer);

            Assert.AreEqual(2, revision);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _buffer);
        }

        [Test]
        public void GetChangesSince_CurrentRevision_ReturnsNothing()
        {
            ExposedChangeLog.Record("a");
            var revision = ExposedChangeLog.GetChangesSince(0, _buffer);

            ExposedChangeLog.GetChangesSince(revision, _buffer);

            Assert.IsEmpty(_buffer);
        }

        [Test]
        public void GetChangesSince_ReturnsOnlyIdsRecordedAfterTheCursor()
        {
            ExposedChangeLog.Record("a");
            var revision = ExposedChangeLog.GetChangesSince(0, _buffer);
            ExposedChangeLog.Record("b");

            ExposedChangeLog.GetChangesSince(revision, _buffer);

            CollectionAssert.AreEqual(new[] { "b" }, _buffer);
        }

        [Test]
        public void Record_SameIdTwice_ReportsItOnceAtTheLatestRevision()
        {
            ExposedChangeLog.Record("a");
            ExposedChangeLog.Record("a");

            var revision = ExposedChangeLog.GetChangesSince(0, _buffer);

            Assert.AreEqual(2, revision, "every record bumps the revision");
            CollectionAssert.AreEqual(new[] { "a" }, _buffer, "but an id is only reported once");
        }

        [Test]
        public void Record_AfterCursor_ResurfacesAnAlreadySeenId()
        {
            ExposedChangeLog.Record("a");
            var revision = ExposedChangeLog.GetChangesSince(0, _buffer);

            // The object changed again — a client that already refetched it must be told to refetch.
            ExposedChangeLog.Record("a");
            ExposedChangeLog.GetChangesSince(revision, _buffer);

            CollectionAssert.AreEqual(new[] { "a" }, _buffer);
        }

        [Test]
        public void Record_IgnoresNullAndEmptyIds()
        {
            ExposedChangeLog.Record(null);
            ExposedChangeLog.Record(string.Empty);

            Assert.AreEqual(0, ExposedChangeLog.revision);
            ExposedChangeLog.GetChangesSince(0, _buffer);
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
                        ExposedChangeLog.Record($"obj-{threadIndex}-{i}");
                    }
                });
            }
            Task.WaitAll(tasks);

            // Distinct ids and one revision each: nothing was lost to a race.
            Assert.AreEqual(kThreads * kPerThread, ExposedChangeLog.revision);
            ExposedChangeLog.GetChangesSince(0, _buffer);
            Assert.AreEqual(kThreads * kPerThread, _buffer.Count);
        }
    }
}
