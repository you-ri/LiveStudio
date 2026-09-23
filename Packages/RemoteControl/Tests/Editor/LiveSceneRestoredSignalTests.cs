// Copyright (c) You-Ri, 2026
using System;

using NUnit.Framework;

using Lilium.RemoteControl.LiveScene;

namespace Lilium.RemoteControl.Tests
{
    /// <summary>
    /// The announcement a finished restore makes, and what a subscriber can count on when it arrives.
    /// <para>
    /// It exists for one job: a <c>@prefab</c> entry whose asset could not be resolved while the passes
    /// ran is queued rather than dropped, and somebody has to come back for it. That somebody owns the
    /// assets, so the work cannot live here — resolving one is asynchronous, and started from inside the
    /// restore it outlives the call and runs during whatever comes next.
    /// </para>
    /// <para>
    /// What this pins is therefore the contract the subscriber relies on: it is told once, it is told
    /// after the queue is filled, and it is not told about a file that was never read.
    /// </para>
    /// </summary>
    public class LiveSceneRestoredSignalTests
    {
        private Action _subscriber;

        [TearDown]
        public void TearDown()
        {
            if (_subscriber != null) LiveSceneSerializer.onLiveSceneRestored -= _subscriber;
            _subscriber = null;
            PendingPrefabStore.Clear();
        }

        private void _Subscribe(Action handler)
        {
            _subscriber = handler;
            LiveSceneSerializer.onLiveSceneRestored += handler;
        }

        // A root entry carrying a @prefab key nothing can resolve, which is what Pass 1 queues.
        private const string kSceneWithUnresolvablePrefab = @"{
            ""format"": ""jp.lilium.remotecontrol.live"",
            ""formatVersion"": 1,
            ""objects"": [
                { ""@prefab"": ""nothing-resolves-this"", ""@type"": ""GameObject"", ""@id"": ""deferred-1"" }
            ]
        }";

        [Test]
        public void ARestore_SaysOnceThatItIsDone()
        {
            int told = 0;
            _Subscribe(() => told++);

            LiveSceneSerializer.LiveSceneFromJson(@"{""format"":""jp.lilium.remotecontrol.live"",""formatVersion"":1,""objects"":[]}",
                DefaultLiveObjectResolver.Instance);

            Assert.AreEqual(1, told);
        }

        [Test]
        public void TheSignal_ArrivesWithTheDeferredEntriesAlreadyQueued()
        {
            // The whole point of being told: by then there is something to come back for.
            bool queuedWhenTold = false;
            _Subscribe(() => queuedWhenTold = PendingPrefabStore.Contains("deferred-1"));

            LiveSceneSerializer.LiveSceneFromJson(kSceneWithUnresolvablePrefab, DefaultLiveObjectResolver.Instance);

            Assert.IsTrue(queuedWhenTold,
                "A subscriber told before Pass 1 filled the queue would find nothing to drain.");
        }

        [Test]
        public void AFileThatWasNeverRead_TellsNobody()
        {
            // Nothing was applied, so there is nothing to come back for. Saying so anyway would have
            // every subscriber do its work for a file that does not exist.
            int told = 0;
            _Subscribe(() => told++);

            LiveSceneSerializer.LiveSceneFromJson(null, DefaultLiveObjectResolver.Instance);
            LiveSceneSerializer.LiveSceneFromJson("", DefaultLiveObjectResolver.Instance);

            Assert.AreEqual(0, told);
        }
    }
}
